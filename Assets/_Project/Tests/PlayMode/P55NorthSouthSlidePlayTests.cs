using System.Collections;
using System.Collections.Generic;
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
    /// P5.5 の<b>南北配置</b>で上下にスライドする（仕様書 §7.1／§11 の P03。工程 P55-05c）。
    ///
    /// <b>ここで初めて「配置が変わってもスライドが成立するか」を実機で見る。</b>
    /// 東西配置しか無いあいだは、接続軸に関わる実装が
    /// 「X 決め打ちでも通ってしまう」——実際 Scene 生成器は東西前提で、
    /// 外周壁の接続口も境界寄せ領域も X／Z を直に書いていた。
    ///
    /// <b>§11 の P03 が求めるのは「画面の方向と実ワールドの方向が一致」である。</b>
    /// カメラの Z が増えたことを見るだけでは足りない——それは<b>世界の話</b>でしかなく、
    /// 「北へ進んだのに画面では左へ流れた」構成でも通ってしまう。
    /// ここでは<b>スライド中の実カメラで出発点と到着点を画面へ射影して</b>、
    /// 北の行き先が画面の<b>上</b>に、東の行き先が画面の<b>右</b>に来ることを見る。
    ///
    /// <b>Gate も Driver も完了通知もテストから直接叩かない</b>（§11 P01／P02 と同じ書き方）。
    /// 主人公を出入口の範囲内へ置いて実キーを押し続けるだけにする。
    /// </summary>
    public sealed class P55NorthSouthSlidePlayTests
    {
        private const string NsAreaSScene =
            "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_AreaS.unity";
        private const string NsAreaNScene =
            "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_AreaN.unity";
        private const string NsTrialScene =
            "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_ConnectionTrial.unity";
        private const string EwAreaAScene =
            "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity";

        private static readonly StableId ExitSNorth = new StableId("exit_p55_s_north");
        private static readonly StableId ExitNSouth = new StableId("exit_p55_n_south");
        private static readonly StableId ExitAEast = new StableId("exit_p55_a_east");
        private static readonly StableId ConnectionSToN = new StableId("conn_p55_s_north_to_n");
        private static readonly StableId ConnectionNToS = new StableId("conn_p55_n_south_to_s");

        /// <summary>
        /// 接続軸の X（南北配置。Editor 側の定数は参照しない）。
        /// 通路を部屋の X 中央へ置いたのは、見える半幅（≒8.89）が部屋の半幅（12）より
        /// 大きく、端へ寄せるとスライド中に床の外が映るため（<c>Phase55NorthSouthLayout</c>）。
        /// </summary>
        private const float NsSeamAxisX = 0f;

        /// <summary>画面の向きを「明らかに」と言えるだけの画素差。</summary>
        private const float ScreenMargin = 50f;

        /// <summary>画面の直交方向のずれに許す画素差。</summary>
        private const float ScreenDrift = 2f;

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

            yield return SceneManager.LoadSceneAsync(NsTrialScene, LoadSceneMode.Single);
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

        // ---------------------------------------------------------------- 北へ（P03）

        /// <summary>
        /// P03：S の北の出入口へ<b>実キー（W）</b>で歩くと、北向きの接続でスライドして N へ着く。
        ///
        /// <b>「北へ行った」だけでは足りない。</b> 途中のカメラを毎フレーム採って、
        /// X が動かないこと・Z が単調に増えることを見る。カウンタで数えるだけでは
        /// 「1 フレームで飛んだ」「行き過ぎて戻った」を見分けられない。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingNorthIntoTheSeam_SlidesUpToTheNorthernArea()
        {
            yield return EnterArea(NsAreaSScene);

            AreaTransitionService transitions = Transitions();
            Assert.IsNotNull(transitions.Connections, "S が接続一覧を渡している（§3.1）。");

            yield return StandJustBefore(FindExitGate(ExitSNorth), Vector3.back);
            yield return SettleCamera();

            Vector3 cameraBefore = RigPosition();
            var track = new SlideTrack(NsSeamAxisX, alongIsZ: true);
            yield return HoldWhileTracking(Key.W, track, () => transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(1, transitions.ConnectionTravelCount, "接続で 1 回だけ受理された。");
            Assert.AreEqual(1, transitions.SlideCommittedCount, "スライドで 1 回だけ到着が確定した。");
            Assert.AreEqual(0, transitions.CompletedCount, "従来の Single 経路は通っていない。");
            Assert.AreEqual(string.Empty, transitions.Slide.LastFailure, "失敗を抱えていない。");

            AreaConnectionSnapshot used = transitions.LastAcceptedConnection;
            Assert.AreEqual(ConnectionSToN.Value, used.ConnectionId.Value, "北向きの接続が選ばれる。");
            Assert.AreEqual(ExitSNorth.Value, used.ExitId.Value, "引いた鍵はこの出入口の ID。");
            Assert.AreEqual(AreaTransitionStyle.Slide, used.Style, "見せ方は Slide。");
            Assert.AreEqual(AreaConnectionDirection.North, used.Direction, "向きは North。");
            Assert.AreEqual("area_p55_n", used.ToAreaId.Value, "行き先は南北配置の N。");

            // ---- 世界の動き ----
            Assert.Greater(track.Samples.Count, 2,
                "スライドが複数フレームにわたっている（1 フレームで飛んでいない）。");
            Assert.Less(track.WorstAxisDrift, 0.1f,
                "南北のスライドで X が動かない（最大のずれ=" + track.WorstAxisDrift + "。§7.1）。");
            Assert.Greater(RigPosition().z, cameraBefore.z + 5f,
                "カメラが北へ大きく動いた（before z=" + cameraBefore.z + " after z=" + RigPosition().z + "）。");
            track.AssertMonotonicIncreasingAlong();

            // ---- 画面の向き（P03 の本体）----
            track.AssertScreenDirection(
                expectUp: true, expectRight: false,
                because: "北の行き先はスライド中の画面で上に見える（§11 の P03）。");

            Assert.AreEqual("area_p55_n", FindAreaRoot().AreaId.Value, "N に居る。");
            Assert.AreEqual(1, SceneManager.sceneCount, "旧 Area の撤去まで終わっている。");
        }

        // ---------------------------------------------------------------- 南へ（P03）

        /// <summary>
        /// P03：N の南の出入口へ実キー（S）で歩くと、南向きの接続で下へスライドして S へ着く。
        ///
        /// 往復を<b>1 レコードで使い回していない</b>ことがここで効く——向きが South で、
        /// 北向きのレコードと対になっている。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingSouthIntoTheSeam_SlidesDownToTheSouthernArea()
        {
            yield return EnterArea(NsAreaNScene);

            AreaTransitionService transitions = Transitions();
            yield return StandJustBefore(FindExitGate(ExitNSouth), Vector3.forward);
            yield return SettleCamera();

            Vector3 cameraBefore = RigPosition();
            var track = new SlideTrack(NsSeamAxisX, alongIsZ: true);
            yield return HoldWhileTracking(Key.S, track, () => transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount, "下へスライドして着いた。");

            AreaConnectionSnapshot used = transitions.LastAcceptedConnection;
            Assert.AreEqual(ConnectionNToS.Value, used.ConnectionId.Value, "南向きの接続が選ばれる。");
            Assert.AreEqual(AreaConnectionDirection.South, used.Direction, "向きは South。");
            Assert.AreEqual(ConnectionSToN.Value, used.ReverseConnectionId.Value,
                "北向きのレコードと対になっている（§3.1。1 レコードを使い回さない）。");
            Assert.AreEqual("area_p55_s", used.ToAreaId.Value);

            Assert.Less(track.WorstAxisDrift, 0.1f, "南北のスライドで X が動かない。");
            Assert.Less(RigPosition().z, cameraBefore.z - 5f, "カメラが南へ大きく動いた。");
            track.AssertMonotonicDecreasingAlong();
            track.AssertScreenDirection(
                expectUp: false, expectRight: false,
                because: "南の行き先はスライド中の画面で下に見える（§11 の P03）。");

            Assert.AreEqual("area_p55_s", FindAreaRoot().AreaId.Value, "S に居る。");
        }

        // ---------------------------------------------------------------- 東西と比べる（P03）

        /// <summary>
        /// P03：<b>同じ見方で東西配置を見ると、行き先は画面の右に来る</b>。
        ///
        /// 南北だけを見て「上に来た」と言っても、画面の向きが<b>配置に従っている</b>ことの
        /// 証拠にはならない——上にしか動かない実装でも通る。
        /// 同じ射影の道具で東西を見て、<b>右</b>に来ることを対で押さえる。
        /// </summary>
        [UnityTest]
        public IEnumerator WalkingEastIntoTheSeam_PutsTheDestinationToTheRightOfTheScreen()
        {
            yield return EnterArea(EwAreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return StandJustBefore(FindExitGate(ExitAEast), Vector3.left);
            yield return SettleCamera();

            var track = new SlideTrack(0f, alongIsZ: false);
            yield return HoldWhileTracking(Key.D, track, () => transitions.SlideCommittedCount > 0, 25f);

            Assert.AreEqual(1, transitions.SlideCommittedCount, "東へスライドして着いた。");
            Assert.Less(track.WorstAxisDrift, 0.1f, "東西のスライドで Z が動かない。");
            track.AssertMonotonicIncreasingAlong();
            track.AssertScreenDirection(
                expectUp: false, expectRight: true,
                because: "東の行き先はスライド中の画面で右に見える（§11 の P03）。");
        }

        // ---------------------------------------------------------------- 採取

        /// <summary>
        /// スライド中に採る事実。
        ///
        /// <b>世界の座標と画面の座標を両方採る。</b> 片方だけでは「画面の方向と実ワールドの
        /// 方向が一致」を言えない——世界だけなら画面が回っていても通り、
        /// 画面だけなら世界が動いていなくても通る。
        /// </summary>
        private sealed class SlideTrack
        {
            private readonly float _axis;
            private readonly bool _alongIsZ;

            internal SlideTrack(float axis, bool alongIsZ)
            {
                _axis = axis;
                _alongIsZ = alongIsZ;
            }

            /// <summary>スライド中のカメラ基準位置。</summary>
            internal List<Vector3> Samples { get; } = new List<Vector3>();

            /// <summary>接続軸からの最大のずれ（直交軸）。</summary>
            internal float WorstAxisDrift { get; private set; }

            /// <summary>スライドの出発点を、その時点の実カメラで画面へ射影したもの。</summary>
            internal Vector3 ScreenFrom { get; private set; }

            /// <summary>同・到着点。</summary>
            internal Vector3 ScreenTo { get; private set; }

            /// <summary>射影を採れたか。</summary>
            internal bool Projected { get; private set; }

            internal void Sample(AreaTransitionService transitions)
            {
                AreaCameraRigHost host = AreaCameraRigHost.Instance;
                if (host == null || !host.IsSliding)
                {
                    return;
                }

                Vector3 at = host.Rig.transform.position;
                Samples.Add(at);
                WorstAxisDrift = Mathf.Max(WorstAxisDrift, Mathf.Abs(Across(at) - _axis));

                if (Projected || host.Camera == null)
                {
                    return;
                }

                // <b>スライド中の実カメラで射影する</b>。到着後のカメラで測ると、
                // 出発点と到着点の関係ではなく「到着した場所の見え方」を測ってしまう。
                ScreenFrom = host.Camera.WorldToScreenPoint(transitions.Slide.LastSlideFrom);
                ScreenTo = host.Camera.WorldToScreenPoint(transitions.Slide.LastSlideTo);
                Projected = true;
            }

            private float Across(Vector3 v) => _alongIsZ ? v.x : v.z;

            private float Along(Vector3 v) => _alongIsZ ? v.z : v.x;

            internal void AssertMonotonicIncreasingAlong()
            {
                for (int i = 1; i < Samples.Count; i++)
                {
                    Assert.GreaterOrEqual(Along(Samples[i]), Along(Samples[i - 1]) - 0.001f,
                        "接続軸へ進み続ける（" + (i - 1) + "→" + i + "で戻った："
                        + Samples[i - 1] + " → " + Samples[i] + "）。");
                }
            }

            internal void AssertMonotonicDecreasingAlong()
            {
                for (int i = 1; i < Samples.Count; i++)
                {
                    Assert.LessOrEqual(Along(Samples[i]), Along(Samples[i - 1]) + 0.001f,
                        "接続軸へ進み続ける（" + (i - 1) + "→" + i + "で戻った："
                        + Samples[i - 1] + " → " + Samples[i] + "）。");
                }
            }

            /// <summary>
            /// 行き先が画面のどちらに来るか（§11 の P03「画面の方向と実ワールドの方向が一致」）。
            ///
            /// <b>直交方向がほとんど動かないことも同時に見る</b>——斜めに流れる配置は
            /// 「一致している」とは言えない。
            /// </summary>
            internal void AssertScreenDirection(bool expectUp, bool expectRight, string because)
            {
                Assert.IsTrue(Projected, "前提：スライド中に画面への射影を採れている。");

                float dx = ScreenTo.x - ScreenFrom.x;
                float dy = ScreenTo.y - ScreenFrom.y;
                string seen = "（画面 Δx=" + dx + " Δy=" + dy + "）";

                if (expectRight)
                {
                    Assert.Greater(dx, ScreenMargin, because + seen);
                    Assert.Less(Mathf.Abs(dy), ScreenDrift, "画面で縦にはほとんど動かない" + seen);
                    return;
                }

                Assert.Less(Mathf.Abs(dx), ScreenDrift, "画面で横にはほとんど動かない" + seen);
                if (expectUp)
                {
                    Assert.Greater(dy, ScreenMargin, because + seen);
                }
                else
                {
                    Assert.Less(dy, -ScreenMargin, because + seen);
                }
            }
        }

        // ---------------------------------------------------------------- 補助

        private IEnumerator HoldWhileTracking(
            Key key, SlideTrack track, System.Func<bool> done, float seconds)
        {
            AreaTransitionService transitions = Transitions();
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!done() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
                track.Sample(transitions);
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;

            // Commit のあとに旧 Area の撤去が続く（§6.2 手順 11）。
            float tail = Time.realtimeSinceStartup + 20f;
            while (SceneManager.sceneCount > 1 && Time.realtimeSinceStartup < tail)
            {
                yield return null;
            }

            yield return null;
        }

        private static Vector3 RigPosition()
        {
            AreaCameraRigHost host = AreaCameraRigHost.Instance;
            Assert.IsNotNull(host, "常駐 Rig が立っている。");
            return host.Rig.transform.position;
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

        private IEnumerator EnterArea(string scenePath)
        {
            AssertSceneRegistered(scenePath);

            DestroyLaunchers();
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55NorthSouthTest");
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

        /// <summary>出入口の範囲内へ立つ（<paramref name="back"/> は出口と逆向きの少しだけ手前）。</summary>
        private IEnumerator StandJustBefore(AreaExitGate gate, Vector3 back)
        {
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
