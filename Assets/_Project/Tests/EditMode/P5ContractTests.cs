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
using Momotaro.Gameplay.Modes;
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

        [SetUp]
        public void SetUp()
        {
            // 前のテストの登録を持ち込まない。
            PerceptionTargetRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            // 静的な提供点・レジストリは次のテストへ持ち越さない（`CLAUDE.md` の静的状態の注意）。
            // 破棄した GameObject の登録が残ると、次のテストが MissingReferenceException で落ちる（実際に踏んだ）。
            GameplayClockProvider.Current = null;
            PerceptionTargetRegistry.Clear();

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

            // ---- 遷移の窓口（Port）も同じ順序を守る ----
            //
            // Controller 単体で順序を確かめても、実際に採取するのは Port なので、
            // Port が「止める」を飛ばしていれば同じ欠陥がそのまま残る。
            // 実際、Port の CancelAttack を外しても本テストは緑のままだった（後から足した検査）。
            AssertTransferPortCancelsBeforeCapture();
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

        // ================================================================ P5-03b

        /// <summary>受付条件の Fake（§6.1）。既定は「遷移してよい」。</summary>
        private sealed class FakeConditions : IAreaTransitionConditions
        {
            public bool IsAreaReady { get; set; } = true;
            public GameMode Mode { get; set; } = GameMode.Exploration;
            public bool IsPlayerAlive { get; set; } = true;
            public bool IsPlayerBusy { get; set; }
            public bool IsEncounterActive { get; set; }
        }

        /// <summary>非同期ロードの Fake。完了・失敗を明示的に切り替える。</summary>
        private sealed class FakeLoad : IAreaLoadOperation
        {
            public bool IsDone { get; set; }
            public bool HasError { get; set; }
        }

        private AreaCatalog BuildTestCatalog()
        {
            AreaDefinition a = NewArea("area_p5_a", "Assets/A.unity", 0, new[]
            {
                ("area_p5_a_start", CardinalDirection.North),
                ("area_p5_a_from_b", CardinalDirection.West),
            }, "area_p5_a_start");
            AreaDefinition b = NewArea("area_p5_b", "Assets/B.unity", 0, new[]
            {
                ("area_p5_b_from_a", CardinalDirection.East),
            }, "area_p5_b_from_a");
            AreaCatalogData data = NewCatalog(new List<AreaDefinition> { a, b }, "area_p5_a", "area_p5_a_start");
            Assert.IsTrue(AreaCatalog.TryBuild(data, out AreaCatalog catalog, out _), "前提：カタログが作れる。");
            return catalog;
        }

        private static AreaTransitionRequest ToB() =>
            new AreaTransitionRequest(new StableId("area_p5_b"), new StableId("area_p5_b_from_a"));

        // ---------------------------------------------------------------- E07

        /// <summary>
        /// P5-E07：Loading 中は<b>直接 Tick を呼んでも</b>時計・Move・Warp・攻撃が進まない（§6.2 手順 3）。
        ///
        /// 駆動系は「Update は Tick を呼ぶだけ」という形なので、Update 側だけで止めても
        /// 直接 Tick すれば進んでしまう。判定を各 Tick の入口に置いたことをここで固定する。
        /// 純粋クラス（StaminaState 等）は渡された deltaTime をそのまま進めるのが契約なので、対象にしない。
        /// </summary>
        [Test]
        public void LoadingGate_FreezesClocksAndStopsDirectTicks()
        {
            var gate = new GameplayClockGate();

            // 供給元が無ければ凍結しない。P3.5／P4 の試遊 Scene がゲートを知らなくても動く（既定の向き）。
            Assert.IsFalse(GameplayClockProvider.HasSource);
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "未設定は「遷移していない」。");

            GameplayClockProvider.Current = gate;
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "差しただけでは止まらない。");

            // ---- 時計：被弾後無敵 ----
            var hitGo = new GameObject("Player_HitReaction");
            _spawned.Add(hitGo);
            var reaction = hitGo.AddComponent<PlayerHitReaction>();
            reaction.BeginHurt();
            float invincibleBefore = reaction.PostHitInvincibleRemaining;
            Assert.Greater(invincibleBefore, 0f, "前提：無敵が動いている。");

            gate.Freeze();
            Assert.IsTrue(GameplayClockProvider.IsFrozen);
            reaction.Tick(0.2f);
            reaction.Tick(0.2f);
            Assert.AreEqual(invincibleBefore, reaction.PostHitInvincibleRemaining, 1e-4f,
                "凍結中は直接 Tick しても時計が進まない。");

            gate.Thaw();
            reaction.Tick(0.2f);
            Assert.Less(reaction.PostHitInvincibleRemaining, invincibleBefore, "解除すれば進む。");

            // ---- 攻撃：仲間の通常攻撃 ----
            CompanionRig rig = MakeCompanionRig();
            rig.Tracker.TickTargeting();
            rig.Combat.TickCombat(0f);
            Assert.IsTrue(rig.Combat.IsAttacking, "前提：攻撃が始まっている。");
            float elapsedBefore = rig.Combat.AttackState.Elapsed;

            gate.Freeze();
            rig.Tracker.TickTargeting();
            rig.Combat.TickCombat(RigStartup);
            rig.Combat.TickCombat(RigStartup);
            Assert.AreEqual(elapsedBefore, rig.Combat.AttackState.Elapsed, 1e-4f,
                "凍結中は攻撃の進捗が進まない。");

            // ---- Move と Warp ----
            Vector3 positionBefore = rig.Motor.transform.position;
            rig.Motor.WarpTo(positionBefore + new Vector3(5f, 0f, 5f));
            Assert.AreEqual(positionBefore, rig.Motor.transform.position, "凍結中は Warp しない。");

            gate.Thaw();
            rig.Motor.WarpTo(positionBefore + new Vector3(5f, 0f, 5f));
            Assert.AreNotEqual(positionBefore, rig.Motor.transform.position, "解除すれば Warp する。");

            // ---- 索敵 ----
            gate.Freeze();
            rig.Tracker.TickTargeting();
            // 凍結中の索敵は何も掴まない（掴んだまま次の Scene へ入らない）。
            Assert.AreEqual(3, gate.FreezeCount, "Freeze は 3 回呼んでいる。");
            Assert.AreEqual(2, gate.ThawCount, "Thaw は 2 回呼んでいる。");

            GameplayClockProvider.Current = null;
            Assert.IsFalse(GameplayClockProvider.IsFrozen, "供給元を外せば凍結は解ける。");
        }

        private sealed class CompanionRig
        {
            public CompanionActor Actor;
            public CompanionMotor Motor;
            public CompanionTargetTracker Tracker;
            public CompanionCombatController Combat;
        }

        private CompanionRig MakeCompanionRig()
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

            var go = new GameObject("Inumaru_Gate");
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

            var enemyGo = new GameObject("Enemy_Gate");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 1f);
            var enemy = enemyGo.AddComponent<TransferFakeEnemy>();
            enemy.Forward = Vector3.back;
            PerceptionTargetRegistry.Register(enemy);

            return new CompanionRig { Actor = actor, Motor = motor, Tracker = tracker, Combat = combat };
        }

        // ---------------------------------------------------------------- E08

        /// <summary>
        /// P5-E08：§6.1 の受付条件どおりに拒否する。戦闘・Pause 等のモード、主人公の攻撃／防御／被弾中、
        /// 死亡、AreaReady 前、解決できない目的地を、それぞれ別の理由で落とす。
        ///
        /// <b>受付条件の正本は §6.1 ひとつ</b>にしてある（裁定 6）。§4.5 の Capture 前提はその帰結で、
        /// 別の受付判定を持たない。だから検査もここへ集約する。
        /// </summary>
        [Test]
        public void AreaTransition_RejectsInvalidModesAndActions()
        {
            AreaCatalog catalog = BuildTestCatalog();
            var conditions = new FakeConditions();
            var clock = new GameplayClockGate();
            var coordinator = new AreaTransitionCoordinator(catalog, conditions, clock);

            // 解決できない目的地は受理前に落ちる。その場に留まり進行を変えない（§6.3）。
            AreaTransitionDecision unknown = coordinator.TryRequest(
                new AreaTransitionRequest(new StableId("area_p5_z"), new StableId("nope")));
            Assert.IsFalse(unknown.Accepted);
            Assert.AreEqual(AreaTransitionRejection.UnknownDestination, unknown.Rejection);
            Assert.AreEqual(AreaTransitionPhase.Idle, coordinator.Phase);
            Assert.IsFalse(clock.IsFrozen, "拒否では時計を止めない。");

            // 同じエリアでも入口 ID が違えば解決できない。
            Assert.AreEqual(AreaTransitionRejection.UnknownDestination,
                coordinator.TryRequest(new AreaTransitionRequest(
                    new StableId("area_p5_b"), new StableId("area_p5_a_start"))).Rejection,
                "入口はエリアに属する。他エリアの入口 ID では解決しない。");

            // モード別（§6.1 の拒否条件）。
            foreach (GameMode mode in new[]
                     {
                         GameMode.Combat, GameMode.Paused, GameMode.Dialogue,
                         GameMode.Event, GameMode.Loading, GameMode.GameOver,
                     })
            {
                conditions.Mode = mode;
                Assert.AreEqual(AreaTransitionRejection.WrongMode,
                    coordinator.TryRequest(ToB()).Rejection, mode + " では遷移しない。");
            }

            conditions.Mode = GameMode.Exploration;

            // AreaReady 前。
            conditions.IsAreaReady = false;
            Assert.AreEqual(AreaTransitionRejection.NotReady, coordinator.TryRequest(ToB()).Rejection);
            conditions.IsAreaReady = true;

            // 主人公が死亡。
            conditions.IsPlayerAlive = false;
            Assert.AreEqual(AreaTransitionRejection.PlayerDefeated, coordinator.TryRequest(ToB()).Rejection);
            conditions.IsPlayerAlive = true;

            // 主人公が行動中（攻撃・Guard・Step・Hurt・GuardBreak）。
            conditions.IsPlayerBusy = true;
            Assert.AreEqual(AreaTransitionRejection.PlayerBusy, coordinator.TryRequest(ToB()).Rejection);
            conditions.IsPlayerBusy = false;

            // 戦闘開始予約中・戦闘中・勝敗処理中は、主人公の行動中より先に落とす（§8.3 は戦闘開始を優先）。
            conditions.IsEncounterActive = true;
            conditions.IsPlayerBusy = true;
            Assert.AreEqual(AreaTransitionRejection.EncounterActive, coordinator.TryRequest(ToB()).Rejection,
                "戦闘開始が移動より先に確定する（§8.3）。");
            conditions.IsEncounterActive = false;
            conditions.IsPlayerBusy = false;

            Assert.AreEqual(AreaTransitionPhase.Idle, coordinator.Phase, "ここまで一度も受理していない。");
            Assert.AreEqual(0, coordinator.CurrentTransitionId, "拒否では世代が進まない。");
            Assert.IsFalse(clock.IsFrozen);

            // 条件が揃えば受理し、世代が 1 つ進んで時計が止まる。
            AreaTransitionDecision accepted = coordinator.TryRequest(ToB());
            Assert.IsTrue(accepted.Accepted);
            Assert.AreEqual(1, accepted.TransitionId);
            Assert.AreEqual(AreaTransitionPhase.Preparing, coordinator.Phase);
            Assert.IsTrue(clock.IsFrozen, "受理したら Gameplay 時計を止める（§6.2 手順 3）。");

            // 遷移中は、ほかの条件より先に「もう遷移している」を返す（§6.2 手順 2）。
            // 受理で活動を閉じる＝ AreaReady が false になるため、条件を先に見ると
            // 再入の理由が NotReady になって原因を取り違える。実 Scene の P13 で踏んだ。
            conditions.IsAreaReady = false;
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning,
                coordinator.TryRequest(ToB()).Rejection,
                "遷移中の再入は AlreadyTransitioning で返す（NotReady ではない）。");
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning,
                coordinator.TryRequest(new AreaTransitionRequest(
                    new StableId("area_p5_z"), new StableId("nope"))).Rejection,
                "解決できない目的地より先に排他を返す。");
        }

        // ---------------------------------------------------------------- E09

        /// <summary>
        /// P5-E09：二重要求・古い完了・通知からの再入・旧 finally から新世代を守る（§6.2 末尾）。
        ///
        /// 遷移は「完了通知 → その中から次の遷移要求」という再入が普通に起きる。
        /// 世代を持たないと、<b>前の遷移の後始末が新しい遷移の排他を解除してしまう</b>。
        /// </summary>
        [Test]
        public void Transition_ReentryAndStaleCompletionCannotReleaseNewRun()
        {
            AreaCatalog catalog = BuildTestCatalog();
            var conditions = new FakeConditions();
            var clock = new GameplayClockGate();
            var coordinator = new AreaTransitionCoordinator(catalog, conditions, clock);

            // 1 回目を受理して最後まで進める。
            AreaTransitionDecision first = coordinator.TryRequest(ToB());
            Assert.IsTrue(first.Accepted);
            int firstId = first.TransitionId;

            // 遷移中の二重要求は拒否する（外部通知より先に再入を拒否する。§6.2 手順 2）。
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning,
                coordinator.TryRequest(ToB()).Rejection);

            var load = new FakeLoad();
            Assert.IsTrue(coordinator.NotifyLoadStarted(firstId, load));
            Assert.IsTrue(coordinator.NotifyBinding(firstId));
            Assert.IsTrue(coordinator.NotifyReady(firstId));
            Assert.IsTrue(coordinator.Release(firstId));
            Assert.AreEqual(AreaTransitionPhase.Idle, coordinator.Phase);
            Assert.AreEqual(1, coordinator.CompletedCount);
            Assert.IsFalse(clock.IsFrozen, "完了で時計が戻る。");

            // 2 回目を受理する（完了通知の中から次の遷移を要求した状況）。
            AreaTransitionDecision second = coordinator.TryRequest(
                new AreaTransitionRequest(new StableId("area_p5_a"), new StableId("area_p5_a_from_b")));
            Assert.IsTrue(second.Accepted);
            int secondId = second.TransitionId;
            Assert.AreNotEqual(firstId, secondId, "世代が進む。");
            Assert.IsTrue(clock.IsFrozen);

            // ---- ここが要。古い世代からの通知・解除は新しい遷移を壊さない ----
            //
            // 各通知を「段階が合っているのに世代だけ古い」状況でぶつける。段階が違う状態で投げると
            // 段階の判定だけで弾かれてしまい、<b>世代の判定が外れていても気付けない</b>。
            // 実際、最初はまとめて Preparing の時点で投げていて、世代判定を外す欠陥を捕まえられなかった。
            int staleBefore = coordinator.StaleNotificationCount;

            // 段階 Preparing：ロード開始の世代違い。
            Assert.AreEqual(AreaTransitionPhase.Preparing, coordinator.Phase);
            Assert.IsFalse(coordinator.NotifyLoadStarted(firstId, new FakeLoad()), "古い世代のロード開始は効かない。");
            Assert.IsFalse(coordinator.NotifyFailed(firstId, oldSceneUsable: true), "古い世代の失敗通知は効かない。");
            Assert.AreEqual(AreaTransitionPhase.Preparing, coordinator.Phase);

            Assert.IsTrue(coordinator.NotifyLoadStarted(secondId, new FakeLoad()));

            // 段階 Loading：Binding 通知の世代違い。
            Assert.AreEqual(AreaTransitionPhase.Loading, coordinator.Phase);
            Assert.IsFalse(coordinator.NotifyBinding(firstId), "古い世代の Binding 通知は効かない。");
            Assert.AreEqual(AreaTransitionPhase.Loading, coordinator.Phase, "段階が進んでしまわない。");

            Assert.IsTrue(coordinator.NotifyBinding(secondId));

            // 段階 Binding：Ready 通知の世代違い。
            Assert.AreEqual(AreaTransitionPhase.Binding, coordinator.Phase);
            Assert.IsFalse(coordinator.NotifyReady(firstId), "古い世代の Ready 通知は効かない。");
            Assert.AreEqual(AreaTransitionPhase.Binding, coordinator.Phase);

            Assert.IsTrue(coordinator.NotifyReady(secondId));

            // 段階 Ready：<b>旧 finally の解除</b>。ここが本丸で、世代を見ないと新しい遷移を完了させてしまう。
            Assert.AreEqual(AreaTransitionPhase.Ready, coordinator.Phase);
            Assert.IsFalse(coordinator.Release(firstId), "旧 finally は新世代の排他を解除しない。");
            Assert.AreEqual(AreaTransitionPhase.Ready, coordinator.Phase, "段階が Idle へ落ちない。");
            Assert.IsTrue(clock.IsFrozen, "旧世代の解除で時計が戻ってしまわない。");
            Assert.AreEqual(1, coordinator.CompletedCount, "完了数が増えない。");
            Assert.AreEqual(secondId, coordinator.CurrentTransitionId);

            // 世代 0（既定値）でも解除できない。
            Assert.IsFalse(coordinator.Release(0), "既定値の世代で解除できない。");
            Assert.AreEqual(AreaTransitionPhase.Ready, coordinator.Phase);
            Assert.IsTrue(clock.IsFrozen);

            Assert.AreEqual(staleBefore + 6, coordinator.StaleNotificationCount, "無視した通知を数えている。");

            // 正しい世代なら通る。
            Assert.IsTrue(coordinator.Release(secondId));
            Assert.AreEqual(2, coordinator.CompletedCount);
            Assert.IsFalse(clock.IsFrozen);

            // ---- 段階を飛ばした通知も効かない（sceneLoaded だけで Ready 扱いにしない。§6.2 手順 7） ----
            AreaTransitionDecision third = coordinator.TryRequest(ToB());
            Assert.IsTrue(third.Accepted);
            Assert.IsFalse(coordinator.NotifyReady(third.TransitionId), "Loading を経ずに Ready へ跳べない。");
            Assert.IsFalse(coordinator.Release(third.TransitionId), "Ready でないのに解除できない。");
            Assert.AreEqual(AreaTransitionPhase.Preparing, coordinator.Phase);
            Assert.AreEqual(2, coordinator.CompletedCount, "飛ばした通知で完了しない。");
        }

        // ---------------------------------------------------------------- E10

        /// <summary>
        /// P5-E10：監視がタイムアウトしても<b>同じロード操作を保持して観測し続け</b>、
        /// 遅れて完了したら復旧を 1 回だけ行う。未完了中に再ロードも活動再開もしない（§6.3）。
        ///
        /// Unity の非同期ロードはキャンセルできないので、「キャンセルできたことにして排他を解除する」と
        /// 生きている古いロードの上に新しいロードが乗る。タイムアウトは停止表示であって解除ではない。
        /// </summary>
        [Test]
        public void TransitionFailure_HasBoundedRecoveryWithoutDuplicateLoads()
        {
            AreaCatalog catalog = BuildTestCatalog();
            var conditions = new FakeConditions();
            var clock = new GameplayClockGate();
            var coordinator = new AreaTransitionCoordinator(catalog, conditions, clock, timeoutSeconds: 30f);

            AreaTransitionDecision accepted = coordinator.TryRequest(ToB());
            Assert.IsTrue(accepted.Accepted);
            int id = accepted.TransitionId;

            var load = new FakeLoad();
            Assert.IsTrue(coordinator.NotifyLoadStarted(id, load));
            Assert.AreEqual(1, coordinator.LoadStartCount);

            // 正常系に固定の待ち時間を足していない（監視だけが進む）。
            coordinator.TickUnscaled(10f);
            Assert.IsFalse(coordinator.TimedOut);
            Assert.AreEqual(AreaTransitionPhase.Loading, coordinator.Phase);

            // タイムアウト。
            coordinator.TickUnscaled(25f);
            Assert.IsTrue(coordinator.TimedOut, "30 秒で監視が切れる。");
            Assert.AreEqual(AreaTransitionPhase.Loading, coordinator.Phase,
                "タイムアウトしても段階は Loading のまま。排他を解除しない。");
            Assert.IsTrue(clock.IsFrozen, "未完了のあいだ活動を再開しない。");
            Assert.AreEqual(0, coordinator.RecoveryCount, "まだ復旧しない（古い操作が終端していない）。");

            // 未完了のあいだは新しい要求も通らない（重複ロードを作らない）。
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning,
                coordinator.TryRequest(ToB()).Rejection);
            Assert.AreEqual(1, coordinator.LoadStartCount, "ロード開始は 1 回のまま。");

            // 監視を進め続けても復旧は始まらない。
            coordinator.TickUnscaled(60f);
            Assert.AreEqual(0, coordinator.RecoveryCount);

            // ---- 遅れてロードが完了した。ここで初めて復旧を 1 回だけ始める ----
            load.IsDone = true;
            coordinator.TickUnscaled(1f);
            Assert.AreEqual(1, coordinator.RecoveryCount, "終端後に復旧を 1 回開始する。");
            Assert.IsTrue(coordinator.RecoveryPending);

            // 何度 Tick しても 2 回目は始まらない（無限再試行しない。§6.3）。
            coordinator.TickUnscaled(1f);
            coordinator.TickUnscaled(10f);
            Assert.AreEqual(1, coordinator.RecoveryCount, "復旧は 1 回だけ。");
            Assert.AreEqual(1, coordinator.LoadStartCount, "復旧の判断だけでは再ロードしない。");

            // 保留していた復旧は実行側が 1 回だけ受け取れる。
            Assert.IsTrue(coordinator.TryConsumeRecovery(), "復旧を受け取れる。");
            Assert.IsFalse(coordinator.TryConsumeRecovery(), "2 回は受け取れない（無限再試行しない）。");

            // タイムアウト後は、遅れて完了した目的地を通常どおり Ready にできない（§6.3）。
            Assert.IsFalse(coordinator.NotifyBinding(id),
                "タイムアウト後の到着は受け付けない（目的地を活動させない）。");

            // 復旧先も失敗したら Error に留める。旧 Scene が使えないので時計は止めたまま。
            Assert.IsTrue(coordinator.NotifyFailed(id, oldSceneUsable: false));
            Assert.AreEqual(AreaTransitionPhase.Failed, coordinator.Phase);
            Assert.IsTrue(clock.IsFrozen, "旧 Scene が使えないなら Gameplay を動かさない。");
            Assert.IsFalse(coordinator.IsTransitioning, "Failed は遷移中ではない（新しい要求を受けられる）。");

            // ---- 旧 Scene が生きている時点での失敗は、元の活動へ戻せる ----
            var clock2 = new GameplayClockGate();
            var recoverable = new AreaTransitionCoordinator(catalog, conditions, clock2);
            AreaTransitionDecision a2 = recoverable.TryRequest(ToB());
            Assert.IsTrue(clock2.IsFrozen);
            Assert.IsTrue(recoverable.NotifyFailed(a2.TransitionId, oldSceneUsable: true));
            Assert.AreEqual(AreaTransitionPhase.Failed, recoverable.Phase);
            Assert.AreEqual(0, recoverable.LoadStartCount, "ロード前の失敗では 1 度もロードしていない。");
            Assert.IsFalse(clock2.IsFrozen,
                "旧 Scene が生きている失敗では時計を戻す。戻さないと留まったまま何も操作できない。");
        }

        /// <summary>
        /// <see cref="AreaActorTransferPort.Capture"/> が<b>止めてから採る</b>ことを実物で確かめる（§4.4／§6.2 手順 4→5）。
        /// 順序を間違えると、中断で生じた攻撃 CD が Snapshot に乗らない。
        /// </summary>
        private void AssertTransferPortCancelsBeforeCapture()
        {
            CompanionRig rig = MakeCompanionRig();
            rig.Tracker.TickTargeting();
            rig.Combat.TickCombat(0f);
            Assert.IsTrue(rig.Combat.IsAttacking, "前提：攻撃が始まっている。");
            rig.Tracker.TickTargeting();
            rig.Combat.TickCombat(RigStartup);

            Assert.AreEqual(0f, rig.Combat.ExportTransferSnapshot().CooldownRemaining, 1e-4f,
                "前提：攻撃中なので CD はまだ 0。");

            var portGo = new GameObject("TransferPort");
            _spawned.Add(portGo);
            var port = portGo.AddComponent<AreaActorTransferPort>();
            port.Bind(null, null, rig.Actor, null, rig.Combat, null, null,
                rig.Actor.GetComponent<CompanionStateArbiter>());

            AreaTransferSnapshot snapshot = port.Capture(
                CompanionIds.Inumaru, new StableId("area_p5_a"), new StableId("area_p5_a_start"));

            Assert.IsFalse(rig.Combat.IsAttacking, "採取の前に攻撃が止まっている。");
            Assert.AreEqual(RigCooldown, snapshot.CompanionCombat.CooldownRemaining, 1e-3f,
                "中断で生じた CD が Snapshot に乗る（Port が止めてから採っている）。");
            Assert.IsTrue(snapshot.HasCompanion);
            Assert.AreEqual(CompanionIds.Inumaru.Value, snapshot.CompanionId.Value);
        }
    }
}
