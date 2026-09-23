using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Momotaro.Core.Identification;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Core.World;
using Momotaro.Data;
using Momotaro.Data.Combat;
using Momotaro.Data.World;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Transfer;
using Momotaro.Gameplay.Vitals;
using Momotaro.Tests.Support;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P5 の純粋契約テスト（仕様書 Momotaro_P5_Detailed_Spec_v1.1.md §15.2）。
    /// 実 Scene・実入力・実 Collider を必要としない判定をここに置く。Scene を通す検証は
    /// <c>P5ExplorationPlayTests</c>、Builder／Validator は <c>P5ValidatorTests</c>。
    ///
    /// 本クラスは工程ごとに増える。現時点で実装済みなのは P5-01（E01〜E05）。
    /// </summary>
    public sealed class P5ContractTests : CompanionActivityFixture
    {
        private readonly List<UnityEngine.Object> _spawned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _spawned)
            {
                if (o != null)
                {
                    UnityEngine.Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private PlayerProgressHolder NewHolder(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<PlayerProgressHolder>();
        }

        private InvestigationRecordHolder NewRecordHolder(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<InvestigationRecordHolder>();
        }

        private static RewardSnapshot Reward(string id, int virtue, bool grantOnce)
        {
            return new RewardSnapshot(new StableId(id), virtue, default, grantOnce);
        }

        private static readonly StableId AreaA = new StableId("area_p5_a");
        private static readonly StableId AreaB = new StableId("area_p5_b");

        // ---------------------------------------------------------------- E01

        /// <summary>
        /// P5-E01：外部 State を Bind した Holder が、参照の同一性・徳・GrantOnce 記録を維持する。
        /// Scene をまたいで Holder が作り直されても、Session の State が正本であり続けることを固定する（§4.2）。
        /// </summary>
        [Test]
        public void BoundProgress_PreservesIdentityVirtueAndGrantOnce()
        {
            var session = new GameSessionState();

            PlayerProgressHolder a = NewHolder("AreaA_Progress");
            Assert.IsTrue(a.Bind(session.Progress), "活動開始前の注入は成立する。");
            Assert.AreSame(session.Progress, a.State, "Holder の正本は Session の State そのもの。");
            Assert.AreNotSame(a.LocalState, a.State, "注入後はローカル State を使わない。");

            a.Grant(Reward("reward_melee", 10, false), out int g1);
            a.Grant(Reward("reward_unique", 5, true), out int g2);
            Assert.AreEqual(10, g1);
            Assert.AreEqual(5, g2);
            Assert.AreEqual(15, session.Progress.Virtue, "徳は Session の State へ入る。");
            Assert.AreEqual(1, session.Progress.GrantedRewardCount);
            Assert.AreEqual(0, a.LocalState.Virtue, "ローカル State は徳を二重に持たない（§4.2）。");

            // Scene が入れ替わり、新しい Holder が同じ State を受け取る（A → B の移動に相当）。
            PlayerProgressHolder b = NewHolder("AreaB_Progress");
            Assert.IsTrue(b.Bind(session.Progress));
            Assert.AreSame(session.Progress, b.State, "参照の同一性が保たれる。");
            Assert.AreEqual(15, b.Virtue, "徳を引き継ぐ。");
            Assert.AreEqual(1, b.GrantedRewardCount, "GrantOnce 記録を引き継ぐ。");

            // 引き継いだ GrantOnce は再付与されない。
            b.Grant(Reward("reward_unique", 5, true), out int g3);
            Assert.AreEqual(0, g3, "同じ RewardId の GrantOnce は 2 度目を付与しない。");
            Assert.AreEqual(15, session.Progress.Virtue);
        }

        // ---------------------------------------------------------------- E02

        /// <summary>
        /// P5-E02：同一参照の再 Bind は冪等。使用後の別参照への差し替えと、共有 State の Reset を拒否する（§4.2）。
        /// 読み取りだけでは Bind 不能にならないことも併せて固定する。
        /// </summary>
        [Test]
        public void ProgressBind_IsIdempotentAndRejectsLateReplacement()
        {
            var first = new GameSessionState();
            var second = new GameSessionState();
            PlayerProgressHolder holder = NewHolder("Progress");

            // 読み取りは「使用」にしない。初期化前に HUD が徳を読んでも Bind できる。
            Assert.AreEqual(0, holder.Virtue);
            Assert.AreEqual(0, holder.GrantedRewardCount);
            Assert.IsFalse(holder.IsUsed, "読み取りでは使用済みにしない。");

            Assert.IsTrue(holder.Bind(first.Progress));
            Assert.IsTrue(holder.Bind(first.Progress), "同一参照の再 Bind は冪等で成立する。");
            Assert.AreSame(first.Progress, holder.State);

            Assert.IsFalse(holder.Bind(null), "null は無視する。");
            Assert.AreSame(first.Progress, holder.State, "拒否しても既存 State を保持する。");

            // 使用前なら別参照への差し替えを許す。
            Assert.IsTrue(holder.Bind(second.Progress), "使用前の差し替えは許可（§4.2）。");
            Assert.AreSame(second.Progress, holder.State);

            // 使用後は拒否する。
            holder.Grant(Reward("reward_melee", 10, false), out _);
            Assert.IsTrue(holder.IsUsed);
            LogAssert.ignoreFailingMessages = true;
            Assert.IsFalse(holder.Bind(first.Progress), "使用後の別参照への差し替えは拒否する。");
            Assert.AreSame(second.Progress, holder.State, "拒否後も既存 State を保持する。");
            Assert.IsTrue(holder.Bind(second.Progress), "使用後でも同一参照なら冪等に成立する。");

            // 共有 State は Scene 側から初期化できない。
            Assert.IsFalse(holder.ResetProgress(), "Bind 済みの共有 State は Reset を拒否する。");
            LogAssert.ignoreFailingMessages = false;
            Assert.AreEqual(10, second.Progress.Virtue, "拒否したので徳は消えない。");
        }

        // ---------------------------------------------------------------- E03

        /// <summary>
        /// P5-E03：未注入の Holder は互いに独立で、共有 Session へ誤って付与しない（§4.2 のローカル Fallback）。
        /// P3.5／P4 の試遊 Scene が P5 の Session と混ざらないことを固定する。
        /// </summary>
        [Test]
        public void LocalProgressFallback_RemainsIsolated()
        {
            var session = new GameSessionState();
            PlayerProgressHolder trialA = NewHolder("TrialA");
            PlayerProgressHolder trialB = NewHolder("TrialB");

            Assert.IsFalse(trialA.IsBound);
            Assert.IsFalse(trialB.IsBound);
            Assert.AreSame(trialA.LocalState, trialA.State, "未注入なら自前のローカル State を使う。");
            Assert.AreNotSame(trialA.State, trialB.State, "Holder ごとに別の State。");

            trialA.Grant(Reward("reward_melee", 10, false), out _);
            Assert.AreEqual(10, trialA.Virtue);
            Assert.AreEqual(0, trialB.Virtue, "もう一方へ漏れない。");
            Assert.AreEqual(0, session.Progress.Virtue, "共有 Session へ誤付与しない。");

            // 試遊の Retry 相当。ローカルなら従来どおり初期化できる（P4-00 互換）。
            Assert.IsTrue(trialA.ResetProgress());
            Assert.AreEqual(0, trialA.Virtue);
            Assert.AreEqual(0, trialA.GrantedRewardCount);
        }

        // ---------------------------------------------------------------- E04

        /// <summary>
        /// P5-E04：調査・開通・訪問の記録が Area State に残り、再入場（再 Bind）で成功イベントを再発行しない（§4.3）。
        /// 「1 回だけ」を返り値で表す契約なので、呼び出し元は通知の抑止をこの false で判断できる。
        /// </summary>
        [Test]
        public void AreaRecords_SurviveRebindWithoutRepeatingCompletion()
        {
            var session = new GameSessionState();
            AreaRuntimeState area = session.GetOrCreateArea(AreaA);
            var point = new StableId("point_p5_a_01");
            var gate = new StableId("flag_p5_a_gate");

            Assert.IsTrue(session.MarkVisited(AreaA), "初回訪問は true。");

            InvestigationRecordHolder first = NewRecordHolder("AreaA_Record");
            Assert.IsTrue(first.Bind(area.Investigation));
            Assert.AreSame(area.Investigation, first.Record);

            Assert.IsTrue(first.TryMarkInvestigated(point), "初回の完了は成立する。");
            Assert.IsTrue(area.Investigation.IsInvestigated(point));
            Assert.AreEqual(1, area.InvestigatedCount);
            Assert.AreEqual(0, first.LocalRecord.Count, "ローカル記録へ二重に書かない。");

            Assert.IsTrue(area.TryOpen(gate), "初回の開通は成立する。");
            Assert.IsTrue(area.IsOpen(gate));

            // B へ行って戻る。Holder は Scene ごと作り直され、同じ Area 記録を受け取る。
            InvestigationRecordHolder second = NewRecordHolder("AreaA_Record_Revisit");
            Assert.IsTrue(second.Bind(area.Investigation));

            Assert.IsTrue(second.Record.IsInvestigated(point), "調査済みが残る。");
            Assert.IsFalse(second.TryMarkInvestigated(point), "再入場で完了を再確定しない＝成功イベントを再発行しない。");
            Assert.AreEqual(1, area.InvestigatedCount, "件数が増えない。");

            Assert.IsFalse(area.TryOpen(gate), "開通済みなら再開通しない＝開通通知を再発火しない。");
            Assert.AreEqual(1, area.OpenedFlagCount);

            Assert.IsFalse(session.MarkVisited(AreaA), "再訪は false。初回入場の扱いにしない。");
            Assert.AreEqual(1, session.VisitedAreaCount);
        }

        // ---------------------------------------------------------------- E05

        /// <summary>
        /// P5-E05：本編型死亡再開が、全 Area の通常 Encounter クリア記録<b>だけ</b>を初期化する（§4.1／§9.1）。
        /// 徳・GrantOnce・調査済み・門の開通・訪問済み・加入は不変であることを、同じ Session 上で並べて確認する。
        /// </summary>
        [Test]
        public void WorldRespawn_ResetsOnlyNormalEncounterRecords()
        {
            var session = new GameSessionState();
            var inumaru = new StableId("companion_inumaru");
            var pointA = new StableId("point_p5_a_01");
            var pointB = new StableId("point_p5_b_01");
            var gate = new StableId("flag_p5_a_gate");
            var encounter = new StableId("encounter_p5_b_road");

            AreaRuntimeState a = session.GetOrCreateArea(AreaA);
            AreaRuntimeState b = session.GetOrCreateArea(AreaB);

            PlayerProgressHolder progress = NewHolder("Progress");
            Assert.IsTrue(progress.Bind(session.Progress));
            progress.Grant(Reward("reward_melee", 10, false), out _);
            progress.Grant(Reward("reward_ranged", 12, false), out _);
            progress.Grant(Reward("reward_unique", 7, true), out _);

            Assert.IsTrue(session.Recruit(inumaru));
            Assert.IsTrue(session.MarkVisited(AreaA));
            Assert.IsTrue(session.MarkVisited(AreaB));
            Assert.IsTrue(a.Investigation.TryMarkInvestigated(pointA));
            Assert.IsTrue(b.Investigation.TryMarkInvestigated(pointB));
            Assert.IsTrue(a.TryOpen(gate));

            int cycle = session.RespawnCycle;
            Assert.IsTrue(b.TryMarkEncounterCleared(encounter, cycle));
            Assert.IsFalse(b.TryMarkEncounterCleared(encounter, cycle), "同じ周期で二重に記録しない。");
            Assert.IsTrue(b.IsEncounterCleared(encounter, session.RespawnCycle), "往復では再出現しない。");

            // 死亡再開。
            session.AdvanceRespawnCycle();

            Assert.AreEqual(cycle + 1, session.RespawnCycle, "再出現周期が 1 進む。");
            Assert.IsFalse(b.IsEncounterCleared(encounter, session.RespawnCycle), "通常敵が再出現する。");
            Assert.AreEqual(0, b.ClearedEncounterRecordCount, "クリア記録は全 Area 分が初期化される。");
            Assert.AreEqual(0, a.ClearedEncounterRecordCount);

            // 恒久進行は不変。
            Assert.AreEqual(29, session.Progress.Virtue, "徳を保持する（10 + 12 + 7）。");
            Assert.AreEqual(1, session.Progress.GrantedRewardCount, "GrantOnce 記録を保持する。");
            Assert.IsTrue(session.IsRecruited(inumaru), "加入を保持する。");
            Assert.AreEqual(2, session.VisitedAreaCount, "訪問済みを保持する。");
            Assert.IsTrue(a.Investigation.IsInvestigated(pointA), "調査済みを保持する（A）。");
            Assert.IsTrue(b.Investigation.IsInvestigated(pointB), "調査済みを保持する（B）。");
            Assert.IsTrue(a.IsOpen(gate), "門の開通を保持する。");

            // 再戦で再び付与される（GrantOnce=false の既存ルール。§8.5）。
            progress.Grant(Reward("reward_melee", 10, false), out int again);
            Assert.AreEqual(10, again);
            progress.Grant(Reward("reward_unique", 7, true), out int onceAgain);
            Assert.AreEqual(0, onceAgain, "GrantOnce は死亡再開でも再付与しない。");
        }

        // ================================================================ P5-03a

        private static void SetPrivate(object target, string field, object value)
        {
            // private フィールドは基底クラスに居ることがあるので階層を辿る（CompanionData の継承元など）。
            for (Type t = target.GetType(); t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail("field not found: " + field + " on " + target.GetType().FullName);
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        // ---------------------------------------------------------------- E06

        /// <summary>
        /// P5-E06：保持対象を<b>それぞれ異なる非初期値</b>へ設定し、Export → 別実体へ Import →
        /// 同じ deltaTime で進行させて、値・復帰時刻・使用可能時刻が連続することを確認する（§4.5）。
        /// 0 だけの往復では合格にしない、という要求にそのまま対応する。
        /// 併せて、中断で始まる CD は<b>中断完了後</b>に採ること（§4.4 の落とし穴）を実物で固定する。
        /// </summary>
        [Test]
        public void TransferSnapshot_CapturesAfterCancellationAndRestoresVitals()
        {
            const float Dt = 0.1f;

            // ---- スタミナ：現在値と回復待ちを別々の非初期値にする ----
            var stamina = new StaminaState(100f, regenPerSecond: 10f, regenDelay: 1.5f);
            stamina.Consume(37f);
            Assert.AreNotEqual(100f, stamina.Current, "前提：非初期値。");
            Assert.Greater(stamina.ExportTransferSnapshot().RegenDelayRemaining, 0f, "前提：回復待ちが動いている。");

            var staminaB = new StaminaState(100f, regenPerSecond: 10f, regenDelay: 1.5f);
            Assert.IsTrue(staminaB.TryImportTransferSnapshot(stamina.ExportTransferSnapshot()));
            Assert.AreEqual(stamina.Current, staminaB.Current, 1e-4f);

            stamina.Tick(Dt, regenBlocked: false);
            staminaB.Tick(Dt, regenBlocked: false);
            Assert.AreEqual(stamina.Current, staminaB.Current, 1e-4f, "同じ deltaTime で進めた後も一致する。");
            Assert.AreEqual(stamina.ExportTransferSnapshot().RegenDelayRemaining,
                staminaB.ExportTransferSnapshot().RegenDelayRemaining, 1e-4f, "回復開始の時刻が連続する。");

            // ---- 被弾後無敵 ----
            var hit = new HitReactionState(hurtSeconds: 0.3f, invincibleSeconds: 0.5f);
            hit.Begin();
            hit.Tick(0.35f); // Hurt は明け、無敵だけ残る区間（§6.1 が遷移を許す状態）。
            Assert.IsFalse(hit.IsHurt, "前提：Hurt は終わっている。");
            Assert.Greater(hit.InvincibleRemaining, 0f, "前提：無敵は非初期値で残っている。");

            var hitB = new HitReactionState(hurtSeconds: 0.3f, invincibleSeconds: 0.5f);
            Assert.IsTrue(hitB.TryImportTransferSnapshot(hit.ExportTransferSnapshot()));
            Assert.AreEqual(hit.InvincibleRemaining, hitB.InvincibleRemaining, 1e-4f);

            hit.Tick(Dt);
            hitB.Tick(Dt);
            Assert.AreEqual(hit.InvincibleRemaining, hitB.InvincibleRemaining, 1e-4f, "無敵の終わる時刻が連続する。");

            // ---- 仲間の生存値（Down・復帰待ち・ひるみ蓄積を同時に非初期値へ） ----
            var vitals = new CompanionVitals(null);
            vitals.Health.SetCurrent(0);
            SetPrivate(vitals, "_recoveryRemaining", 3.25f);
            SetPrivate(vitals, "_postHitInvincibleRemaining", 0.2f);
            typeof(CompanionVitals).GetProperty("IsDown").SetValue(vitals, true);
            FlinchState flinch = (FlinchState)typeof(CompanionVitals)
                .GetField("_flinch", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(vitals);
            SetPrivate(flinch, "_accumulation", 17f);
            SetPrivate(flinch, "_holdRemaining", 0.9f);

            CompanionVitalsTransferSnapshot snap = vitals.ExportTransferSnapshot();
            Assert.AreEqual(0, snap.Hp);
            Assert.IsTrue(snap.IsDown);
            Assert.AreEqual(3.25f, snap.RecoveryRemaining, 1e-4f);
            Assert.AreEqual(17f, snap.Flinch.Accumulation, 1e-4f, "入れ子のひるみも採れている（readonly でも中身は可変）。");

            var vitalsB = new CompanionVitals(null);
            Assert.IsTrue(vitalsB.TryImportTransferSnapshot(snap));
            Assert.AreEqual(0, vitalsB.Health.Current, "Revive を呼ばないので HP が勝手に戻らない。");
            Assert.IsTrue(vitalsB.IsDown);
            Assert.AreEqual(3.25f, vitalsB.RecoveryRemaining, 1e-4f);
            Assert.AreEqual(17f, vitalsB.FlinchAccumulation, 1e-4f);

            vitals.Tick(Dt);
            vitalsB.Tick(Dt);
            Assert.AreEqual(vitals.RecoveryRemaining, vitalsB.RecoveryRemaining, 1e-4f, "復帰時刻が連続する。");
            Assert.AreEqual(vitals.IsDown, vitalsB.IsDown);

            // ---- 不正値は部分適用せずに拒否する ----
            var reject = new CompanionVitals(null);
            int before = reject.Health.Current;
            Assert.IsFalse(reject.TryImportTransferSnapshot(new CompanionVitalsTransferSnapshot(
                10, true, 1f, 0f, default)), "Down なのに HP が残る矛盾を拒否する。");
            Assert.IsFalse(reject.TryImportTransferSnapshot(new CompanionVitalsTransferSnapshot(
                5, false, 0f, float.NaN, default)), "NaN を拒否する。");
            Assert.AreEqual(before, reject.Health.Current, "拒否したので何も変わっていない。");

            // ---- 構え・回避：解除／中断のあとに採る ----
            var guard = new EnemyGuardAbility(cooldownSeconds: 2f, maxHoldSeconds: 5f);
            Assert.IsTrue(guard.TryStart());
            guard.Tick(0.4f);
            Assert.AreEqual(0f, guard.ExportTransferSnapshot().CooldownRemaining, 1e-4f,
                "構え中はまだ CD が始まっていない。ここで採ると CD が落ちる。");
            guard.Release();
            float guardCd = guard.ExportTransferSnapshot().CooldownRemaining;
            Assert.Greater(guardCd, 0f, "Release 後に採れば CD が乗る（§4.5）。");

            var guardB = new EnemyGuardAbility(cooldownSeconds: 2f, maxHoldSeconds: 5f);
            Assert.IsTrue(guardB.TryImportTransferSnapshot(guard.ExportTransferSnapshot()));
            Assert.IsFalse(guardB.IsReady, "CD 中なので使えない。");
            guard.Tick(Dt);
            guardB.Tick(Dt);
            Assert.AreEqual(guard.CooldownRemaining, guardB.CooldownRemaining, 1e-4f, "使用可能になる時刻が連続する。");

            var evade = new EnemyEvadeAbility(cooldownSeconds: 1.5f, invulnerableSeconds: 0.3f);
            Assert.IsTrue(evade.TryStart());
            evade.Interrupt();
            float evadeCd = evade.ExportTransferSnapshot().CooldownRemaining;
            Assert.Greater(evadeCd, 0f, "中断後に採れば CD が乗る。");

            var evadeB = new EnemyEvadeAbility(cooldownSeconds: 1.5f, invulnerableSeconds: 0.3f);
            Assert.IsTrue(evadeB.TryImportTransferSnapshot(evade.ExportTransferSnapshot()));
            Assert.IsFalse(evadeB.IsInvulnerable, "回避由来の無敵は持ち越さない（§4.5 末尾）。");
            Assert.AreEqual(evadeCd, evadeB.CooldownRemaining, 1e-4f);

            // ---- 中断で生じた攻撃 CD を実物の Controller で確認する（§4.4 の落とし穴） ----
            AssertCancelledAttackCooldownIsCaptured();
        }

        private const float RigStartup = 0.2f;
        private const float RigActive = 0.1f;
        private const float RigRecovery = 0.3f;
        private const float RigCooldown = 1f;

        /// <summary>
        /// 攻撃中に <c>CancelAttack()</c> すると CD が <c>Max(現在, Plan.CooldownSeconds)</c> で始まる。
        /// <b>中断前に採ると 0</b>、<b>中断後に採ると CD が乗る</b>ことを実際の Controller で示す。
        /// </summary>
        private void AssertCancelledAttackCooldownIsCaptured()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivate(attack, "_useRange", 2f);
            SetPrivate(attack, "_useAngle", 90f);
            SetPrivate(attack, "_cooldownSeconds", RigCooldown);
            SetPrivate(attack, "_startupSeconds", RigStartup);
            SetPrivate(attack, "_activeSeconds", RigActive);
            SetPrivate(attack, "_recoverySeconds", RigRecovery);
            SetPrivate(attack, "_hpMultiplier", 0.8f);

            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivate(data, "_attackPower", 60f);
            SetPrivate(data, "_basicAttack", attack);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);
            var motor = go.AddComponent<CompanionMotor>();
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);
            InvokePrivate(combat, "OnEnable");

            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 1f);
            var enemy = enemyGo.AddComponent<TransferFakeEnemy>();
            enemy.Forward = Vector3.back;
            PerceptionTargetRegistry.Register(enemy);

            tracker.TickTargeting();
            combat.TickCombat(0f);
            Assert.IsTrue(combat.IsAttacking, "前提：攻撃が始まっている。");
            tracker.TickTargeting();
            combat.TickCombat(RigStartup);

            Assert.AreEqual(0f, combat.ExportTransferSnapshot().CooldownRemaining, 1e-4f,
                "攻撃中に採ると CD は 0。この順で採るのが §4.4 が名指しする誤り。");

            combat.CancelAttack();
            CompanionCombatTransferSnapshot cancelled = combat.ExportTransferSnapshot();
            Assert.AreEqual(RigCooldown, cancelled.CooldownRemaining, 1e-3f,
                "中断完了後に採れば、中断で生じた CD が Snapshot に乗る。");

            PerceptionTargetRegistry.Clear();
        }

        private sealed class TransferFakeEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward { get; set; } = Vector3.forward;
            public int DamageableId => GetInstanceID();
            public int ActorId => GetInstanceID();
            public Vector3 Position => transform.position;
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat => 0f;
            public float AcquiredThreatMultiplier => 1f;

            public void ReceiveHit(in HitInfo hit)
            {
            }
        }

        // ---------------------------------------------------------------- E28

        private const string InventoryFileName = "P5_ActorTransferInventory.md";

        private static readonly string[] Classifications =
        {
            "保持", "Capture前に終了", "固定設定・参照", "再構築", "合成",
        };

        private sealed class LedgerRow
        {
            public string Field;
            public string Classification;
            public string Reason;
        }

        /// <summary>台帳を読む。見出し <c>## `完全型名`</c> と、その下の表の行を拾う。</summary>
        private static Dictionary<string, List<LedgerRow>> ReadInventory(out string path)
        {
            path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", InventoryFileName));
            Assert.IsTrue(File.Exists(path), "持ち越し台帳が見つかりません: " + path);

            var result = new Dictionary<string, List<LedgerRow>>();
            string current = null;
            var heading = new Regex(@"^##\s+`([A-Za-z0-9_.]+)`\s*$");
            var row = new Regex(@"^\|\s*`([^`]+)`\s*\|\s*([^|]+?)\s*\|\s*(.+?)\s*\|\s*$");

            foreach (string raw in File.ReadAllLines(path))
            {
                Match h = heading.Match(raw);
                if (h.Success)
                {
                    current = h.Groups[1].Value;
                    if (!result.ContainsKey(current))
                    {
                        result.Add(current, new List<LedgerRow>());
                    }

                    continue;
                }

                if (current == null)
                {
                    continue;
                }

                Match r = row.Match(raw);
                if (r.Success)
                {
                    result[current].Add(new LedgerRow
                    {
                        Field = r.Groups[1].Value,
                        Classification = r.Groups[2].Value.Trim(),
                        Reason = r.Groups[3].Value.Trim(),
                    });
                }
            }

            return result;
        }

        /// <summary>自動プロパティのバッキングフィールドはプロパティ名へ正規化する。</summary>
        private static string NormalizeFieldName(string name)
        {
            int close = name.IndexOf('>');
            return name.StartsWith("<", StringComparison.Ordinal) && close > 1
                ? name.Substring(1, close - 1)
                : name;
        }

        private static IEnumerable<FieldInfo> DeclaredFields(Type t)
        {
            return t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        }

        /// <summary>
        /// P5-E28：対象型を<b>反射で発見</b>し、持ち越し台帳と双方向に照合する（§4.5、裁定 1）。
        ///
        /// 手書きの対象型リストを使わないのが要点。リストの更新漏れでテストが嘘をつくのを防ぐ。
        /// 新しい可変フィールドが増えて分類されていなければ失敗する。<b>後工程でここが落ちるのは正常</b>で、
        /// そのとき台帳を更新するのが正しい対応（裁定 4）。
        /// </summary>
        [Test]
        public void TransferInventory_ClassifiesEveryMutableRuntimeField()
        {
            Dictionary<string, List<LedgerRow>> ledger = ReadInventory(out string path);
            Assembly gameplay = typeof(ITransferableRuntime).Assembly;

            var markerTypes = gameplay.GetTypes()
                .Where(t => typeof(ITransferableRuntime).IsAssignableFrom(t) && !t.IsInterface)
                .OrderBy(t => t.FullName)
                .ToList();
            Assert.Greater(markerTypes.Count, 0, "ITransferableRuntime を実装する型が 1 つも見つかりません（反射の走査先が誤り）。");

            var problems = new List<string>();

            // (1) 反射で見つかった対象型は、すべて台帳に節を持つ。
            foreach (Type t in markerTypes)
            {
                if (!ledger.ContainsKey(t.FullName))
                {
                    problems.Add("台帳に節がありません: " + t.FullName
                        + "（ITransferableRuntime を実装したなら " + InventoryFileName + " へ分類を足すこと）");
                }
            }

            // (2) 台帳の節はすべて実在する型で、行はすべて実在するフィールドを指す。
            foreach (KeyValuePair<string, List<LedgerRow>> section in ledger)
            {
                Type t = gameplay.GetType(section.Key);
                if (t == null)
                {
                    problems.Add("台帳にあるが型が存在しません: " + section.Key + "（改名・削除したら台帳も直すこと）");
                    continue;
                }

                var actual = DeclaredFields(t).ToDictionary(f => NormalizeFieldName(f.Name), f => f);
                var listed = new HashSet<string>();

                foreach (LedgerRow r in section.Value)
                {
                    if (!listed.Add(r.Field))
                    {
                        problems.Add(section.Key + "." + r.Field + " が台帳に重複しています。");
                    }

                    if (!actual.ContainsKey(r.Field))
                    {
                        problems.Add("台帳にあるがフィールドが存在しません: " + section.Key + "." + r.Field);
                        continue;
                    }

                    if (Array.IndexOf(Classifications, r.Classification) < 0)
                    {
                        problems.Add("分類が不正です: " + section.Key + "." + r.Field + " = '" + r.Classification
                            + "'（使えるのは " + string.Join(" / ", Classifications) + "）");
                    }

                    if (string.IsNullOrWhiteSpace(r.Reason))
                    {
                        problems.Add("理由が空です: " + section.Key + "." + r.Field);
                    }

                    // (3) 合成は、参照先も台帳の節でなければならない（入れ子の時間所有型。§4.5）。
                    if (r.Classification == "合成")
                    {
                        Type ft = actual[r.Field].FieldType;
                        if (!ledger.ContainsKey(ft.FullName))
                        {
                            problems.Add("合成の参照先が台帳にありません: " + section.Key + "." + r.Field
                                + " -> " + ft.FullName);
                        }
                    }
                }

                // (4) 未分類のフィールドを検出する。ここが増えたら台帳を更新する（裁定 4）。
                foreach (string name in actual.Keys)
                {
                    if (!listed.Contains(name))
                    {
                        problems.Add("未分類のフィールド: " + section.Key + "." + name
                            + "（" + actual[name].FieldType.Name + "）");
                    }
                }
            }

            Assert.IsEmpty(problems,
                "持ち越し台帳（" + path + "）と実装が一致しません。\n  - " + string.Join("\n  - ", problems) + "\n");
        }

        // ---------------------------------------------------------------- E29

        private CompanionActor NewCompanionActor(string name, CompanionState initial)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            var actor = go.AddComponent<CompanionActor>();
            actor.ResetState(initial);
            return actor;
        }

        /// <summary>
        /// P5-E29：到着時の状態復元が、値・状態・診断の整合を保つ（§4.6）。
        /// <b>IllegalTransitionCount == 0</b>、行動所有者なし、HP・CD を変えないこと、
        /// そして被弾を偽造しない（理由が <see cref="CompanionStateChangeReason.Restored"/> である）ことを見る。
        ///
        /// Away は P5 に実 gameplay 経路が無いため（裁定 5）、§4.6 が認める初期化中の専用 Restore API で検証する。
        /// </summary>
        [Test]
        public void CompanionRestore_ReconcilesDownAwayWithoutIllegalTransitions()
        {
            foreach (CompanionState target in new[]
                     { CompanionState.Follow, CompanionState.Stagger, CompanionState.Down, CompanionState.Away })
            {
                CompanionActor actor = NewCompanionActor("Inumaru_" + target, CompanionState.Follow);
                // CompanionActor は [RequireComponent(typeof(CompanionStateArbiter))]。
                // AddComponent すると [DisallowMultipleComponent] で null が返るので、付いているものを取る。
                CompanionStateArbiter arbiter = actor.GetComponent<CompanionStateArbiter>();
                Assert.IsNotNull(arbiter, "Arbiter は RequireComponent で自動付与される。");
                arbiter.Bind(actor);

                var listener = new RecordingStateListener();
                actor.States.AddListener(listener);
                List<CompanionStateChangeReason> reasons = listener.Reasons;

                Assert.IsTrue(arbiter.TryRestoreState(target), target + " の復元が成立する。");
                Assert.AreEqual(target, actor.State, target + " へ復元される。");
                Assert.AreEqual(0, actor.IllegalTransitionCount, target + "：不正遷移を出さない。");
                Assert.AreEqual(CompanionActionOwner.None, arbiter.CurrentOwner, target + "：行動所有者を残さない。");

                if (target != CompanionState.Follow)
                {
                    CollectionAssert.DoesNotContain(reasons, CompanionStateChangeReason.Staggered,
                        target + "：被弾を偽造しない。");
                    CollectionAssert.DoesNotContain(reasons, CompanionStateChangeReason.Defeated,
                        target + "：撃破を偽造しない。");
                    CollectionAssert.Contains(reasons, CompanionStateChangeReason.Restored,
                        target + "：復元専用の理由で通知する。");
                }

                // 冪等：同じ状態への再復元は成立し、不正遷移も増えない。
                Assert.IsTrue(arbiter.TryRestoreState(target), target + "：再復元は冪等。");
                Assert.AreEqual(0, actor.IllegalTransitionCount);
            }

            // ---- 値へ触れないこと。Down 復元の前後で HP・復帰残り・CD が変わらない ----
            var vitals = new CompanionVitals(null);
            vitals.Health.SetCurrent(0);
            SetPrivate(vitals, "_recoveryRemaining", 2.5f);
            typeof(CompanionVitals).GetProperty("IsDown").SetValue(vitals, true);

            CompanionActor downActor = NewCompanionActor("Inumaru_Values", CompanionState.Follow);
            var guardian = downActor.gameObject.AddComponent<CompanionGuardianController>();
            Assert.IsTrue(guardian.TryImportTransferSnapshot(new CompanionGuardianTransferSnapshot(1.75f)));

            CompanionStateArbiter downArbiter = downActor.GetComponent<CompanionStateArbiter>();
            downArbiter.Bind(downActor);

            int hpBefore = vitals.Health.Current;
            float recoveryBefore = vitals.RecoveryRemaining;
            float guardianCdBefore = guardian.ExportTransferSnapshot().CooldownRemaining;

            Assert.IsTrue(downArbiter.TryRestoreState(CompanionState.Down));

            Assert.AreEqual(hpBefore, vitals.Health.Current, "復元は HP を変えない。");
            Assert.AreEqual(recoveryBefore, vitals.RecoveryRemaining, 1e-4f, "復元は復帰時計を変えない。");
            Assert.AreEqual(guardianCdBefore, guardian.ExportTransferSnapshot().CooldownRemaining, 1e-4f,
                "復元は CD を再設定しない。");
            Assert.AreEqual(0, downActor.IllegalTransitionCount);

            // ---- 行動状態は到着時に復元しない（§4.6） ----
            CompanionActor reject = NewCompanionActor("Inumaru_Reject", CompanionState.Follow);
            CompanionStateArbiter rejectArbiter = reject.GetComponent<CompanionStateArbiter>();
            rejectArbiter.Bind(reject);
            Assert.IsFalse(rejectArbiter.TryRestoreState(CompanionState.AttackActive), "攻撃中へは復元しない。");
            Assert.IsFalse(rejectArbiter.TryRestoreState(CompanionState.Investigate), "探索中へは復元しない。");
            Assert.AreEqual(CompanionState.Follow, reject.State, "拒否しても状態は変わらない。");
            Assert.AreEqual(0, reject.IllegalTransitionCount, "拒否は不正遷移として数えない。");
        }

        /// <summary>状態通知の理由を記録するだけの購読者（E29 用）。</summary>
        private sealed class RecordingStateListener : ICompanionStateListener
        {
            public List<CompanionStateChangeReason> Reasons { get; } = new List<CompanionStateChangeReason>();

            public void OnCompanionStateChanged(in CompanionStateChanged change) => Reasons.Add(change.Reason);
        }

        // ================================================================ P5-02

        // ---------------------------------------------------------------- E25

        private AreaDefinition NewArea(
            string areaId, string scenePath, int floorId,
            (string id, CardinalDirection facing)[] entries, string defaultEntryId)
        {
            var asset = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(asset);
            SetPrivate(asset, "_id", new StableId(areaId));

            var list = new List<AreaEntryDefinition>();
            foreach ((string id, CardinalDirection facing) in entries)
            {
                var e = new AreaEntryDefinition();
                e.EditorSet(new StableId(id), facing);
                list.Add(e);
            }

            asset.EditorSet(scenePath, floorId, list, new StableId(defaultEntryId));
            return asset;
        }

        private AreaCatalogData NewCatalog(List<AreaDefinition> areas, string respawnArea, string respawnEntry)
        {
            var asset = ScriptableObject.CreateInstance<AreaCatalogData>();
            _spawned.Add(asset);
            SetPrivate(asset, "_id", new StableId("area_catalog_test"));
            asset.EditorSet(areas, new StableId(respawnArea), new StableId(respawnEntry));
            return asset;
        }

        private static bool HasError(IReadOnlyList<string> errors, string fragment)
        {
            foreach (string e in errors)
            {
                if (e.Contains(fragment))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// P5-E25：カタログが安定 ID・入口・既定入口・死亡再開点を解決し、<b>非対応の Floor を拒否</b>する
        /// （仕様書 v1.1 §3.1／§3.3／§13.3）。
        ///
        /// FloorId は「論理的に同じ階層か」を示す int で、Y 座標や buildIndex から推定しない。
        /// <b>不整合を自動補正して同じ階とみなさない</b>のが要点で、黙って直すと多層の実装時に嘘が残る。
        /// </summary>
        [Test]
        public void AreaCatalog_ResolvesEntriesAndRejectsUnsupportedFloor()
        {
            const string ScenePathA = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity";
            const string ScenePathB = "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaB.unity";

            AreaDefinition a = NewArea("area_p5_a", ScenePathA, 0, new[]
            {
                ("area_p5_a_start", CardinalDirection.North),
                ("area_p5_a_from_b", CardinalDirection.West),
            }, "area_p5_a_start");

            AreaDefinition b = NewArea("area_p5_b", ScenePathB, 0, new[]
            {
                ("area_p5_b_from_a", CardinalDirection.East),
            }, "area_p5_b_from_a");

            // ---- 正常系：A↔B と死亡再開の経路がすべて解決できる ----
            AreaCatalogData data = NewCatalog(new List<AreaDefinition> { a, b }, "area_p5_a", "area_p5_a_start");
            Assert.IsTrue(AreaCatalog.TryBuild(data, out AreaCatalog catalog, out IReadOnlyList<string> errors),
                "正常なカタログは構築できる。errors=" + string.Join(" / ", errors));
            Assert.AreEqual(2, catalog.AreaCount);
            Assert.AreEqual(3, catalog.EntryCount);

            Assert.IsTrue(catalog.TryGetScenePath(new StableId("area_p5_a"), out string pathA));
            Assert.AreEqual(ScenePathA, pathA);

            Assert.IsTrue(catalog.TryGetEntry(new StableId("area_p5_a"), new StableId("area_p5_a_from_b"),
                out AreaEntryInfo fromB), "A の戻り入口を解決できる。");
            Assert.AreEqual(CardinalDirection.West, fromB.Facing, "向きは Data が正本。");
            Assert.AreEqual(ScenePathA, fromB.ScenePath, "入口は所属エリアの Scene パスを運ぶ。");

            // 直開きの既定入口（§5.2）。B は area_p5_b_from_a。
            Assert.IsTrue(catalog.TryGetDefaultEntry(new StableId("area_p5_b"), out AreaEntryInfo defaultB));
            Assert.AreEqual("area_p5_b_from_a", defaultB.EntryId.Value);

            // 死亡再開点（§3.1。P5 では固定）。
            Assert.IsTrue(catalog.TryGetRespawnEntry(out AreaEntryInfo respawn));
            Assert.AreEqual("area_p5_a", respawn.AreaId.Value);
            Assert.AreEqual("area_p5_a_start", respawn.EntryId.Value);

            // 存在しない ID は解決できない（既定値を返して成功にしない）。
            Assert.IsFalse(catalog.TryGetEntry(new StableId("area_p5_a"), new StableId("area_p5_a_nope"), out _));
            Assert.IsFalse(catalog.TryGetScenePath(new StableId("area_p5_c"), out _));

            // ---- 非対応 Floor は拒否する。自動補正して同じ階とみなさない（§3.3） ----
            AreaDefinition upper = NewArea("area_p5_upper", ScenePathB, 1, new[]
            {
                ("area_p5_upper_start", CardinalDirection.North),
            }, "area_p5_upper_start");

            AreaCatalogData withUpper = NewCatalog(
                new List<AreaDefinition> { a, upper }, "area_p5_a", "area_p5_a_start");
            Assert.IsFalse(AreaCatalog.TryBuild(withUpper, out AreaCatalog rejected, out IReadOnlyList<string> floorErrors),
                "非 0 の FloorId を含むカタログは構築しない。");
            Assert.IsNull(rejected, "失敗時は部分的に使えるカタログを返さない。");
            Assert.IsTrue(HasError(floorErrors, "unsupported FloorId 1"), "理由に Floor を名指しする。errors="
                + string.Join(" / ", floorErrors));

            // Data 側の Validate も同じ判断をする（Bridge の validate-project-data が拾う経路）。
            var report = new DataValidationReport();
            upper.Validate(report);
            Assert.IsTrue(report.HasErrors, "AreaDefinition.Validate も非 0 Floor を error にする。");

            // ---- 既定入口が入口一覧に無い ----
            AreaDefinition badDefault = NewArea("area_p5_bad", ScenePathA, 0, new[]
            {
                ("area_p5_bad_start", CardinalDirection.North),
            }, "area_p5_bad_missing");
            AreaCatalogData badCatalog = NewCatalog(
                new List<AreaDefinition> { badDefault }, "area_p5_bad", "area_p5_bad_start");
            Assert.IsFalse(AreaCatalog.TryBuild(badCatalog, out _, out IReadOnlyList<string> defaultErrors));
            Assert.IsTrue(HasError(defaultErrors, "default entry"), "既定入口の不整合を名指しする。");

            // ---- 死亡再開点が解決できない ----
            AreaCatalogData noRespawn = NewCatalog(
                new List<AreaDefinition> { a, b }, "area_p5_b", "area_p5_a_start");
            Assert.IsFalse(AreaCatalog.TryBuild(noRespawn, out _, out IReadOnlyList<string> respawnErrors),
                "死亡再開点が解決できないカタログは構築しない（敗北から復帰できなくなる）。");
            Assert.IsTrue(HasError(respawnErrors, "Respawn point"), "理由に再開点を名指しする。");

            // ---- 重複 ID ----
            AreaCatalogData duplicate = NewCatalog(
                new List<AreaDefinition> { a, a }, "area_p5_a", "area_p5_a_start");
            Assert.IsFalse(AreaCatalog.TryBuild(duplicate, out _, out IReadOnlyList<string> dupErrors));
            Assert.IsTrue(HasError(dupErrors, "Duplicate area id"), "重複を名指しする。");
        }

        /// <summary>
        /// P5-E25（出荷カタログ）：実際に生成された P5 の Data から構築できることを確認する。
        /// 手で組んだ Fixture だけでは、Builder の出力が壊れていても気付けない
        /// （`CLAUDE.md`「実アセット → 出荷される SO の配線を AssetDatabase で読んで検査する」）。
        /// </summary>
        [Test]
        public void AreaCatalog_ShippedPhase5CatalogResolves()
        {
            const string CatalogPath = "Assets/_Project/Data/Tests/Phase5/SO_AreaCatalog_P5.asset";
            var data = UnityEditor.AssetDatabase.LoadAssetAtPath<AreaCatalogData>(CatalogPath);
            Assert.IsNotNull(data, "出荷カタログが見つかりません: " + CatalogPath
                + "（Momotaro / Phase 5 / Generate Exploration Trial で生成する）");

            Assert.IsTrue(AreaCatalog.TryBuild(data, out AreaCatalog catalog, out IReadOnlyList<string> errors),
                "出荷カタログが構築できません: " + string.Join(" / ", errors));

            Assert.AreEqual(2, catalog.AreaCount, "A と B の 2 エリア。");
            Assert.AreEqual(3, catalog.EntryCount, "入口は 3 つ（a_start / a_from_b / b_from_a）。");

            // A↔B と死亡再開の経路が存在する（§13.3）。
            Assert.IsTrue(catalog.TryGetEntry(new StableId("area_p5_a"), new StableId("area_p5_a_from_b"), out _));
            Assert.IsTrue(catalog.TryGetEntry(new StableId("area_p5_b"), new StableId("area_p5_b_from_a"), out _));
            Assert.IsTrue(catalog.TryGetRespawnEntry(out AreaEntryInfo respawn));
            Assert.AreEqual("area_p5_a_start", respawn.EntryId.Value, "死亡再開点は A の開始点（§3.1）。");

            // Scene が実在し、Build Settings へ登録されている（§13.3）。
            foreach (string areaId in new[] { "area_p5_a", "area_p5_b" })
            {
                Assert.IsTrue(catalog.TryGetScenePath(new StableId(areaId), out string scenePath));
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(scenePath),
                    "Scene が実在しません: " + scenePath);

                bool registered = false;
                foreach (UnityEditor.EditorBuildSettingsScene s in UnityEditor.EditorBuildSettings.scenes)
                {
                    if (s.path == scenePath)
                    {
                        registered = true;
                        break;
                    }
                }

                Assert.IsTrue(registered, "Build Settings へ未登録です: " + scenePath);
            }
        }

        /// <summary>
        /// P5-E25（実 Scene）：生成された A／B の Scene を<b>読み直して</b>、AreaRoot と入口が
        /// Data の入口一覧と 1 対 1 で対応することを確認する。
        ///
        /// 保存した Scene を読み直すまで <b>Missing Script</b> は現れない（`CLAUDE.md` が名指しする事故で、
        /// <c>InvestigationRecordHolder</c> と <c>CompanionRosterContext</c> で実際に起きた）。
        /// Builder 直後の検査だけでは通ってしまうので、ここで実ファイルから開き直す。
        ///
        /// Scene を置換するため、<b>未保存の変更があるときだけ</b> Skip する（F01 の方針。
        /// 無題 Scene というだけで止めると、常態で永久に走らない）。
        /// </summary>
        [Test]
        public void GeneratedAreaScenes_LoadWithEntriesMatchingCatalog()
        {
            for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene open =
                    UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    Assert.Ignore("未保存の変更がある Scene が開いているため実行しません: "
                        + (string.IsNullOrEmpty(open.path) ? "(無題 Scene)" : open.path));
                }
            }

            var pairs = new[]
            {
                ("Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity", "area_p5_a", 2),
                ("Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaB.unity", "area_p5_b", 1),
            };

            try
            {
                foreach ((string scenePath, string areaId, int entryCount) in pairs)
                {
                    UnityEngine.SceneManagement.Scene scene =
                        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                            scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
                    Assert.IsTrue(scene.IsValid(), "Scene を開けません: " + scenePath);

                    var roots = new List<GameObject>(scene.GetRootGameObjects());
                    AreaRoot areaRoot = null;
                    foreach (GameObject go in roots)
                    {
                        AreaRoot found = go.GetComponentInChildren<AreaRoot>(true);
                        if (found != null)
                        {
                            Assert.IsNull(areaRoot, scenePath + ": AreaRoot は 1 つだけ。");
                            areaRoot = found;
                        }
                    }

                    Assert.IsNotNull(areaRoot, scenePath + ": AreaRoot が見つかりません（Missing Script の疑い）。");
                    Assert.IsNotNull(areaRoot.Definition, scenePath + ": AreaDefinition が未配線です。");
                    Assert.AreEqual(areaId, areaRoot.AreaId.Value, scenePath + ": AreaId が一致しません。");
                    Assert.AreEqual(entryCount, areaRoot.EntryPoints.Count, scenePath + ": 入口の数。");

                    // Data の入口一覧と Scene 上の入口が 1 対 1（§13.3「正本と参照が一致する」）。
                    foreach (AreaEntryDefinition def in areaRoot.Definition.Entries)
                    {
                        Assert.IsTrue(areaRoot.TryGetEntryPoint(def.EntryId, out AreaEntryPoint point),
                            scenePath + ": Data の入口 '" + def.EntryId.Value + "' に対応する Scene 上の入口がありません。");
                        Assert.IsNotNull(point, scenePath + ": 入口が Missing です。");
                        Assert.Greater(point.AlternatePlacements.Count, 0,
                            scenePath + ": 入口 '" + def.EntryId.Value + "' に代替配置候補がありません（§6.3）。");
                    }

                    // 到着位置・代替候補が壁や水へ埋まっていないこと（§3.2）。床の上に居ることだけを見る。
                    foreach (AreaEntryPoint point in areaRoot.EntryPoints)
                    {
                        Assert.AreEqual(0f, point.ArrivalPosition.y, 0.01f,
                            scenePath + ": 入口 '" + point.EntryId.Value + "' の到着位置が床面（Y=0）にありません。");
                        foreach (Transform alt in point.AlternatePlacements)
                        {
                            Assert.IsNotNull(alt, scenePath + ": 代替配置候補が Missing です。");
                            Assert.AreEqual(0f, alt.position.y, 0.01f,
                                scenePath + ": 代替配置候補が床面にありません。");
                        }
                    }
                }
            }
            finally
            {
                // 生成物を開いたまま残さない。
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                    UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                    UnityEditor.SceneManagement.NewSceneMode.Single);
            }
        }
    }
}
