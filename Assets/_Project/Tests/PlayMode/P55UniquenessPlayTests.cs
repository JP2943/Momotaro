using System.Collections;
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
using Momotaro.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P5.5 の<b>HUD と入力の唯一性</b>（仕様書 §11 の P12 の残り。工程 P55-06c）。
    ///
    /// Camera／AudioListener の唯一性と、移動中の飛び・二重書込は P55-04b で見た。
    /// ここで残っているのは <b>HUD と入力</b>である。
    ///
    /// <b>HUD は Area ごとに置かれている</b>（`CreateAreaSystems` が Area Scene へ組む）。
    /// スライド中は Area が 2 つ載るので、<b>2 枚出る</b>危険と、
    /// 受け渡しの隙間で<b>0 枚になる</b>危険の両方がある。
    /// 前者は数字が二重に見え、後者は HP が一瞬消える。
    /// <b>どちらも「1 枚」を毎フレーム数えれば分かる。</b>
    ///
    /// <b>入力は常駐が 1 つ持つ</b>（<c>InputBootService</c>）。こちらは数ではなく
    /// <b>同じものが使われ続けるか</b>が問題になる。遷移のたびに作り直されていると、
    /// 押しっぱなしの状態や無効化の約束がそこで切れる。
    /// </summary>
    public sealed class P55UniquenessPlayTests
    {
        private sealed class Route
        {
            internal string AreaAScene;
            internal string TrialScene;
            internal StableId ExitFromA;
            internal Key Forward;
            internal Vector3 BackStep;
        }

        private static Route RouteOf(string key) => key == "NorthSouth"
            ? new Route
            {
                AreaAScene = "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_AreaS.unity",
                TrialScene = "Assets/_Project/Scenes/Tests/Phase55NS/SCN_Phase55NS_ConnectionTrial.unity",
                ExitFromA = new StableId("exit_p55_s_north"),
                Forward = Key.W,
                BackStep = Vector3.back,
            }
            : new Route
            {
                AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity",
                TrialScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity",
                ExitFromA = new StableId("exit_p55_a_east"),
                Forward = Key.D,
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

        // ---------------------------------------------------------------- P12（残り）

        /// <summary>
        /// P12：スライドの<b>あいだずっと</b>、HUD が 1 枚・AudioListener が 1 つ・
        /// Interact の窓口が 1 つで、入力は<b>同じもの</b>が使われ続ける。
        ///
        /// <b>端の 2 フレームでは足りない</b>（R14 指摘 2 と同じ）。受け渡しの隙間は
        /// 出発側を閉じてから到着側を開けるまでの<b>途中</b>にしか出ない。
        /// 要求の瞬間から旧 Area の撤去が終わるまで、毎フレーム数える。
        /// </summary>
        [UnityTest]
        public IEnumerator TheSlide_KeepsOneHudOneListenerAndTheSameInput(
            [Values("EastWest", "NorthSouth")] string arrangement)
        {
            _route = RouteOf(arrangement);
            yield return EnterArea(_route.AreaAScene);

            AreaTransitionService transitions = Transitions();
            yield return PlaceBeforeExit(_route.ExitFromA, _route.BackStep);

            IPlayerInput inputBefore = PlayerInputProvider.Current;
            Assert.IsNotNull(inputBefore, "前提：入力の供給元が立っている。");

            var seen = new Counts();
            seen.Sample(inputBefore);
            Assert.AreEqual(1, seen.MaxHud, "前提：遷移前も HUD は 1 枚。");

            // ---- 要求から撤去まで、毎フレーム数える ----
            float deadline = Time.realtimeSinceStartup + 25f;
            while ((transitions.SlideCommittedCount == 0 || SceneManager.sceneCount > 1)
                   && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(_route.Forward));
                yield return null;
                seen.Sample(inputBefore);
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            seen.Sample(inputBefore);

            Assert.AreEqual(1, transitions.SlideCommittedCount,
                "スライドで渡れている（失敗=" + transitions.Slide.LastFailure + "）。");
            Assert.Greater(seen.Frames, 5,
                "遷移が複数フレームにわたっている（数えた枚数=" + seen.Frames + "）。");

            // ---- HUD ----
            Assert.AreEqual(1, seen.MaxHud,
                "HUD が 2 枚出た瞬間がある（最大 " + seen.MaxHud
                + " 枚）。Area ごとに HUD を持つので、受け渡しで重なりうる（§11 の P12）。");
            Assert.AreEqual(1, seen.MinHud,
                "HUD が 0 枚になった瞬間がある（最小 " + seen.MinHud
                + " 枚）。出発側を閉じてから到着側を開けるまでの隙間で HP が消える（§11 の P12）。");

            // ---- AudioListener ----
            Assert.AreEqual(1, seen.MaxListener,
                "AudioListener が 2 つになった瞬間がある（最大 " + seen.MaxListener + "）。");
            Assert.AreEqual(1, seen.MinListener,
                "AudioListener が 0 になった瞬間がある（最小 " + seen.MinListener + "）。");

            // ---- Interact の窓口 ----
            Assert.AreEqual(1, seen.MaxInteract,
                "Interact の窓口が 2 つになった瞬間がある（最大 " + seen.MaxInteract
                + "）。両 Area の窓口が同時に効くと、1 回の押下で 2 回反応する。");

            // ---- 入力 ----
            //
            // <b>数ではなく同一性で見る。</b> 供給元は常駐が 1 つ持つので、
            // 数えても常に 1 である。壊れるとしたら「遷移のたびに作り直される」形で、
            // それは押しっぱなしの状態や無効化の約束がそこで切れることを意味する。
            Assert.AreEqual(0, seen.InputSwaps,
                "遷移のあいだに入力の供給元が入れ替わった（" + seen.InputSwaps
                + " 回）。押しっぱなしの状態も無効化の約束もそこで切れる（§11 の P12）。");
            Assert.IsTrue(ReferenceEquals(PlayerInputProvider.Current, inputBefore),
                "到着後も同じ入力の供給元である。");

            // ---- 到着後の HUD が生きている ----
            CombatPlayHud hud = ActiveHud();
            Assert.IsNotNull(hud, "到着後も HUD がある。");
            Assert.IsNotNull(hud.ProgressSource, "到着後の HUD が進行データに配線されている（徳が映る）。");
            Assert.AreEqual(1, CountActive<AreaCameraRigHost>(), "常駐 Rig は 1 つのまま。");
        }

        // ---------------------------------------------------------------- 数える

        /// <summary>毎フレームの数（最大と最小の両方を持つ）。</summary>
        private sealed class Counts
        {
            internal int Frames { get; private set; }

            internal int MaxHud { get; private set; }

            internal int MinHud { get; private set; } = int.MaxValue;

            internal int MaxListener { get; private set; }

            internal int MinListener { get; private set; } = int.MaxValue;

            internal int MaxInteract { get; private set; }

            /// <summary>入力の供給元が入れ替わった回数。</summary>
            internal int InputSwaps { get; private set; }

            internal void Sample(IPlayerInput expected)
            {
                Frames++;

                int hud = CountActive<CombatPlayHud>();
                MaxHud = Mathf.Max(MaxHud, hud);
                MinHud = Mathf.Min(MinHud, hud);

                int listeners = CountActiveListeners();
                MaxListener = Mathf.Max(MaxListener, listeners);
                MinListener = Mathf.Min(MinListener, listeners);

                MaxInteract = Mathf.Max(MaxInteract, CountActive<AreaInteractionController>());

                if (!ReferenceEquals(PlayerInputProvider.Current, expected))
                {
                    InputSwaps++;
                }
            }
        }

        /// <summary>いま効いている（有効な）部品の数。無効なものは数えない。</summary>
        private static int CountActive<T>() where T : MonoBehaviour
        {
            int count = 0;
            foreach (T item in Object.FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (item != null && item.isActiveAndEnabled)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountActiveListeners()
        {
            int count = 0;
            foreach (AudioListener item in
                Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (item != null && item.enabled && item.gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static CombatPlayHud ActiveHud()
        {
            foreach (CombatPlayHud hud in
                Object.FindObjectsByType<CombatPlayHud>(FindObjectsSortMode.None))
            {
                if (hud != null && hud.isActiveAndEnabled)
                {
                    return hud;
                }
            }

            return null;
        }

        // ---------------------------------------------------------------- 補助

        private IEnumerator EnterArea(string scenePath)
        {
            AssertSceneRegistered(scenePath);

            DestroyLaunchers();
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55UniquenessTest");
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
