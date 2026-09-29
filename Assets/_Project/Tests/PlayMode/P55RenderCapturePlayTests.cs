using System.Collections;
using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Transition;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P5.5 の<b>実描画</b>と録画（仕様書 §11 の P15。工程 P55-06b）。
    ///
    /// §11 は「<b>自動的な存在・表示判定に加え、16:9 の実録画を残す</b>。
    /// 録画だけで数値契約を合格にせず、数値だけで見た目を受入済みにしない」と定める。
    /// だからここは 2 本立てにしてある。
    ///
    /// <list type="number">
    /// <item><description><b>画素で言えることは画素で言う</b>——地形が画面を覆っているか、
    /// 全画面が暗転していないか。どちらも「見えているか」の話で、
    /// 数を数える検査では言えない（記録 019 の教訓）。</description></item>
    /// <item><description><b>存在・表示は部品で言う</b>——主人公の絵が
    /// 出発・中間・到着のどの瞬間も<b>ちょうど 1 つ</b>出ていて、画面の中に居る。
    /// これを画素だけで言おうとすると、色の当てっこになって脆い。</description></item>
    /// </list>
    ///
    /// <b>穴は目立つ色で見る。</b> 覆えていない場所は Camera の背景色になる。
    /// 空の色のままでは「空なのか穴なのか」を画素から区別できないので、
    /// 判定用の 1 枚だけ背景を<b>世界のどこにも無い色</b>（マゼンタ）で描き直し、
    /// その色が 1 画素でもあれば穴とする。録画へ残す 1 枚は出荷時の設定のまま撮る。
    ///
    /// <b>暗転は相対で見る。</b> 明るさの絶対値は照明を変えれば動くので、
    /// <b>遷移前のフレームと比べて</b>暗くなっていないことを見る。
    /// </summary>
    public sealed class P55RenderCapturePlayTests
    {
        /// <summary>試遊基準の画面比（§7.3「試遊基準は 16:9」）。</summary>
        private const int CaptureWidth = 1280;

        /// <summary>同・縦。1280×720 ＝ 16:9。</summary>
        private const int CaptureHeight = 720;

        /// <summary>穴を見るための背景色（世界のどこにも無い色）。</summary>
        private static readonly Color32 HoleColor = new Color32(255, 0, 255, 255);

        /// <summary>穴と数える色の許容差（圧縮も AA も無いので厳しくてよい）。</summary>
        private const int HoleTolerance = 8;

        /// <summary>
        /// 主人公が塗っていると言える最小の画素数。
        ///
        /// 主人公の絵は 1280×720 でおよそ 2000 画素を塗る。200 は「隠れていない」と言うには
        /// 十分小さく、輪郭だけがはみ出た状態を合格にしない程度には大きい。
        /// </summary>
        private const int MinimumHeroPixels = 200;

        private sealed class Route
        {
            internal string Label;
            internal string Folder;
            internal string AreaAScene;
            internal string TrialScene;
            internal StableId ExitFromA;
            internal Key Forward;

            /// <summary>来た方へ戻る向き（即座の逆移動を作るのに要る）。</summary>
            internal Key Backward;

            internal Vector3 BackStep;
        }

        private static Route RouteOf(string key) => key == "NorthSouth"
            ? new Route
            {
                Label = "南北",
                Folder = "NorthSouth",
                AreaAScene = "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_AreaS.unity",
                TrialScene = "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_ConnectionTrial.unity",
                ExitFromA = new StableId("exit_p55_s_north"),
                Forward = Key.W,
                Backward = Key.S,
                BackStep = Vector3.back,
            }
            : new Route
            {
                Label = "東西",
                Folder = "EastWest",
                AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity",
                TrialScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity",
                ExitFromA = new StableId("exit_p55_a_east"),
                Forward = Key.D,
                Backward = Key.A,
                BackStep = Vector3.left,
            };

        private GameObject _bootstrap;
        private Keyboard _keyboard;
        private Route _route;

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            RemoveStrayDevices();
            ClearStatics();
            AreaPendingArrival.ResetDiagnostics();
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            if (_route != null)
            {
                yield return SceneManager.LoadSceneAsync(_route.TrialScene, LoadSceneMode.Single);
                DestroyLaunchers();
                yield return null;
                DestroyLaunchers();
            }

            P55ResidentRig.Reset();

            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            if (_bootstrap != null)
            {
                Object.DestroyImmediate(_bootstrap);
                _bootstrap = null;
            }

            ClearStatics();
            RemoveDevices();
            _route = null;
            yield return null;
        }

        // ---------------------------------------------------------------- P15

        /// <summary>
        /// P15：<b>出発・中間・到着の実描画</b>で、地形がつながっていて、主人公がちょうど 1 つ見えていて、
        /// 全画面が暗転していないこと。16:9 の連番を <c>Recordings/</c> へ残す。
        ///
        /// <b>「黒くない」と「覆えている」は別の話である。</b> 空が映っていれば黒くはないが、
        /// 地形は途切れている。だから覆いは<b>穴の色</b>で見て、暗転は<b>遷移前との比</b>で見る。
        /// </summary>
        [UnityTest]
        public IEnumerator TheSlide_DrawsConnectedGroundWithOneHeroAndNoBlackout(
            [Values("EastWest", "NorthSouth")] string arrangement)
        {
            _route = RouteOf(arrangement);
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return PlaceBeforeExit(_route.ExitFromA, _route.BackStep);
            yield return SettleCamera();

            Camera camera = ResidentCamera();
            string folder = RecordingFolder(_route.Folder);

            // 遷移前の 1 枚。<b>暗転の物差しはここで採る</b>（絶対値ではなく比で見る）。
            //
            // <b>この 1 枚では穴を判定しない。</b> 出入口の手前でカメラは境界へ寄っているので、
            // 隣の Area が<b>まだ読み終わっていなければ</b>境界の向こうは虚空になる
            // （距離による先読みが入る前は東西 295030／南北 48449 画素だった。付録 C.17.4）。
            // §5 の先読みが効けば近づく前に載るが、<b>初回のロード待ちは残る</b>——
            // そこは §7.3 が「背景の補完も設ける」と言っている区間で、裁定 1 の 4 場面には入らない。
            // ここでは明るさの物差しとしてだけ使う。
            Frame before = Capture(camera, folder, "00_before");
            Assert.Greater(before.MeanLuminance, 0.01f,
                "前提：遷移前の画面が真っ暗ではない（平均輝度 " + before.MeanLuminance + "）。");

            var frames = new List<Frame>();
            bool capturedStart = false;
            bool capturedMiddle = false;

            // ---- スライド中：出発・中間・終端を撮る ----
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_route.Forward));
                yield return null;

                AreaCameraRigHost host = AreaCameraRigHost.Instance;
                if (host == null || !host.IsSliding)
                {
                    continue;
                }

                if (!capturedStart)
                {
                    capturedStart = true;
                    frames.Add(CaptureMoment(camera, folder, "01_departure", before));
                }
                else if (!capturedMiddle && host.SlideEased >= 0.5f)
                {
                    capturedMiddle = true;
                    frames.Add(CaptureMoment(camera, folder, "02_middle", before));
                }
                else if (host.SlideEased >= 0.9f)
                {
                    frames.Add(CaptureMoment(camera, folder, "03_arriving", before));
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで渡れている（失敗=" + transitions.Slide.LastFailure + "）。");
            Assert.IsTrue(capturedStart, "出発の 1 枚を撮れている。");
            Assert.IsTrue(capturedMiddle, "中間の 1 枚を撮れている。");
            Assert.GreaterOrEqual(frames.Count, 3,
                "出発・中間・終端の 3 枚以上を撮れている（実際=" + frames.Count + "）。");

            // ---- 到着後：Scene 操作が終端してから 1 枚 ----
            //
            // <b>「Scene が 1 枚に戻る」はもう落ち着いた状態ではない</b>（工程 P55-10c）。
            // 旧 Area は撤去せず非活動のまま預かるので、落ち着いた先は 2 枚である。
            float tail = Time.realtimeSinceStartup + 20f;
            while (transitions.Slide.HasLiveSceneOperation && Time.realtimeSinceStartup < tail)
            {
                yield return null;
            }

            yield return SettleCamera();
            Renderer arrivedHero = AssertExactlyOneHero(camera, sliding: false, label: "到着後");
            Frame arrived = Capture(camera, folder, "04_arrived", arrivedHero);
            // <b>到着後も穴 0 を求める</b>（裁定 1。工程 P55-10d）。
            // 以前はここで「虚空は主人公より手前側にしかない」までしか見ていなかった——
            // 旧 Area を撤去していたので、来た側に帯が出るのが当たり前だったからである
            // （付録 C.17.4）。引き継ぎ（§6.2 手順 11）が入って、帯そのものが出なくなった。
            // 連続フレームの最悪は P16（<c>TheArrival_ShowsNoVoidAcrossTheWholeRoundTrip</c>）が見る。
            AssertDrawing(arrived, before, "到着後");
            AssertHeroIsActuallyDrawn(arrived, "到着後");

            Assert.AreEqual(0, AreaTransitionDisplayHost.Instance.ProxyCount,
                "到着後に表示代理は畳まれている（§7.2）。");

            // ---- まとめ（記録に貼る数字）----
            float worstBrightness = 1f;
            int worstHole = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                worstBrightness = Mathf.Min(worstBrightness, frames[i].MeanLuminance / before.MeanLuminance);
                worstHole = Mathf.Max(worstHole, frames[i].HolePixels);
            }

            Assert.AreEqual(0, worstHole,
                "スライドのどのフレームにも穴が無い（最大 " + worstHole + " 画素）。");
            Assert.Greater(worstBrightness, 0.5f,
                "スライド中の画面が遷移前より暗転していない（最小の明るさ比 " + worstBrightness
                + "。§5「全画面を黒くしない」）。");

            Debug.Log("P15 録画: " + folder + "（" + (frames.Count + 2) + " 枚・"
                + CaptureWidth + "x" + CaptureHeight + "）最小明るさ比=" + worstBrightness);
        }

        // ---------------------------------------------------------------- P16（工程 P55-10d）

        /// <summary>
        /// <b>P16</b>：到着直後も<b>穴 0</b>（§7.3。裁定 1。工程 P55-10d）。
        ///
        /// <b>P15 との違いは「いつ見るか」である。</b> P15 は出発・中間・到着の<b>代表的な数枚</b>を
        /// 撮って録画に残す検査で、到着後の 1 枚は「落ち着いてから」撮っていた。
        /// 裁定 1 は<b>安定後の 1 枚では合格にしない</b>と定める——帯は Commit の前後に出るので、
        /// <b>連続フレームの最悪</b>を見る必要がある。
        ///
        /// 見る場面は 4 つ（§7.3）。
        /// <list type="number">
        /// <item><description>スライド中から Commit 直後への<b>連続した</b>描画。</description></item>
        /// <item><description>到着位置で立ち止まっている間。</description></item>
        /// <item><description>到着後、境界から離れる際の保持・解放の切替。</description></item>
        /// <item><description>即座に逆方向へ戻る場合。</description></item>
        /// </list>
        ///
        /// <b>穴＝描画すべき地形・背景が欠けて未描画の領域が露出すること。</b>
        /// 意図した背景装飾による補完は認める（描かれてはいるので、穴の色にならない）。
        /// <b>暗転や黒い帯で覆って合格にすることは認めない</b>ので、同じフレームで明るさの比も見る。
        ///
        /// <b>録画は残さない。</b> 数百フレームを撮るので PNG は書かない——
        /// 人が見るための録画は P15 が残す。ここは数だけを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator TheArrival_ShowsNoVoidAcrossTheWholeRoundTrip(
            [Values("EastWest", "NorthSouth")] string arrangement)
        {
            _route = RouteOf(arrangement);
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return PlaceBeforeExit(_route.ExitFromA, _route.BackStep);
            yield return SettleCamera();

            Camera camera = ResidentCamera();
            using (var watch = new HoleWatch(camera))
            {
                // 暗転の物差し。<b>絶対値では言えない</b>（照明を変えれば動く）。
                float reference = watch.ObserveLuminance();
                Assert.Greater(reference, 0.01f,
                    "前提：遷移前の画面が真っ暗ではない（平均輝度 " + reference + "）。");

                // ---- 1. スライド中 → Commit 直後（連続） ----
                float deadline = Time.realtimeSinceStartup + 25f;
                while (transitions.SlideCommittedCount == 0 && Time.realtimeSinceStartup < deadline)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_route.Forward));
                    yield return null;

                    AreaCameraRigHost host = AreaCameraRigHost.Instance;
                    if (host != null && host.IsSliding)
                    {
                        watch.Observe("スライド中", reference);
                    }
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                Assert.AreEqual(1, transitions.SlideCommittedCount,
                    "前提：スライドで渡れている（失敗=" + transitions.Slide.LastFailure + "）。");
                Assert.Greater(watch.Observations, 3, "スライド中を複数フレーム見ている。");

                // <b>ここが裁定 1 の本題。</b> 以前はこの区間で旧 Area を撤去していたので、
                // 撤去した側に虚空の帯が出ていた（付録 C.17.4。東西 190555／南北 56320 画素）。
                for (int i = 0; i < 20; i++)
                {
                    yield return null;
                    watch.Observe("Commit 直後", reference);
                }

                // ---- 2. 到着位置で立ち止まっている間 ----
                yield return SettleCamera();
                for (int i = 0; i < 20; i++)
                {
                    yield return null;
                    watch.Observe("到着位置で停止中", reference);
                }

                // ---- 3. 境界から離れる（保持・解放の切替）----
                //
                // §5 は「一度読み始めた先は、同じ Area で活動している間は境界から離れても保持する」と
                // 定める。離れても来た方が載ったままであることを、<b>絵で</b>確かめる。
                for (int i = 0; i < 60; i++)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_route.Forward));
                    yield return null;
                    watch.Observe("境界から離れる", reference);
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;

                // ---- 4. 即座に逆方向へ戻る ----
                float back = Time.realtimeSinceStartup + 30f;
                while (transitions.SlideCommittedCount < 2 && Time.realtimeSinceStartup < back)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_route.Backward));
                    yield return null;
                    watch.Observe("逆方向へ戻る", reference);
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                Assert.AreEqual(2, transitions.SlideCommittedCount,
                    "前提：逆方向へも渡れている（失敗=" + transitions.Slide.LastFailure + "）。");

                for (int i = 0; i < 20; i++)
                {
                    yield return null;
                    watch.Observe("戻った直後", reference);
                }

                // ---- 判定 ----
                Assert.AreEqual(0, watch.WorstHolePixels,
                    "どのフレームにも穴が無い（最悪 " + watch.WorstHolePixels + " 画素／"
                    + (CaptureWidth * CaptureHeight) + " 中・場面「" + watch.WorstHoleLabel
                    + "」・画面 x " + watch.WorstHoleFrame.HoleMinX + "〜" + watch.WorstHoleFrame.HoleMaxX
                    + " y " + watch.WorstHoleFrame.HoleMinY + "〜" + watch.WorstHoleFrame.HoleMaxY
                    + "）。見たフレーム数=" + watch.Observations + "（§7.3。裁定 1）。");

                Assert.Greater(watch.WorstLuminanceRatio, 0.5f,
                    "どのフレームも遷移前より暗転していない（最小の明るさ比 "
                    + watch.WorstLuminanceRatio + "・場面「" + watch.WorstLuminanceLabel
                    + "」）。<b>黒く覆って穴を消す</b>のは認めない（§7.3）。");

                Debug.Log("P16 " + _route.Label + "：" + watch.Observations
                    + " フレームを見て穴 0・最小明るさ比 " + watch.WorstLuminanceRatio);
            }
        }

        /// <summary>
        /// 連続フレームの穴と明るさを見る道具（工程 P55-10d）。
        ///
        /// <b>録画用の <see cref="Capture"/> とは別にする。</b> あちらは 1 枚ごとに PNG を書き、
        /// 描画も 3 回（出荷時・判定用・主人公抜き）行う。数百フレームを見るここでは重すぎる。
        /// 使い回す RenderTexture を 1 つ持ち、<b>穴の数え方は同じ助けを通す</b>——
        /// 穴の定義が 2 か所に散ると、片方だけが直る。
        /// </summary>
        private sealed class HoleWatch : System.IDisposable
        {
            private readonly Camera _camera;
            private readonly RenderTexture _rt;
            private readonly Texture2D _buffer;

            internal HoleWatch(Camera camera)
            {
                _camera = camera;
                _rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
                _buffer = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
            }

            /// <summary>見たフレーム数。</summary>
            internal int Observations { get; private set; }

            /// <summary>いちばん穴の大きかったフレームの画素数。</summary>
            internal int WorstHolePixels { get; private set; }

            /// <summary>そのフレームの場面名。</summary>
            internal string WorstHoleLabel { get; private set; } = string.Empty;

            /// <summary>そのフレームの中身（穴の位置を言うため）。</summary>
            internal Frame WorstHoleFrame { get; private set; }

            /// <summary>いちばん暗かったフレームの明るさ比。</summary>
            internal float WorstLuminanceRatio { get; private set; } = float.MaxValue;

            /// <summary>そのフレームの場面名。</summary>
            internal string WorstLuminanceLabel { get; private set; } = string.Empty;

            /// <summary>いまの画面の平均輝度（出荷時の設定で 1 枚描く）。</summary>
            internal float ObserveLuminance() => MeanLuminance(Shoot(hole: false));

            /// <summary>このフレームの穴と明るさを見る。</summary>
            internal void Observe(string label, float referenceLuminance)
            {
                Observations++;

                Frame frame = MeasureHoles(label, Shoot(hole: true), 0f);
                if (frame.HolePixels > WorstHolePixels)
                {
                    WorstHolePixels = frame.HolePixels;
                    WorstHoleLabel = label;
                    WorstHoleFrame = frame;
                }

                float ratio = MeanLuminance(Shoot(hole: false)) / referenceLuminance;
                if (ratio < WorstLuminanceRatio)
                {
                    WorstLuminanceRatio = ratio;
                    WorstLuminanceLabel = label;
                }
            }

            /// <summary>1 枚描いて読み取る。<paramref name="hole"/> なら背景を世界に無い色にする。</summary>
            private Texture2D Shoot(bool hole)
            {
                CameraClearFlags flags = _camera.clearFlags;
                Color background = _camera.backgroundColor;
                RenderTexture previous = _camera.targetTexture;
                RenderTexture active = RenderTexture.active;

                try
                {
                    _camera.targetTexture = _rt;
                    if (hole)
                    {
                        _camera.clearFlags = CameraClearFlags.SolidColor;
                        _camera.backgroundColor = HoleColor;
                    }

                    _camera.Render();
                    RenderTexture.active = _rt;
                    _buffer.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                    _buffer.Apply();
                }
                finally
                {
                    _camera.clearFlags = flags;
                    _camera.backgroundColor = background;
                    _camera.targetTexture = previous;
                    RenderTexture.active = active;
                }

                return _buffer;
            }

            public void Dispose()
            {
                _rt.Release();
                Object.DestroyImmediate(_rt);
                Object.DestroyImmediate(_buffer);
            }
        }

        // ---------------------------------------------------------------- 撮る

        /// <summary>1 フレームぶんの事実（判定用の数字と、録画に残した枚）。</summary>
        private readonly struct Frame
        {
            internal Frame(string name, int holePixels, float meanLuminance, float holeMinX,
                float holeMaxX, float holeMinY, float holeMaxY, int heroPixels = 0)
            {
                HeroPixels = heroPixels;
                Name = name;
                HolePixels = holePixels;
                MeanLuminance = meanLuminance;
                HoleMinX = holeMinX;
                HoleMaxX = holeMaxX;
                HoleMinY = holeMinY;
                HoleMaxY = holeMaxY;
            }

            internal string Name { get; }

            /// <summary>穴の色だった画素数（0 でなければ地形が覆えていない）。</summary>
            internal int HolePixels { get; }

            /// <summary>出荷時の設定で描いた 1 枚の平均輝度。</summary>
            internal float MeanLuminance { get; }

            /// <summary>穴の広がり（画面座標 0〜1。穴が無ければ意味を持たない）。</summary>
            internal float HoleMinX { get; }

            /// <summary>同・右端。</summary>
            internal float HoleMaxX { get; }

            /// <summary>同・下端。</summary>
            internal float HoleMinY { get; }

            /// <summary>同・上端。</summary>
            internal float HoleMaxY { get; }

            /// <summary>
            /// 主人公の絵が<b>実際に塗った</b>画素数。
            ///
            /// Renderer が有効でも、手前の物に隠れていれば 1 画素も塗らない。
            /// 「居る」と「見えている」は別である（§11 の P15）。
            /// </summary>
            internal int HeroPixels { get; }

            internal Frame WithHero(int heroPixels) => new Frame(
                Name, HolePixels, MeanLuminance, HoleMinX, HoleMaxX, HoleMinY, HoleMaxY, heroPixels);
        }

        /// <summary>スライド中の 1 瞬を撮り、その場で判定まで済ませる。</summary>
        private static Frame CaptureMoment(Camera camera, string folder, string name, Frame before)
        {
            Renderer hero = AssertExactlyOneHero(camera, sliding: true, label: name);
            Frame frame = Capture(camera, folder, name, hero);
            AssertDrawing(frame, before, name);
            AssertHeroIsActuallyDrawn(frame, name);
            return frame;
        }

        /// <summary>
        /// 主人公の絵が<b>実際に画面を塗っている</b>こと。
        ///
        /// Renderer が有効で、位置が画面の中にあっても、手前に物があれば 1 画素も塗らない。
        /// 「存在・表示判定」だけでは通ってしまうところで、<b>録画を見て初めて気付いた</b>——
        /// 南北配置では出入口の目印（高さ 1.6m の板）が主人公を隠していた（記録 027）。
        /// </summary>
        private static void AssertHeroIsActuallyDrawn(Frame frame, string label)
        {
            Assert.Greater(frame.HeroPixels, MinimumHeroPixels,
                label + "：主人公の絵が画面を塗っていない（塗った画素 " + frame.HeroPixels
                + "／下限 " + MinimumHeroPixels
                + "）。Renderer は有効でも手前の物に隠れている（§11 の P15）。");
        }

        /// <summary>
        /// 16:9 で 2 枚描く。<b>1 枚は出荷時の設定（録画用）、もう 1 枚は背景をマゼンタ（判定用）。</b>
        ///
        /// 同じフレームの中で 2 回描くので、2 枚は同じ瞬間を指す。
        /// 録画を判定用の色で撮ると、人が見る絵が本番と違ってしまう。
        /// </summary>
        private static Frame Capture(Camera camera, string folder, string name, Renderer hero = null)
        {
            CameraClearFlags flags = camera.clearFlags;
            Color background = camera.backgroundColor;
            RenderTexture previous = camera.targetTexture;
            RenderTexture active = RenderTexture.active;

            var rt = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            var shot = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
            var probe = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
            var without = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGBA32, false);
            bool hasWithout = false;

            try
            {
                camera.targetTexture = rt;

                // 1 枚目：出荷時の設定のまま（録画へ残す）。
                camera.Render();
                RenderTexture.active = rt;
                shot.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                shot.Apply();

                // 2 枚目：背景だけ世界に無い色へ（穴の判定用）。
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = HoleColor;
                camera.Render();
                RenderTexture.active = rt;
                probe.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                probe.Apply();

                // 3 枚目：主人公の絵だけを消して描く。1 枚目との差が<b>主人公が塗った画素</b>である。
                // Renderer が有効でも手前の物に隠れていれば差は 0 になる——そこを見たい。
                if (hero != null)
                {
                    camera.clearFlags = flags;
                    camera.backgroundColor = background;
                    bool wasEnabled = hero.enabled;
                    hero.enabled = false;
                    camera.Render();
                    hero.enabled = wasEnabled;
                    RenderTexture.active = rt;
                    without.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                    without.Apply();
                    hasWithout = true;
                }
            }
            finally
            {
                camera.clearFlags = flags;
                camera.backgroundColor = background;
                camera.targetTexture = previous;
                RenderTexture.active = active;
                rt.Release();
                Object.DestroyImmediate(rt);
            }

            float luminance = MeanLuminance(shot);

            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), shot.EncodeToPNG());

            Frame frame = MeasureHoles(name, probe, luminance);
            if (hasWithout)
            {
                frame = frame.WithHero(CountDifferingPixels(shot, without));
            }

            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(probe);
            Object.DestroyImmediate(without);
            return frame;
        }

        /// <summary>2 枚の違う画素の数（主人公が塗った面積）。</summary>
        private static int CountDifferingPixels(Texture2D a, Texture2D b)
        {
            Color32[] left = a.GetPixels32();
            Color32[] right = b.GetPixels32();
            int count = 0;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i].r != right[i].r || left[i].g != right[i].g || left[i].b != right[i].b)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 穴の<b>数と広がり</b>を採る。
        ///
        /// 数だけでは「どこが空いているか」を言えない。到着直後は来た方向に虚空が出るので、
        /// 「進む先には無い」と言うには位置が要る（記録 019 の「数えるだけでは見えているかを言えない」）。
        /// </summary>
        private static Frame MeasureHoles(string name, Texture2D probe, float luminance)
        {
            Color32[] pixels = probe.GetPixels32();
            int count = 0;
            int minX = CaptureWidth;
            int maxX = -1;
            int minY = CaptureHeight;
            int maxY = -1;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (Mathf.Abs(p.r - HoleColor.r) > HoleTolerance
                    || Mathf.Abs(p.g - HoleColor.g) > HoleTolerance
                    || Mathf.Abs(p.b - HoleColor.b) > HoleTolerance)
                {
                    continue;
                }

                count++;
                int x = i % CaptureWidth;
                int y = i / CaptureWidth;
                if (x < minX) { minX = x; }
                if (x > maxX) { maxX = x; }
                if (y < minY) { minY = y; }
                if (y > maxY) { maxY = y; }
            }

            return count == 0
                ? new Frame(name, 0, luminance, 0f, 0f, 0f, 0f)
                : new Frame(name, count, luminance,
                    minX / (float)CaptureWidth, (maxX + 1) / (float)CaptureWidth,
                    minY / (float)CaptureHeight, (maxY + 1) / (float)CaptureHeight);
        }

        private static float MeanLuminance(Texture2D shot)
        {
            Color32[] pixels = shot.GetPixels32();
            double total = 0d;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                total += (0.2126d * p.r + 0.7152d * p.g + 0.0722d * p.b) / 255d;
            }

            return (float)(total / pixels.Length);
        }

        // ---------------------------------------------------------------- 判定

        /// <summary>地形が覆えていて、全画面が暗転していないこと（スライド中に使う）。</summary>
        private static void AssertDrawing(Frame frame, Frame before, string label)
        {
            Assert.AreEqual(0, frame.HolePixels,
                label + "：地形が画面を覆えていない（穴 " + frame.HolePixels + " 画素／"
                + (CaptureWidth * CaptureHeight) + " 中・画面 x " + frame.HoleMinX + "〜"
                + frame.HoleMaxX + " y " + frame.HoleMinY + "〜" + frame.HoleMaxY
                + "）。境界に黒い帯が出る（§7.3）。");

            AssertNotBlacked(frame, before, label);
        }

        /// <summary>全画面が暗転していないこと。</summary>
        private static void AssertNotBlacked(Frame frame, Frame before, string label)
        {
            float ratio = frame.MeanLuminance / before.MeanLuminance;
            Assert.Greater(ratio, 0.5f,
                label + "：画面が遷移前より暗い（明るさ比 " + ratio
                + "／遷移前 " + before.MeanLuminance + " いま " + frame.MeanLuminance
                + "）。通常の Slide で全画面を黒くしない（§5）。");
        }

        /// <summary>
        /// 主人公の絵が<b>ちょうど 1 つ</b>、画面の中に出ていること。
        ///
        /// スライド中は<b>表示代理</b>が描かれ、実 Actor は隠れている（§6.2 手順 6）。
        /// 到着後はその逆。どちらの瞬間も「2 つ見える」「0 個になる」が起きてはいけない——
        /// 前者は主人公が 2 人居るように見え、後者は主人公が消える。
        /// </summary>
        private static Renderer AssertExactlyOneHero(Camera camera, bool sliding, string label)
        {
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, label + "：主人公の根がある。");

            bool realVisible = TryFindVisibleSprite(
                player.transform, out SpriteRenderer realRenderer, out string realName);
            AreaTransitionDisplayHost display = AreaTransitionDisplayHost.Instance;
            AreaTransitionDisplayProxy proxy = display != null && display.Set != null
                ? display.Set.Player : null;
            bool proxyVisible = proxy != null && proxy.Renderer != null
                && proxy.Renderer.enabled && proxy.Renderer.sprite != null
                && proxy.gameObject.activeInHierarchy;

            int visible = (realVisible ? 1 : 0) + (proxyVisible ? 1 : 0);
            Assert.AreEqual(1, visible,
                label + "：主人公の絵がちょうど 1 つでない（実 Actor=" + realVisible
                + "『" + realName + "』／表示代理=" + proxyVisible
                + "）。重複も消失も受け入れない（§11 の P15）。");

            if (sliding)
            {
                Assert.IsTrue(proxyVisible,
                    label + "：スライド中に見えているのは表示代理である（§6.2 手順 6）。");
                Assert.IsTrue(OnScreen(camera, proxy.transform.position),
                    label + "：表示代理が画面の中に居る（位置="
                    + proxy.transform.position + "）。");
                return proxy.Renderer;
            }

            Assert.IsTrue(realVisible, label + "：到着後は実 Actor が見えている。");
            Assert.IsTrue(OnScreen(camera, player.transform.position),
                label + "：主人公が画面の中に居る（位置=" + player.transform.position + "）。");
            return realRenderer;
        }

        private static bool OnScreen(Camera camera, Vector3 worldPosition)
        {
            Vector3 v = camera.WorldToViewportPoint(worldPosition);
            return v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        private static bool TryFindVisibleSprite(
            Transform actorRoot, out SpriteRenderer found, out string rendererName)
        {
            foreach (SpriteRenderer r in actorRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.sprite != null)
                {
                    found = r;
                    rendererName = r.name + "(order=" + r.sortingOrder + ")";
                    return true;
                }
            }

            found = null;
            rendererName = string.Empty;
            return false;
        }

        // ---------------------------------------------------------------- 補助

        /// <summary>録画の置き場（<c>Recordings/</c> は .gitignore 済み）。</summary>
        private static string RecordingFolder(string label)
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(root, "Recordings", "P55-06b", label);
        }

        private static Camera ResidentCamera()
        {
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");
            Assert.IsNotNull(host.Camera, "常駐 Rig が Camera を持っている。");
            return host.Camera;
        }

        private static IEnumerator SettleCamera()
        {
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");

            float deadline = Time.realtimeSinceStartup + 3f;
            while (host.Rig.Blend.IsBlending && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsFalse(host.Rig.Blend.IsBlending, "前提：補間が終わっている。");
        }

        private IEnumerator EnterArea(string scenePath)
        {
            AssertSceneRegistered(scenePath);

            DestroyLaunchers();
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55RenderCaptureTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐が立つ。理由=" + BootstrapRoot.Instance.BootstrapFailure);
            DestroyLaunchers();

            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            yield return null;

            AreaInitializer initializer = Object.FindFirstObjectByType<AreaInitializer>();
            Assert.IsNotNull(initializer, "初期化担当が居る。");
            Assert.IsTrue(initializer.Initialized, "初期化が成立する。理由=" + initializer.FailureReason);

            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        private IEnumerator PlaceBeforeExit(StableId exitId, Vector3 back)
        {
            AreaExitGate gate = FindExitGate(exitId);
            Vector3 spot = gate.transform.position + back * 0.4f;
            var root = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(root, "主人公の根がある。");
            root.transform.position = new Vector3(spot.x, root.transform.position.y, spot.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(gate.IsWired, "出入口に主人公の根が配線されている。");
            Assert.IsTrue(gate.PlayerInside, "範囲内に居る。");
        }

        private static AreaExitGate FindExitGate(StableId exitId)
        {
            foreach (AreaExitGate gate in
                Object.FindObjectsByType<AreaExitGate>(FindObjectsSortMode.None))
            {
                if (gate != null && gate.ExitId.Equals(exitId))
                {
                    return gate;
                }
            }

            Assert.Fail("出入口 '" + exitId.Value + "' が Scene にありません。");
            return null;
        }

        private static AreaTransitionService Transitions()
        {
            AreaTransitionService service = BootstrapServices.Get<AreaTransitionService>();
            Assert.IsNotNull(service, "遷移サービスが常駐していません。");
            return service;
        }

        private static void AssertSceneRegistered(string scenePath)
        {
            foreach (UnityEditor.EditorBuildSettingsScene s in UnityEditor.EditorBuildSettings.scenes)
            {
                if (s.path == scenePath)
                {
                    return;
                }
            }

            Assert.Fail("Scene が Build Settings へ未登録です: " + scenePath);
        }

        private static void DestroyLaunchers()
        {
            foreach (Phase5TrialLauncher launcher in
                Object.FindObjectsByType<Phase5TrialLauncher>(FindObjectsSortMode.None))
            {
                if (launcher != null)
                {
                    Object.DestroyImmediate(launcher.gameObject);
                }
            }
        }

        private static void ClearStatics()
        {
            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            AreaPendingArrival.Clear();
        }

        private void RemoveDevices()
        {
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            RemoveStrayDevices();
        }

        private static void RemoveStrayDevices()
        {
            for (int i = InputSystem.devices.Count - 1; i >= 0; i--)
            {
                InputDevice device = InputSystem.devices[i];
                if (device != null && device.name != null && device.name.StartsWith("P55Keyboard"))
                {
                    InputSystem.RemoveDevice(device);
                }
            }
        }
    }
}
