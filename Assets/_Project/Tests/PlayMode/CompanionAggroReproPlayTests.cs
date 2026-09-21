using System.Collections;
using System.Collections.Generic;
using System.Text;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Bootstrap;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 試遊で報告された「敵のヘイトが犬丸へ向かない」を<b>実 Scene・実 Prefab で再現し、数値を記録する</b>
    /// （試遊報告 2026-09-21。①敵は未認識 →②犬丸が先に殴る →③敵は主人公へ向かう →④目の前の犬丸を殴らない →⑤犬丸だけが殴り続ける）。
    ///
    /// 主人公は<b>一切操作しない</b>。犬丸だけが敵を殴る状況を作り、初撃からの時系列で
    /// 「獲得ヘイト・現在対象・認識段階と最終確認位置・交戦モード・敵の攻撃対象」を記録する。
    /// 期待する挙動（自分を殴っている相手を狙い、目の前の相手を殴る）を assert しているので、
    /// 現状では<b>失敗し、失敗メッセージに計測結果が出る</b>。是正後に緑になることで回帰を押さえる。
    /// </summary>
    public sealed class CompanionAggroReproPlayTests
    {
        private const string SceneName = "SCN_Phase4_CompanionTrial";

        /// <summary>初撃から、敵が「自分を殴っている相手」を狙い始めるまでに許す秒数（game time）。</summary>
        private const float ExpectSwitchWithinSeconds = 5f;

        /// <summary>計測する総時間（game time）。切替が遅れて起きる場合も記録に残すため、期待値より長く見る。</summary>
        private const float ObserveSeconds = 12f;

        private GameObject _bootstrap;
        private float _prevTimeScale;
        private Keyboard _keyboard;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();

            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
                yield return null;
            }

            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            _bootstrap = new GameObject("[BootstrapRoot:AggroRepro]");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.HasInstance && BootstrapRoot.Instance.BootstrapSucceeded, "常駐サービスが起動する。");

            _keyboard = InputSystem.AddDevice<Keyboard>("AggroReproKeyboard");
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.timeScale = _prevTimeScale;

            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && active.name == SceneName)
            {
                foreach (GameObject root in active.GetRootGameObjects())
                {
                    if (root != null)
                    {
                        Object.DestroyImmediate(root);
                    }
                }
            }

            if (_bootstrap != null)
            {
                Object.DestroyImmediate(_bootstrap);
                _bootstrap = null;
            }

            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
            yield return null;
        }

        /// <summary>
        /// 計測結果をプロジェクト直下の <c>_bridge/measure/&lt;名前&gt;.txt</c> へ書き出す。
        /// テストが緑でも数値を読めるようにするため（失敗メッセージだけが頼りだと、通った瞬間に計測値が見えなくなる）。
        /// <c>_bridge/</c> は git 管理外なので成果物を汚さない。
        /// </summary>
        private static void WriteMeasurement(string name, string report)
        {
            try
            {
                string dir = System.IO.Path.Combine(Application.dataPath, "..", "_bridge", "measure");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, name + ".txt"), report);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[計測] 書き出しに失敗: " + e.Message);
            }
        }

        private static List<T> All<T>() where T : Object
        {
            return new List<T>(Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        private static IEnumerator LoadTrial()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                "試遊 Scene が Build Settings に未登録です（" + SceneName + "）。");
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;
        }

        private static IEnumerator Teleport(PlayerRoot root, Vector3 position)
        {
            if (root.Body != null)
            {
                root.Body.position = position;
                root.Body.linearVelocity = Vector3.zero;
            }

            root.transform.position = position;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private static PerceptionTargetBinder FindPlayerThreatTarget()
        {
            foreach (PerceptionTargetBinder b in All<PerceptionTargetBinder>())
            {
                if (b.Faction == Momotaro.Gameplay.Combat.CombatFaction.Player)
                {
                    return b;
                }
            }

            return null;
        }

        /// <summary>
        /// ①〜⑤ の状況を作って計測する。
        ///
        /// 作り方：戦闘を明示開始し、敵が湧いたら<b>party に背を向けさせ</b>（視野 120°・背後認識 2m の外＝未認識＝①）、
        /// 主人公を敵から約 5.5m の位置へ置く（犬丸の索敵 8m の内側なので、犬丸だけが自分から接敵する＝②）。
        /// 以後、主人公は一切操作しない。
        /// </summary>
        [UnityTest]
        public IEnumerator OnlyCompanionAttacks_EnemyTurnsOnTheCompanion_AndFightsBack()
        {
            yield return LoadTrial();

            var stage = All<TrialStageController>()[0];
            var player = All<PlayerStateController>()[0];
            PlayerRoot playerRoot = player.GetComponentInParent<PlayerRoot>() ?? All<PlayerRoot>()[0];
            var playerVitals = All<PlayerVitalsHolder>()[0];
            var companion = All<CompanionActor>()[0];
            var companionCombat = companion.GetComponent<CompanionCombatController>();
            var companionBinder = companion.GetComponent<CompanionThreatBinder>();
            var companionReceiver = companion.GetComponent<CompanionHitReceiver>();
            PerceptionTargetBinder playerBinder = FindPlayerThreatTarget();
            Assert.IsNotNull(companionBinder, "犬丸に脅威登録（CompanionThreatBinder）がある。");
            Assert.IsNotNull(playerBinder, "主人公に脅威登録（PerceptionTargetBinder）がある。");

            Assert.IsTrue(stage.RequestCombatStart(), "戦闘開始。");

            float spawnDeadline = Time.realtimeSinceStartup + 10f;
            while (All<EnemyActor>().Count == 0)
            {
                Assert.Less(Time.realtimeSinceStartup, spawnDeadline, "Wave1 の敵が湧かない。");
                yield return null;
            }

            EnemyActor enemy = All<EnemyActor>()[0];
            var enemyThreat = enemy.GetComponent<EnemyThreatTracker>();
            var enemyPerception = enemy.GetComponent<EnemyPerception>();
            var enemyBrain = enemy.GetComponent<EnemyBrain>();
            var enemyAttack = enemy.GetComponent<EnemyAttackController>();
            Assert.IsNotNull(enemyThreat, "敵にヘイト追跡がある。");

            // ①：敵に背を向けさせ、主人公・犬丸を視野の外に置く。
            Vector3 enemyPos = enemy.transform.position;
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up); // party は -Z 側。
            Vector3 stand = enemyPos + new Vector3(0f, 0f, -5.5f);
            yield return Teleport(playerRoot, stand);
            yield return null;

            Assert.AreEqual(PerceptionPhase.Unaware, enemyPerception.Phase,
                "前提①：敵はまだ気づいていない（背を向けている）。");

            // ②：犬丸だけが接敵して殴る。主人公は無操作のまま。
            Time.timeScale = 2f; // 1 フレーム ≒ 0.033 秒。判定時間（Active 0.12 秒）を跨がない。
            float engageDeadline = Time.realtimeSinceStartup + 20f;
            while (companionCombat.HitCount == 0)
            {
                if (Time.realtimeSinceStartup > engageDeadline)
                {
                    Time.timeScale = 1f;
                    Assert.Fail("前提②：犬丸が敵を一度も殴れなかった。"
                        + " 犬丸=" + companion.WorldPosition + "/" + companion.State
                        + " 判断=" + companionCombat.Decision
                        + " 距離=" + Vector3.Distance(companion.WorldPosition, enemy.transform.position).ToString("0.00")
                        + " 敵=" + enemy.transform.position + "/" + enemy.State);
                }

                yield return null;
            }

            // ③〜⑤：初撃からの時系列を記録する。
            var log = new StringBuilder();
            log.Append('\n').Append("t(s) | 敵HP | 犬丸ヘイト | 主人公ヘイト | 現在対象 | 認識 | 最終確認位置 | 交戦 | 敵の攻撃対象 | 敵→犬丸 | 敵→主人公 | 犬丸の命中数");
            log.Append('\n').Append("---");

            float elapsed = 0f;
            float nextSample = 0f;
            float switchedAt = -1f;
            float firstEnemyAttackAt = -1f;
            int companionDamageTaken = 0;
            int playerHpStart = playerVitals.Vitals.Health.Current;
            int companionHpStart = companionReceiver.CurrentHp;

            while (elapsed < ObserveSeconds && !enemy.IsDefeated)
            {
                elapsed += Time.deltaTime;

                if (switchedAt < 0f && enemyThreat.CurrentTargetId == companionBinder.ActorId)
                {
                    switchedAt = elapsed;
                }

                if (firstEnemyAttackAt < 0f && enemyAttack != null && enemyAttack.IsAttacking)
                {
                    firstEnemyAttackAt = elapsed;
                }

                if (elapsed >= nextSample)
                {
                    nextSample += 0.5f;
                    string targetName = enemyThreat.CurrentTargetId == companionBinder.ActorId ? "犬丸"
                        : enemyThreat.CurrentTargetId == playerBinder.ActorId ? "主人公" : "なし";
                    string attackTargetName = enemyAttack == null || enemyAttack.AttackTargetId == 0 ? "-"
                        : enemyAttack.AttackTargetId == companionBinder.ActorId ? "犬丸"
                        : enemyAttack.AttackTargetId == playerBinder.ActorId ? "主人公" : "?";
                    log.Append('\n')
                        .Append(elapsed.ToString("0.0")).Append(" | ")
                        .Append(enemy.CurrentHp).Append(" | ")
                        .Append(enemyThreat.AcquiredOf(companionBinder).ToString("0.0")).Append(" | ")
                        .Append(enemyThreat.AcquiredOf(playerBinder).ToString("0.0")).Append(" | ")
                        .Append(targetName).Append(" | ")
                        .Append(enemyPerception.Phase).Append(" | ")
                        .Append(enemyPerception.HasLastKnownPosition ? Near(enemyPerception.LastKnownPosition, companion.WorldPosition, playerRoot.transform.position) : "なし").Append(" | ")
                        .Append(enemyBrain != null ? enemyBrain.Mode.ToString() : "-").Append(" | ")
                        .Append(attackTargetName).Append(" | ")
                        .Append(Vector3.Distance(enemy.transform.position, companion.WorldPosition).ToString("0.00")).Append(" | ")
                        .Append(Vector3.Distance(enemy.transform.position, playerRoot.transform.position).ToString("0.00")).Append(" | ")
                        .Append(companionCombat.HitCount);
                }

                yield return null;
            }

            Time.timeScale = 1f;
            companionDamageTaken = companionHpStart - companionReceiver.CurrentHp;
            int playerDamageTaken = playerHpStart - playerVitals.Vitals.Health.Current;

            log.Append('\n').Append("---");
            log.Append('\n').Append("犬丸が現在対象になった時刻: ")
                .Append(switchedAt < 0f ? "ならなかった" : switchedAt.ToString("0.0") + " 秒");
            log.Append('\n').Append("敵が最初に攻撃を開始した時刻: ")
                .Append(firstEnemyAttackAt < 0f ? "一度も攻撃しなかった" : firstEnemyAttackAt.ToString("0.0") + " 秒");
            log.Append('\n').Append("犬丸の総命中数: ").Append(companionCombat.HitCount)
                .Append(" / 敵 HP: ").Append(enemy.CurrentHp).Append(" / 敵は撃破された: ").Append(enemy.IsDefeated);
            log.Append('\n').Append("この間に受けた被害 — 犬丸: ").Append(companionDamageTaken)
                .Append(" / 主人公: ").Append(playerDamageTaken).Append("（主人公は無操作）");
            log.Append('\n').Append("基礎ヘイト — 主人公: ").Append(playerBinder.BaseThreat)
                .Append(" / 犬丸: ").Append(companionBinder.BaseThreat)
                .Append("、犬丸の獲得倍率: ").Append(companionBinder.AcquiredThreatMultiplier);

            string report = log.ToString();

            Assert.GreaterOrEqual(firstEnemyAttackAt, 0f,
                "敵は殴られている間、無抵抗のままではいけない（誰かへ攻撃を開始する）。" + report);
            Assert.GreaterOrEqual(switchedAt, 0f,
                "敵は自分を殴っている犬丸を狙うべき。" + report);
            Assert.LessOrEqual(switchedAt, ExpectSwitchWithinSeconds,
                "犬丸が狙われるまでが遅すぎる（" + ExpectSwitchWithinSeconds + " 秒以内を期待）。" + report);
        }

        /// <summary>
        /// ④ の「敵は犬丸に引っかかっているのに犬丸を殴らない」を、主人公が後退し続ける条件で確かめる。
        ///
        /// 主人公が下がり続けると、敵は（ヘイト最大の）主人公を追い続けて停止帯へ入れない。停止帯に入らなければ
        /// 攻撃は一度も始まらないので、目の前で殴ってくる犬丸に対して<b>無抵抗のまま押し込まれる</b>——という筋書きの検証。
        /// 攻撃は誰に対してでもよいが、殴られ続けて何もしないのは不具合として扱う。
        /// </summary>
        [UnityTest]
        public IEnumerator RetreatingPlayer_EnemyKeepsChasingHim_AndStillFightsBack()
        {
            yield return LoadTrial();

            var stage = All<TrialStageController>()[0];
            var player = All<PlayerStateController>()[0];
            PlayerRoot playerRoot = player.GetComponentInParent<PlayerRoot>() ?? All<PlayerRoot>()[0];
            var companion = All<CompanionActor>()[0];
            var companionCombat = companion.GetComponent<CompanionCombatController>();
            var companionBinder = companion.GetComponent<CompanionThreatBinder>();

            Assert.IsTrue(stage.RequestCombatStart(), "戦闘開始。");
            float spawnDeadline = Time.realtimeSinceStartup + 10f;
            while (All<EnemyActor>().Count == 0)
            {
                Assert.Less(Time.realtimeSinceStartup, spawnDeadline, "Wave1 の敵が湧かない。");
                yield return null;
            }

            EnemyActor enemy = All<EnemyActor>()[0];
            var enemyThreat = enemy.GetComponent<EnemyThreatTracker>();
            var enemyAttack = enemy.GetComponent<EnemyAttackController>();
            var enemyBrain = enemy.GetComponent<EnemyBrain>();

            Vector3 enemyPos = enemy.transform.position;
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            yield return Teleport(playerRoot, enemyPos + new Vector3(0f, 0f, -5.5f));
            yield return null;

            Time.timeScale = 2f;
            float engageDeadline = Time.realtimeSinceStartup + 20f;
            while (companionCombat.HitCount == 0)
            {
                if (Time.realtimeSinceStartup > engageDeadline)
                {
                    Time.timeScale = 1f;
                    Assert.Fail("前提：犬丸が敵を一度も殴れなかった。");
                }

                yield return null;
            }

            // 主人公は敵に背を向けて後退し続ける（敵の移動速度 3.5 より遅い 2.0 m/s。壁の手前で止める）。
            const float retreatSpeed = 2.0f;
            const float minZ = -9f;
            float elapsed = 0f;
            float firstEnemyAttackAt = -1f;
            float switchedAt = -1f;
            float minDistanceToPlayer = float.MaxValue;
            float minDistanceToCompanion = float.MaxValue;
            var modes = new HashSet<string>();

            while (elapsed < ObserveSeconds && !enemy.IsDefeated)
            {
                elapsed += Time.deltaTime;

                Vector3 p = playerRoot.transform.position;
                if (p.z > minZ)
                {
                    p.z -= retreatSpeed * Time.deltaTime;
                    if (playerRoot.Body != null)
                    {
                        playerRoot.Body.position = p;
                    }

                    playerRoot.transform.position = p;
                }

                if (firstEnemyAttackAt < 0f && enemyAttack != null && enemyAttack.IsAttacking)
                {
                    firstEnemyAttackAt = elapsed;
                }

                if (switchedAt < 0f && enemyThreat.CurrentTargetId == companionBinder.ActorId)
                {
                    switchedAt = elapsed;
                }

                minDistanceToPlayer = Mathf.Min(minDistanceToPlayer, Vector3.Distance(enemy.transform.position, playerRoot.transform.position));
                minDistanceToCompanion = Mathf.Min(minDistanceToCompanion, Vector3.Distance(enemy.transform.position, companion.WorldPosition));
                if (enemyBrain != null)
                {
                    modes.Add(enemyBrain.Mode.ToString());
                }

                yield return null;
            }

            Time.timeScale = 1f;

            string report = "\n主人公が 2.0 m/s で後退し続けた " + ObserveSeconds + " 秒間の結果"
                + "\n敵が最初に攻撃を開始した時刻: " + (firstEnemyAttackAt < 0f ? "一度も攻撃しなかった" : firstEnemyAttackAt.ToString("0.0") + " 秒")
                + "\n犬丸が現在対象になった時刻: " + (switchedAt < 0f ? "ならなかった" : switchedAt.ToString("0.0") + " 秒")
                + "\n敵→主人公 の最小距離: " + minDistanceToPlayer.ToString("0.00") + "（停止帯 1.3m）"
                + "\n敵→犬丸 の最小距離: " + minDistanceToCompanion.ToString("0.00")
                + "\n観測した交戦モード: " + string.Join(",", modes)
                + "\n犬丸の命中数: " + companionCombat.HitCount + " / 敵 HP: " + enemy.CurrentHp;

            Assert.GreaterOrEqual(firstEnemyAttackAt, 0f,
                "殴られ続けている敵が、主人公を追うだけで一度も攻撃しないのはおかしい（目の前の犬丸に無抵抗）。" + report);
        }

        /// <summary>
        /// 難易度の確認（試遊フィードバック 2026-09-21 その 2）。「仲間がヘイトを持ち続けて主人公が殴り放題になる」かを測る。
        ///
        /// 犬丸に先制させたあと、<b>主人公が実キー J で殴り続ける</b>。狙いが主人公へ戻るまでの秒数と、
        /// 戻る前に敵が死んでしまわないかを記録する。蓄積速度（毎秒）も出すので、HP の多い敵での挙動を外挿できる。
        /// 期待は「主人公が本気で殴れば、戦闘が終わる前に狙いが戻ってくる」。
        /// </summary>
        [UnityTest]
        public IEnumerator AfterTheCompanionOpens_AFightingPlayerTakesAggroBack_BeforeTheEnemyDies()
        {
            yield return LoadTrial();

            var stage = All<TrialStageController>()[0];
            var player = All<PlayerStateController>()[0];
            PlayerRoot playerRoot = player.GetComponentInParent<PlayerRoot>() ?? All<PlayerRoot>()[0];
            var companion = All<CompanionActor>()[0];
            var companionCombat = companion.GetComponent<CompanionCombatController>();
            var companionBinder = companion.GetComponent<CompanionThreatBinder>();
            PerceptionTargetBinder playerBinder = FindPlayerThreatTarget();

            Assert.IsTrue(stage.RequestCombatStart(), "戦闘開始。");
            float spawnDeadline = Time.realtimeSinceStartup + 10f;
            while (All<EnemyActor>().Count == 0)
            {
                Assert.Less(Time.realtimeSinceStartup, spawnDeadline, "Wave1 の敵が湧かない。");
                yield return null;
            }

            EnemyActor enemy = All<EnemyActor>()[0];
            var enemyThreat = enemy.GetComponent<EnemyThreatTracker>();
            int enemyMaxHp = enemy.CurrentHp;

            // 犬丸に先制させる（敵は背を向けた状態＝未認識から）。
            Vector3 enemyPos = enemy.transform.position;
            enemy.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
            yield return Teleport(playerRoot, enemyPos + new Vector3(0f, 0f, -5.5f));
            yield return null;

            Time.timeScale = 2f;
            float engageDeadline = Time.realtimeSinceStartup + 20f;
            while (companionCombat.HitCount == 0)
            {
                if (Time.realtimeSinceStartup > engageDeadline)
                {
                    Time.timeScale = 1f;
                    Assert.Fail("前提：犬丸が先制できなかった。");
                }

                yield return null;
            }

            // 口火の切替は再評価（1 秒ごと）を待つ。
            float switchDeadline = Time.realtimeSinceStartup + 5f;
            while (enemyThreat.CurrentTargetId != companionBinder.ActorId)
            {
                if (Time.realtimeSinceStartup > switchDeadline)
                {
                    Time.timeScale = 1f;
                    Assert.Fail("前提：口火で狙いが犬丸へ移らなかった。");
                }

                yield return null;
            }

            // ここから主人公が殴り続ける。敵は犬丸を追って動くので、毎フレーム敵の手前へ位置を合わせ、
            // 向きも敵へ向けたうえで実キー J を連打する（「主人公が本気で張り付いて殴る」状況の再現）。
            var facing = playerRoot.GetComponentInChildren<PlayerFacing>();
            Assert.IsNotNull(facing, "主人公の向き（PlayerFacing）がある。");
            yield return Teleport(playerRoot, enemy.transform.position + new Vector3(0f, 0f, -1.0f));
            yield return null;

            float start = 0f;
            float elapsed = 0f;
            float backToPlayerAt = -1f;
            float enemyDiedAt = -1f;
            float nextPress = 0f;
            bool pressed = false;
            var log = new StringBuilder();
            log.Append('\n').Append("t(s) | 敵HP | 犬丸ヘイト | 主人公ヘイト | 現在対象");
            float nextSample = 0f;

            while (elapsed < ObserveSeconds)
            {
                elapsed += Time.deltaTime;

                // 敵の手前（-Z 側 1.0m）へ張り付き、敵の方（+Z＝Up）を向く。
                if (!enemy.IsDefeated)
                {
                    Vector3 stick = enemy.transform.position + new Vector3(0f, 0f, -1.0f);
                    if (playerRoot.Body != null)
                    {
                        playerRoot.Body.position = stick;
                        playerRoot.Body.linearVelocity = Vector3.zero;
                    }

                    playerRoot.transform.position = stick;
                    facing.ConfirmFromInput(Vector2.up);
                }

                // 実入力で 3 連撃を回す（押して離すを繰り返す）。
                if (elapsed >= nextPress)
                {
                    pressed = !pressed;
                    InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.J) : new KeyboardState());
                    nextPress = elapsed + 0.12f;
                }

                if (backToPlayerAt < 0f && enemyThreat.CurrentTargetId == playerBinder.ActorId)
                {
                    backToPlayerAt = elapsed;
                }

                if (enemyDiedAt < 0f && enemy.IsDefeated)
                {
                    enemyDiedAt = elapsed;
                }

                if (elapsed >= nextSample)
                {
                    nextSample += 0.5f;
                    string who = enemyThreat.CurrentTargetId == companionBinder.ActorId ? "犬丸"
                        : enemyThreat.CurrentTargetId == playerBinder.ActorId ? "主人公" : "なし";
                    log.Append('\n')
                        .Append(elapsed.ToString("0.0")).Append(" | ")
                        .Append(enemy.CurrentHp).Append(" | ")
                        .Append(enemyThreat.AcquiredOf(companionBinder).ToString("0.0")).Append(" | ")
                        .Append(enemyThreat.AcquiredOf(playerBinder).ToString("0.0")).Append(" | ")
                        .Append(who);
                }

                if (enemy.IsDefeated && elapsed > enemyDiedAt + 1f)
                {
                    break; // 撃破後は 1 秒だけ記録して終える。
                }

                yield return null;
            }

            Time.timeScale = 1f;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());

            float playerRate = backToPlayerAt > 0f
                ? enemyThreat.AcquiredOf(playerBinder) / Mathf.Max(0.01f, elapsed - start)
                : enemyThreat.AcquiredOf(playerBinder) / Mathf.Max(0.01f, elapsed);
            float dogRate = enemyThreat.AcquiredOf(companionBinder) / Mathf.Max(0.01f, elapsed);

            string report = log.ToString()
                + "\n---"
                + "\n狙いが主人公へ戻った時刻: " + (backToPlayerAt < 0f ? "戻らなかった" : backToPlayerAt.ToString("0.0") + " 秒")
                + "\n敵が倒れた時刻: " + (enemyDiedAt < 0f ? "生存（HP " + enemy.CurrentHp + "/" + enemyMaxHp + "）" : enemyDiedAt.ToString("0.0") + " 秒")
                + "\n蓄積速度（この計測区間の平均）: 主人公 " + playerRate.ToString("0.0") + "/秒 / 犬丸 " + dogRate.ToString("0.0") + "/秒"
                + "\n最終の獲得ヘイト: 主人公 " + enemyThreat.AcquiredOf(playerBinder).ToString("0.0")
                + " / 犬丸 " + enemyThreat.AcquiredOf(companionBinder).ToString("0.0")
                + "\n犬丸の命中数: " + companionCombat.HitCount;

            WriteMeasurement("player_takes_aggro_back", report);

            Assert.GreaterOrEqual(backToPlayerAt, 0f,
                "主人公が殴り続けても狙いが戻らないなら、仲間がヘイトを抱え込みすぎている。" + report);
            Assert.IsTrue(enemyDiedAt < 0f || backToPlayerAt <= enemyDiedAt,
                "狙いが戻る前に敵が死んでいる（＝主人公は実質ずっと殴り放題）。" + report);
        }

        /// <summary>最終確認位置が誰の位置に近いかを読みやすく表す。</summary>
        private static string Near(Vector3 lkp, Vector3 companionPos, Vector3 playerPos)
        {
            float toCompanion = Vector3.Distance(lkp, companionPos);
            float toPlayer = Vector3.Distance(lkp, playerPos);
            string who = toCompanion <= toPlayer ? "犬丸寄り" : "主人公寄り";
            return who + "(犬" + toCompanion.ToString("0.0") + "/人" + toPlayer.ToString("0.0") + ")";
        }
    }
}
