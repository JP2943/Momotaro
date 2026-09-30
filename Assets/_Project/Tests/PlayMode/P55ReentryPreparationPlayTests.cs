using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// <b>初回の Scene 構築と、入場ごとの準備処理を分ける</b>（P5.5 §6.2 手順 5。工程 P55-10b）。
    ///
    /// <b>なぜ要るのか。</b> 旧実装では入場ごとに Scene を読み直していたので、
    /// <see cref="AreaInitializer.Initialize"/> は一生に一度しか走らなかった——
    /// 冒頭に <c>if (Initialized) return true;</c> があり、<c>Start()</c> も二度目は呼ばれない。
    /// 旧 Area を<b>保持して再利用する</b>と（§6.2 手順 11。裁定 2）二度目の入場が起きるので、
    /// そのままでは<b>入口配置・Snapshot の復元・門／調査／Encounter の反映・到着世代の報告が
    /// まるごと落ちる</b>。GPT の指摘した「再利用はゲートを開くだけでは成立しない」がこれである。
    ///
    /// <b>ここで見るのは「二度目が走ること」と「二度目が壊さないこと」。</b>
    /// 引き継ぎそのもの（往復のロード回数・Scene handle の再利用・B で値を変えて A へ戻る）は
    /// 工程 P55-10c で保持が入ってから見る——いまは戻る先が残っていないので、
    /// 二度目の入場は<b>明示的に呼んで</b>作る。
    ///
    /// <b>EditMode では作れない。</b> 見たいのは実 Scene の部品（入口・出入口・門・Encounter）と
    /// 常駐（Session・遷移サービス）が絡んだ順序なので、実 Area を読んで確かめる。
    /// </summary>
    public sealed class P55ReentryPreparationPlayTests
    {
        private const string P55AreaAScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaA.unity";
        private const string P55AreaBScene = "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_AreaB.unity";
        private const string P55TrialScene =
            "Assets/_Project/Scenes/Tests/Phase55/SCN_Phase55_ConnectionTrial.unity";

        private static readonly StableId AreaA = new StableId("area_p55_a");

        private GameObject _bootstrap;

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

        // ------------------------------------------------ 分けたこと自体

        /// <summary>
        /// 二度目の入場準備は<b>通り</b>、構築は<b>増えない</b>。
        ///
        /// <b>見るのは <see cref="AreaInitializer.BuildCount"/> である。</b>
        /// 「二度目が通ったこと」では言えない——注入（<c>PlayerProgressHolder.Bind</c>・
        /// <c>InvestigationRecordHolder.Bind</c>）は<b>同じ相手なら二度目も受ける</b>ので、
        /// 構築をまるごとやり直しても戻り値は <c>true</c> のままになる（注入 89 で実測した）。
        /// 「断られるはずだから通れば正しい」は成り立たない。数えた回数で言う。
        /// </summary>
        [UnityTest]
        public IEnumerator PreparingTheEntryTwice_BuildsTheSceneOnce()
        {
            yield return OpenAreaA();

            AreaInitializer initializer = FindInitializer();
            Assert.AreEqual(1, initializer.BuildCount, "前提：構築は 1 回。");
            Assert.AreEqual(1, initializer.EntryCount, "前提：入場は 1 回。");
            Assert.IsTrue(initializer.SceneBuilt, "前提：構築済み。");

            Assert.IsTrue(initializer.PrepareForEntry(),
                "二度目の入場準備が通る（注入をやり直していない）。理由=" + initializer.FailureReason);

            Assert.AreEqual(1, initializer.BuildCount, "構築は増えない（一度だけ）。");
            Assert.AreEqual(2, initializer.EntryCount, "入場準備は増える。");
            Assert.IsTrue(initializer.Initialized, "初期化は成立したまま。");
            Assert.IsEmpty(initializer.FailureReason, "理由も残らない。");
        }

        /// <summary>
        /// 入場ごとに<b>報告し直す</b>（§6.2 手順 5 → 9）。
        ///
        /// 準備完了の報告が一度しか出ないと、保持した Area へ戻った遷移は
        /// <b>永遠に到着を待つ</b>。二度目でも <c>PreparedCount</c> が増えることを見る。
        /// </summary>
        [UnityTest]
        public IEnumerator EachEntry_ReportsPreparedAgain()
        {
            yield return OpenAreaA();

            AreaInitializer initializer = FindInitializer();
            AreaContext context = Object.FindFirstObjectByType<AreaContext>();
            Assert.IsNotNull(context, "AreaContext が Scene にある。");
            int before = context.PreparedCount;

            Assert.IsTrue(initializer.PrepareForEntry(), "理由=" + initializer.FailureReason);

            Assert.AreEqual(before + 1, context.PreparedCount, "入場ごとに準備完了を報告する。");
            Assert.AreEqual(AreaA, context.AreaId, "Area は変わらない。");
            Assert.IsTrue(context.EntryId.IsValid, "入口も言い直されている。");
        }

        // ------------------------------------------------ 入場ごとに反映されるもの

        /// <summary>
        /// <b>入口へ置き直す。</b> これが落ちると、保持した Area へ戻った主人公が
        /// <b>出て行った場所</b>に立っている——通路の向こうから来たはずなのに。
        /// </summary>
        [UnityTest]
        public IEnumerator EachEntry_PlacesThePlayerAtTheEntryAgain()
        {
            yield return OpenAreaA();

            AreaInitializer initializer = FindInitializer();
            var player = Object.FindFirstObjectByType<PlayerRoot>();
            Assert.IsNotNull(player, "主人公が Scene にある。");

            Vector3 entry = ArrivalPositionOf(initializer);
            Vector3 away = entry + new Vector3(5f, 0f, 5f);
            player.transform.position = away;
            if (player.Body != null)
            {
                player.Body.position = away;
            }

            yield return null;
            Assert.Greater(Flat(player.transform.position - entry).magnitude, 3f,
                "前提：入口から離れた場所へ動かした。位置=" + player.transform.position);

            Assert.IsTrue(initializer.PrepareForEntry(), "理由=" + initializer.FailureReason);

            Assert.Less(Flat(player.transform.position - entry).magnitude, 0.01f,
                "入口へ置き直される。位置=" + player.transform.position + " 入口=" + entry);
        }

        /// <summary>
        /// <b>出入口の跳ね返り止めを掛け直す。</b> これが落ちると、保持した Area へ戻った瞬間に
        /// 出口方向の入力が残っていて<b>来た道へ跳ね返る</b>（往復が勝手に始まる）。
        /// </summary>
        [UnityTest]
        public IEnumerator EachEntry_DisarmsTheExitGatesAgain()
        {
            yield return OpenAreaA();

            AreaInitializer initializer = FindInitializer();
            var root = Object.FindFirstObjectByType<AreaRoot>();
            Assert.IsNotNull(root, "AreaRoot が Scene にある。");
            Assert.Greater(root.ExitGates.Count, 0, "前提：出入口がある。");

            // 出口方向から外れた入力を一度通すと、跳ね返り止めが解ける（= 武装する）。
            for (int i = 0; i < root.ExitGates.Count; i++)
            {
                root.ExitGates[i].Tick(0.016f, Vector3.zero);
            }

            for (int i = 0; i < root.ExitGates.Count; i++)
            {
                Assert.IsTrue(root.ExitGates[i].IsArmed,
                    "前提：出入口 " + root.ExitGates[i].ExitId.Value + " は武装している。");
            }

            Assert.IsTrue(initializer.PrepareForEntry(), "理由=" + initializer.FailureReason);

            for (int i = 0; i < root.ExitGates.Count; i++)
            {
                Assert.IsFalse(root.ExitGates[i].IsArmed,
                    "入場ごとに跳ね返り止めが掛かる（" + root.ExitGates[i].ExitId.Value + "）。");
            }
        }

        /// <summary>
        /// <b>前の入場の途中動作を捨てる。しかし値には触らない。</b>
        ///
        /// 保持した Area へ戻ると<b>同じ Actor</b>へ帰ってくるので、出て行ったときの
        /// 攻撃モーション・構え・先行入力が残る。それは捨てる。
        /// 一方 HP・スタミナ・CD は運ばれてきた Snapshot が正本なので、
        /// 中立化が触ってはいけない——触ると、B で削った分が A へ戻った瞬間に戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator EachEntry_DropsTheStaleActionButKeepsTheValues()
        {
            yield return OpenAreaA();

            AreaInitializer initializer = FindInitializer();
            var state = Object.FindFirstObjectByType<PlayerStateController>();
            var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
            var motor = Object.FindFirstObjectByType<PlayerMotor>();
            var facing = Object.FindFirstObjectByType<PlayerFacing>();
            Assert.IsNotNull(state, "主人公の状態機械が Scene にある。");
            Assert.IsNotNull(vitals, "主人公の生存値が Scene にある。");
            Assert.IsNotNull(motor, "主人公の移動が Scene にある。");
            Assert.IsNotNull(facing, "主人公の向きが Scene にある。");

            // 「行動の途中で出て行った」状態を作る。<b>作らないと assert が空振りする</b>——
            // 何もしなければ主人公は最初から中立なので、中立化を外しても検査は通ってしまう。
            motor.SpeedMultiplier = 0.4f;   // 構え中の減速
            motor.MovementSuppressed = true; // 攻撃中の移動止め
            facing.IsLocked = true;          // 攻撃中の向き固定

            vitals.ConsumeStamina(12f);
            int staminaBefore = vitals.CurrentStamina;
            Assert.Less(staminaBefore, 100, "前提：スタミナを削った。値=" + staminaBefore);

            Assert.IsTrue(initializer.PrepareForEntry(), "理由=" + initializer.FailureReason);

            Assert.AreEqual(PlayerState.Idle, state.Current,
                "途中動作は捨てて中立へ戻る。状態=" + state.Current);
            Assert.AreEqual(1f, motor.SpeedMultiplier, "減速が残らない。");
            Assert.IsFalse(motor.MovementSuppressed, "移動止めが残らない。");
            Assert.IsFalse(facing.IsLocked, "向きの固定が残らない。");
            Assert.AreEqual(staminaBefore, vitals.CurrentStamina,
                "値には触らない（運ばれてきた Snapshot が正本）。");
        }

        /// <summary>
        /// <b>一度限りのものを二度やらない。</b> 徳と訪問記録は入場を重ねても動かない。
        ///
        /// 二重処理は「入場ごとの処理」に一度限りの処理を混ぜたときに起きる。
        /// ここが落ちたら、分け方（構築か入場か）を間違えている。
        /// </summary>
        [UnityTest]
        public IEnumerator EachEntry_DoesNotGrantAnythingTwice()
        {
            yield return OpenAreaA();

            AreaInitializer initializer = FindInitializer();
            GameSessionState session = GameSessionProvider.Current;
            Assert.IsNotNull(session, "Session が常駐にある。");

            int virtueBefore = session.Progress.Virtue;
            Assert.IsTrue(session.HasVisited(AreaA), "前提：訪問済みになっている（直開きの自己許可）。");

            Assert.IsTrue(initializer.PrepareForEntry(), "理由=" + initializer.FailureReason);
            Assert.IsTrue(initializer.PrepareForEntry(), "三度目も通る。理由=" + initializer.FailureReason);

            Assert.AreEqual(virtueBefore, session.Progress.Virtue, "徳は増えない。");
            Assert.IsTrue(session.HasVisited(AreaA), "訪問済みのまま（記録は集合なので増えない）。");
            Assert.AreEqual(3, initializer.EntryCount, "入場は 3 回数えている。");
            Assert.AreEqual(1, initializer.BuildCount, "構築は 1 回のまま。");
        }

        // ---------------------------------------------------------------- 補助

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static Vector3 ArrivalPositionOf(AreaInitializer initializer)
        {
            var root = Object.FindFirstObjectByType<AreaRoot>();
            Assert.IsNotNull(root, "AreaRoot が Scene にある。");

            AreaContext context = Object.FindFirstObjectByType<AreaContext>();
            StableId entryId = context != null && context.EntryId.IsValid
                ? context.EntryId
                : root.Definition.DefaultEntryId;

            Assert.IsTrue(root.TryGetEntryPoint(entryId, out AreaEntryPoint point),
                "入口 '" + entryId.Value + "' が Scene にある。");
            return point.ArrivalPosition;
        }

        private static AreaInitializer FindInitializer()
        {
            var found = Object.FindFirstObjectByType<AreaInitializer>(FindObjectsInactive.Include);
            Assert.IsNotNull(found, "AreaInitializer が Scene にありません。");
            return found;
        }

        // ------------------------------------------------- 出荷状態の復元（工程 P55-14a。§4.2）

        /// <summary>
        /// <b>ゲートを開けても、戦闘外のアリーナ封鎖は 1 枚も立たない</b>
        /// （§4.2。工程 P55-14a。試遊報告「赤いラインが通過できず戦闘区域に入れない」）。
        ///
        /// <b>試遊で最初に詰まったのがここである。</b> 活動ゲートの初回 <c>Open()</c> は
        /// 「復元すべき前の状態がない」として<b>一律に有効化</b>していた。
        /// Builder は封鎖 Collider を<b>わざと無効で出荷</b>している（戦闘中だけ有効にするもの）ので、
        /// 到着した瞬間に 4 枚の壁が立ち、**戦闘区域へ歩いて入れなくなっていた**。
        ///
        /// 実測値（直す前）：<c>IsEnabled=False</c>・<c>ActiveBlockerCount=4</c>・<c>EnableCount=0</c>。
        /// <b>意図は「封鎖していない」、実体は「4 枚とも壁」。</b>
        /// <c>AreaArenaBoundary</c> 自身が
        /// 「<c>IsEnabled</c> は意図で、こちらは実体。解放したつもりで壁が残る形を外から見分ける」
        /// と書いていた口が、この経路では誰にも見られていなかった。
        ///
        /// <b>意図と実体を両方見る。</b> どちらか片方では、この壊れ方は素通りする。
        /// </summary>
        [UnityTest]
        public IEnumerator OpeningTheGate_LeavesTheArenaBlockersDisabled()
        {
            yield return OpenArea(P55AreaBScene);

            var gate = Object.FindFirstObjectByType<AreaActivityGate>();
            Assert.IsNotNull(gate, "活動ゲートが居る。");
            Assert.IsTrue(gate.IsOpen, "直開きなので開いている。");
            Assert.AreEqual(1, gate.OpenCount, "開けたのは 1 回。");
            Assert.IsFalse(gate.HasRestoreState, "前提：閉める直前の記録は無い（初回である）。");

            var arena = Object.FindFirstObjectByType<AreaArenaBoundary>();
            Assert.IsNotNull(arena, "アリーナ境界が居る。");
            Assert.IsTrue(arena.IsWired, "封鎖 Collider が配線されている。");
            Assert.Greater(arena.BlockerCount, 0, "前提：封鎖する Collider がある。");

            Assert.IsFalse(arena.IsEnabled, "意図：戦闘外なので封鎖していない。");
            Assert.AreEqual(0, arena.EnableCount, "戦闘が封鎖した回数も 0。");
            Assert.AreEqual(0, arena.ActiveBlockerCount,
                "<b>実体でも 1 枚も立っていない</b>（" + arena.ActiveBlockerCount + " / "
                + arena.BlockerCount + " 枚が有効）。ゲートの初回 Open が一律に有効化していない。");
        }

        /// <summary>
        /// <b>初回の Open は出荷状態を復元する。一律に有効化しない</b>（工程 P55-14a。GPT 受入 2）。
        ///
        /// 「復元すべき前の状態がない」というのが誤りで、**出荷状態こそが初期状態**だった。
        /// ここでは記録そのものを見る——地形は有効で、わざと無効にしたものは無効のまま。
        ///
        /// <b>一律有効化への落ちを数で見る。</b> <c>UniformOpenCount</c> が 0 でない Scene は
        /// 出荷状態の記録が抜けている。黙って落ちると、この工程の修正が
        /// Scene を作り直し忘れた日に静かに戻る。
        /// </summary>
        [UnityTest]
        public IEnumerator TheFirstOpen_RestoresTheShippedState([Values("A", "B")] string which)
        {
            yield return OpenArea(which == "A" ? P55AreaAScene : P55AreaBScene);

            var gate = Object.FindFirstObjectByType<AreaActivityGate>();
            Assert.IsNotNull(gate, "活動ゲートが居る。");
            Assert.IsTrue(gate.HasInitialState,
                "出荷状態の記録を持っている（Scene を作り直してあるか）。");
            Assert.AreEqual(0, gate.UniformOpenCount,
                "一律有効化へ落ちていない（落ちた回数=" + gate.UniformOpenCount + "）。");
            Assert.AreEqual(gate.GatedColliders.Count, gate.InitialColliderState.Count,
                "記録の件数が対象と一致している。");

            // <b>「全部有効」でも「全部無効」でもない</b>ことを見る。
            // 全部有効なら一律有効化と区別が付かず、全部無効なら地形が通れない。
            int enabledNow = 0;
            int shippedEnabled = 0;
            for (int i = 0; i < gate.GatedColliders.Count; i++)
            {
                if (gate.GatedColliders[i] != null && gate.GatedColliders[i].enabled)
                {
                    enabledNow++;
                }

                if (gate.InitialColliderState[i])
                {
                    shippedEnabled++;
                }
            }

            Assert.AreEqual(shippedEnabled, enabledNow,
                "いま有効な Collider の数が出荷状態と一致する（いま " + enabledNow
                + " / 出荷 " + shippedEnabled + " / 対象 " + gate.GatedColliders.Count + "）。");
            Assert.Greater(enabledNow, 0, "地形は通れる（1 枚も有効でないなら閉じたままである）。");

            if (which == "B")
            {
                Assert.Less(enabledNow, gate.GatedColliders.Count,
                    "B には<b>わざと無効で出荷したもの</b>がある（アリーナ封鎖）。"
                    + "全部有効なら一律有効化と区別が付かない。");
            }
        }

        private IEnumerator OpenAreaA() => OpenArea(P55AreaAScene);

        private IEnumerator OpenArea(string scenePath)
        {
            DestroyLaunchers();
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            _bootstrap = new GameObject("BootstrapRoot_P55ReentryTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded,
                "常駐が立つ。理由=" + BootstrapRoot.Instance.BootstrapFailure);
            DestroyLaunchers();

            yield return SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Single);
            yield return null;

            AreaInitializer initializer = FindInitializer();
            Assert.IsTrue(initializer.Initialized, "初期化が成立する。理由=" + initializer.FailureReason);
            yield return null;
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
    }
}
