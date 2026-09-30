using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
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
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P5.5 の実ワールド配置で<b>実入力から接続が解決される</b>（仕様書 §3.1／§6.1。工程 P55-03d-1）。
    ///
    /// P5 の出入口は「行き先の Area と入口」を直接指していた。P5.5 では出入口に <c>ExitId</c> を持たせ、
    /// <b>接続レコード</b>（見せ方・向き・所要秒・到着入口）を引いてから要求する。
    /// これが無いと「東へスライドする」という情報がどこにも無く、演出を書く足場ができない。
    ///
    /// <b>Gate も Driver も完了通知もテストから直接叩かない</b>（§11 P01／P02 の書き方）。
    /// 主人公を出入口の手前へ置いて<b>実キーを押し続ける</b>だけで、あとは出荷物の配線に任せる。
    /// <b>スライド演出そのものはまだ無い</b>（P55-04）。ここで見るのは接続の解決と受理である。
    /// </summary>
    public sealed class P55ConnectionInputPlayTests
    {
        private const string P55AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity";
        private const string P55AreaBScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaB.unity";
        private const string P55TrialScene =
            "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity";
        private const string P5AreaAScene = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity";

        private static readonly StableId ExitAEast = new StableId("exit_p55_a_east");
        private static readonly StableId ExitBWest = new StableId("exit_p55_b_west");
        private static readonly StableId ConnectionAToB = new StableId("conn_p55_a_east_to_b");
        private static readonly StableId ConnectionBToA = new StableId("conn_p55_b_west_to_a");

        /// <summary>接続軸の Z（<c>Phase55WorldLayout.SeamZ</c>。Editor 側の定数は参照しない）。</summary>
        private const float SeamAxisZ = 0f;

        private GameObject _bootstrap;
        private Keyboard _keyboard;

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            RemoveStrayDevices();

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

            yield return SceneManager.LoadSceneAsync(P55TrialScene, LoadSceneMode.Single);
            DestroyLaunchers();
            yield return null;
            DestroyLaunchers();

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
            RemoveDevices();
            yield return null;
        }

        // ---------------------------------------------------------------- 東へ

        /// <summary>
        /// A の東の出入口へ<b>実キーで</b>歩くと、東向きの接続が解決されて受理される（§3.1）。
        ///
        /// 見るのは要求が通ったことだけではない。<b>どの接続が固定されたか</b>——
        /// 接続 ID・出入口 ID・見せ方（Slide）・向き（East）・到着入口——を全部見る。
        /// 「遷移が起きた」だけでは、行き先を直接指す従来の経路と区別できない。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingEastIntoTheSeam_ResolvesTheEastwardSlideConnection()
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            Assert.IsNotNull(transitions.Connections, "A が接続一覧を渡している（§3.1）。");
            Assert.AreEqual(0, transitions.ConnectionTravelCount, "前提：まだ接続で受理していない。");

            AreaExitGate gate = FindExitGate(ExitAEast);
            yield return StandJustBefore(gate, Vector3.left);

            yield return HoldUntil(Key.D,
                () => transitions.ConnectionTravelCount > 0, 8f);

            Assert.AreEqual(1, transitions.ConnectionTravelCount,
                "実キーの連続入力で接続が解決され、受理された（Gate も Driver も直接叩いていない）。");

            AreaConnectionSnapshot used = transitions.LastAcceptedConnection;
            Assert.AreEqual(ConnectionAToB.Value, used.ConnectionId.Value, "東向きの接続が選ばれる。");
            Assert.AreEqual(ExitAEast.Value, used.ExitId.Value, "引いた鍵はこの出入口の ID。");
            Assert.AreEqual(AreaTransitionStyle.Slide, used.Style, "見せ方は Slide（§3.1）。");
            Assert.AreEqual(AreaConnectionDirection.East, used.Direction, "向きは East。");
            Assert.AreEqual("area_p55_b", used.ToAreaId.Value, "行き先は P5.5 の B。");
            Assert.AreEqual("area_p5_b_from_a", used.EntryId.Value, "到着入口は P5 のものを再利用（§3.2）。");
            Assert.Greater(used.SlideDuration, 0f, "所要秒が固定されている。");

            // 到着まで通る（スライド演出そのものは P55SlideTransitionPlayTests が見る）。
            yield return WaitForArrival(transitions, 1);
            // 「Scene に居る AreaRoot」では言えない——旧 Area を保持するので両方の部品が載っている
            // （工程 P55-10c）。「どこに居るか」は<b>活動中の指定</b>で言う。
            Assert.AreEqual("area_p55_b", CurrentAreaProvider.Current.AreaId.Value, "B に居る。");
        }

        // ---------------------------------------------------------------- 西へ

        /// <summary>
        /// B の西の出入口へ実キーで歩くと、西向きの接続が解決される（§3.1）。
        ///
        /// 往復を<b>1 レコードで使い回していない</b>ことがここで効く——向きが West、
        /// 接続 ID が逆方向のもので、東向きのレコードと対になっている。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingWestIntoTheSeam_ResolvesTheWestwardSlideConnection()
        {
            yield return EnterArea(P55AreaBScene);

            AreaTransitionService transitions = Transitions();
            Assert.IsNotNull(transitions.Connections, "B も接続一覧を渡している。");

            AreaExitGate gate = FindExitGate(ExitBWest);
            yield return StandJustBefore(gate, Vector3.right);

            yield return HoldUntil(Key.A,
                () => transitions.ConnectionTravelCount > 0, 8f);

            Assert.AreEqual(1, transitions.ConnectionTravelCount, "西へも実キーで受理される。");

            AreaConnectionSnapshot used = transitions.LastAcceptedConnection;
            Assert.AreEqual(ConnectionBToA.Value, used.ConnectionId.Value, "西向きの接続が選ばれる。");
            Assert.AreEqual(ExitBWest.Value, used.ExitId.Value);
            Assert.AreEqual(AreaConnectionDirection.West, used.Direction, "向きは West。");
            Assert.AreEqual(ConnectionAToB.Value, used.ReverseConnectionId.Value,
                "東向きのレコードと対になっている（§3.1。1 レコードを使い回さない）。");
            Assert.AreEqual("area_p55_a", used.ToAreaId.Value);

            yield return WaitForArrival(transitions, 1);
            Assert.AreEqual("area_p55_a", CurrentAreaProvider.Current.AreaId.Value, "A に居る。");
        }

        // ---------------------------------------------------------------- P5 の互換

        /// <summary>
        /// <b>P5 の Area は従来どおり</b>（§3.1「既存未指定は Fade にして既存 Data の挙動を保持」）。
        ///
        /// 接続を持たない構成で接続経路へ落ちると、<c>ExitId</c> が空なので引けず、
        /// 黙って移動できなくなる。ここが守られていないと、P5 の試遊が一斉に壊れる。
        /// </summary>
        [UnityTest]
        public IEnumerator TheP5Area_StillTravelsWithoutAnyConnectionRecord()
        {
            yield return EnterArea(P5AreaAScene);

            AreaTransitionService transitions = Transitions();
            Assert.IsNull(transitions.Connections, "P5 の Area は接続一覧を持たない。");

            AreaExitGate gate = FindAnyExitGate();
            Assert.IsFalse(gate.ExitId.IsValid, "P5 の出入口は ExitId を持たない（空でよい）。");

            yield return StandJustBefore(gate, Vector3.left);
            yield return HoldUntil(Key.D, () => transitions.ArrivalCount > 0, 8f);

            Assert.AreEqual(0, transitions.ConnectionTravelCount, "接続経路は通らない。");
            Assert.AreEqual(1, transitions.CompletedCount, "従来の Single 経路で着く（Slide ではない）。");
            yield return WaitForArrival(transitions, 1);
            Assert.AreEqual("area_p5_b", FindAreaRoot().AreaId.Value, "従来どおり B へ着く。");
        }

        // ---------------------------------------------------------------- カメラ軸（§7.1）

        /// <summary>
        /// <b>通路の端から入ると、カメラは主人公について接続軸の外へ出る。ただし帯の内側に留まる</b>
        /// （§7.1 改定。工程 P55-14d。GPT 裁定 (a)）。
        ///
        /// <b>この検査は契約ごと書き換わった。</b> 以前は「近づく間ずっと軸に乗っている」を要求し、
        /// それは<b>継ぎ目領域がカメラを軸へピン留めしていた</b>から成り立っていた。
        /// 裁定 1 で同一 Area 内の領域を 1 つにしたので、
        /// エリア内のカメラは<b>主人公へ連続追従する</b>——通路の端から入れば軸から外れる。
        ///
        /// <b>「軸から外れてよい」だけでは受入にならない。</b> 見るのは 3 つ：
        /// <list type="number">
        /// <item><b>端から入っても遷移が成立する</b>。ここが試遊で壊れる場所である——
        /// 軸外を許さない実装では、通路の端を歩いてきたプレイヤーは隣の Area へ行けない。</item>
        /// <item>近づく間、カメラが<b>帯（通路の幅）の内側</b>に留まる。
        /// 帯は接続 Data の <c>CorridorWidth</c> から求め、<b>実行時・Scene 検査と同じ関数</b>を通す。</item>
        /// <item><b>実際に軸外へ出ている</b>（誤差許容より大きい）。
        /// これを書かないと、こっそり軸へピン留めし直した実装でも緑になる——
        /// 「連続追従している」ことを、この検査自身が言えなくなる。</item>
        /// </list>
        ///
        /// 到着後は入口が軸上なので、カメラは軸へ戻る（裁定 5：到着位置は入口のまま）。
        /// </summary>
        [UnityTest]
        public IEnumerator EnteringTheSeamOffCentre_KeepsTheCameraInsideTheConnectionBand(
            [Values(1f, -1f)] float offsetZ)
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            float corridorWidth = CorridorWidthOf(transitions, ConnectionAToB);
            float band = AreaConnectionRules.SlideAcrossHalfWidth(corridorWidth);
            Assert.Greater(band, 0f,
                "前提：接続 Data が通路幅を持っている（幅=" + corridorWidth + "）。");

            AreaExitGate gate = FindExitGate(ExitAEast);
            yield return StandJustBefore(gate, Vector3.left, offsetZ);

            // 置き直した直後の補間を終わらせてから見る（配置の話をしたいので）。
            yield return SettleCamera();

            AreaCameraRig rig = AreaCameraRigHost.Instance.Rig;
            float worst = 0f;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (transitions.ConnectionTravelCount == 0 && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                yield return null;
                worst = Mathf.Max(worst, Mathf.Abs(rig.transform.position.z - SeamAxisZ));
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            Assert.AreEqual(1, transitions.ConnectionTravelCount,
                "<b>通路の端から入っても受理される</b>（進入位置 z ずらし=" + offsetZ
                + "）。軸外を許さない実装では、ここで受理されない。失敗="
                + transitions.Slide.LastFailure);
            Assert.LessOrEqual(worst, band + AreaConnectionRules.AxisEpsilon,
                "<b>カメラが帯の内側に留まっている</b>（最大のずれ=" + worst + "／"
                + AreaConnectionRules.DescribeSlideBand(corridorWidth) + "）。§7.1 改定。");
            Assert.Greater(worst, AreaConnectionRules.AxisEpsilon,
                "<b>実際に軸外へ出ている</b>（最大のずれ=" + worst
                + "／誤差許容 " + AreaConnectionRules.AxisEpsilon
                + "）。出ていないなら、どこかがカメラを軸へ留めている——"
                + "エリア内の連続追従（裁定 1）が効いていない。");

            yield return WaitForArrival(transitions, 1);
            yield return null;

            AreaCameraRig arrived = AreaCameraRigHost.Instance.Rig;
            Assert.Less(Mathf.Abs(arrived.transform.position.z - SeamAxisZ), 0.1f,
                "到着後は接続軸の上に居る（z=" + arrived.transform.position.z
                + "）。到着位置は入口のままなので（裁定 5）。");
        }

        /// <summary>
        /// <b>通路の中央・両端のどこから入っても、スライドは帯の内側で成立する</b>
        /// （§7.1 改定。工程 P55-14d。GPT 受入表の 1 行目）。
        ///
        /// 上の検査が「近づく間のカメラ」を見るのに対し、こちらは
        /// <b>スライドそのものの始点・終点</b>を見る——実行時の帯の検査が通っていること、
        /// 軸方向へちゃんと動いていること、軸外成分が帯に収まっていることを、実測値で言う。
        ///
        /// <b>値を assert のメッセージへ埋める。</b> スライドの斜めがどれだけかは
        /// 再試遊の判断材料になるので、失敗したときに数が読めるようにしておく。
        /// </summary>
        [UnityTest]
        public IEnumerator FromTheCorridorCentreAndEdges_TheSlideStaysInsideTheBand(
            [Values(0f, 1.5f, -1.5f)] float offsetZ)
        {
            yield return EnterArea(P55AreaAScene);

            AreaTransitionService transitions = Transitions();
            float corridorWidth = CorridorWidthOf(transitions, ConnectionAToB);
            float band = AreaConnectionRules.SlideAcrossHalfWidth(corridorWidth);

            AreaExitGate gate = FindExitGate(ExitAEast);
            yield return StandJustBefore(gate, Vector3.left, offsetZ);
            yield return SettleCamera();

            yield return HoldUntil(Key.D, () => transitions.SlideCommittedCount > 0, 12f);

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "<b>スライドが成立した</b>（進入 z ずらし=" + offsetZ + "）。失敗="
                + transitions.Slide.LastFailure);
            Assert.AreEqual(0, transitions.Slide.RolledBackCount,
                "戻していない（帯の検査で弾かれていない）。失敗=" + transitions.Slide.LastFailure);

            Vector3 from = transitions.Slide.LastSlideFrom;
            Vector3 to = transitions.Slide.LastSlideTo;
            float along = Mathf.Abs(to.x - from.x);
            float acrossFrom = Mathf.Abs(from.z - SeamAxisZ);
            float acrossTo = Mathf.Abs(to.z - SeamAxisZ);

            Assert.LessOrEqual(acrossFrom, band + AreaConnectionRules.AxisEpsilon,
                "<b>始点が帯の内側</b>（軸外 " + acrossFrom + "／"
                + AreaConnectionRules.DescribeSlideBand(corridorWidth)
                + "）。始点=" + from + " 終点=" + to + "。");
            Assert.LessOrEqual(acrossTo, AreaConnectionRules.AxisEpsilon,
                "<b>終点は接続軸の上</b>（軸外 " + acrossTo + "／誤差許容 "
                + AreaConnectionRules.AxisEpsilon + "）。到着位置は入口のまま（裁定 5）。"
                + " 始点=" + from + " 終点=" + to + "。");
            Assert.GreaterOrEqual(along, AreaConnectionRules.MinAlongMovement,
                "<b>接続方向へ動いている</b>（x の移動 " + along + "／最低 "
                + AreaConnectionRules.MinAlongMovement + "）。始点=" + from + " 終点=" + to + "。");
        }

        /// <summary>
        /// その接続の通路幅（カメラの帯の正本。§7.1）。
        ///
        /// <b>受理側と同じ台帳から引く。</b> Data の Asset を直接読むと、
        /// 実行時が見ている値と別のものを期待値にしてしまう。
        /// </summary>
        private static float CorridorWidthOf(AreaTransitionService transitions, StableId connectionId)
        {
            Assert.IsNotNull(transitions.Connections, "接続の台帳が差さっている。");
            Assert.IsTrue(transitions.Connections.TryGet(connectionId, out AreaConnectionSnapshot c),
                "接続 '" + connectionId.Value + "' が台帳にあります。");
            return c.CorridorWidth;
        }

        /// <summary>置き直した直後の補間を終わらせる（配置の検査に補間の残りを混ぜない）。</summary>
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

        // ---------------------------------------------------------------- 補助

        /// <summary>常駐を立てて Area を直開きする。</summary>
        private IEnumerator EnterArea(string scenePath)
        {
            AssertSceneRegistered(scenePath);

            DestroyLaunchers();
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55ConnectionTest");
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

        /// <summary>
        /// 出入口の範囲内へ立つ（<paramref name="back"/> は出口と逆向きの少しだけ手前）。
        ///
        /// <b>外壁へめり込ませない</b>ために出口側ではなく手前へ寄せる（既存 P5 検査と同じ置き方）。
        /// ここから先はテストは何も操作しない——§6.1 の「範囲内で出口方向へ連続入力」を
        /// 実キーだけで成立させる。
        /// </summary>
        private IEnumerator StandJustBefore(AreaExitGate gate, Vector3 back, float offsetZ = 0f)
        {
            Vector3 spot = gate.transform.position + back * 0.4f + new Vector3(0f, 0f, offsetZ);
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

            Assert.IsTrue(gate.IsWired,
                "出入口に主人公の根が配線されている（無いと Trigger を無視して永久に反応しない）。");
            Assert.IsTrue(gate.PlayerInside,
                "範囲内に居る（SetPlayerInside を外から呼ばずに成立する）。");
        }

        /// <summary>条件が成るまで押しっぱなしにする（連続入力を見るので押し直さない）。</summary>
        private IEnumerator HoldUntil(Key key, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            yield return null;
        }

        /// <summary>
        /// 到着が確定し、旧 Area の撤去まで終わるのを待つ。
        ///
        /// <b>経路を問わず数える</b>（<see cref="AreaTransitionService.ArrivalCount"/>）——
        /// P5.5 の接続は P55-04b からスライド経路を通るので、Single 経路の
        /// <c>CompletedCount</c> だけを見ていると「着いていない」ことになる。
        ///
        /// <b>「Scene が 1 枚に戻る」はもう落ち着いた状態ではない</b>（工程 P55-10c）。
        /// 裁定 2 で旧 Area は撤去せず非活動のまま預かるので、落ち着いた先は
        /// <b>活動中 1 枚 ＋ 非活動 1 枚</b>である。待つのは「到着が確定して、
        /// Scene 操作が終端している」ところまで。
        /// </summary>
        private static IEnumerator WaitForArrival(AreaTransitionService transitions, int expected)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while ((transitions.ArrivalCount < expected
                    || transitions.Slide.HasLiveSceneOperation)
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(expected, transitions.ArrivalCount, "到着が確定する。");
            Assert.IsFalse(transitions.Slide.HasLiveSceneOperation,
                "終端していない Scene 操作は残っていない。");
            Assert.LessOrEqual(SceneManager.sceneCount, 2,
                "載っているのは活動中 ＋ 非活動の最大 2 枚（上限 2。§6.2 手順 11）。");
            yield return null;
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

        private static AreaExitGate FindAnyExitGate()
        {
            var gate = Object.FindFirstObjectByType<AreaExitGate>();
            Assert.IsNotNull(gate, "開放出入口が Scene にありません。");
            return gate;
        }

        private static AreaRoot FindAreaRoot()
        {
            var root = Object.FindFirstObjectByType<AreaRoot>();
            Assert.IsNotNull(root, "AreaRoot が Scene にありません。");
            return root;
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

        private void RemoveDevices()
        {
            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            RemoveStrayDevices();
        }

        /// <summary>前の実行が残した仮想デバイスを外す（同名で足すと枝番の 2 台目になる）。</summary>
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
