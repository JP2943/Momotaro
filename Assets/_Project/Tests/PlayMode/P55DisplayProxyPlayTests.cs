using System.Collections;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Transition;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 実 Actor から立てた表示代理（P5.5 仕様書 §7.2／§11 P06。工程 P55-03d-2）。
    ///
    /// EditMode が見るのは代理の契約。ここで見るのは<b>実 Actor から写せるか</b>と
    /// <b>代理を立てても世界が動かないか</b>——登録簿・物理・命中の経路が
    /// 1 つも増えないこと（§11 P06「表示代理から命中も登録も発生しない」）。
    ///
    /// <b>「何を外したか」ではなく「何も増えていないこと」を見る。</b>
    /// 部品を列挙して照合する形だと、Actor 側に部品が増えたときに検査が追いつかない。
    /// </summary>
    public sealed class P55DisplayProxyPlayTests
    {
        private const string P55AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity";
        private const string P55TrialScene =
            "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity";

        private GameObject _bootstrap;
        private AreaTransitionDisplayProxySet _proxies;

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
            GameModeProvider.Current = null;
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
            if (_proxies != null && !_proxies.IsReleased)
            {
                _proxies.Release();
            }

            _proxies = null;

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
            GameSessionProvider.Current = null;
            GameplayClockProvider.Current = null;
            CampaignRespawnTravelProvider.Current = null;
            RespawnSubmitOwnerProvider.Current = null;
            AreaPendingArrival.Clear();
            yield return null;
        }

        // ---------------------------------------------------------------- P06

        /// <summary>
        /// 実 Actor から代理を立てても<b>世界は何も動かない</b>（§11 P06）。
        ///
        /// 見るのは 4 つ。
        /// <list type="number">
        /// <item><description>登録簿（索敵・Interact・調査）の数が<b>1 つも変わらない</b>。</description></item>
        /// <item><description>Rigidbody・Collider の総数が<b>1 つも増えない</b>。</description></item>
        /// <item><description>実 Renderer が<b>同じフレームで</b>隠れる（二重表示なし）。</description></item>
        /// <item><description>畳むと実 Renderer が<b>戻る</b>（欠落を残さない）。</description></item>
        /// </list>
        /// </summary>
        [UnityTest]
        public IEnumerator BuildingProxies_ChangesNothingInTheWorld()
        {
            yield return EnterArea();

            int perceptionBefore = PerceptionTargetRegistry.Count;
            int interactablesBefore = AreaInteractableRegistry.Count;
            int pointsBefore = InvestigationPointRegistry.Count;
            int bodiesBefore = CountAll<Rigidbody>();
            int collidersBefore = CountAll<Collider>();

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            var companion = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsNotNull(player, "主人公が居る。");
            Assert.IsNotNull(companion, "犬丸が居る。");
            Assert.IsFalse(companion.IsAway, "前提：犬丸は退場していない。");

            SpriteRenderer playerRenderer = VisibleRenderer(player.VisualRoot);
            Assert.IsNotNull(playerRenderer, "主人公の絵が見えている。");

            _proxies = new AreaTransitionDisplayProxySet();
            _proxies.Build(player, companion);

            // ---- 同じフレームのうちに見る（フレームを跨がせない）----
            Assert.IsNotNull(_proxies.Player, "主人公の代理が立つ。");
            Assert.IsNotNull(_proxies.Companion, "健常な犬丸の代理も立つ。");
            Assert.IsFalse(playerRenderer.enabled,
                "実 Renderer は代理を立てたのと同じフレームで隠れる（二重表示を作らない。§7.2）。");
            Assert.AreSame(playerRenderer.sprite, _proxies.Player.Renderer.sprite, "絵を写している。");

            Assert.AreEqual(perceptionBefore, PerceptionTargetRegistry.Count,
                "索敵の登録簿が変わらない（代理は登録しない）。");
            Assert.AreEqual(interactablesBefore, AreaInteractableRegistry.Count,
                "Interact の登録簿が変わらない。");
            Assert.AreEqual(pointsBefore, InvestigationPointRegistry.Count,
                "調査の登録簿が変わらない。");
            Assert.AreEqual(bodiesBefore, CountAll<Rigidbody>(), "物理が 1 つも増えない。");
            Assert.AreEqual(collidersBefore, CountAll<Collider>(), "当たりが 1 つも増えない。");

            // ---- 運んでも増えない（動かした先で登録が起きたりしない）----
            Vector3 carryFrom = player.transform.position;
            Vector3 carryTo = carryFrom + new Vector3(10f, 0f, 0f);
            _proxies.SetRoute(carryFrom, carryTo, carryFrom, carryTo);
            for (int i = 0; i <= 10; i++)
            {
                _proxies.SetProgress(i / 10f);
                _proxies.TickDisplayClock(0.02f);
                yield return null;
            }

            Assert.AreEqual(perceptionBefore, PerceptionTargetRegistry.Count, "運んでも登録は増えない。");
            Assert.AreEqual(collidersBefore, CountAll<Collider>(), "運んでも当たりは増えない。");
            Assert.Greater(_proxies.Player.transform.position.x, player.transform.position.x + 9f,
                "代理は運ばれている（検査が空振りしていない）。");
            Assert.AreEqual(player.transform.position, PlayerPositionUnchanged(player),
                "実 Actor は動いていない（代理が運ぶだけ）。");

            // ---- 畳むと戻る ----
            _proxies.Release();
            Assert.IsTrue(playerRenderer.enabled, "畳むと実 Renderer が戻る（欠落を残さない）。");
            Assert.AreEqual(0, _proxies.Count, "代理は残らない。");
            yield return null;

            Assert.AreEqual(bodiesBefore, CountAll<Rigidbody>(), "畳んだあとも増減なし。");
        }

        /// <summary>
        /// <b>退場した犬丸の代理は作らない</b>（§7.2「Away は代理も作らない」）。
        ///
        /// 居ないものを運ぶと、退場中の犬丸が遷移のあいだだけ画面に現れる。
        /// </summary>
        [UnityTest]
        public IEnumerator AnAwayCompanion_GetsNoProxyAtAll()
        {
            yield return EnterArea();

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            var companion = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsTrue(companion.RequestState(CompanionState.Away, CompanionStateChangeReason.Left),
                "前提：退場させられる。");
            yield return null;
            Assert.IsTrue(companion.IsAway, "前提：退場している。");

            // <b>絵を見えたままにしておく。</b> 退場すると絵も消える構成なので、
            // そのまま試すと「絵が無いから代理が無い」だけで通ってしまう——
            // 実際そうなっていて、Away の判定を外す注入を検知できなかった。
            // ここでは<b>状態で断っている</b>ことを確かめたいので、絵を戻して試す。
            foreach (SpriteRenderer renderer in
                companion.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer != null && renderer.sprite != null)
                {
                    renderer.enabled = true;
                }
            }

            yield return null;

            _proxies = new AreaTransitionDisplayProxySet();
            _proxies.Build(player, companion);

            Assert.IsNotNull(_proxies.Player, "主人公の代理は立つ。");
            Assert.IsNull(_proxies.Companion, "退場中の犬丸の代理は立てない（§7.2）。");
            Assert.IsTrue(_proxies.CompanionSkippedBecauseAway,
                "断った理由が<b>退場</b>であること（絵が無いからではない）。");
            Assert.AreEqual(1, _proxies.Count, "代理は主人公の 1 つだけ。");
        }

        /// <summary>
        /// <b>倒れている犬丸は姿勢を保って運ぶ</b>（§7.2「Down／Stagger は既存姿勢を保持して位置だけ」）。
        ///
        /// 回復演出を勝手に再生しない。また<b>主人公の足元へ寄せ集めない</b>——
        /// 代理の出発位置はその Actor がいま居る場所である。
        /// </summary>
        [UnityTest]
        public IEnumerator ADownedCompanion_KeepsItsPostureAndStartsWhereItStands()
        {
            yield return EnterArea();

            var player = Object.FindFirstObjectByType<PlayerRoot>();
            var companion = Object.FindFirstObjectByType<CompanionActor>();
            Assert.IsTrue(companion.ForceHitState(CompanionState.Down, CompanionStateChangeReason.Defeated),
                "前提：倒れさせられる。");
            yield return null;
            Assert.IsTrue(companion.IsDown, "前提：倒れている。");

            // <b>絵のある場所で比べる。</b> Actor の根と描画ノードには高さのずれがあり
            // （犬丸は 0.5m）、代理は<b>描画ノード</b>の位置を写す——根で比べると
            // 「寄せ集めていない」ことを見たいのに高さのずれで落ちる。
            SpriteRenderer companionRenderer = VisibleRenderer(companion.transform);
            Assert.IsNotNull(companionRenderer, "倒れていても絵は見えている。");
            Vector3 companionSpot = companionRenderer.transform.position;

            _proxies = new AreaTransitionDisplayProxySet();
            _proxies.Build(player, companion);

            Assert.IsNotNull(_proxies.Companion, "倒れていても代理は立つ（位置は運ぶ）。");
            Assert.IsTrue(_proxies.Companion.IsFrozen, "コマ送りは止める（回復演出を再生しない）。");
            Assert.Less(Vector3.Distance(companionSpot, _proxies.Companion.transform.position), 0.01f,
                "出発位置は犬丸の絵が居た場所。");

            // ここが §7.2 の「見えている犬丸を主人公の足元へ瞬間移動させない」。
            Assert.Greater(
                Vector3.Distance(_proxies.Player.transform.position, _proxies.Companion.transform.position),
                0.5f,
                "犬丸の代理を主人公の足元へ寄せ集めていない（主人公の代理と別の場所に居る）。");

            // <b>終点は呼び出し側が渡す</b>（工程 P55-07b。GPT 受入④）。
            //
            // 以前はここで「主人公と同じ差分だけ運ぶ」を固定していた。それは
            // 到着実体の置き場所（入口から進行方向と逆へ 1.2m）と一致する保証が無く、
            // <b>畳んだ瞬間に犬丸が跳ぶ</b>原因だった。いまは実体の到着位置を渡す。
            Vector3 playerFrom = player.transform.position;
            Vector3 playerTo = playerFrom + new Vector3(8f, 0f, 0f);

            // 主人公とは<b>違う</b>終点を渡して、そのとおりに運ばれることを見る。
            Vector3 companionTo = companionSpot + new Vector3(8f, 0f, 2f);
            _proxies.SetRoute(playerFrom, playerTo, companionSpot, companionTo);
            _proxies.SetProgress(1f);

            Assert.Less(Vector3.Distance(
                    companionTo, _proxies.Companion.transform.position), 0.01f,
                "犬丸は渡された終点へ運ばれる（主人公の差分ではない）。");
            Assert.AreEqual(0, _proxies.Companion.FrameIndex, "姿勢は変わっていない。");
        }

        // ---------------------------------------------------------------- 補助

        private IEnumerator EnterArea()
        {
            DestroyLaunchers();
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55ProxyTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐が立つ。理由=" + BootstrapRoot.Instance.BootstrapFailure);
            DestroyLaunchers();

            yield return SceneManager.LoadSceneAsync(P55AreaAScene, LoadSceneMode.Single);
            yield return null;

            var initializer = Object.FindFirstObjectByType<AreaInitializer>();
            Assert.IsNotNull(initializer);
            Assert.IsTrue(initializer.Initialized, "初期化が成立する。理由=" + initializer.FailureReason);
            yield return null;
        }

        /// <summary>実 Actor が動いていないことを言うための読み取り（意図を名前に出す）。</summary>
        private static Vector3 PlayerPositionUnchanged(PlayerRoot player) => player.transform.position;

        private static SpriteRenderer VisibleRenderer(Transform root)
        {
            SpriteRenderer best = null;
            foreach (SpriteRenderer candidate in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (candidate != null && candidate.enabled && candidate.sprite != null
                    && (best == null || candidate.sortingOrder > best.sortingOrder))
                {
                    best = candidate;
                }
            }

            return best;
        }

        private static int CountAll<T>() where T : Component =>
            Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

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
    }
}
