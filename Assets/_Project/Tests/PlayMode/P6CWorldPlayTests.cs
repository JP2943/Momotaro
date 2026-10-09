using System.Collections;
using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Save;
using Momotaro.Infrastructure.World;
using Momotaro.Presentation.Combat;
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
    /// P6C の検証 campaign（P6B の構成を設定で再利用）を<b>実 Scene・実入力・配置された敵の通常の攻撃経路</b>で通す（P6C 仕様 §9・§10）。
    ///
    /// 回避は実キー Space（＋方向キー）、攻撃は J、ガードは K。敵の攻撃は敵自身の AI が始め、命中は敵の攻撃機械 → 実 Hitbox／矢 →
    /// 主人公の被弾入口の経路で起きる。時機を合わせるために敵の攻撃機械の経過（読み取り）だけを見る。
    /// 細かい刻みで時機を測るため、試行の間だけ timeScale を 0.5 にする（Gameplay 時間の判定は刻みに依らない。EditMode で確認）。
    /// 各試行の時刻と結果は <c>_bridge/p6c_just_evade_timeline.txt</c> へ書き出す。
    /// </summary>
    public sealed class P6CWorldPlayTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Tests/Phase6C/SO_AreaCatalog_P6C.asset";
        private const float TrialTimeScale = 0.5f;

        private static readonly StableId AreaA = new StableId("area_p6c_a");
        private static readonly StableId AreaB = new StableId("area_p6c_b");
        private static readonly StableId AreaC = new StableId("area_p6c_c");
        private static readonly StableId ExitAEast = new StableId("exit_p6_a_east");
        private static readonly StableId ExitBEast = new StableId("exit_p6_b_east");
        private static readonly StableId EncounterBSouth = new StableId("encounter_p6c_b_south");
        private static readonly StableId EncounterCBoss = new StableId("encounter_p6c_c_boss");
        private static readonly StableId ShrineA = new StableId("shrine_p6_a");
        private static readonly StableId ShrineC = new StableId("shrine_p6_c");

        private static readonly System.Reflection.FieldInfo MachineField = typeof(EnemyAttackController).GetField("_machine",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private GameObject _bootstrap;
        private Keyboard _keyboard;
        private string _saveDir;
        private int _expectedSlides;
        private readonly List<string> _log = new List<string>();

        [SetUp]
        public void SetUp()
        {
            P55ResidentRig.Reset();
            RemoveStrayDevices();
            ClearStatics();
            AreaPendingArrival.ResetDiagnostics();
            _saveDir = Path.Combine(Application.temporaryCachePath, "p6c_play_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDir);
            CampaignSaveService.TestDirectoryOverride = _saveDir;
            _expectedSlides = 0;
            _log.Clear();
            Time.timeScale = 1f;
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            Time.timeScale = 1f;
            if (_log.Count > 0)
            {
                File.AppendAllLines(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "_bridge",
                    "p6c_just_evade_timeline.txt"), _log);
            }

            if (GameModeProvider.Current != null && GameModeProvider.Current.Current != GameMode.Exploration)
            {
                GameModeProvider.Current.ChangeMode(GameMode.Exploration);
                yield return null;
            }

            Scene empty = SceneManager.CreateScene("P6CTestEmpty_" + System.Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene sc = SceneManager.GetSceneAt(i);
                if (sc != empty && sc.isLoaded)
                {
                    yield return SceneManager.UnloadSceneAsync(sc);
                }
            }

            yield return null;
            DestroyBootstrap();
            P55ResidentRig.Reset();
            ClearStatics();
            RemoveDevices();
            CampaignSaveService.TestDirectoryOverride = null;
            yield return null;
            try
            {
                if (Directory.Exists(_saveDir))
                {
                    Directory.Delete(_saveDir, true);
                }
            }
            catch (IOException)
            {
                // 片付けの失敗は検証結果に影響しない。
            }
        }

        // ================================================================ 1. 近接の実攻撃：ジャスト回避 → 反撃（早い通常回避と比べる）

        /// <summary>
        /// P6C 01・03・05・06・14（試遊 2・3）：A に配置された普通敵（近接）の通常の攻撃に、実キー Space＋W（敵の方へ）で合わせる。
        /// <list type="bullet">
        /// <item>判定が出る瞬間にステップ開始から 0.05〜0.12 秒 → ジャスト回避が 1 回成立、強化（1.5 倍・2 秒）を得る。敵の攻撃は中断されず続く。</item>
        /// <item>そのまま S＋J（敵の方を向いて攻撃）→ 段の開始で消費し、その一段が強化された HP ダメージを与える。</item>
        /// <item>同じ攻撃を早い通常回避（判定の 0.17 秒前に回避）で避けた後の反撃は通常のダメージ。強化の方が大きい。</item>
        /// <item>表示：活動中の Area の手応え演出がジャスト回避の音（SE_JustEvade）と点滅を出し、試遊表示が「ジャスト回避！」と保有中の表示を出す。</item>
        /// </list>
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator Melee_RealAttack_RealKeys_JustEvadeCounterHitsHarder_NormalEvadeDoesNot()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            yield return KnockDownDog();
            JustEvadeHudPresenter hud = InActiveScene<JustEvadeHudPresenter>();
            CombatFeedbackPresenter feedback = InActiveScene<CombatFeedbackPresenter>();
            Assert.IsNotNull(hud, "試遊表示が活動中の Area にある。");
            Assert.IsNotNull(feedback, "手応え演出が活動中の Area にある。");

            Attempt just = null;
            Attempt early = null;
            for (int i = 0; i < 14 && (just == null || early == null); i++)
            {
                bool wantJust = just == null && (early != null || i % 2 == 0);
                Attempt a = null;
                yield return MeleeAttempt(wantJust ? 0.085f : 0.17f, wantJust ? "ジャスト狙い" : "早い回避", i, x => a = x);
                if (a == null)
                {
                    continue;
                }

                if (wantJust && a.Outcome == HitResultKind.JustEvade && a.CounterHitKind == HitResultKind.Damage)
                {
                    just = a;
                }
                else if (!wantJust && a.Outcome != HitResultKind.JustEvade && a.Outcome != HitResultKind.Damage
                         && a.CounterHitKind == HitResultKind.Damage)
                {
                    early = a;
                }
            }

            Assert.IsNotNull(just, "実攻撃でジャスト回避と反撃が 1 回以上成立する。\n" + string.Join("\n", _log));
            Assert.IsNotNull(early, "早い通常回避と反撃が 1 回以上成立する。\n" + string.Join("\n", _log));

            // 成功の条件：判定の瞬間の経過が受付と無敵の重なり。報酬は 1 回。敵の攻撃は続いている。
            Assert.GreaterOrEqual(just.StepElapsedAtResult, 0.05f - 1e-4f);
            Assert.Less(just.StepElapsedAtResult, 0.12f);
            Assert.AreEqual(1, just.SuccessDelta, "1 ステップ 1 回。");
            Assert.IsTrue(just.EnemyStillAttacking, "ジャスト回避で敵の攻撃を中断しない（体幹反射・強制ひるみなし）。");
            Assert.AreEqual(0f, just.EnemyPoiseLossAtEvade, 1e-3f, "成功で敵の体幹を削らない。");
            Assert.IsTrue(just.Countered && just.ConsumedAtStart, "反撃の段の開始で消費し、その段が強化された。");
            Assert.IsFalse(early.Countered, "早い通常回避の後は強化なし。");

            // ダメージ：防御 10 の普通敵へ、通常 10（背後 11）→ 9（10）、強化 15（背後 16.5）→ 14（15）。
            CollectionAssert.Contains(new[] { 14, 15 }, just.CounterDamage, "強化された反撃。");
            CollectionAssert.Contains(new[] { 9, 10 }, early.CounterDamage, "通常の反撃。");
            Assert.Greater(just.CounterDamage, early.CounterDamage, "ジャスト回避の反撃の方が大きい。");

            // 表示と音（活動中の Area の配信元）。
            Assert.GreaterOrEqual(feedback.JustEvadeFeedbackCount, 1, "成功の点滅・揺れ。");
            Assert.IsNotNull(feedback.Se);
            Assert.GreaterOrEqual(just.SeJustEvadePlayed ? 1 : 0, 1, "成功の音（SE_JustEvade）が鳴った。");
            Assert.GreaterOrEqual(hud.ShownSuccessCount, 1, "「ジャスト回避！」を出した。");
            Assert.IsTrue(just.HudShowedCounter, "保有中の表示（反撃 ×1.5・残時間）が出ていた。");
            Assert.IsFalse(just.HudShowedCounterAfterAttack, "攻撃の開始で保有中の表示が消える。");
        }

        // ================================================================ 2. 射撃の実攻撃：矢をジャスト回避

        /// <summary>
        /// P6C 04（試遊 4 の射撃）：B 南の遭遇戦の遠距離敵の矢（飛び道具の経路）を、実キー Space＋W（射手の方へ）で避ける。
        /// 矢もジャスト回避の対象で、射手への体幹反射・ひるみはしない。矢は既存の命中規則どおり消える（反射しない）。
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator Projectile_RealArrow_RealKeys_JustEvade_NoReflectToArcher()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return KnockDownDog();
            AreaEncounterRunner runner = FindRunner(EncounterBSouth);
            yield return StartEncounter(runner);
            yield return WaitUntilOrTimeout(() => EnemiesOf(runner).Length >= 2, 5f);
            EnemyActor archer = null;
            foreach (EnemyActor e in EnemiesOf(runner))
            {
                if (e.name.Contains("Ranged"))
                {
                    archer = e;
                }
                else
                {
                    e.gameObject.SetActive(false); // 近接は止める（この検証は矢だけを見る）
                }
            }

            Assert.IsNotNull(archer, "遠距離敵がいる。");
            archer.SetAttackPowerScale(0.3f);
            EnemyAttackController shooter = archer.GetComponentInChildren<EnemyAttackController>();
            var (root, player, vitals, facing) = Active();
            int successBefore = player.JustEvadeSuccessCount;
            bool ok = false;
            for (int i = 0; i < 10 && !ok; i++)
            {
                RestoreVitals(vitals);
                // 射手の真南 5m に立ち、北（射手）を向く。射手が矢を放つのを待つ。
                Time.timeScale = 1f;
                float deadline = Time.realtimeSinceStartup + 12f;
                EnemyProjectile arrow = null;
                while (Time.realtimeSinceStartup < deadline && arrow == null)
                {
                    RestoreVitals(vitals);
                    EnemyAttackMachine m = Machine(shooter);
                    if (m == null || m.Current == EnemyAttackMachine.Phase.None)
                    {
                        Place(root, archer.transform.position + new Vector3(0f, 0f, -5f));
                        facing.ConfirmFromInput(Vector2.up);
                    }
                    else
                    {
                        Time.timeScale = TrialTimeScale;
                    }

                    arrow = LiveArrowInActiveScene();
                    yield return null;
                }

                if (arrow == null)
                {
                    _log.Add("射撃 試行 " + i + "：矢が出なかった");
                    continue;
                }

                // 矢と主人公の距離が「接近速度 25m/s × 0.085 秒＋半径」まで来たら、射手の方へ回避。
                var rec = new ResultRecorder(player);
                vitals.Results.AddListener(rec);
                float poiseBefore = archer.CurrentPoise;
                bool pressed = false;
                float pressDistance = 0f;
                float arrowDeadline = Time.realtimeSinceStartup + 4f;
                while (arrow != null && arrow.IsLive && Time.realtimeSinceStartup < arrowDeadline)
                {
                    float d = Vector3.Distance(Flat(arrow.transform.position), Flat(root.transform.position));
                    if (!pressed && d <= 0.45f + 25f * 0.085f + 10f * Time.deltaTime)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space, Key.W));
                        pressed = true;
                        pressDistance = d;
                    }
                    else if (pressed)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                    }

                    yield return null;
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
                vitals.Results.RemoveListener(rec);
                Time.timeScale = 1f;
                _log.Add("射撃 試行 " + i + "：押した距離=" + pressDistance.ToString("0.00") + " 結果=" + rec.Describe()
                         + " 射手の体幹 " + poiseBefore.ToString("0.0") + "→" + archer.CurrentPoise.ToString("0.0"));
                if (rec.First == HitResultKind.JustEvade)
                {
                    ok = true;
                    Assert.AreEqual(poiseBefore, archer.CurrentPoise, 1e-3f, "射手へ体幹反射しない。");
                    Assert.IsFalse(arrow != null && arrow.IsLive, "矢は既存の規則どおり消える（反射しない）。");
                }

                yield return WaitUntilOrTimeout(() => !player.IsStepping, 1f);
            }

            Assert.IsTrue(ok, "矢をジャスト回避できる。\n" + string.Join("\n", _log));
            Assert.AreEqual(successBefore + 1, player.JustEvadeSuccessCount);
            Assert.IsTrue(player.HasJustEvadeCounter || player.JustEvadeCounterExpireCount > 0, "強化を得た。");
        }

        // ================================================================ 3. 通常ガード不可の実攻撃：通常ガード失敗・ジャスガ成功

        /// <summary>
        /// P6C 09・10（試遊 1）：C の仮ボス（精鋭）のガード不能（刀の突き）を、まず K を押しっぱなしの通常ガードで受けて失敗（被弾）、
        /// 次の同じ攻撃を K のジャスガ（判定の直前に押す）で受けて成功（体幹反射）。予告中は「通常ガード不可・ジャスガ可能」を出す。
        /// ほかの攻撃は通常ガードで受ける（試行を続けるため HP・スタミナは保つ）。
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator Unblockable_RealEliteAttack_NormalGuardFails_JustGuardSucceeds_NoticeShown()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return SlideTo(ExitBEast, Key.D, Vector3.left, "B→C");
            yield return KnockDownDog();
            AreaEncounterRunner runner = FindRunner(EncounterCBoss);
            yield return StartEncounter(runner);
            yield return WaitUntilOrTimeout(() => EnemiesOf(runner).Length >= 1, 5f);
            EnemyActor elite = EnemiesOf(runner)[0];
            EnemyAttackController attack = elite.GetComponentInChildren<EnemyAttackController>();
            JustEvadeHudPresenter hud = InActiveScene<JustEvadeHudPresenter>();
            Assert.IsNotNull(hud);
            var (root, player, vitals, facing) = Active();
            // 犬丸の「かばう」はこの検証の対象外（主人公のガードを見る。かばうとの優先関係は P6C 11 の EditMode が確かめる）。
            vitals.SetGuardianResolver(null);

            HitResultKind? normalGuardResult = null;
            HitResultKind? justGuardResult = null;
            bool noticeSeen = false;
            float poiseLoss = 0f;
            float deadline = Time.realtimeSinceStartup + 240f;
            int unblockables = 0;
            while (Time.realtimeSinceStartup < deadline && (normalGuardResult == null || justGuardResult == null) && !elite.IsDefeated)
            {
                RestoreVitals(vitals);
                EnemyAttackMachine m = Machine(attack);
                bool attacking = m != null && m.Current != EnemyAttackMachine.Phase.None;
                if (!attacking)
                {
                    CompanionHitReceiver dog = Dog();
                    if (dog != null && !dog.Vitals.IsDown)
                    {
                        yield return KnockDownDog(); // 犬丸に「かばう」をさせない（主人公への命中を見る）
                    }

                    Place(root, elite.transform.position + new Vector3(0f, 0f, -1.6f));
                    facing.ConfirmFromInput(Vector2.up);
                    // 通常の攻撃は通常ガードで受ける。2 回目のガード不能を待つ間も K は押したまま。
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K));
                    yield return null;
                    continue;
                }

                if (attack.CurrentAttackClass != Momotaro.Data.Combat.EnemyAttackClass.Unblockable
                    || m.Current != EnemyAttackMachine.Phase.Prepare)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K));
                    yield return null;
                    continue;
                }

                // ガード不能の予兆（予兆の間だけ細かい刻みにする）。
                unblockables++;
                Time.timeScale = TrialTimeScale;
                bool tryJust = normalGuardResult != null;
                var rec = new ResultRecorder(player);
                vitals.Results.AddListener(rec);
                vitals.GuardianTransfers.AddListener(rec);
                Vector3 offsetAtStart = root.transform.position - elite.transform.position;
                float poiseBefore = elite.CurrentPoise;
                bool pressed = false;
                if (tryJust)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState()); // 一度離す（JG は押した瞬間の窓）
                }

                while (m.Current == EnemyAttackMachine.Phase.Prepare)
                {
                    RestoreVitals(vitals);
                    noticeSeen |= hud.IsShowingUnblockableNotice;
                    float remaining = m.Snapshot.PrepareSeconds - m.Elapsed;
                    if (remaining > 0.2f)
                    {
                        // ガードの押し戻しで届かない位置へずれていたら、狙いの線上（精鋭の正面 1.4m）へ戻す。
                        Place(root, elite.transform.position + attack.AimDirection.normalized * 1.4f);
                    }

                    if (tryJust && !pressed && remaining <= 0.06f + Time.deltaTime)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K));
                        pressed = true;
                    }
                    else if (!tryJust)
                    {
                        InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K));
                    }

                    yield return null;
                }

                float activeDeadline = Time.realtimeSinceStartup + 2f;
                while (m.Current == EnemyAttackMachine.Phase.Active && rec.First == null && Time.realtimeSinceStartup < activeDeadline)
                {
                    yield return null;
                }

                yield return null;
                Time.timeScale = 1f;
                vitals.Results.RemoveListener(rec);
                vitals.GuardianTransfers.RemoveListener(rec);
                _log.Add("位置（精鋭から）=" + offsetAtStart.ToString("0.00") + " 精鋭の向き=" + elite.Forward.ToString("0.0")
                         + " 狙い=" + attack.AimDirection.ToString("0.0") + " " + DescribeDog());
                _log.Add("ガード不能 " + unblockables + "（" + (tryJust ? "ジャスガ" : "通常ガード") + "）：結果=" + rec.Describe()
                         + " 精鋭の体幹 " + poiseBefore.ToString("0.0") + "→" + elite.CurrentPoise.ToString("0.0")
                         + " 予告の文言=" + noticeSeen);
                if (rec.First == null)
                {
                    continue; // 届かなかった（位置ずれ）。次の機会へ。
                }

                if (!tryJust)
                {
                    normalGuardResult = rec.First;
                }
                else
                {
                    justGuardResult = rec.First;
                    poiseLoss = poiseBefore - elite.CurrentPoise;
                }
            }

            Time.timeScale = 1f;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.AreEqual(HitResultKind.Damage, normalGuardResult, "通常ガードでは受け止められない。\n" + string.Join("\n", _log));
            Assert.AreEqual(HitResultKind.JustGuard, justGuardResult, "ジャスガは成立する。\n" + string.Join("\n", _log));
            Assert.Greater(poiseLoss, 0f, "ジャスガで体幹を崩す（既存の体幹反射）。");
            Assert.IsTrue(noticeSeen, "予告中に「通常ガード不可・ジャスガ可能」を出す。");
        }

        // ================================================================ 4. 保存・遷移・休息・Continue・きびだんご・二つの Area

        /// <summary>
        /// P6C 12・13・14（試遊 5）：
        /// <list type="bullet">
        /// <item>強化を持ったまま保存を採っても強化は消えない（保存しない一時状態）。失敗した移動要求でも消えない。</item>
        /// <item>通常エリア移動の成功（A→B のスライド）で消える。在留が二つある間、成功の表示と音は<b>活動中の Area の配信元だけ</b>が出す。</item>
        /// <item>持ったまま実キー F できびだんごを使っても、Space・K は効かず、使用は中断されない。強化は使用で消費されない。</item>
        /// <item>休息（お地蔵様）・死亡再開で消える。Continue の後は強化なし、徳（撃破報酬）は保たれている。</item>
        /// </list>
        /// 強化の付与は、この検証では実際のステップ（実キー Space）と既存の敵攻撃の命中生成（EnemyHitFactory）で行う
        /// （配置された敵の実攻撃での成立は上の 3 本が確かめる）。
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator Lifecycle_SaveKeeps_FailedMoveKeeps_SlideClears_HudOnlyActiveArea_Kibidango_RestRespawnContinue()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            QuietFieldEnemies();
            GameSessionState s = Session();

            // 保存の採取では消えない。
            yield return GrantByStep("保存前");
            var (rootA, playerA, vitalsA, _) = Active();
            Assert.IsTrue(playerA.HasJustEvadeCounter);
            s.Changes.RequestAutosave("p6c_test");
            yield return WaitSaved("採取");
            Assert.IsTrue(playerA.HasJustEvadeCounter, "保存の採取で強化を消さない。");

            // 失敗した移動要求（存在しない入口）では消えない。
            AreaTransitionDecision refused = Transitions().TryTravel(AreaB, new StableId("entry_p6c_missing"));
            Assert.IsFalse(refused.Accepted, "前提：要求が断られる。");
            yield return null;
            Assert.IsTrue(playerA.HasJustEvadeCounter, "失敗した移動要求では消さない。");

            // きびだんご（実キー F）：使用中は Space・K が効かない。中断されない。強化は消費されない。
            yield return GrantByStep("きびだんご前");
            yield return WaitUntilOrTimeout(() => !playerA.IsStepping, 1f);
            vitalsA.Vitals.Health.SetCurrent(40);
            s.RefillKibidango(3);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.F));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.IsTrue(playerA.IsUsingItem, "使用を開始（拒否=" + playerA.LastItemUseRejection + "）。");
            int consumed = playerA.JustEvadeCounterConsumeCount;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space, Key.K));
            yield return null;
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.K));
            yield return null;
            Assert.IsFalse(playerA.IsStepping, "使用中は回避しない。");
            Assert.IsFalse(playerA.IsGuarding, "使用中はガードしない。");
            Assert.IsTrue(playerA.IsUsingItem, "強化を持っていても使用は中断しない。");
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return WaitUntilOrTimeout(() => !playerA.IsUsingItem, 4f);
            Assert.AreEqual(1, playerA.ItemUseCommitCount, "使用は確定する。");
            Assert.AreEqual(consumed, playerA.JustEvadeCounterConsumeCount, "使用で強化を消費しない。");
            yield return WaitSaved("使用後");

            // 通常エリア移動の成功で消える（A→B）。在留の A・B の表示・音の配信元。
            yield return GrantByStep("移動前");
            JustEvadeHudPresenter hudA = InActiveScene<JustEvadeHudPresenter>();
            CombatFeedbackPresenter fbA = InActiveScene<CombatFeedbackPresenter>();
            int hudAShown = hudA.ShownSuccessCount;
            int fbAShown = fbA.JustEvadeFeedbackCount;
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            var (rootB, playerB, vitalsB, facingB) = Active();
            Assert.IsFalse(playerB.HasJustEvadeCounter, "通常エリア移動の成功で消す。");
            Assert.IsFalse(playerA != null && playerA.HasJustEvadeCounter, "移動元の主人公にも残さない。");
            JustEvadeHudPresenter hudB = InActiveScene<JustEvadeHudPresenter>();
            CombatFeedbackPresenter fbB = InActiveScene<CombatFeedbackPresenter>();
            Assert.AreNotSame(hudA, hudB, "前提：Area ごとの表示役。");
            hudB.Resolve();
            Assert.AreSame(playerB, hudB.BoundPlayer, "B の表示は B の主人公に結ぶ。");
            bool aResident = hudA != null;
            _log.Add("二つの Area：B 活動中に A が在留=" + aResident + "（A の表示 " + (aResident ? hudA.ShownSuccessCount.ToString() : "-")
                     + "、A の手応え " + (aResident ? fbA.JustEvadeFeedbackCount.ToString() : "-") + "）");
            yield return GrantByStep("B で成功");
            yield return null;
            yield return null;
            Assert.GreaterOrEqual(hudB.ShownSuccessCount, 1, "活動中の B が表示する。");
            Assert.GreaterOrEqual(fbB.JustEvadeFeedbackCount, 1, "活動中の B の手応え演出が出す。");
            if (aResident)
            {
                Assert.IsFalse(hudA.IsDisplayActive, "非活動の A は描かない。");
                Assert.AreEqual(hudAShown, hudA.ShownSuccessCount, "非活動の A に誤表示しない。");
                Assert.AreEqual(fbAShown, fbA.JustEvadeFeedbackCount, "非活動の A の手応え演出は出さない。");
            }

            // B → A へ戻る（再入場）→ A の表示が A の主人公に結び直されている。
            yield return SlideTo(new StableId("exit_p6_b_west"), Key.A, Vector3.right, "B→A");
            var (rootA2, playerA2, vitalsA2, _) = Active();
            // 出入口から離れる（後ろ向きの回避で出入口へ入ると、再び B へ渡ってしまう）。
            Place(rootA2, new Vector3(-3f, 0f, -3f));
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsFalse(playerA2.HasJustEvadeCounter, "再入場でも残さない。");
            JustEvadeHudPresenter hudA2 = InActiveScene<JustEvadeHudPresenter>();
            Assert.IsTrue(hudA2.IsDisplayActive, "再入場した A が描く。");
            int hudA2Before = hudA2.ShownSuccessCount;
            yield return GrantByStep("再入場後");
            yield return null;
            yield return null;
            Assert.Greater(hudA2.ShownSuccessCount, hudA2Before, "遷移後の購読先が切り替わっている。");

            // 休息（お地蔵様）で消える。
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            yield return GrantByStep("休息前");
            Assert.IsTrue(playerA2.HasJustEvadeCounter);
            yield return InteractShrine(ShrineA);
            Assert.AreEqual(ShrineMenuResult.Rested, shrines.Rest(), shrines.Message);
            shrines.Close();
            yield return null;
            Assert.IsFalse(Active().state.HasJustEvadeCounter, "休息で消す。");

            // 死亡再開で消える。
            yield return GrantByStep("死亡前");
            yield return KillPlayerWithRealHits();
            Assert.IsFalse(Active().state.HasJustEvadeCounter, "死亡で消す。");
            yield return WaitForRespawnPrompt();
            AreaTransitionService transitions = Transitions();
            int completed = transitions.CompletedCount;
            yield return PressKeyUntil(Key.Enter, () => transitions.CompletedCount > completed || transitions.HasTerminalFailure, 25f);
            yield return WaitAreaReady(AreaA);
            Assert.IsFalse(Active().state.HasJustEvadeCounter, "死亡再開の後も無い。");

            // Continue：強化なし、徳は保たれる。
            yield return GrantByStep("終了前");
            int virtue = Session().Progress.AvailableVirtue;
            s = Session();
            s.Changes.RequestAutosave("p6c_before_restart");
            yield return Restart();
            yield return ContinueAndWait(AreaA);
            Assert.IsFalse(Active().state.HasJustEvadeCounter, "Continue の後は強化なし（保存しない）。");
            Assert.AreEqual(virtue, Session().Progress.AvailableVirtue, "徳は保たれる。");
        }

        // ================================================================ 5. 出発側を閉じた後の失敗（レビュー a24d92c R1）

        /// <summary>
        /// P6C 12（レビュー a24d92c R1）：強化を持ったまま<b>通常エリア移動を受理</b>させ、出発側の活動ゲートが閉じて主人公が非 Active になった後に
        /// <b>到着側の準備がタイムアウト</b>して Rollback する（B を在留させてから B の初期化担当を取り除く。P55 のスライド試験と同じ作り方）。
        /// Rollback 後も権利が残り、残時間は凍結中（実時間 1 秒以上）に減らず、再開後は通常どおり減る。
        /// 成功の Commit で消えることは <see cref="Lifecycle_SaveKeeps_FailedMoveKeeps_SlideClears_HudOnlyActiveArea_Kibidango_RestRespawnContinue"/> が見る。
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator FailedSlide_AfterDepartureGateClosed_TimeoutRollback_KeepsCounter_FrozenThenTicks()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            QuietFieldEnemies();
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return SlideTo(new StableId("exit_p6_b_west"), Key.A, Vector3.right, "B→A（B を在留させる）");
            AreaTransitionService transitions = Transitions();

            // 到着側（在留している B）の初期化担当を取り除く → 到着準備の報告が来ず、BindTimeoutSeconds で Rollback する。
            int removed = 0;
            foreach (AreaInitializer initializer in Object.FindObjectsByType<AreaInitializer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (initializer != null && initializer.AreaId.Equals(AreaB))
                {
                    Object.DestroyImmediate(initializer);
                    removed++;
                }
            }

            Assert.AreEqual(1, removed, "前提：在留している B の初期化担当を 1 つ取り除いた。");
            transitions.BindTimeoutSeconds = 1.2f;

            var (root, player, vitals, _) = Active();
            Place(root, new Vector3(-3f, 0f, -3f));
            yield return new WaitForFixedUpdate();
            yield return GrantByStep("スライド失敗の前");
            Assert.IsTrue(player.HasJustEvadeCounter);

            AreaExitGate gate = FindExitGate(ExitAEast);
            Place(root, gate.transform.position + Vector3.left * 0.4f);
            yield return new WaitForFixedUpdate();
            int rolledBefore = transitions.SlideRolledBackCount;
            int committedBefore = transitions.SlideCommittedCount;
            float remainingAtFreeze = -1f;
            float realAtFreeze = 0f;
            bool playerWasInactive = false;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.SlideRolledBackCount == rolledBefore && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D));
                if (GameplayClockProvider.IsFrozen && remainingAtFreeze < 0f)
                {
                    remainingAtFreeze = player.JustEvadeCounterRemaining;
                    realAtFreeze = Time.realtimeSinceStartup;
                }

                playerWasInactive |= !player.isActiveAndEnabled;
                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            float frozenReal = Time.realtimeSinceStartup - realAtFreeze;
            _log.Add("スライド失敗：受理時の残り=" + remainingAtFreeze.ToString("0.000") + " Rollback 後の残り="
                     + player.JustEvadeCounterRemaining.ToString("0.000") + " 凍結の実時間=" + frozenReal.ToString("0.00")
                     + " 主人公が非 Active になった=" + playerWasInactive + " 理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(rolledBefore + 1, transitions.SlideRolledBackCount, "到着準備のタイムアウトで戻した。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(committedBefore, transitions.SlideCommittedCount, "成功扱いにしない。");
            Assert.AreEqual(AreaA.Value, CurrentAreaProvider.Current.AreaId.Value, "A へ戻った。");
            Assert.IsTrue(playerWasInactive, "前提：出発側の活動ゲートが閉じ、主人公が一時的に非 Active になった。");
            Assert.Greater(remainingAtFreeze, 0f, "前提：受理の時点で強化を持っていた。");
            Assert.GreaterOrEqual(frozenReal, 1.0f, "前提：凍結は実時間で 1 秒以上続いた。");
            Assert.IsTrue(player.HasJustEvadeCounter, "Rollback 後も権利が残る。");
            Assert.AreEqual(remainingAtFreeze, player.JustEvadeCounterRemaining, 0.02f, "凍結中の実時間では減らない。");
            float before = player.JustEvadeCounterRemaining;
            yield return WaitUntilOrTimeout(() => player.JustEvadeCounterRemaining < before - 0.1f || !player.HasJustEvadeCounter, 2f);
            Assert.Less(player.JustEvadeCounterRemaining, before - 0.1f + 1e-4f, "再開後は通常どおり減る。");
        }

        /// <summary>
        /// P6C 12（レビュー a24d92c R1）：旅立ち（お地蔵様からの遠隔移動）でも、強化を持ったまま受理 → 出発側の活動ゲートが閉じた後に
        /// 到着側の準備・配置が失敗（既存の故障注入 <c>FastTravelPrepareFault</c>）→ Rollback で権利が残り、凍結中に減らない。
        /// 故障を外してもう一度旅立つと成功し、出発側の主人公の権利は Commit で消え、到着側にも無い（到着後の休息）。
        /// </summary>
        [UnityTest, Timeout(540000)]
        public IEnumerator FastTravel_FailAfterDepartureGateClosed_KeepsCounter_SuccessClears()
        {
            yield return NewGame();
            yield return WaitSaved("New Game");
            QuietFieldEnemies();
            yield return SlideTo(ExitAEast, Key.D, Vector3.left, "A→B");
            yield return SlideTo(ExitBEast, Key.D, Vector3.left, "B→C");
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            yield return InteractShrine(ShrineC);
            shrines.Close();
            yield return null;
            yield return WaitSaved("C のお地蔵様");

            AreaTransitionService transitions = Transitions();
            var (root, player, vitals, _) = Active();
            yield return GrantByStep("旅立ち失敗の前");
            yield return InteractShrine(ShrineC);
            transitions.Slide.FastTravelPrepareFault = _ => "P6C 検証の故障注入（到着準備の後・Commit の前）";
            int rolledBefore = transitions.Slide.FastTravelRolledBackCount;
            float remainingAtStart = player.JustEvadeCounterRemaining;
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineA), shrines.Message);
            float realAtStart = Time.realtimeSinceStartup;
            bool playerWasInactive = false;
            float remainingAtFreeze = -1f;
            float deadline = Time.realtimeSinceStartup + 25f;
            while (transitions.Slide.FastTravelRolledBackCount == rolledBefore && Time.realtimeSinceStartup < deadline)
            {
                if (GameplayClockProvider.IsFrozen && remainingAtFreeze < 0f)
                {
                    remainingAtFreeze = player.JustEvadeCounterRemaining;
                }

                playerWasInactive |= !player.isActiveAndEnabled;
                yield return null;
            }

            float remainingAtRollback = player.JustEvadeCounterRemaining; // 戻った直後（再開の Tick より前）
            transitions.Slide.FastTravelPrepareFault = null;
            yield return WaitUntilOrTimeout(() => !transitions.Slide.IsTransitioning, 5f);
            _log.Add("旅立ち失敗：開始時の残り=" + remainingAtStart.ToString("0.000") + " 凍結時の残り=" + remainingAtFreeze.ToString("0.000")
                     + " 戻った直後の残り=" + remainingAtRollback.ToString("0.000") + " 少し後の残り="
                     + player.JustEvadeCounterRemaining.ToString("0.000") + " 実時間=" + (Time.realtimeSinceStartup - realAtStart).ToString("0.00")
                     + " 主人公が非 Active になった=" + playerWasInactive + " 理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(rolledBefore + 1, transitions.Slide.FastTravelRolledBackCount, "旅立ちが戻った。理由=" + transitions.Slide.LastFailure);
            Assert.AreEqual(AreaC.Value, CurrentAreaProvider.Current.AreaId.Value, "C に留まった。");
            Assert.IsTrue(playerWasInactive, "前提：出発側の活動ゲートが閉じ、主人公が一時的に非 Active になった。");
            Assert.IsTrue(player.HasJustEvadeCounter, "旅立ちの失敗では権利が残る。");
            Assert.Greater(remainingAtFreeze, 0f, "前提：凍結の時点で強化を持っていた。");
            Assert.AreEqual(remainingAtFreeze, remainingAtRollback, 0.02f, "凍結中に減らない。");

            // 故障を外して旅立つ → 成功。出発側（C）の主人公の権利は Commit で消え、到着（A）にも無い。
            yield return InteractShrine(ShrineC);
            Assert.IsTrue(player.HasJustEvadeCounter, "前提：持ったまま旅立つ。");
            int committedBefore = transitions.Slide.FastTravelCommittedCount;
            Assert.AreEqual(ShrineMenuResult.FastTravelStarted, shrines.FastTravel(ShrineA), shrines.Message);
            yield return WaitUntilOrTimeout(() => transitions.Slide.FastTravelCommittedCount > committedBefore, 25f);
            yield return WaitAreaReady(AreaA);
            Assert.AreEqual(committedBefore + 1, transitions.Slide.FastTravelCommittedCount, "旅立ちが成立した。");
            Assert.IsFalse(player != null && player.HasJustEvadeCounter, "出発側の主人公の権利は Commit で消える（在留しても復活しない）。");
            Assert.IsFalse(Active().state.HasJustEvadeCounter, "到着側の主人公にも無い。");
        }

        // ================================================================ 試行

        private sealed class Attempt
        {
            public HitResultKind Outcome = (HitResultKind)(-1);
            public float StepElapsedAtResult = -1f;
            public int SuccessDelta;
            public bool EnemyStillAttacking;
            public float EnemyPoiseLossAtEvade;
            public bool Countered;
            public bool ConsumedAtStart;
            public HitResultKind CounterHitKind = (HitResultKind)(-1);
            public int CounterDamage = -1;
            public bool SeJustEvadePlayed;
            public bool HudShowedCounter;
            public bool HudShowedCounterAfterAttack;
        }

        /// <summary>
        /// 近接の 1 試行：敵の正面 1.9m（南）に立って J の音で気付かせ、攻撃の予備動作の残りが <paramref name="lead"/> 秒になったら
        /// Space＋W（敵の方へ）。命中結果を記録し、ステップが終わったら S＋J で振り向いて反撃し、敵へ与えた HP を記録する。
        /// </summary>
        private IEnumerator MeleeAttempt(float lead, string label, int index, System.Action<Attempt> done)
        {
            var (root, player, vitals, facing) = Active();
            EnemyActor enemy = NearestAliveMelee(root.transform.position);
            if (enemy == null)
            {
                RebuildFieldEnemies();
                yield return null;
                yield return null;
                enemy = NearestAliveMelee(root.transform.position);
            }

            if (enemy == null)
            {
                _log.Add(label + " 試行 " + index + "：生きている敵がいない（" + DescribeEnemies() + "）");
                yield break;
            }

            // 試行の準備：犬丸は Down のまま（敵を倒されない・かばわれない）、敵は初期状態（HP・体幹）へ戻し、攻撃力を弱める。
            yield return KnockDownDog();
            enemy.ResetState();
            enemy.SetAttackPowerScale(0.3f);
            EnemyAttackController attack = enemy.GetComponentInChildren<EnemyAttackController>();
            JustEvadeHudPresenter hud = InActiveScene<JustEvadeHudPresenter>();

            // 予備動作に入るまで：敵の真南 1.9m に立ち、<b>背を向けて</b> J（音）で気付かせる（刀を当てて敵の攻撃を止めない）。
            Time.timeScale = 1f;
            float deadline = Time.realtimeSinceStartup + 12f;
            float nextTap = 0f;
            EnemyAttackMachine m = Machine(attack);
            while (Time.realtimeSinceStartup < deadline && !enemy.IsDefeated
                   && (m == null || m.Current != EnemyAttackMachine.Phase.Prepare))
            {
                RestoreVitals(vitals);
                CompanionHitReceiver dog = Dog();
                if (dog != null && !dog.Vitals.IsDown)
                {
                    yield return KnockDownDog(); // 犬丸が起き上がったら倒し直す（敵を攻撃して予備動作を止めないように）
                }

                // 敵からの向きを東西南北のどれかへ揃え、1.9m の帯から外れたときだけ置き直す（毎フレーム動かすと敵が持ち場へ戻り続ける）。
                Vector3 toPlayer = Flat(root.transform.position - enemy.transform.position);
                Vector3 axis = AxisOf(toPlayer);
                float dist = toPlayer.magnitude;
                if ((dist > 1.9f + 0.45f || dist < 1.9f - 0.35f || Vector3.Angle(toPlayer, axis) > 10f)
                    && (player.Current == PlayerState.Idle || player.Current == PlayerState.Move || player.Current == PlayerState.Attack))
                {
                    Place(root, enemy.transform.position + axis * 1.9f);
                }

                facing.ConfirmFromInput(new Vector2(axis.x, axis.z)); // 背を向ける

                if (Time.realtimeSinceStartup >= nextTap && player.Current != PlayerState.Hurt)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J));
                    nextTap = Time.realtimeSinceStartup + 2.5f;
                }
                else
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                }

                yield return null;
                m = Machine(attack);
            }

            if (m == null || m.Current != EnemyAttackMachine.Phase.Prepare)
            {
                _log.Add(label + " 試行 " + index + "：敵の攻撃が始まらなかった（敵 HP " + enemy.CurrentHp + " 状態 " + enemy.State
                         + " 主人公 " + player.Current + " " + DescribeDog() + "）");
                yield break;
            }

            float prepareAtStart = m.Snapshot.PrepareSeconds - m.Elapsed;
            Vector3 approach = -AxisOf(Flat(root.transform.position - enemy.transform.position)); // 敵の方へ
            Key stepKey = KeyOf(approach);

            // 予備動作の残りが lead 秒になったら回避（細かく測るため timeScale 0.5）。
            Time.timeScale = TrialTimeScale;
            var a = new Attempt();
            var rec = new ResultRecorder(player, attack, enemy);
            vitals.Results.AddListener(rec);
            int successBefore = player.JustEvadeSuccessCount;
            bool pressed = false;
            float remainingAtPress = 0f;
            while (m.Current == EnemyAttackMachine.Phase.Prepare)
            {
                RestoreVitals(vitals);
                float remaining = m.Snapshot.PrepareSeconds - m.Elapsed;
                if (!pressed && remaining <= lead + Time.deltaTime)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space, stepKey));
                    pressed = true;
                    remainingAtPress = remaining;
                }
                else if (pressed)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            float activeDeadline = Time.realtimeSinceStartup + 2f;
            while (m.Current == EnemyAttackMachine.Phase.Active && Time.realtimeSinceStartup < activeDeadline)
            {
                if (rec.First == HitResultKind.JustEvade && !a.HudShowedCounter)
                {
                    a.HudShowedCounter = hud != null && hud.IsShowingCounter;
                }

                yield return null;
            }

            yield return null;
            a.EnemyStillAttacking = rec.EnemyAttackingAtFirst && (m.Current == EnemyAttackMachine.Phase.Recovery
                                                                   || m.Current == EnemyAttackMachine.Phase.Active
                                                                   || rec.EnemyPhaseAfterFirst == EnemyAttackMachine.Phase.Recovery);
            vitals.Results.RemoveListener(rec);
            a.Outcome = rec.First ?? (HitResultKind)(-1);
            a.StepElapsedAtResult = rec.StepElapsedAtFirst;
            a.SuccessDelta = player.JustEvadeSuccessCount - successBefore;
            a.EnemyPoiseLossAtEvade = rec.EnemyPoiseBefore - rec.EnemyPoiseAtFirst;
            a.SeJustEvadePlayed = rec.SeAfterFirst == "SE_JustEvade";
            if (a.Outcome == HitResultKind.JustEvade && !a.HudShowedCounter)
            {
                a.HudShowedCounter = hud != null && hud.IsShowingCounter;
            }

            // 反撃：ステップが終わったら S＋J（南＝敵の方を向いて攻撃）。
            yield return WaitUntilOrTimeout(() => !player.IsStepping && player.Current != PlayerState.Hurt, 2f);
            bool hadCounter = player.HasJustEvadeCounter;
            int consumeBefore = player.JustEvadeCounterConsumeCount;
            int boostedBefore = player.CounterBoostedHitCount;
            var enemyRec = new EnemyHitRecorder(player);
            enemy.Results.AddListener(enemyRec);
            // 敵の方を向いて攻撃する（段の開始時の移動入力で向きが決まる）。回避で敵の体を抜けたか手前で止まったかで向きが変わる。
            Vector3 offsetAtCounter = root.transform.position - enemy.transform.position;
            Key toward = KeyOf(-AxisOf(Flat(offsetAtCounter)));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(toward, Key.J));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(toward));
            yield return null;
            a.ConsumedAtStart = player.JustEvadeCounterConsumeCount > consumeBefore && player.Current == PlayerState.Attack;
            a.HudShowedCounterAfterAttack = hud != null && hud.IsShowingCounter;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            float hitDeadline = Time.realtimeSinceStartup + 2f;
            while (enemyRec.Kind == null && Time.realtimeSinceStartup < hitDeadline)
            {
                yield return null;
            }

            enemy.Results.RemoveListener(enemyRec);
            a.Countered = player.CounterBoostedHitCount > boostedBefore;
            a.CounterHitKind = enemyRec.Kind ?? (HitResultKind)(-1);
            a.CounterDamage = enemyRec.Damage;
            Time.timeScale = 1f;
            _log.Add(label + " 試行 " + index + "：検出時の残り=" + prepareAtStart.ToString("0.000") + " 押した=" + pressed
                     + " 押した残り=" + remainingAtPress.ToString("0.000") + " 結果=" + rec.Describe()
                     + " 成功+" + a.SuccessDelta + " 敵は攻撃継続=" + a.EnemyStillAttacking
                     + " 強化あり=" + hadCounter + " 反撃=" + a.CounterHitKind + " HP " + a.CounterDamage
                     + " 強化された段=" + a.Countered + " 段の開始で消費=" + a.ConsumedAtStart
                     + " 音=" + rec.SeAfterFirst + " 保有表示=" + a.HudShowedCounter + "→" + a.HudShowedCounterAfterAttack
                     + " 反撃時の位置（敵から）=" + offsetAtCounter.ToString("0.00") + " 向き=" + player.Forward.ToString("0.0")
                     + " 敵の向き=" + enemy.Forward.ToString("0.0") + " " + DescribeDog());
            yield return WaitUntilOrTimeout(() => m.Current == EnemyAttackMachine.Phase.None && player.Current != PlayerState.Attack, 3f);
            done(a);
        }

        /// <summary>実際のステップ（実キー Space）中に、既存の敵攻撃の命中生成で受付中の命中を入れて強化を得る（付与の準備）。</summary>
        private IEnumerator GrantByStep(string label)
        {
            var (root, player, vitals, _) = Active();
            RestoreVitals(vitals);
            yield return WaitUntilOrTimeout(() => player.Current == PlayerState.Idle || player.Current == PlayerState.Move, 2f);
            int before = player.JustEvadeSuccessCount;
            float scale = Time.timeScale;
            Time.timeScale = 0.25f; // 受付 [0.05, 0.12) を細かい刻みで確実に捉える
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return WaitUntilOrTimeout(() => player.IsStepping && player.StepElapsed >= 0.06f, 2f);
            Time.timeScale = scale;
            Assert.IsTrue(player.IsStepping && player.CanJustEvade, label + "：前提（受付中のステップ。経過 " + player.StepElapsed
                + " 状態=" + player.Current + " スタミナ=" + vitals.CurrentStamina + " 入力有効=" + (PlayerInputProvider.Current != null && PlayerInputProvider.Current.Active)
                + " mode=" + GameModeProvider.Current?.Current + " 位置=" + root.transform.position + "）。");
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<Momotaro.Data.Combat.EnemyAttackData>(
                "Assets/_Project/Data/Enemies/SO_EnemyAttack_Melee_Normal.asset");
            vitals.ReceiveHit(EnemyHitFactory.Build(EnemyAttackSnapshot.From(data), 10f, null, vitals,
                -player.GuardForward, vitals.transform.position, HitId.Single(77000 + before)));
            Assert.AreEqual(before + 1, player.JustEvadeSuccessCount, label + "：ジャスト回避が成立する。");
            Assert.IsTrue(player.HasJustEvadeCounter);
            yield return WaitUntilOrTimeout(() => !player.IsStepping, 1f); // 回避の移動を終えてから次の操作へ
        }

        private sealed class ResultRecorder : IHitResultListener, IGuardianTransferListener
        {
            private readonly PlayerStateController _player;
            private readonly EnemyAttackController _attack;
            private readonly EnemyActor _enemy;
            private readonly List<string> _lines = new List<string>();

            public ResultRecorder(PlayerStateController player, EnemyAttackController attack = null, EnemyActor enemy = null)
            {
                _player = player;
                _attack = attack;
                _enemy = enemy;
                EnemyPoiseBefore = enemy != null ? enemy.CurrentPoise : 0f;
            }

            public HitResultKind? First;
            public float StepElapsedAtFirst = -1f;
            public bool EnemyAttackingAtFirst;
            public EnemyAttackMachine.Phase EnemyPhaseAfterFirst;
            public float EnemyPoiseBefore;
            public float EnemyPoiseAtFirst;
            public string SeAfterFirst = string.Empty;

            public void OnHitResult(in HitResult result)
            {
                _lines.Add(result.Kind + "@" + (_player.IsStepping ? _player.StepElapsed.ToString("0.000") : "-"));
                if (First != null)
                {
                    return;
                }

                First = result.Kind;
                StepElapsedAtFirst = _player.IsStepping ? _player.StepElapsed : -1f;
                EnemyAttackingAtFirst = _attack == null || _attack.IsAttacking;
                EnemyPhaseAfterFirst = _attack != null ? _attack.Phase : EnemyAttackMachine.Phase.None;
                EnemyPoiseAtFirst = _enemy != null ? _enemy.CurrentPoise : 0f;
                CombatFeedbackPresenter feedback = InActiveScene<CombatFeedbackPresenter>();
                SeAfterFirst = string.Empty;
                if (feedback != null && feedback.Se != null)
                {
                    // 配信は同期（結果 → 手応え）なので、この時点の最後に鳴った音を見る。
                    SeAfterFirst = feedback.Se.LastPlayedSeId ?? string.Empty;
                }
            }

            public void OnGuardianTransfer(in GuardianTransferEvent transfer) => _lines.Add("犬丸がかばった");

            public string Describe() => _lines.Count == 0 ? "命中なし" : string.Join(",", _lines);
        }

        private sealed class EnemyHitRecorder : IHitResultListener
        {
            private readonly PlayerStateController _player;

            public EnemyHitRecorder(PlayerStateController player) => _player = player;

            public HitResultKind? Kind;
            public int Damage = -1;

            public void OnHitResult(in HitResult result)
            {
                if (Kind != null || !ReferenceEquals(result.Attacker, _player))
                {
                    return;
                }

                Kind = result.Kind;
                Damage = Mathf.RoundToInt(result.AppliedDamage.Hp);
            }
        }

        // ================================================================ 補助

        private static (PlayerRoot root, PlayerStateController state, PlayerVitalsHolder vitals, PlayerFacing facing) Active()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            Assert.IsNotNull(bundle, "活動中のエリアがある。");
            Assert.IsTrue(bundle.TryResolve(out PlayerRoot root), "活動中のエリアに主人公がいる。");
            return (root, root.GetComponentInChildren<PlayerStateController>(), root.GetComponentInChildren<PlayerVitalsHolder>(),
                root.GetComponentInChildren<PlayerFacing>());
        }

        private static T InActiveScene<T>() where T : Component
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            return bundle != null && bundle.TryResolve(out T found) ? found : null;
        }

        private static EnemyAttackMachine Machine(EnemyAttackController attack) =>
            attack != null ? (EnemyAttackMachine)MachineField.GetValue(attack) : null;

        private static void RestoreVitals(PlayerVitalsHolder vitals)
        {
            if (vitals == null || vitals.IsDefeated)
            {
                return;
            }

            // HP とスタミナの<b>正本</b>を最大へ（表示用の値だけを書き換えると、内部のスタミナは減ったままで回避が出ない）。
            vitals.RestoreForWaveRecovery();
        }

        private static void Place(PlayerRoot root, Vector3 position)
        {
            root.transform.position = new Vector3(position.x, root.transform.position.y, position.z);
            if (root.Body != null)
            {
                root.Body.position = root.transform.position;
                root.Body.linearVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>水平の向きを東西南北のいずれかへ揃える（長さ 0 なら南）。</summary>
        private static Vector3 AxisOf(Vector3 v)
        {
            if (v.sqrMagnitude < 1e-6f)
            {
                return Vector3.back;
            }

            return Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        /// <summary>東西南北の向きに対応する移動キー（W＝北 +Z、S＝南、D＝東 +X、A＝西）。</summary>
        private static Key KeyOf(Vector3 axis) =>
            Mathf.Abs(axis.x) > Mathf.Abs(axis.z) ? (axis.x > 0f ? Key.D : Key.A) : (axis.z > 0f ? Key.W : Key.S);

        private static EnemyActor NearestAliveMelee(Vector3 from)
        {
            Scene scene = CurrentScene();
            EnemyActor best = null;
            float bestDist = float.MaxValue;
            foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                if (e == null || e.IsDefeated || !e.gameObject.activeInHierarchy || e.gameObject.scene != scene
                    || !e.name.Contains("Melee"))
                {
                    continue;
                }

                float d = Vector3.Distance(e.transform.position, from);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = e;
                }
            }

            return best;
        }

        private static string DescribeEnemies()
        {
            var sb = new System.Text.StringBuilder();
            foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.InstanceID))
            {
                sb.Append(e.name).Append(" scene=").Append(e.gameObject.scene.name).Append(" active=").Append(e.gameObject.activeInHierarchy)
                  .Append(" defeated=").Append(e.IsDefeated).Append("; ");
            }

            return sb.Length == 0 ? "敵 0" : sb.ToString();
        }

        private static void RebuildFieldEnemies()
        {
            foreach (AreaFieldEnemyDirector d in Object.FindObjectsByType<AreaFieldEnemyDirector>(FindObjectsSortMode.None))
            {
                if (d.gameObject.scene == CurrentScene())
                {
                    d.RebuildNow();
                }
            }
        }

        private static EnemyProjectile LiveArrowInActiveScene()
        {
            foreach (EnemyProjectile p in Object.FindObjectsByType<EnemyProjectile>(FindObjectsSortMode.None))
            {
                if (p != null && p.IsLive)
                {
                    return p;
                }
            }

            return null;
        }

        private static CompanionHitReceiver Dog()
        {
            CompanionHitReceiver found = null;
            foreach (CompanionHitReceiver d in Object.FindObjectsByType<CompanionHitReceiver>(FindObjectsSortMode.None))
            {
                if (d != null && d.gameObject.scene == CurrentScene())
                {
                    found = d;
                }
            }

            return found;
        }

        private static string DescribeDog()
        {
            CompanionHitReceiver dog = Dog();
            return dog == null ? "犬丸なし" : "犬丸 Down=" + dog.Vitals.IsDown + "@" + dog.transform.position;
        }

        private IEnumerator KnockDownDog()
        {
            CompanionHitReceiver dog = Dog();
            if (dog == null || dog.Vitals.IsDown)
            {
                yield break;
            }

            var attackerGo = new GameObject("P6CCompanionAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = dog.transform.position + Vector3.forward;
            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!dog.Vitals.IsDown && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                dog.ReceiveHit(new HitInfo(attacker, dog, Vector3.back, dog.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(9700 + hits)));
                for (int i = 0; i < 40 && !dog.Vitals.IsDown; i++)
                {
                    yield return null;
                }
            }

            Object.DestroyImmediate(attackerGo);
        }

        private static void QuietFieldEnemies()
        {
            foreach (AreaFieldEnemyDirector d in Object.FindObjectsByType<AreaFieldEnemyDirector>(FindObjectsSortMode.None))
            {
                d.gameObject.SetActive(false);
            }
        }

        private IEnumerator NewGame()
        {
            yield return StartBootstrap();
            CampaignAdventureFlow flow = Saves().Flow;
            Assert.IsNotNull(flow);
            Assert.IsTrue(flow.TryNewGame(Catalog(), out string error), "New Game を受理する。理由=" + error);
            yield return WaitAreaReady(AreaA);
            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        private IEnumerator StartBootstrap()
        {
            DestroyBootstrap();
            _bootstrap = new GameObject("BootstrapRoot_P6CWorldTest");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null;
            Assert.IsTrue(BootstrapRoot.Instance.BootstrapSucceeded, "常駐が立つ。理由=" + BootstrapRoot.Instance.BootstrapFailure);
            Saves().SaveDirectory = _saveDir;
        }

        private IEnumerator Restart()
        {
            yield return WaitSaved("再起動前");
            RemoveDevices();
            DestroyBootstrap();
            P55ResidentRig.Reset();
            ClearStatics();
            yield return null;
            yield return StartBootstrap();
        }

        private IEnumerator ContinueAndWait(StableId expectedArea)
        {
            CampaignAdventureFlow flow = Saves().Flow;
            bool continued = false;
            string failure = null;
            flow.Continued += () => continued = true;
            flow.ContinueFailed += reason => failure = reason;
            Assert.IsTrue(flow.TryContinue(Catalog(), out string error), "Continue を受理する。理由=" + error);
            yield return WaitUntilOrTimeout(() => continued || failure != null, 30f);
            Assert.IsNull(failure, "Continue が失敗した: " + failure);
            yield return WaitAreaReady(expectedArea);
            _keyboard = InputSystem.AddDevice<Keyboard>("P55Keyboard");
            yield return null;
        }

        private static IEnumerator WaitAreaReady(StableId areaId)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                AreaRuntimeBundle current = CurrentAreaProvider.Current;
                if (current != null && current.AreaId.Equals(areaId) && current.Context != null
                    && current.Context.IsAreaReady && !Transitions().IsTransitionUnsettled)
                {
                    break;
                }

                yield return null;
            }

            Assert.IsNotNull(CurrentAreaProvider.Current, "活動中のエリアがある。");
            Assert.AreEqual(areaId.Value, CurrentAreaProvider.Current.AreaId.Value, "到着したエリア。");
            yield return null;
        }

        private static IEnumerator WaitSaved(string label)
        {
            SaveCoordinator c = Saves().Coordinator;
            Assert.IsNotNull(c, label + "：保存の調停役がある。");
            float deadline = Time.realtimeSinceStartup + 10f;
            while ((c.IsDirty || c.IsWriting) && c.Status != SaveStatus.Failed && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreNotEqual(SaveStatus.Failed, c.Status, label + "：保存に失敗した: " + c.LastError);
            Assert.IsFalse(c.IsDirty, label + "：保存が終わらない（状態=" + c.Status + "）。");
        }

        private IEnumerator SlideTo(StableId exitId, Key key, Vector3 back, string label)
        {
            AreaTransitionService transitions = Transitions();
            _expectedSlides = transitions.SlideCommittedCount + 1;
            AreaExitGate gate = FindExitGate(exitId);
            Place(Active().root, gate.transform.position + back * 0.4f);
            yield return new WaitForFixedUpdate();
            yield return null;

            float deadline = Time.realtimeSinceStartup + 25f;
            float nextRelease = Time.realtimeSinceStartup + 1.5f;
            while (transitions.SlideCommittedCount < _expectedSlides && !transitions.HasTerminalFailure
                   && Time.realtimeSinceStartup < deadline)
            {
                bool release = Time.realtimeSinceStartup >= nextRelease;
                InputSystem.QueueStateEvent(_keyboard, release ? new KeyboardState() : new KeyboardState(key));
                if (release)
                {
                    nextRelease = Time.realtimeSinceStartup + 1.5f;
                    if (!gate.PlayerInside && transitions.SlideCommittedCount < _expectedSlides && !transitions.Slide.IsTransitioning)
                    {
                        Place(Active().root, gate.transform.position + back * 0.4f);
                    }
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            Assert.IsFalse(transitions.HasTerminalFailure, label + "：終端失敗。理由=" + transitions.TerminalFailureReason);
            Assert.AreEqual(_expectedSlides, transitions.SlideCommittedCount, label + "：スライドで渡れていない（失敗="
                + transitions.Slide.LastFailure + " 受付の拒否=" + transitions.Slide.Coordinator.LastRejection + "）。");

            float quietDeadline = Time.realtimeSinceStartup + 20f;
            int quiet = 0;
            while (quiet < 3 && Time.realtimeSinceStartup < quietDeadline)
            {
                yield return null;
                bool busy = transitions.Slide.IsTransitioning || transitions.Slide.IsRetireInFlight
                    || transitions.Slide.Preloader.HasLiveSceneOperation || transitions.IsSingleLoadInFlight;
                quiet = busy ? 0 : quiet + 1;
            }

            yield return null;
        }

        private IEnumerator InteractShrine(StableId shrineId)
        {
            ShrinePoint point = null;
            foreach (ShrinePoint p in Object.FindObjectsByType<ShrinePoint>(FindObjectsSortMode.None))
            {
                if (p.ShrineId.Equals(shrineId) && p.gameObject.scene == CurrentScene())
                {
                    point = p;
                }
            }

            Assert.IsNotNull(point, "お地蔵様 " + shrineId.Value + " がある。");
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            var (root, _, _, facing) = Active();
            Place(root, point.InteractionAnchor + new Vector3(0f, 0f, 1.0f));
            yield return new WaitForFixedUpdate();
            facing.ConfirmFromInput(Vector2.down);
            yield return null;
            int before = point.InteractCount;
            yield return PressKeyUntil(Key.E, () => point.InteractCount > before, 6f);
            Assert.Greater(point.InteractCount, before, "実キー E でお地蔵様を調べられる。");
            Assert.IsTrue(shrines.IsMenuOpen, "メニューが開く（" + shrines.Message + "）。");
        }

        private static IEnumerator StartEncounter(AreaEncounterRunner runner)
        {
            AreaEncounterTrigger trigger = null;
            foreach (AreaEncounterTrigger t in Object.FindObjectsByType<AreaEncounterTrigger>(FindObjectsSortMode.None))
            {
                if (TriggerRunner(t) == runner)
                {
                    trigger = t;
                }
            }

            Assert.IsNotNull(trigger, "遭遇戦 " + runner.EncounterId.Value + " の Trigger がある。");
            Place(Active().root, trigger.transform.position);
            yield return new WaitForFixedUpdate();
            yield return WaitUntilOrTimeout(() => runner.State == AreaEncounterState.Playing, 6f);
            Assert.AreEqual(AreaEncounterState.Playing, runner.State, "Trigger 進入で戦闘が始まる。拒否=" + runner.LastRejection);
        }

        private static AreaEncounterRunner TriggerRunner(AreaEncounterTrigger trigger)
        {
            System.Reflection.FieldInfo f = typeof(AreaEncounterTrigger).GetField("_runner",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return f != null ? f.GetValue(trigger) as AreaEncounterRunner : null;
        }

        private static EnemyActor[] EnemiesOf(AreaEncounterRunner runner)
        {
            AreaEncounterSpawner spawner = runner.transform.GetComponentInChildren<AreaEncounterSpawner>(true);
            var list = new List<EnemyActor>();
            foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
            {
                if (e != null && !e.IsDefeated && spawner != null && e.transform.IsChildOf(spawner.transform))
                {
                    list.Add(e);
                }
            }

            if (list.Count == 0)
            {
                foreach (EnemyActor e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None))
                {
                    if (e != null && !e.IsDefeated && e.gameObject.scene == CurrentScene()
                        && e.GetComponentInParent<AreaFieldEnemyDirector>() == null)
                    {
                        list.Add(e);
                    }
                }
            }

            return list.ToArray();
        }

        private static AreaEncounterRunner FindRunner(StableId encounterId)
        {
            foreach (AreaEncounterRunner r in Object.FindObjectsByType<AreaEncounterRunner>(FindObjectsSortMode.None))
            {
                if (r.EncounterId.Equals(encounterId) && r.gameObject.scene == CurrentScene())
                {
                    return r;
                }
            }

            Assert.Fail("遭遇戦 " + encounterId.Value + " が活動中のエリアにない。");
            return null;
        }

        private static Scene CurrentScene()
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            return bundle != null ? bundle.gameObject.scene : default;
        }

        private IEnumerator KillPlayerWithRealHits()
        {
            PlayerVitalsHolder vitals = Active().vitals;
            var attackerGo = new GameObject("P6CLethalAttacker");
            var attacker = attackerGo.AddComponent<LethalAttacker>();
            attackerGo.transform.position = vitals.transform.position + Vector3.forward;
            int hits = 0;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (!vitals.IsDefeated && Time.realtimeSinceStartup < deadline)
            {
                hits++;
                vitals.ReceiveHit(new HitInfo(attacker, vitals, Vector3.back, vitals.transform.position,
                    new HitDamage(9999, 0f, 0f), guardable: false, justGuardable: false, hitId: HitId.Single(9800 + hits)));
                yield return null;
            }

            Object.DestroyImmediate(attackerGo);
            yield return null;
            Assert.IsTrue(vitals.IsDefeated, "前提：主人公が倒れている。");
        }

        private static IEnumerator WaitForRespawnPrompt()
        {
            var view = Object.FindFirstObjectByType<CampaignRespawnView>();
            Assert.IsNotNull(view, "再開操作の表示がある。");
            float deadline = Time.realtimeSinceStartup + 8f;
            while (!view.IsShowing && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.IsTrue(view.IsShowing, "倒れたら再開操作が出る。");
        }

        private sealed class LethalAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public int ActorId => GetInstanceID();
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
        }

        private IEnumerator PressKeyUntil(Key key, System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                yield return null;
                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                yield return null;
                for (int i = 0; i < 10 && !condition(); i++)
                {
                    yield return null;
                }
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        private static IEnumerator WaitUntilOrTimeout(System.Func<bool> condition, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private static AreaExitGate FindExitGate(StableId exitId)
        {
            foreach (AreaExitGate gate in Object.FindObjectsByType<AreaExitGate>(FindObjectsSortMode.None))
            {
                if (gate != null && gate.ExitId.Equals(exitId) && gate.gameObject.scene == CurrentScene())
                {
                    return gate;
                }
            }

            Assert.Fail("出入口 '" + exitId.Value + "' が活動中のエリアにない。");
            return null;
        }

        private static AreaCatalogData Catalog()
        {
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaCatalogData>(CatalogPath);
            Assert.IsNotNull(data, "P6C のカタログがある（build-phase6c-world）。");
            return data;
        }

        private static AreaTransitionService Transitions()
        {
            AreaTransitionService service = BootstrapServices.Get<AreaTransitionService>();
            Assert.IsNotNull(service, "遷移サービスが常駐していない。");
            return service;
        }

        private static CampaignSaveService Saves()
        {
            CampaignSaveService service = BootstrapServices.Get<CampaignSaveService>();
            Assert.IsNotNull(service, "保存サービスが常駐していない。");
            return service;
        }

        private static GameSessionState Session()
        {
            Assert.IsNotNull(GameSessionProvider.Current, "Session がある。");
            return GameSessionProvider.Current;
        }

        private void DestroyBootstrap()
        {
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
            }

            if (_bootstrap != null)
            {
                Object.DestroyImmediate(_bootstrap);
                _bootstrap = null;
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
            ShrineOperationsProvider.Current = null;
            KibidangoUseProvider.Current = null;
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
                if (device != null && device.name != null
                    && (device.name.StartsWith("P55Keyboard") || device.name.StartsWith("P55Gamepad")))
                {
                    InputSystem.RemoveDevice(device);
                }
            }
        }
    }
}
