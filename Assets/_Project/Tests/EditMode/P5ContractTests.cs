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
using Momotaro.Data.Events;
using Momotaro.Data.Progression;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Enemy;
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
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Navigation;
using Momotaro.Presentation.Cameras;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.World;
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

        // ---------------------------------------------------------------- E11〜E14・E30（Interact）

        /// <summary>
        /// P5-E11：Interact の候補は<b>近い順、同距離なら StableId の辞書順</b>で決まり、
        /// 表示した対象と実行する対象が一致する（§7.1 の 3・4）。
        ///
        /// 同距離の決着を ID で固定するのは、並び順や登録順で結果が変わらないようにするため。
        /// 「たまたま先に登録された方」が選ばれる実装は、Scene を作り直すたびに挙動が変わる。
        /// </summary>
        [Test]
        public void Interaction_SelectsNearestThenStableId()
        {
            var area = new StableId("area_p5_a");
            var probe = new AlwaysClearProbe();

            var far = new FakeInteractable("door_far", area, new Vector3(1.2f, 0f, 0f));
            var near = new FakeInteractable("door_near", area, new Vector3(0.4f, 0f, 0f));
            var candidates = new List<IAreaInteractable> { far, near };

            Assert.IsTrue(AreaInteractionSelector.TrySelect(
                candidates, Vector3.zero, area, 0, 1.6f, probe,
                out IAreaInteractable chosen, out AreaInteractionRejection reason));
            Assert.AreEqual(AreaInteractionRejection.None, reason);
            Assert.AreSame(near, chosen, "近い方が選ばれる。");

            // 登録順を入れ替えても結果は変わらない。
            candidates.Reverse();
            Assert.IsTrue(AreaInteractionSelector.TrySelect(
                candidates, Vector3.zero, area, 0, 1.6f, probe, out chosen, out _));
            Assert.AreSame(near, chosen, "登録順で結果が変わらない。");

            // ---- 同距離は StableId の辞書順で決める ----
            var tieB = new FakeInteractable("lever_b", area, new Vector3(0.8f, 0f, 0f));
            var tieA = new FakeInteractable("lever_a", area, new Vector3(0f, 0f, 0.8f));
            var tied = new List<IAreaInteractable> { tieB, tieA };

            Assert.IsTrue(AreaInteractionSelector.TrySelect(
                tied, Vector3.zero, area, 0, 1.6f, probe, out chosen, out _));
            Assert.AreSame(tieA, chosen, "同距離は StableId の辞書順（lever_a < lever_b）。");

            tied.Reverse();
            Assert.IsTrue(AreaInteractionSelector.TrySelect(
                tied, Vector3.zero, area, 0, 1.6f, probe, out chosen, out _));
            Assert.AreSame(tieA, chosen, "順番を変えても同じ対象が選ばれる。");

            // ---- 表示と実行が一致する（窓口を通して見る） ----
            Rig rig = MakeInteractionRig(area);
            AreaInteractableRegistry.Register(tieA);
            AreaInteractableRegistry.Register(tieB);

            Assert.IsTrue(rig.Controller.Peek(out IAreaInteractable shown, out _));
            Assert.AreSame(tieA, shown, "表示される候補。");
            Assert.IsTrue(rig.Controller.TryInteract(out AreaInteractionOutcome outcome));
            Assert.IsTrue(outcome.Handled);
            Assert.AreEqual(1, tieA.InteractCount, "表示した対象が実行される。");
            Assert.AreEqual(0, tieB.InteractCount, "別の対象は実行されない。");
        }

        /// <summary>
        /// P5-E12：壁越し・別エリア・別 Floor・距離の外は候補にしない（§7.1 の 1・2）。
        ///
        /// 距離は<b>境界</b>で見る。「だいたい近い」で通す実装は、
        /// 表示だけ出て押せない／押せるのに表示が出ないという食い違いを生む。
        /// 対象固有の受付距離が短い場合に<b>短い方</b>を使うことも併せて固定する。
        /// </summary>
        [Test]
        public void Interaction_RejectsOccludedWrongAreaOrFloor()
        {
            var area = new StableId("area_p5_a");
            var other = new StableId("area_p5_b");
            var clear = new AlwaysClearProbe();

            var target = new FakeInteractable("door_a", area, new Vector3(1.0f, 0f, 0f));
            var list = new List<IAreaInteractable> { target };

            // ---- 壁越しは除外 ----
            Assert.IsFalse(AreaInteractionSelector.TrySelect(
                list, Vector3.zero, area, 0, 1.6f, new NeverClearProbe(),
                out _, out AreaInteractionRejection reason));
            Assert.AreEqual(AreaInteractionRejection.NoTargetInRange, reason, "壁越しは候補にしない。");
            Assert.IsTrue(AreaInteractionSelector.TrySelect(list, Vector3.zero, area, 0, 1.6f, clear, out _, out _),
                "遮蔽が無ければ選ばれる（前提）。");

            // ---- 別エリア・別 Floor ----
            Assert.IsFalse(AreaInteractionSelector.TrySelect(list, Vector3.zero, other, 0, 1.6f, clear, out _, out _),
                "別エリアの対象は候補にしない。");
            Assert.IsFalse(AreaInteractionSelector.TrySelect(list, Vector3.zero, area, 1, 1.6f, clear, out _, out _),
                "別 Floor の対象は候補にしない。");

            // ---- 無効な対象 ----
            target.Available = false;
            Assert.IsFalse(AreaInteractionSelector.TrySelect(list, Vector3.zero, area, 0, 1.6f, clear, out _, out _),
                "無効な対象は候補にしない。");
            target.Available = true;

            // ---- 距離の境界（水平距離だけで見る。高さは無視する） ----
            var edge = new FakeInteractable("door_edge", area, new Vector3(1.6f, 3f, 0f));
            var edgeList = new List<IAreaInteractable> { edge };
            Assert.IsTrue(AreaInteractionSelector.TrySelect(edgeList, Vector3.zero, area, 0, 1.6f, clear, out _, out _),
                "ちょうど受付距離なら入る（高さは見ない）。");

            edge.Anchor = new Vector3(1.6001f, 0f, 0f);
            Assert.IsFalse(AreaInteractionSelector.TrySelect(edgeList, Vector3.zero, area, 0, 1.6f, clear, out _, out _),
                "受付距離を超えたら入らない。");

            // ---- 対象固有の受付距離が短ければ短い方を使う ----
            var strict = new FakeInteractable("point_strict", area, new Vector3(1.0f, 0f, 0f)) { Radius = 0.5f };
            var strictList = new List<IAreaInteractable> { strict };
            Assert.IsFalse(AreaInteractionSelector.TrySelect(strictList, Vector3.zero, area, 0, 1.6f, clear, out _, out _),
                "対象固有の受付距離が短ければ、そちらで弾く。");

            // 逆に固有の値が長くても、窓口の既定値は超えられない。
            var loose = new FakeInteractable("point_loose", area, new Vector3(2.0f, 0f, 0f)) { Radius = 9f };
            var looseList = new List<IAreaInteractable> { loose };
            Assert.IsFalse(AreaInteractionSelector.TrySelect(looseList, Vector3.zero, area, 0, 1.6f, clear, out _, out _),
                "対象固有の値で受付距離を伸ばせない。");
        }

        /// <summary>
        /// P5-E13：押下 1 回は<b>1 回だけ</b>使われ、次の候補にも次のフレームにも流れない（§7.1 末尾）。
        ///
        /// ここが崩れると「押したら手前の扉が断り、そのまま奥のレバーが動く」が起きる。
        /// 断りも実行のうちで、押下はそこで使い切られる。
        /// </summary>
        [Test]
        public void Interaction_ConsumesOneEdgeWithoutFallbackToSecondTarget()
        {
            var area = new StableId("area_p5_a");
            Rig rig = MakeInteractionRig(area);

            var refuser = new FakeInteractable("door_near", area, new Vector3(0.4f, 0f, 0f)) { Accept = false };
            var second = new FakeInteractable("door_far", area, new Vector3(0.9f, 0f, 0f));
            AreaInteractableRegistry.Register(refuser);
            AreaInteractableRegistry.Register(second);

            // ---- 断られても次点へ流さない ----
            rig.Input.InteractPressed = true;
            Assert.IsTrue(rig.Mediator.TickInput(), "押下は実行まで通る。");
            Assert.AreEqual(1, refuser.InteractCount, "手前の対象が 1 回だけ実行される。");
            Assert.AreEqual(0, second.InteractCount, "断られても次点は実行されない。");
            Assert.IsFalse(rig.Mediator.LastOutcome.Handled, "結果は「断られた」。");
            Assert.IsFalse(rig.Input.InteractPressed, "押下は使い切られている。");

            // ---- 押しっぱなし（新しいエッジが無い）では繰り返さない ----
            Assert.IsFalse(rig.Mediator.TickInput());
            Assert.IsFalse(rig.Mediator.TickInput());
            Assert.AreEqual(1, refuser.InteractCount, "押しっぱなしで連続実行しない。");

            // ---- 対象が無い押下は捨てる（次のフレームへ持ち越さない） ----
            AreaInteractableRegistry.Clear();
            rig.Input.InteractPressed = true;
            Assert.IsFalse(rig.Mediator.TickInput(), "対象が無ければ実行しない。");
            Assert.AreEqual(1, rig.Mediator.DiscardedCount, "捨てたことを数える。");
            Assert.IsFalse(rig.Input.InteractPressed, "捨てた押下は残らない。");

            AreaInteractableRegistry.Register(second);
            Assert.IsFalse(rig.Mediator.TickInput(), "捨てた押下が、あとから現れた対象に当たらない。");
            Assert.AreEqual(0, second.InteractCount);

            // ---- AreaReady でない・探索モードでない間は実行しない ----
            rig.Context.CloseForTransition();
            rig.Input.InteractPressed = true;
            Assert.IsFalse(rig.Mediator.TickInput(), "AreaReady でなければ実行しない。");
            Assert.AreEqual(AreaInteractionRejection.AreaNotReady, rig.Controller.LastRejection);
            rig.Context.ReopenAfterFailedTransition();

            rig.Modes.ChangeMode(GameMode.Combat);
            rig.Input.InteractPressed = true;
            Assert.IsFalse(rig.Mediator.TickInput(), "探索モードでなければ実行しない。");
            Assert.AreEqual(AreaInteractionRejection.WrongMode, rig.Controller.LastRejection);
        }

        /// <summary>
        /// P5-E14：レバーは Flag を<b>1 回だけ</b>確定し、門はその Flag から復元される（§7.3）。
        ///
        /// 順序は「内部 Flag → 通行・見た目 → 通知」。通知を先に出すと、購読者が見る世界がまだ閉じている。
        /// 通知の中から引き直されても変更は 1 回で済むことも、ここで固定する。
        /// </summary>
        [Test]
        public void Lever_CommitsOnceAndRestoresDoorFromSameFlag()
        {
            var area = new StableId("area_p5_a");
            var flag = new StableId("flag_p5_a_gate");

            var session = new GameSessionState();
            GameSessionProvider.Current = session;
            AreaRuntimeState record = session.GetOrCreateArea(area);

            var doorGo = new GameObject("Door");
            _spawned.Add(doorGo);
            var blocker = doorGo.AddComponent<BoxCollider>();
            var closedVisual = new GameObject("ClosedVisual");
            _spawned.Add(closedVisual);
            var door = doorGo.AddComponent<AreaFlagDoor>();
            door.Bind(flag, blocker, closedVisual);

            var leverGo = new GameObject("Lever");
            _spawned.Add(leverGo);
            var lever = leverGo.AddComponent<AreaFlagLever>();
            lever.Bind(flag, area, door, leverGo.transform);

            // 通知の中から引き直す購読者を付ける（再入でも変更は 1 回）。
            //
            // 再入は<b>1 回だけ</b>にする。無制限に再入させると、守られていない実装は
            // StackOverflow で落ちるだけになり、「変更が 2 回起きた」という核心を見ないまま
            // 「とにかく落ちた」で通ってしまう（欠陥注入でそうなった）。
            int notified = 0;
            bool reentered = false;
            lever.Opened += _ =>
            {
                notified++;
                if (reentered)
                {
                    return;
                }

                reentered = true;
                lever.Interact(); // 通知の中から引き直す。
            };

            // ---- 1 回目：開通する ----
            AreaInteractionOutcome outcome = lever.Interact();
            Assert.IsTrue(outcome.Handled, "開通する。");
            Assert.AreEqual(1, lever.OpenedCount, "開通は 1 回。");
            Assert.IsTrue(reentered, "前提：通知の中から引き直している。");
            Assert.AreEqual(1, notified, "通知も 1 回（再入で二重に発火しない）。");
            Assert.AreEqual(1, door.AppliedCount, "門への適用も 1 回。");
            Assert.IsTrue(record.IsOpen(flag), "記録が正本として開通している。");
            Assert.IsFalse(blocker.enabled, "通行が開いている。");
            Assert.IsFalse(closedVisual.activeSelf, "閉じている見た目が消えている。");

            // ---- 2 回目：開通済みなので状態も通知も動かさない ----
            outcome = lever.Interact();
            Assert.IsFalse(outcome.Handled, "開通済みは断る。");
            Assert.AreEqual("開通済み", outcome.Message);
            Assert.AreEqual(1, lever.OpenedCount, "開通回数は増えない。");
            Assert.AreEqual(1, notified, "通知を再発火しない。");
            Assert.AreEqual(1, door.AppliedCount, "門への適用も増えない。");

            // ---- 別 Scene の門でも、同じ Flag から復元される ----
            var doorGo2 = new GameObject("Door2");
            _spawned.Add(doorGo2);
            var blocker2 = doorGo2.AddComponent<BoxCollider>();
            var door2 = doorGo2.AddComponent<AreaFlagDoor>();
            door2.Bind(flag, blocker2, null);

            Assert.IsTrue(blocker2.enabled, "前提：作り直した門は閉じている。");
            Assert.IsTrue(door2.RestoreFrom(record), "記録から復元する。");
            Assert.IsFalse(blocker2.enabled, "復元後は通行できる。");
            Assert.AreEqual(1, door2.AppliedCount);

            // ---- 通行を開けられない門は、見た目だけ開かない ----
            var brokenGo = new GameObject("BrokenDoor");
            _spawned.Add(brokenGo);
            var brokenVisual = new GameObject("BrokenVisual");
            _spawned.Add(brokenVisual);
            var broken = brokenGo.AddComponent<AreaFlagDoor>();
            broken.Bind(new StableId("flag_p5_a_broken"), null, brokenVisual);

            LogAssert.Expect(LogType.Error, new Regex("Door could not be opened"));
            Assert.IsFalse(broken.TryApplyOpened(out string error), "Collider が無ければ失敗させる。");
            Assert.IsNotEmpty(error);
            Assert.IsFalse(broken.IsOpened);
            Assert.IsTrue(brokenVisual.activeSelf, "失敗したら見た目も変えない（開いて見えて通れない、を作らない）。");

            GameSessionProvider.Current = null;
        }

        /// <summary>
        /// P5-E30：P5 では<b>地点を指定しない調査依頼を拒否</b>し、指定した地点がそのまま依頼される（§7.1）。
        ///
        /// P4 の選択は「距離 → 前方 → ID」、P5 の共通窓口は「距離 → ID」で、向きを見ない。
        /// だから<b>前方順位と ID 順位が逆転する配置</b>では両者の答えが違う。
        /// 共通窓口が選んだ地点をそのまま渡していれば表示と依頼は一致し、
        /// 依頼先が選び直していれば食い違う。ここで見ているのはその差。
        /// </summary>
        [Test]
        public void ExplicitInvestigation_RejectsImplicitSelectionAndKeepsChosenId()
        {
            var area = new StableId("area_p5_a");

            // ---- 引数なし入口は、選択も開始もせずに拒否する ----
            var go = new GameObject("Coordinator");
            _spawned.Add(go);
            var coordinator = go.AddComponent<InvestigationCoordinator>();
            coordinator.ExplicitTargetOnly = true;

            InvestigationRequestResult implicitResult = coordinator.TryRequest();
            Assert.IsFalse(implicitResult.Accepted, "引数なしの依頼は通らない。");
            Assert.AreEqual(InvestigationRejectReason.ImplicitSelectionDisabled, implicitResult.Reason,
                "誤呼出と分かる理由で拒否する（NotWired 等に紛れさせない）。");
            Assert.AreEqual(0, implicitResult.RequestId, "依頼を開始していない。");
            Assert.IsNull(implicitResult.Point, "地点を選んでもいない。");
            Assert.AreEqual(1, coordinator.ImplicitRequestRefusedCount, "誤呼出を数える（Validator が見る）。");

            Assert.AreEqual(InvestigationRejectReason.ImplicitSelectionDisabled,
                coordinator.Peek(out IInvestigationPoint peeked), "表示側の入口も同じ扱い。");
            Assert.IsNull(peeked);

            // P4 Scene の既存互換：モードを外せば従来どおり動く（未配線なので NotWired まで進む）。
            coordinator.ExplicitTargetOnly = false;
            Assert.AreEqual(InvestigationRejectReason.NotWired, coordinator.TryRequest().Reason,
                "P4 構成では従来どおり評価へ進む。");

            // ---- 前方順位と ID 順位が逆転する配置で、表示対象がそのまま依頼される ----
            //
            // 主人公は +Z を向いている。前方にあるのは forward 側だが ID は後ろ（point_z）。
            // P4 の規則なら前方が勝ち、P5 の規則なら ID が勝つ。P5 の窓口は後者を選ぶ。
            var forward = new FakeInteractable("point_z_forward", area, new Vector3(0f, 0f, 0.8f));
            var side = new FakeInteractable("point_a_side", area, new Vector3(0.8f, 0f, 0f));

            Assert.IsTrue(AreaInteractionSelector.TrySelect(
                new List<IAreaInteractable> { forward, side }, Vector3.zero, area, 0, 1.6f,
                new AlwaysClearProbe(), out IAreaInteractable chosen, out _));
            Assert.AreSame(side, chosen, "P5 は向きを見ないので、同距離は ID 順で決まる。");

            // 選ばれた対象を、地点指定の狭い入口へそのまま渡す。
            var requests = new RecordingExplicitRequest();
            var adapterGo = new GameObject("Adapter");
            _spawned.Add(adapterGo);
            var adapter = adapterGo.AddComponent<InvestigationInteractable>();
            var point = new FakeInvestigationPoint(chosen.InteractableId);
            adapter.Bind(point, requests, area);

            Assert.AreEqual(chosen.InteractableId.Value, adapter.InteractableId.Value,
                "Adapter は担当地点をそのまま名乗る。");
            adapter.Interact();
            Assert.AreEqual(1, requests.Calls, "依頼は 1 回。");
            Assert.AreEqual(chosen.InteractableId.Value, requests.LastPointId.Value,
                "表示した PointId と実依頼の PointId が一致する（近くの別地点へすり替わらない）。");
        }

        // ---------------------------------------------------------------- Interact の道具

        private sealed class Rig
        {
            public AreaContext Context;
            public AreaInteractionController Controller;
            public AreaInteractInput Mediator;
            public FakeInteractInput Input;
            public GameModeService Modes;
        }

        /// <summary>窓口・仲介・Context を組んだ最小構成（Scene を作らずに押下の流れだけを見る）。</summary>
        private Rig MakeInteractionRig(StableId areaId)
        {
            AreaInteractableRegistry.Clear();

            var modes = new GameModeService(GameMode.Exploration);
            GameModeProvider.Current = modes;

            var contextGo = new GameObject("AreaContext");
            _spawned.Add(contextGo);
            var context = contextGo.AddComponent<AreaContext>();
            context.BeginInitialize(areaId, new StableId("area_p5_a_start"));
            context.MarkPrepared();
            context.Activate();

            var playerGo = new GameObject("PlayerAnchor");
            _spawned.Add(playerGo);
            playerGo.transform.position = Vector3.zero;

            var controllerGo = new GameObject("Interaction");
            _spawned.Add(controllerGo);
            var controller = controllerGo.AddComponent<AreaInteractionController>();
            controller.Bind(context, playerGo.transform);
            controller.SetObstacleProbe(new AlwaysClearProbe());

            var mediator = controllerGo.AddComponent<AreaInteractInput>();
            mediator.Bind(controller);
            var input = new FakeInteractInput();
            mediator.SetInput(input);

            return new Rig
            {
                Context = context,
                Controller = controller,
                Mediator = mediator,
                Input = input,
                Modes = modes,
            };
        }

        private sealed class FakeInteractable : IAreaInteractable
        {
            private readonly string _id;

            public FakeInteractable(string id, StableId areaId, Vector3 anchor)
            {
                _id = id;
                AreaId = areaId;
                Anchor = anchor;
            }

            public Vector3 Anchor { get; set; }
            public bool Available { get; set; } = true;
            public bool Accept { get; set; } = true;
            public float Radius { get; set; }
            public int InteractCount { get; private set; }

            public StableId InteractableId => new StableId(_id);
            public StableId AreaId { get; }
            public int FloorId => 0;
            public Vector3 InteractionAnchor => Anchor;
            public float InteractionRadius => Radius;
            public bool IsAvailable => Available;
            public string Prompt => _id;

            public AreaInteractionOutcome Interact()
            {
                InteractCount++;
                return Accept
                    ? AreaInteractionOutcome.Accepted(_id)
                    : AreaInteractionOutcome.Refused("いまはできない");
            }
        }

        private sealed class AlwaysClearProbe : IObstacleProbe
        {
            public bool IsClear(Vector3 from, Vector3 to) => true;
        }

        private sealed class NeverClearProbe : IObstacleProbe
        {
            public bool IsClear(Vector3 from, Vector3 to) => false;
        }

        private sealed class FakeInteractInput : IInteractInput
        {
            public bool InteractPressed { get; set; }

            public bool ConsumeInteractPressed()
            {
                if (!InteractPressed)
                {
                    return false;
                }

                InteractPressed = false;
                return true;
            }

            public void DiscardInteractPressed() => InteractPressed = false;
        }

        private sealed class RecordingExplicitRequest : MonoBehaviour, IExplicitInvestigationRequest
        {
            public int Calls { get; private set; }
            public StableId LastPointId { get; private set; }

            public InvestigationRequestResult RequestAt(StableId pointId)
            {
                Calls++;
                LastPointId = pointId;
                return new InvestigationRequestResult(true, Calls, InvestigationRejectReason.None, null);
            }

            public InvestigationRejectReason PeekAt(StableId pointId, out IInvestigationPoint point)
            {
                point = null;
                return InvestigationRejectReason.None;
            }
        }

        private sealed class FakeInvestigationPoint : IInvestigationPoint
        {
            public FakeInvestigationPoint(StableId pointId)
            {
                PointId = pointId;
            }

            public StableId PointId { get; }
            public StableId RequiredCompanion => CompanionIds.Inumaru;
            public StableId DiscoveryId => new StableId("discovery_test");
            public Vector3 Position => Vector3.zero;
            public Vector3 ApproachPosition => Vector3.zero;
            public Vector3 ApproachFacing => Vector3.forward;
            public bool IsAvailable => true;
            public InvestigationSettings Settings => new InvestigationSettings(1.6f, 3f, 0.5f, 5f, 1f, 2f);
            public string Prompt => "調べる";
            public string MissingCompanionHint => "未加入";
            public string CompletedText => "調査済み";
        }

        // ---------------------------------------------------------------- E22・E23（経路とワープ）

        /// <summary>
        /// P5-E22：経路追従は <b>Complete だけを成功扱いにし</b>、再探索は上限つきで、
        /// 位置の書込み口は増えない（§10.1）。
        ///
        /// Partial を成功にすると、閉じた門の手前まで歩いては止まり、
        /// 止まったことを停滞と数えてワープへ進む、という筋の悪い流れになる。
        /// 再探索に上限が無いと、届かない場所を延々と計算し続けて何も起きない。
        /// </summary>
        [Test]
        public void FollowPath_UsesOneMovementWriterAndBoundedRetries()
        {
            var settings = new PathFollowSettings(
                directDistance: 2.5f, requeryInterval: 0.5f, targetMoveThreshold: 1f,
                cornerArriveDistance: 0.6f, stallSeconds: 3f, maxRetries: 2, progressEpsilon: 0.02f);

            var provider = new FakePathProvider();
            var model = new CompanionPathFollowModel();

            Vector3 self = Vector3.zero;
            Vector3 target = new Vector3(10f, 0f, 0f);

            // ---- 直線で通れる近距離は既存追従（§10.1 の 1 行目） ----
            Assert.AreEqual(PathFollowDecision.Direct,
                model.Tick(new PathFollowInput(self, new Vector3(1f, 0f, 0f), true), settings, provider, 0.1f),
                "近くて直線が通るなら経路を使わない。");
            Assert.IsTrue(model.LastDirectWasNear, "それが §10.1 の 1 行目の「近距離」。");
            Assert.AreEqual(0, provider.Calls, "問い合わせもしない。");

            Assert.AreEqual(PathFollowDecision.Direct,
                model.Tick(new PathFollowInput(self, target, true), settings, provider, 0.1f),
                "遠くても直線で通れるなら経路は要らない。");
            Assert.IsFalse(model.LastDirectWasNear, "こちらは「近距離」ではない。");
            Assert.AreEqual(0, provider.Calls);

            // ---- <b>近くても壁に遮られていれば経路を使う</b>（§10.1 の 2 行目。GPT レビュー R4 の指摘 3）----
            //
            // 距離だけで遮蔽を無視すると、薄い壁を挟んだ 1m で直進し続けて張り付く。
            // 「近い」は通れることの保証ではない。
            var nearBlocked = new CompanionPathFollowModel();
            var nearProvider = new FakePathProvider
            {
                Result = PathQueryResult.Complete(new[] { new Vector3(0f, 0f, 1.5f), new Vector3(1f, 0f, 0f) }),
            };

            Assert.AreEqual(PathFollowDecision.MoveToCorner,
                nearBlocked.Tick(
                    new PathFollowInput(self, new Vector3(1f, 0f, 0f), false), settings, nearProvider, 0.1f),
                "近くても遮られていれば迂回する。");
            Assert.AreEqual(1, nearProvider.Calls, "近距離でも問い合わせる。");
            Assert.AreEqual(new Vector3(0f, 0f, 1.5f), nearBlocked.NextCorner, "角へ向かう。");

            // ---- Complete：角へ向かう ----
            provider.Result = PathQueryResult.Complete(new[] { new Vector3(0f, 0f, 5f), target });
            Assert.AreEqual(PathFollowDecision.MoveToCorner,
                model.Tick(new PathFollowInput(self, target, false), settings, provider, 0.1f));
            Assert.AreEqual(PathQueryStatus.Complete, model.Status);
            Assert.AreEqual(new Vector3(0f, 0f, 5f), model.NextCorner, "最初の角へ向かう。");
            Assert.AreEqual(1, provider.Calls);

            // ---- 再探索は間隔を守る（§10.1 の 0.5 秒） ----
            model.Tick(new PathFollowInput(self, target, false), settings, provider, 0.2f);
            Assert.AreEqual(1, provider.Calls, "間隔の内では探し直さない。");
            model.Tick(new PathFollowInput(self, target, false), settings, provider, 0.4f);
            Assert.AreEqual(2, provider.Calls, "間隔を超えたら探し直す。");

            // ---- 目標が 1 unit 以上動いたら、間隔を待たずに探し直す ----
            int before = provider.Calls;
            model.Tick(new PathFollowInput(self, target + new Vector3(0f, 0f, 1.5f), false), settings, provider, 0.01f);
            Assert.AreEqual(before + 1, provider.Calls, "目標が動いたら間隔を待たない。");

            // ---- 門の開通は、間隔を待たずに探し直す ----
            before = provider.Calls;
            model.NotifyWorldChanged();
            model.Tick(new PathFollowInput(self, target + new Vector3(0f, 0f, 1.5f), false), settings, provider, 0.01f);
            Assert.AreEqual(before + 1, provider.Calls, "通行状態が変わったら間隔を待たない。");

            // ---- Partial は成功にしない。上限まで探し直して失敗する ----
            var partialModel = new CompanionPathFollowModel();
            var partialProvider = new FakePathProvider
            {
                Result = PathQueryResult.Partial(new[] { new Vector3(0f, 0f, 3f) }),
            };

            // 最初の 1 回は「再探索」ではない。届かない間は<b>待つ</b>（壁へ突っ込ませない）。
            PathFollowDecision decision =
                partialModel.Tick(new PathFollowInput(self, target, false), settings, partialProvider, 0.1f);
            Assert.AreEqual(PathFollowDecision.Waiting, decision, "Partial は成功扱いにしない。まだ待つ。");
            Assert.AreEqual(PathQueryStatus.Partial, partialModel.Status);
            Assert.AreEqual(0, partialModel.RetryCount, "初回の問い合わせは再探索に数えない。");
            Assert.AreEqual(1, partialProvider.Calls);

            // 間隔を置いて探し直す。2 回まで。
            Assert.AreEqual(PathFollowDecision.Waiting,
                partialModel.Tick(new PathFollowInput(self, target, false), settings, partialProvider, 0.6f));
            Assert.AreEqual(1, partialModel.RetryCount);

            Assert.AreEqual(PathFollowDecision.Failed,
                partialModel.Tick(new PathFollowInput(self, target, false), settings, partialProvider, 0.6f),
                "上限まで探し直しても届かなければ失敗。");
            Assert.AreEqual(2, partialModel.RetryCount, "再探索は 2 回まで。");
            Assert.AreEqual(3, partialProvider.Calls, "最初の 1 回＋再探索 2 回。");

            // さらに Tick しても、再探索は増えない（無制限に探し続けない）。
            partialModel.Tick(new PathFollowInput(self, target, false), settings, partialProvider, 0.6f);
            Assert.AreEqual(2, partialModel.RetryCount, "上限を超えて探し直さない。");

            // ---- Invalid も同じ扱い ----
            var invalidModel = new CompanionPathFollowModel();
            var invalidProvider = new FakePathProvider { Result = PathQueryResult.Invalid() };
            Assert.AreEqual(PathFollowDecision.Waiting,
                invalidModel.Tick(new PathFollowInput(self, target, false), settings, invalidProvider, 0.1f));
            invalidModel.Tick(new PathFollowInput(self, target, false), settings, invalidProvider, 0.6f);
            Assert.AreEqual(PathFollowDecision.Failed,
                invalidModel.Tick(new PathFollowInput(self, target, false), settings, invalidProvider, 0.6f));
            Assert.AreEqual(2, invalidModel.RetryCount);

            // ---- 供給元が未注入なら、経路追従をせずに止まる（勝手に直線で突っ込まない） ----
            var unwired = new CompanionPathFollowModel();
            Assert.AreEqual(PathFollowDecision.Failed,
                unwired.Tick(new PathFollowInput(self, target, false), settings, null, 0.1f),
                "供給元が無ければ経路追従を止める（§10.1）。");

            // ---- 停滞：角へ近づけない時間が上限を超えたら失敗へ ----
            var stallModel = new CompanionPathFollowModel();
            var stallProvider = new FakePathProvider
            {
                Result = PathQueryResult.Complete(new[] { new Vector3(0f, 0f, 5f), target }),
            };

            Assert.AreEqual(PathFollowDecision.MoveToCorner,
                stallModel.Tick(new PathFollowInput(self, target, false), settings, stallProvider, 0.1f));

            // 同じ場所に留まり続ける（角へ近づけない）。
            for (int i = 0; i < 200 && stallModel.Decision != PathFollowDecision.Failed; i++)
            {
                stallModel.Tick(new PathFollowInput(self, target, false), settings, stallProvider, 0.2f);
            }

            Assert.AreEqual(PathFollowDecision.Failed, stallModel.Decision, "停滞したら失敗にする。");
            Assert.AreEqual(2, stallModel.RetryCount, "失敗までに探し直すのは上限まで。");

            // ---- 所有権：位置を書く口は増やさない（§10.1「NavMeshAgent を置かない」） ----
            AssertNoNavMeshAgentInGameplay();

            // ---- 二重の停滞判定にしない：経路追従中は既存 FollowModel が停滞を積まない ----
            var followModel = new CompanionFollowModel();
            CompanionFollowSettings followSettings = new CompanionFollowSettings(
                spacing: 1.2f, stopDistance: 0.5f, resumeDistance: 1.0f,
                warpDistance: 100f, stuckSeconds: 1f, stuckProgressEpsilon: 0.02f);

            // 迂回中の想定：隊列位置から離れた場所に居続ける。
            var stuckInput = new CompanionFollowInput(
                Vector3.zero, Vector3.forward, new Vector3(8f, 0f, 0f), 0, pathFollowActive: true);
            for (int i = 0; i < 20; i++)
            {
                followModel.Tick(stuckInput, followSettings, 0.2f);
            }

            Assert.AreEqual(0f, followModel.StuckSeconds, 1e-4f,
                "経路で迂回している間は停滞を積まない（判定を 2 つ競わせない。§10.1）。");
            Assert.AreEqual(0, followModel.WarpRequests, "迂回しているだけでワープしない。");
        }

        /// <summary>
        /// P5-E23：ワープ先は<b>固定順で検査して、危険なら使わない</b>。
        /// 候補がなければ<b>止まる</b>（壁内へ押し込まない。§10.2）。
        ///
        /// 「経路計算が失敗した」は、閉じた門の未開通側へ先回りしてよい理由にならない。
        /// だから候補ごとに「主人公と同じ通行可能側か」を確かめる。
        /// </summary>
        [Test]
        public void SafeWarp_RejectsClosedSideBlockedAndWrongFloor()
        {
            Vector3 leader = Vector3.zero;
            var first = new Vector3(1f, 0f, 0f);
            var second = new Vector3(0f, 0f, 1f);
            var third = new Vector3(-1f, 0f, 0f);
            var candidates = new List<Vector3> { first, second, third };

            // ---- 全部安全なら、固定順の先頭が選ばれる（近い順に並べ替えない） ----
            var probe = new FakeWarpProbe();
            Assert.IsTrue(SafeWarpSelector.TrySelect(
                candidates, leader, warpAllowed: true, probe, out Vector3 chosen, out SafeWarpRejection reason));
            Assert.AreEqual(SafeWarpRejection.None, reason);
            Assert.AreEqual(first, chosen, "与えられた順の先頭。");

            // ---- 床でない候補は飛ばす ----
            probe.NotGround.Add(first);
            Assert.IsTrue(SafeWarpSelector.TrySelect(candidates, leader, true, probe, out chosen, out _));
            Assert.AreEqual(second, chosen, "床でない候補は使わない。");

            // ---- 壁・水・閉門に重なる候補は飛ばす ----
            probe.Blocked.Add(second);
            Assert.IsTrue(SafeWarpSelector.TrySelect(candidates, leader, true, probe, out chosen, out _));
            Assert.AreEqual(third, chosen, "塞がっている候補は使わない。");

            // ---- 主人公と繋がっていない候補（閉門の向こう側）は飛ばす ----
            probe.Disconnected.Add(third);
            Assert.IsFalse(SafeWarpSelector.TrySelect(candidates, leader, true, probe, out chosen, out reason),
                "繋がっていない側へ先回りさせない（§10.2 の 1 行目）。");
            Assert.AreEqual(SafeWarpRejection.AllUnsafe, reason);
            Assert.AreEqual(default(Vector3), chosen, "選ばなかったときは値を返さない。");

            // ---- 候補が無ければ止まる（最後の候補へ逃げない） ----
            Assert.IsFalse(SafeWarpSelector.TrySelect(
                new List<Vector3>(), leader, true, probe, out _, out reason));
            Assert.AreEqual(SafeWarpRejection.NoCandidate, reason);

            // ---- 安全性を確かめられない構成では置かない（安全側へ倒す） ----
            Assert.IsFalse(SafeWarpSelector.TrySelect(candidates, leader, true, null, out _, out reason));
            Assert.AreEqual(SafeWarpRejection.AllUnsafe, reason);

            // ---- 攻撃・防御・被弾・Down／Away・探索占有中はワープしない ----
            var clean = new FakeWarpProbe();
            Assert.IsFalse(SafeWarpSelector.TrySelect(candidates, leader, false, clean, out _, out reason),
                "禁止されている状態では、安全な候補があっても跳ばない。");
            Assert.AreEqual(SafeWarpRejection.NotAllowed, reason);
            Assert.AreEqual(0, clean.Calls, "候補を調べにすら行かない。");
        }

        /// <summary>
        /// §10.1 の「長距離 Follow 経路に NavMeshAgent を置かない」を構造で確かめる。
        ///
        /// 規則は「位置・速度・向きの実書込みは <c>CompanionMovementArbiter</c> → <c>CompanionMotor</c> を維持する」。
        /// <c>NavMeshAgent</c> は自分で Transform を動かすので、置いた時点で書込み口が 2 つになる。
        /// コメントで約束するのではなく、<b>型を持っていないこと</b>で担保する。
        /// </summary>
        private static void AssertNoNavMeshAgentInGameplay()
        {
            System.Reflection.Assembly gameplay = typeof(CompanionFollowController).Assembly;
            var offenders = new List<string>();

            foreach (System.Type type in gameplay.GetTypes())
            {
                foreach (System.Reflection.FieldInfo field in type.GetFields(
                             System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                             | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                             | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType == typeof(UnityEngine.AI.NavMeshAgent))
                    {
                        offenders.Add(type.FullName + "." + field.Name);
                    }
                }
            }

            CollectionAssert.IsEmpty(offenders,
                "Gameplay が NavMeshAgent を持っている（位置の書込み口が 2 つになる。§10.1）: "
                + string.Join(", ", offenders));
        }

        private sealed class FakePathProvider : IPathProvider
        {
            public PathQueryResult Result { get; set; } = PathQueryResult.Invalid();

            public int Calls { get; private set; }

            public PathQueryResult Query(Vector3 from, Vector3 to)
            {
                Calls++;
                return Result;
            }
        }

        private sealed class FakeWarpProbe : IWarpCandidateProbe
        {
            public readonly List<Vector3> NotGround = new List<Vector3>();
            public readonly List<Vector3> Blocked = new List<Vector3>();
            public readonly List<Vector3> Disconnected = new List<Vector3>();

            public int Calls { get; private set; }

            public bool IsOnNavigableGround(Vector3 position)
            {
                Calls++;
                return !NotGround.Contains(position);
            }

            public bool IsBlocked(Vector3 position) => Blocked.Contains(position);

            public bool IsConnectedToLeader(Vector3 position, Vector3 leaderPosition) =>
                !Disconnected.Contains(position);
        }

        // ---------------------------------------------------------------- E24（カメラ範囲）

        /// <summary>
        /// P5-E24：斜め見下ろしの<b>床での見える範囲</b>を俯角と画面比から正しく出し、
        /// 部屋へ収める。部屋が狭い軸は中央固定。領域の重なりは決まった順で決着する（§11）。
        ///
        /// ここを間違えると、部屋の端でカメラが寄りすぎる／床の外（仮背景）が見える、という
        /// 見ればすぐ分かる壊れ方をする。だが「どのくらい伸びるか」は目分量では詰められないので、
        /// 数で固定しておく。
        /// </summary>
        [Test]
        public void CameraBounds_HandlePitchAspectAndSmallRooms()
        {
            // ---- 俯角で奥行きが伸びる（Z 幅を orthographicSize と同じにしない） ----
            //
            // 真上（90 度）なら縦はそのまま。45 度なら 1/sin45 ≒ 1.414 倍、30 度なら 2 倍。
            const float size = 10f;
            const float aspect169 = 16f / 9f;

            Vector2 top = CameraBoundsMath.HalfFootprint(size, aspect169, 90f);
            Assert.AreEqual(size * aspect169, top.x, 1e-3f, "横は orthographicSize × aspect。");
            Assert.AreEqual(size, top.y, 1e-3f, "真上なら奥行きは伸びない。");

            Vector2 tilted45 = CameraBoundsMath.HalfFootprint(size, aspect169, 45f);
            Assert.AreEqual(size / Mathf.Sin(45f * Mathf.Deg2Rad), tilted45.y, 1e-3f,
                "45 度では 1/sin45 倍に伸びる。");
            Assert.Greater(tilted45.y, size * 1.4f, "実際に伸びている（素通しになっていない）。");

            Vector2 tilted30 = CameraBoundsMath.HalfFootprint(size, aspect169, 30f);
            Assert.AreEqual(size * 2f, tilted30.y, 1e-3f, "30 度では 2 倍。");
            Assert.AreEqual(tilted45.x, tilted30.x, 1e-3f, "俯角で横幅は変わらない（横軸は床と平行）。");

            // ---- 画面比で変わるのは横だけ ----
            Vector2 a43 = CameraBoundsMath.HalfFootprint(size, 4f / 3f, 45f);
            Vector2 a219 = CameraBoundsMath.HalfFootprint(size, 21f / 9f, 45f);

            Assert.AreEqual(size * (4f / 3f), a43.x, 1e-3f, "4:3 の横。");
            Assert.AreEqual(size * (21f / 9f), a219.x, 1e-3f, "21:9 の横。");
            Assert.AreEqual(tilted45.y, a43.y, 1e-3f, "奥行きは画面比で変わらない。");
            Assert.AreEqual(tilted45.y, a219.y, 1e-3f);
            Assert.Less(a43.x, tilted45.x, "4:3 は 16:9 より狭い。");
            Assert.Greater(a219.x, tilted45.x, "21:9 は 16:9 より広い。");

            // ---- 部屋へ収める ----
            //
            // 40 × 40 の部屋に、半分が 8 × 6 の見える範囲。端へ寄っても外が見えない位置で止まる。
            var room = new CameraRegionDefinition(
                new StableId("region_room"), 0, new Vector2(0f, 0f), new Vector2(40f, 40f));
            var half = new Vector2(8f, 6f);

            Vector3 inside = CameraBoundsMath.ClampFocus(new Vector3(3f, 0f, -2f), room, half);
            Assert.AreEqual(3f, inside.x, 1e-3f, "内側なら動かさない。");
            Assert.AreEqual(-2f, inside.z, 1e-3f);

            Vector3 edge = CameraBoundsMath.ClampFocus(new Vector3(100f, 0f, -100f), room, half);
            Assert.AreEqual(20f - 8f, edge.x, 1e-3f, "右端は 部屋の端 − 見える半分。");
            Assert.AreEqual(-20f + 6f, edge.z, 1e-3f, "奥行きも同じ考え方。");

            // 高さ（y）は触らない。カメラの高さは別に決まる。
            Assert.AreEqual(0f, edge.y, 1e-3f);

            // ---- 部屋が見える範囲より小さい軸は中央固定（無理な clamp をしない） ----
            var small = new CameraRegionDefinition(
                new StableId("region_small"), 0, new Vector2(5f, -3f), new Vector2(6f, 40f));

            Vector3 centered = CameraBoundsMath.ClampFocus(new Vector3(100f, 0f, 0f), small, half);
            Assert.AreEqual(5f, centered.x, 1e-3f,
                "横が入りきらないので中央固定（min が max を超えて跳ねたりしない）。");
            Assert.AreEqual(0f, centered.z, 1e-3f, "入る軸は普通に収める。");

            Vector3 centeredOther = CameraBoundsMath.ClampFocus(new Vector3(-100f, 0f, 0f), small, half);
            Assert.AreEqual(centered.x, centeredOther.x, 1e-3f,
                "どちら側から来ても同じ位置（境目で往復しない）。");

            // ---- 領域の重なりは Priority 降順 → RegionId 辞書順 ----
            var low = new CameraRegionDefinition(
                new StableId("region_a_low"), 0, Vector2.zero, new Vector2(20f, 20f));
            var high = new CameraRegionDefinition(
                new StableId("region_z_high"), 5, Vector2.zero, new Vector2(20f, 20f));
            var sameHigh = new CameraRegionDefinition(
                new StableId("region_b_high"), 5, Vector2.zero, new Vector2(20f, 20f));

            var overlapping = new List<CameraRegionDefinition> { low, high, sameHigh };
            Assert.IsTrue(CameraRegionSelector.TrySelect(overlapping, Vector3.zero, out CameraRegionDefinition chosen));
            Assert.AreEqual(sameHigh.RegionId.Value, chosen.RegionId.Value,
                "優先度が同じなら RegionId の辞書順（region_b_high < region_z_high）。");

            overlapping.Reverse();
            Assert.IsTrue(CameraRegionSelector.TrySelect(overlapping, Vector3.zero, out chosen));
            Assert.AreEqual(sameHigh.RegionId.Value, chosen.RegionId.Value, "並び順で結果が変わらない。");

            // 1 つしか含まないなら、優先度に関わらずそれを使う。
            Assert.IsTrue(CameraRegionSelector.TrySelect(
                new List<CameraRegionDefinition> { low, high }, new Vector3(0f, 0f, 0f), out chosen));
            Assert.AreEqual(high.RegionId.Value, chosen.RegionId.Value, "重なっていれば優先度が高い方。");

            var far = new CameraRegionDefinition(
                new StableId("region_far"), 9, new Vector2(100f, 100f), new Vector2(10f, 10f));
            Assert.IsTrue(CameraRegionSelector.TrySelect(
                new List<CameraRegionDefinition> { far, low }, Vector3.zero, out chosen));
            Assert.AreEqual(low.RegionId.Value, chosen.RegionId.Value,
                "含んでいない領域は、優先度が高くても選ばない。");

            // どこにも入らなければ false（呼び出し側が Area の既定領域を使う）。
            Assert.IsFalse(CameraRegionSelector.TrySelect(
                new List<CameraRegionDefinition> { low }, new Vector3(1000f, 0f, 0f), out _));
            Assert.IsFalse(CameraRegionSelector.TrySelect(null, Vector3.zero, out _));

            // ---- 部屋切替の補間：途中も範囲内、到着は即時 ----
            var blend = new CameraFocusBlend();
            var roomA = new CameraRegionDefinition(
                new StableId("region_a"), 0, new Vector2(0f, 0f), new Vector2(40f, 40f));
            var roomB = new CameraRegionDefinition(
                new StableId("region_b"), 0, new Vector2(100f, 0f), new Vector2(40f, 40f));

            blend.Tick(new Vector3(0f, 0f, 0f), roomA, half, 0.016f);
            Assert.IsFalse(blend.IsBlending, "最初のフレームは補間しない（流れて見えるものが無い）。");

            // 部屋が変わった。0.15 秒かけて寄る。
            Vector3 mid = blend.Tick(new Vector3(100f, 0f, 0f), roomB, half, 0.05f);
            Assert.IsTrue(blend.IsBlending, "切替で補間が始まる。");
            Assert.AreEqual(1, blend.RegionChangeCount);
            Assert.Greater(mid.x, roomB.Min.x + half.x - 1e-3f,
                "補間の途中でも新しい部屋の範囲内に居る（はみ出さない）。x=" + mid.x);

            // 残りを進めれば終点に落ち着く。
            for (int i = 0; i < 10; i++)
            {
                blend.Tick(new Vector3(100f, 0f, 0f), roomB, half, 0.05f);
            }

            Assert.IsFalse(blend.IsBlending, "時間が経てば補間は終わる。");
            Assert.AreEqual(100f, blend.Current.x, 1e-2f, "終点に落ち着く。");

            // Scene 到着・死亡再開は補間しない。ただし範囲は守る（到着の 1 フレームだけ外が見える、を作らない）。
            blend.SnapTo(new Vector3(-50f, 0f, 7f), roomA, half);
            Assert.IsFalse(blend.IsBlending, "到着では補間しない。");
            Assert.AreEqual(-20f + 8f, blend.Current.x, 1e-3f, "即時配置でも部屋からはみ出さない。");

            Vector3 afterSnap = blend.Tick(new Vector3(-50f, 0f, 7f), roomA, half, 0.016f);
            Assert.AreEqual(-20f + 8f, afterSnap.x, 1e-3f, "直後のフレームで切替と誤判定しない。");
            Assert.AreEqual(1, blend.RegionChangeCount, "SnapTo は切替として数えない。");
        }

        // ---------------------------------------------------------------- E09（到着トークンの世代）

        /// <summary>
        /// P5-E09（補強）：到着側が報告に使うのは<b>初期化を始めた時点で捕まえたトークン</b>であって、
        /// 報告のときに共有領域を読み直した値ではない（§6.2 末尾）。
        ///
        /// 読み直すと「渡す値」と「比べる値」が同じものになり、照合が常に成立してしまう。
        /// すると、初期化の途中で次の遷移が始まっていた場合に
        /// <b>古い Scene の初期化担当が、まだ読み込んでもいない新しい世代を「準備できた」ことにする</b>。
        /// 同じエリア・同じ入口への再遷移（死亡再開）で現実に起きる形なので、そこを固定する。
        /// </summary>
        [Test]
        public void ArrivalToken_CapturedAtStart_DoesNotMarkANewerGeneration()
        {
            var areaA = new StableId("area_p5_a");
            var entry = new StableId("area_p5_a_start");

            AreaPendingArrival.Clear();
            AreaPendingArrival.ResetDiagnostics();
            try
            {
                // 世代 11 の到着が始まり、A の初期化担当がトークンを捕まえる。
                AreaPendingArrival.Set(11, areaA, entry);
                int captured = AreaInitializer.CaptureArrivalToken(areaA);
                Assert.AreEqual(11, captured, "開始時点の世代を捕まえる。");

                // 初期化の途中で、同じエリア・同じ入口への新しい遷移が始まった。
                AreaPendingArrival.Set(12, areaA, entry);

                // 捕まえたトークンで報告する＝効かない。読み直していたらここが通ってしまう。
                Assert.IsFalse(AreaPendingArrival.TryMarkPrepared(captured, areaA, entry),
                    "古い世代の準備完了は効かない。");
                Assert.IsFalse(AreaPendingArrival.IsPreparedFor(12),
                    "新しい世代が、古い Scene の報告で準備済みにならない。");
                Assert.AreEqual(1, AreaPendingArrival.MismatchedCompletionCount, "無視して数える。");

                // 自分の世代なら通る（拒否するだけの実装にしていない）。
                Assert.IsTrue(AreaPendingArrival.TryMarkPrepared(12, areaA, entry));
                Assert.IsTrue(AreaPendingArrival.IsPreparedFor(12));
                Assert.IsFalse(AreaPendingArrival.IsPreparedFor(11), "世代が違えば準備済みにならない。");

                // 別エリア宛ての要求は自分のものではない＝直開き扱い（0）。
                AreaPendingArrival.Set(13, new StableId("area_p5_b"), new StableId("area_p5_b_from_a"));
                Assert.AreEqual(0, AreaInitializer.CaptureArrivalToken(areaA),
                    "別エリア宛ての要求を自分の到着と取り違えない。");
            }
            finally
            {
                AreaPendingArrival.Clear();
                AreaPendingArrival.ResetDiagnostics();
            }
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

        // ---------------------------------------------------------------- E15〜E19（Encounter）

        /// <summary>P5-07 の Encounter 検査で使う一式。</summary>
        private sealed class EncounterRig
        {
            public AreaEncounterRunner Runner;
            public CombatSessionController Session;
            public FakeEncounterConditions Conditions;
            public FakeEncounterSpawner Spawner;
            public FakeArena Arena;
            public RecordingInterrupts Interrupts;
            public PlayerDefeatChannel PlayerDefeats;
            public AreaRuntimeState Area;
            public CombatRewardCollector Rewards;
            public PlayerProgressHolder Progress;
            public GameSessionState Session5;
            public AreaInteractionController Interaction;
            public FakeInteractable Interactable;
            public GameModeService Modes;
        }

        private sealed class FakeEncounterConditions : IAreaEncounterConditions
        {
            public bool IsAreaReady { get; set; } = true;
            public bool IsExploration { get; set; } = true;
            public bool IsPlayerAlive { get; set; } = true;
            public bool IsTransitioning { get; set; }
        }

        private sealed class FakeArena : IArenaBoundary
        {
            public bool AllowEnable { get; set; } = true;
            public string Error { get; set; } = "境界の内側へ安全に配置できません。";
            public bool IsEnabled { get; private set; }
            public int EnableCount { get; private set; }
            public int DisableCount { get; private set; }

            public bool TryEnable(out string error)
            {
                if (!AllowEnable)
                {
                    error = Error;
                    return false;
                }

                IsEnabled = true;
                EnableCount++;
                error = null;
                return true;
            }

            public void Disable()
            {
                if (IsEnabled)
                {
                    DisableCount++;
                }

                IsEnabled = false;
            }
        }

        /// <summary>撤収の通知から開始を要求し直す購読者（§8.2 手順 3 の狙いを再現する）。</summary>
        private sealed class RecordingInterrupts : IEncounterInterruptSink
        {
            public AreaEncounterRunner Runner;
            public AreaInteractionController Interaction;
            public bool RequestStartAgain;
            public int Count { get; private set; }
            public int RunIdSeenAtInterrupt { get; private set; } = -1;
            public GameMode ModeSeenAtInterrupt { get; private set; } = GameMode.GameOver;
            public EncounterStartRejection ReentryRejection { get; private set; } = EncounterStartRejection.None;
            public bool ReentryStarted { get; private set; }

            /// <summary>撤収の通知から<b>実 Interact 窓口</b>へ再入したときの結果。</summary>
            public bool ReentryInteractExecuted { get; private set; }

            public AreaInteractionRejection ReentryInteractRejection { get; private set; } =
                AreaInteractionRejection.None;

            public void InterruptForEncounter()
            {
                Count++;
                RunIdSeenAtInterrupt = Runner != null ? Runner.RunId : -1;
                ModeSeenAtInterrupt = GameModeProvider.Current != null
                    ? GameModeProvider.Current.Current
                    : GameMode.GameOver;

                // 再入は<b>1 回だけ</b>にする。門が閉じていない実装では再要求がまた撤収を呼ぶので、
                // 無制限にすると StackOverflow で落ちて「何が壊れたか」が読めなくなる（P5-04 で踏んだ形）。
                if (!RequestStartAgain || Count > 1)
                {
                    return;
                }

                // ① 実 Interact 窓口から（扉・レバー・調査はすべてここを通る）。
                if (Interaction != null)
                {
                    ReentryInteractExecuted = Interaction.TryInteract(out _);
                    ReentryInteractRejection = Interaction.LastRejection;
                }

                // ② 開始要求そのものから。
                if (Runner != null)
                {
                    EncounterStartDecision again = Runner.TryStart();
                    ReentryStarted = again.Started;
                    ReentryRejection = again.Rejection;
                }
            }
        }

        /// <summary>撃破を手で起こせる敵役（実 Prefab を使わずに勝敗と報酬の経路を通す）。</summary>
        private sealed class FakeSpawnedEnemy : IEnemyDefeatSource
        {
            private static int _next = 9000;

            public FakeSpawnedEnemy(StableId enemyId, EnemyRole role, RewardData reward)
            {
                EnemyId = enemyId;
                Role = role;
                Reward = reward;
                DamageableId = ++_next;
            }

            public StableId EnemyId { get; }
            public EnemyRole Role { get; set; }

            /// <summary>この敵が載せる報酬（共有・GrantOnce の検査でテストが差し替える）。</summary>
            public RewardData Reward { get; set; }
            public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();
            public int DamageableId { get; }
            public bool IsDefeated { get; private set; }

            /// <summary>活動を許可されているか（生成途中に動いていないことを見る）。</summary>
            public bool Active { get; set; }

            public void Defeat()
            {
                IsDefeated = true;
                Publish();
            }

            /// <summary>同じ撃破をもう一度通知する（重複排除の検査用）。</summary>
            public void Publish()
            {
                Defeats.Publish(new EnemyDefeatedEvent(
                    DamageableId, new EnemyRewardRequest(DamageableId, Role, Reward, Vector3.zero)));
            }
        }

        /// <summary>生成の Fake。全数失敗・部分生成・黙って少なく作る、を作り分ける。</summary>
        private sealed class FakeEncounterSpawner : IEncounterSpawner
        {
            private readonly List<FakeSpawnedEnemy> _enemies = new List<FakeSpawnedEnemy>();

            public CombatSessionController Session;

            /// <summary>この数を超えて作らない（部分生成）。</summary>
            public int MaxSpawn = int.MaxValue;

            /// <summary>作れないことを<b>申告する</b>（TrySpawnAll が false）。</summary>
            public bool ReportFailure;

            /// <summary>作れなかったのに<b>成功を返す</b>（手順 8 の全数確認が拾う形）。</summary>
            public bool SilentPartial;

            public Func<StableId, (EnemyRole role, RewardData reward)> Resolve;

            public int ActivateCount { get; private set; }
            public int ReleaseCount { get; private set; }
            public bool SpawnedActive { get; private set; }
            public int SpawnedCount => _enemies.Count;
            public IReadOnlyList<IEnemyDefeatSource> Spawned => _enemies;
            public IReadOnlyList<FakeSpawnedEnemy> Enemies => _enemies;

            public bool TrySpawnAll(in EncounterPlan plan, out string error)
            {
                SpawnedActive = false;

                for (int i = 0; i < plan.PlannedCount; i++)
                {
                    if (_enemies.Count >= MaxSpawn || ReportFailure)
                    {
                        if (SilentPartial)
                        {
                            break; // 少なく作ったまま「成功」を返す。
                        }

                        // 途中まで作ったものは自分で片付ける。
                        ReleaseAll();
                        error = "敵 '" + plan.EnemyIds[i].Value + "' を解決できません。";
                        return false;
                    }

                    (EnemyRole role, RewardData reward) resolved = Resolve != null
                        ? Resolve(plan.EnemyIds[i])
                        : (EnemyRole.Melee, null);

                    var enemy = new FakeSpawnedEnemy(plan.EnemyIds[i], resolved.role, resolved.reward);
                    _enemies.Add(enemy);
                    Session?.RegisterEnemy(enemy);
                }

                error = null;
                return true;
            }

            /// <summary>生成直後の Feedback 接続の通知（§8.2 手順 7）。</summary>
            public event Action SpawnedActivated;

            public void ActivateSpawned()
            {
                ActivateCount++;
                SpawnedActive = true;
                SpawnedActivated?.Invoke();
                for (int i = 0; i < _enemies.Count; i++)
                {
                    _enemies[i].Active = true;
                }
            }

            public void ReleaseAll()
            {
                if (_enemies.Count > 0)
                {
                    ReleaseCount++;
                }

                for (int i = 0; i < _enemies.Count; i++)
                {
                    Session?.UnregisterEnemy(_enemies[i]);
                    _enemies[i].Active = false;
                }

                _enemies.Clear();
                SpawnedActive = false;
            }
        }

        private static readonly StableId EncounterBRoad = new StableId("encounter_p5_b_road");
        private static readonly StableId EnemyMelee = new StableId("enemy_melee_prototype");
        private static readonly StableId EnemyRanged = new StableId("enemy_ranged_prototype");

        private EncounterRig MakeEncounterRig(params string[] enemyIds)
        {
            if (enemyIds == null || enemyIds.Length == 0)
            {
                enemyIds = new[] { EnemyMelee.Value, EnemyRanged.Value };
            }

            var encounter = ScriptableObject.CreateInstance<EncounterData>();
            _spawned.Add(encounter);
            encounter.name = "SO_Encounter_P5_B_Road";
            SetPrivate(encounter, "_id", EncounterBRoad);
            var ids = new List<StableId>();
            foreach (string id in enemyIds)
            {
                ids.Add(new StableId(id));
            }

            SetPrivate(encounter, "_enemyIds", ids);

            var go = new GameObject("AreaEncounter");
            _spawned.Add(go);

            CombatSessionController session = go.AddComponent<CombatSessionController>();
            PlayerProgressHolder progress = NewHolder("P07_Progress");
            var session5 = new GameSessionState();
            progress.Bind(session5.Progress);
            AreaRuntimeState area = session5.GetOrCreateArea(new StableId("area_p5_b"));

            CombatRewardCollector rewards = go.AddComponent<CombatRewardCollector>();
            rewards.Bind(session, progress);
            InvokePrivate(rewards, "OnEnable");

            var conditions = new FakeEncounterConditions();
            var arena = new FakeArena();
            var spawner = new FakeEncounterSpawner { Session = session, Resolve = ResolveTestEnemy };
            var interrupts = new RecordingInterrupts();
            var playerDefeats = new PlayerDefeatChannel();

            // 実 Interact 窓口。§8.3 の「戦闘開始を先に確定し、Interact を拒否する」を
            // <b>本物の窓口で</b>確かめるために組む（開始要求を直接叩くだけでは窓口を通らない）。
            var areaId = new StableId("area_p5_b");
            Rig interactRig = MakeInteractionRig(areaId);
            var interactable = new FakeInteractable("door_p5_b_to_a", areaId, new Vector3(0.5f, 0f, 0f));
            AreaInteractableRegistry.Register(interactable);

            AreaEncounterRunner runner = go.AddComponent<AreaEncounterRunner>();
            interactRig.Controller.BindEncounter(runner);
            interrupts.Runner = runner;
            interrupts.Interaction = interactRig.Controller;
            runner.Bind(encounter, session, conditions, spawner, arena, interrupts, () => area, () => session5.RespawnCycle);
            runner.BindPlayerDefeat(playerDefeats);
            InvokePrivate(runner, "OnEnable");

            return new EncounterRig
            {
                Runner = runner,
                Session = session,
                Conditions = conditions,
                Spawner = spawner,
                Arena = arena,
                Interrupts = interrupts,
                PlayerDefeats = playerDefeats,
                Area = area,
                Rewards = rewards,
                Progress = progress,
                Session5 = session5,
                Interaction = interactRig.Controller,
                Interactable = interactable,
                Modes = interactRig.Modes,
            };
        }

        /// <summary>敵 ID から役割と報酬を決める（P5 の 2 種。10＋12＝22。§8.5）。</summary>
        private (EnemyRole role, RewardData reward) ResolveTestEnemy(StableId enemyId)
        {
            if (enemyId.Equals(EnemyRanged))
            {
                return (EnemyRole.Ranged, NewReward("reward_enemy_ranged", 12, grantOnce: false));
            }

            return (EnemyRole.Melee, NewReward("reward_enemy_melee", 10, grantOnce: false));
        }

        private RewardData NewReward(string id, int virtue, bool grantOnce)
        {
            var reward = ScriptableObject.CreateInstance<RewardData>();
            _spawned.Add(reward);
            reward.name = "SO_" + id;
            SetPrivate(reward, "_id", new StableId(id));
            SetPrivate(reward, "_virtueAmount", virtue);
            SetPrivate(reward, "_grantOnce", grantOnce);
            return reward;
        }

        /// <summary>
        /// P5-E15：開始の門は<b>探索の撤収より前</b>に閉じる（§8.2 手順 3 → 4）。
        ///
        /// 手順 4 で調査を撤収すると、撤収の通知を受けた購読者がその場で開始を要求し直すことがある。
        /// 先に Starting と RunId を確定していないと、その再要求が<b>新しい Starting</b> を作り、
        /// 先に走っていた実行の後始末が新しい実行を壊す。
        /// </summary>
        [Test]
        public void Encounter_StartGatePrecedesInvestigationCallbacks()
        {
            EncounterRig rig = MakeEncounterRig();

            // 前提：戦闘が始まる前なら、この Interact は通る。
            // ここが通らないと、あとで「拒否された」ことに意味が無くなる。
            Assert.IsTrue(rig.Interaction.TryInteract(out _), "前提：探索中は Interact が通る。");
            Assert.AreEqual(1, rig.Interactable.InteractCount);

            rig.Interrupts.RequestStartAgain = true;

            EncounterStartDecision decision = rig.Runner.TryStart();

            Assert.IsTrue(decision.Started, "開始する。理由=" + decision.Rejection);
            Assert.AreEqual(1, rig.Interrupts.Count, "撤収は 1 回だけ呼ぶ。");
            Assert.AreEqual(1, rig.Interrupts.RunIdSeenAtInterrupt,
                "撤収の時点で世代が確定している（手順 3 が手順 4 より前）。");

            // <b>撤収の時点ではまだ Exploration</b>。GameMode を Combat へ変えるのは手順 6 なので、
            // モードだけで閉じている実装はここで素通りする（GPT レビュー R4 の指摘 2）。
            Assert.AreEqual(GameMode.Exploration, rig.Interrupts.ModeSeenAtInterrupt,
                "前提：撤収の時点のモードはまだ探索。");

            // ---- 実 Interact 窓口からの再入も閉じる（§8.3 の競合表）----
            Assert.IsFalse(rig.Interrupts.ReentryInteractExecuted,
                "撤収の通知からの Interact は実行されない。");
            Assert.AreEqual(AreaInteractionRejection.EncounterStarting,
                rig.Interrupts.ReentryInteractRejection,
                "理由は「戦闘が始まっている」（モードではなく開始の確定で閉じる）。");
            Assert.AreEqual(1, rig.Interactable.InteractCount, "対象は 1 回しか実行されていない。");

            Assert.IsFalse(rig.Interrupts.ReentryStarted, "撤収の通知からの再要求は始まらない。");
            Assert.AreEqual(EncounterStartRejection.AlreadyRunning, rig.Interrupts.ReentryRejection,
                "再要求は「すでに開始済み」で閉じる。");

            Assert.AreEqual(1, rig.Runner.RunId, "再要求で世代を上書きしない。");
            Assert.AreEqual(AreaEncounterState.Playing, rig.Runner.State);
            Assert.AreEqual(1, rig.Spawner.ActivateCount, "生成と活動許可も 1 回だけ。");
            Assert.AreEqual(1, rig.Arena.EnableCount, "境界の有効化も 1 回だけ。");

            // 外からの追加要求も同じく閉じる（Interact・別 Trigger）。
            Assert.AreEqual(EncounterStartRejection.AlreadyRunning, rig.Runner.TryStart().Rejection);
            Assert.AreEqual(1, rig.Runner.RunId);
            Assert.IsFalse(rig.Interaction.TryInteract(out _), "戦闘中の Interact も閉じたまま。");
            Assert.AreEqual(1, rig.Interactable.InteractCount);

            // 解放されれば Interact は戻る（閉じっぱなしにしない）。
            rig.Spawner.Enemies[0].Defeat();
            rig.Spawner.Enemies[1].Defeat();
            rig.Runner.ResolvePending();
            Assert.AreEqual(AreaEncounterState.Cleared, rig.Runner.State);

            rig.Modes.ChangeMode(GameMode.Exploration);
            Assert.IsTrue(rig.Interaction.TryInteract(out _), "戦闘が終われば Interact は戻る。");
            Assert.AreEqual(2, rig.Interactable.InteractCount);
        }

        /// <summary>
        /// P5-E16：生成に失敗したら<b>勝利にも報酬にもしない</b>（§8.2 末尾）。
        /// 生成の途中で敵が活動しないことも見る。
        ///
        /// 「初期生存数 0 だけで勝利にしない」がここの核心。0 体で始めれば生存数は 0 なので、
        /// 生存数だけを見る実装は即勝利する。
        /// </summary>
        [Test]
        public void Encounter_SpawnFailureDoesNotWinOrGrantRewards()
        {
            // ---- 申告された失敗（必須 Prefab 欠落など）----
            EncounterRig rig = MakeEncounterRig();
            rig.Spawner.ReportFailure = true;

            EncounterStartDecision failed = rig.Runner.TryStart();

            Assert.IsFalse(failed.Started);
            Assert.AreEqual(EncounterStartRejection.SpawnFailed, failed.Rejection);
            Assert.AreEqual(AreaEncounterState.Failed, rig.Runner.State);
            Assert.AreEqual(0, rig.Spawner.SpawnedCount, "生成済みを破棄する。");
            Assert.AreEqual(0, rig.Session.RegisteredEnemyCount, "登録も戻す。");
            Assert.IsFalse(rig.Arena.IsEnabled, "境界も戻す。");
            Assert.AreEqual(0, rig.Spawner.ActivateCount, "活動は一度も許可しない。");
            Assert.AreEqual(0, rig.Area.ClearedEncounterRecordCount, "クリア記録は付けない。");
            Assert.AreEqual(0, rig.Progress.State.Virtue, "報酬も付かない。");

            // 生存数 0 のまま刻みを進めても勝利にならない。
            rig.Runner.Tick(0.1f);
            Assert.AreEqual(AreaEncounterState.Failed, rig.Runner.State, "0 体は勝利ではない。");
            Assert.AreEqual(0, rig.Area.ClearedEncounterRecordCount);

            // ---- 退出 → 再進入で再試行できる（§8.2）----
            rig.Spawner.ReportFailure = false;
            rig.Spawner.MaxSpawn = 1; // 部分生成。今度は「黙って少なく作る」形。
            rig.Spawner.SilentPartial = true;

            EncounterStartDecision partial = rig.Runner.TryStart();

            Assert.IsFalse(partial.Started, "予定数に届かない生成は開始にしない（手順 8 の全数確認）。");
            Assert.AreEqual(EncounterStartRejection.SpawnFailed, partial.Rejection);
            Assert.AreEqual(2, rig.Runner.RunId, "再試行は新しい世代で走る。");
            Assert.AreEqual(0, rig.Spawner.SpawnedCount, "部分生成も片付ける（死体を残さない）。");
            Assert.AreEqual(0, rig.Session.RegisteredEnemyCount);
            Assert.AreEqual(0, rig.Spawner.ActivateCount, "部分生成でも活動は許可しない。");
            Assert.AreEqual(0, rig.Progress.State.Virtue);

            // ---- 生成中に敵が活動していない（§8.2 末尾）----
            rig.Spawner.SilentPartial = false;
            rig.Spawner.MaxSpawn = int.MaxValue;

            Assert.IsTrue(rig.Runner.TryStart().Started);
            Assert.AreEqual(2, rig.Spawner.SpawnedCount);
            Assert.IsTrue(rig.Spawner.SpawnedActive, "全数そろって初めて活動を許可する。");
            Assert.AreEqual(1, rig.Spawner.ActivateCount);
        }


        /// <summary>
        /// P5-E16 の補助：アリーナ内部への再配置は<b>通常 Follow の Warp とは別の配置処理</b>
        /// （§8.2 手順 5、§10.2 末尾。GPT レビュー R4 の指摘 1）。
        ///
        /// §10.2 末尾は「Down／Away を含む HP・復帰残り・CD・表示資格を維持したまま、
        /// 安全なアリーナ内部候補へ配置する。Away を出撃扱いに変えない。
        /// 内部候補がなければ Encounter 開始を失敗とし、境界外に Down の本体を置き去りにしない」と決めている。
        ///
        /// <b>「要求を出した」で成功にしない。</b> Down 中に Warp 状態への遷移を要求すれば不正遷移になるし、
        /// 強制停止のラッチで移動要求が握り潰されることもある。それを成功と読むと、
        /// 境界の外に倒れた犬丸を置き去りにしたまま封鎖してしまう。
        /// </summary>
        [Test]
        public void ArenaRelocation_KeepsDownAwayValuesAndPlacesInside()
        {
            var bounds = new Bounds(new Vector3(4f, 0f, 0f), new Vector3(14f, 2f, 14f));

            foreach (CompanionState state in new[] { CompanionState.Down, CompanionState.Away })
            {
                RelocationRig rig = MakeRelocationRig(state, outsideAt: new Vector3(-20f, 0f, 0f));

                int hpBefore = rig.Vitals.CurrentHp;
                float recoveryBefore = rig.Vitals.Vitals.RecoveryRemaining;
                float cooldownBefore = rig.Combat.CooldownRemaining;
                int warpsBefore = rig.Motor.WarpCount;

                Assert.IsFalse(bounds.Contains(new Vector3(rig.Actor.transform.position.x, 0f,
                    rig.Actor.transform.position.z)), "前提：境界の外に居る。");

                bool placed = rig.Follow.TryRelocateInside(bounds, out SafeWarpRejection rejection);

                Assert.IsTrue(placed, "内側へ配置できる。理由=" + rejection);
                Vector3 after = rig.Actor.transform.position;
                Assert.IsTrue(
                    after.x >= bounds.min.x && after.x <= bounds.max.x
                    && after.z >= bounds.min.z && after.z <= bounds.max.z,
                    state + "：実位置が内側に収まる。位置=" + after);

                // <b>状態は触らない。</b> Away を出撃扱いに変えない。Down のまま置き直す。
                Assert.AreEqual(state, rig.Actor.State, state + "：状態を変えない。");
                Assert.AreEqual(0, rig.Actor.IllegalTransitionCount, state + "：不正遷移を出さない。");

                // 値は保持する。
                Assert.AreEqual(hpBefore, rig.Vitals.CurrentHp, state + "：HP を保つ。");
                Assert.AreEqual(recoveryBefore, rig.Vitals.Vitals.RecoveryRemaining, 1e-4f,
                    state + "：復帰残りを保つ。");
                Assert.AreEqual(cooldownBefore, rig.Combat.CooldownRemaining, 1e-4f, state + "：CD を保つ。");

                // 通常 Follow の Warp とは別の口を通る（診断が混ざらない）。
                Assert.AreEqual(warpsBefore, rig.Motor.WarpCount, state + "：通常 Warp には数えない。");
                Assert.AreEqual(1, rig.Motor.PlaceCount, state + "：配置の口を 1 回だけ通る。");
            }
        }

        /// <summary>
        /// P5-E16 の補助：強制停止で移動要求が握り潰される構成でも、<b>配置は成立する</b>。
        /// 逆に安全な内部候補が無ければ<b>成立しない</b>（境界外に置き去りにしない）。
        /// </summary>
        [Test]
        public void ArenaRelocation_SucceedsWhenMovementIsLatchedAndFailsWithoutSafeCandidate()
        {
            var bounds = new Bounds(new Vector3(4f, 0f, 0f), new Vector3(14f, 2f, 14f));

            // ---- 同じフレームに強制停止されていても置ける ----
            RelocationRig latched = MakeRelocationRig(CompanionState.Follow, new Vector3(-20f, 0f, 0f));
            latched.Arbiter.ForceStop(); // 以後の移動要求は所有者を問わず止められる。

            Assert.IsTrue(latched.Follow.TryRelocateInside(bounds, out SafeWarpRejection latchedReason),
                "移動要求が止まっていても配置はできる。理由=" + latchedReason);
            Vector3 placed = latched.Actor.transform.position;
            Assert.IsTrue(
                placed.x >= bounds.min.x && placed.x <= bounds.max.x
                && placed.z >= bounds.min.z && placed.z <= bounds.max.z,
                "実位置が内側に収まる。位置=" + placed);
            Assert.AreEqual(1, latched.Motor.PlaceCount);

            // ---- 内部の安全候補が無ければ失敗する ----
            RelocationRig unsafeRig = MakeRelocationRig(CompanionState.Down, new Vector3(-20f, 0f, 0f));
            unsafeRig.Probe.AllSafe = false;

            Vector3 before = unsafeRig.Actor.transform.position;
            Assert.IsFalse(unsafeRig.Follow.TryRelocateInside(bounds, out SafeWarpRejection reason),
                "安全な内部候補が無ければ配置しない。");
            Assert.AreEqual(SafeWarpRejection.AllUnsafe, reason);
            Assert.AreEqual(before, unsafeRig.Actor.transform.position, "壁内へ押し込まない（動かさない）。");
            Assert.AreEqual(0, unsafeRig.Motor.PlaceCount);
            Assert.AreEqual(CompanionState.Down, unsafeRig.Actor.State, "状態も変えない。");
            Assert.AreEqual(0, unsafeRig.Actor.IllegalTransitionCount);
        }

        /// <summary>あれば拾い、無ければ足す（RequireComponent の自動追加と競合させない）。</summary>
        private static T Ensure<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private sealed class RelocationRig
        {
            public CompanionActor Actor;
            public CompanionMotor Motor;
            public CompanionFollowController Follow;
            public CompanionHitReceiver Vitals;
            public CompanionCombatController Combat;
            public CompanionMovementArbiter Arbiter;
            public ConfigurableWarpProbe Probe;
            public Transform Leader;
        }

        /// <summary>安全性の答えを切り替えられる候補 probe（実 Scene を使わずに §10.2 の分岐を通す）。</summary>
        private sealed class ConfigurableWarpProbe : IWarpCandidateProbe
        {
            public bool AllSafe { get; set; } = true;

            public bool IsOnNavigableGround(Vector3 position) => AllSafe;

            public bool IsBlocked(Vector3 position) => !AllSafe;

            public bool IsConnectedToLeader(Vector3 position, Vector3 leaderPosition) => AllSafe;
        }

        private RelocationRig MakeRelocationRig(CompanionState state, Vector3 outsideAt)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivate(data, "_maxHp", 60);

            var leaderGo = new GameObject("Leader");
            _spawned.Add(leaderGo);
            leaderGo.transform.position = new Vector3(4f, 0f, 0f); // 境界の中心＝主人公。

            var go = new GameObject("Inumaru_Relocate");
            _spawned.Add(go);
            go.transform.position = outsideAt;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            actor.ResetState(state);

            // RequireComponent で自動追加される型がある。DisallowMultipleComponent の二重追加は
            // null を返すので、必ず「あれば拾う・無ければ足す」で取る。
            CompanionMotor motor = Ensure<CompanionMotor>(go);
            CompanionMovementArbiter arbiter = Ensure<CompanionMovementArbiter>(go);
            CompanionHitReceiver vitals = Ensure<CompanionHitReceiver>(go);
            vitals.Bind(actor);
            CompanionCombatController combat = Ensure<CompanionCombatController>(go);
            combat.Bind(actor, motor);

            CompanionFollowController follow = Ensure<CompanionFollowController>(go);
            follow.Bind(leaderGo.transform, actor, motor);
            var probe = new ConfigurableWarpProbe();
            follow.BindWarpProbe(probe);

            return new RelocationRig
            {
                Actor = actor,
                Motor = motor,
                Follow = follow,
                Vitals = vitals,
                Combat = combat,
                Arbiter = arbiter,
                Probe = probe,
                Leader = leaderGo.transform,
            };
        }

        /// <summary>
        /// P5-E17：勝利は<b>予定生成完了・生存 0・主人公生存</b>の全部が要る（§8.4 手順 3）。
        /// どれか 1 つでも欠けたら勝利にしない。
        /// </summary>
        [Test]
        public void Encounter_VictoryRequiresAllSpawnedAndPlayerAlive()
        {
            EncounterRig rig = MakeEncounterRig();
            Assert.IsTrue(rig.Runner.TryStart().Started);
            Assert.AreEqual(2, rig.Spawner.SpawnedCount);

            // ---- 1 体だけ倒しても勝利候補は出ない（生存 0 でない）----
            rig.Spawner.Enemies[0].Defeat();
            rig.Runner.Tick(0.1f);
            Assert.AreEqual(AreaEncounterState.Playing, rig.Runner.State, "生存が残っていれば勝利しない。");
            Assert.AreEqual(1, rig.Session.AliveEnemyCount);
            Assert.AreEqual(0, rig.Area.ClearedEncounterRecordCount);

            // ---- 全滅しても、主人公が生きていなければ勝利にしない ----
            rig.Conditions.IsPlayerAlive = false;
            rig.Spawner.Enemies[1].Defeat();
            Assert.AreEqual(0, rig.Session.AliveEnemyCount, "前提：生存 0。");

            rig.Runner.Tick(0.1f);
            Assert.AreEqual(AreaEncounterState.Defeated, rig.Runner.State,
                "全滅していても主人公が死んでいれば勝利にしない（§8.3 の死亡優先）。");
            Assert.AreEqual(0, rig.Area.ClearedEncounterRecordCount, "クリア記録は付かない。");

            // ---- 三条件が揃えばクリアし、活動 Context は「Encounter なし」へ戻る ----
            EncounterRig ok = MakeEncounterRig();
            Assert.IsTrue(ok.Runner.TryStart().Started);
            Assert.AreEqual(CombatSessionState.Playing, ok.Runner.ActivitySession,
                "戦闘中は戦闘として供給する。");

            ok.Spawner.Enemies[0].Defeat();
            ok.Spawner.Enemies[1].Defeat();
            ok.Runner.Tick(0.1f);

            Assert.AreEqual(AreaEncounterState.Cleared, ok.Runner.State);
            Assert.AreEqual(CombatSessionState.Victory, ok.Session.State, "既存 Session は Victory へ。");
            Assert.IsNull(ok.Runner.ActivitySession,
                "解放後は「活動中 Encounter なし」（§8.4 末尾。犬丸を永久停止させない）。");
            Assert.IsTrue(ok.Area.IsEncounterCleared(EncounterBRoad, 0), "この周期のクリアを記録する。");
            Assert.IsFalse(ok.Arena.IsEnabled, "一時境界を解放する。");
            Assert.AreEqual(0, ok.Spawner.SpawnedCount, "残留物を残さない。");
            Assert.AreEqual("戦闘終了", ok.Runner.ResultMessage, "短文だけを出す（結果パネルで止めない）。");

            // 一度クリアしたら通常往復では始まらない（§8.4 末尾）。
            Assert.AreEqual(EncounterStartRejection.AlreadyCleared, ok.Runner.TryStart().Rejection);

            // 死亡再開で周期が進めば、また始まる（§9.1）。
            ok.Session5.AdvanceRespawnCycle();
            Assert.IsFalse(ok.Area.IsEncounterCleared(EncounterBRoad, ok.Session5.RespawnCycle));
        }

        /// <summary>
        /// P5-E18：同じ刻みに死亡と全滅が届いたら<b>死亡が勝つ</b>（§8.3）。通知順に依らない。
        ///
        /// 最後の敵の通知でその場で勝利を確定すると、処理順だけで相打ちが勝利になる。
        /// 候補を溜めて刻みの終わりに決める形になっているかを、両方の順で見る。
        /// </summary>
        [Test]
        public void Encounter_SameTickPlayerDeathWinsOverClear()
        {
            // ---- 全滅 → 死亡 の順 ----
            EncounterRig a = MakeEncounterRig();
            Assert.IsTrue(a.Runner.TryStart().Started);

            a.Spawner.Enemies[0].Defeat();
            a.Spawner.Enemies[1].Defeat();
            Assert.AreEqual(AreaEncounterState.Playing, a.Runner.State,
                "最後の敵の通知だけでは外へ確定しない（刻みの終わりまで待つ）。");

            a.PlayerDefeats.Publish(new PlayerDefeatedEvent(1, Vector3.zero));
            a.Runner.Tick(0.1f);

            Assert.AreEqual(AreaEncounterState.Defeated, a.Runner.State, "死亡が優先される。");
            Assert.AreEqual(0, a.Area.ClearedEncounterRecordCount, "勝利記録は付けない。");
            Assert.IsFalse(a.Arena.IsEnabled);
            Assert.AreEqual(0, a.Spawner.SpawnedCount);

            // ---- 死亡 → 全滅 の順 ----
            EncounterRig b = MakeEncounterRig();
            Assert.IsTrue(b.Runner.TryStart().Started);

            b.PlayerDefeats.Publish(new PlayerDefeatedEvent(1, Vector3.zero));
            b.Spawner.Enemies[0].Defeat();
            b.Spawner.Enemies[1].Defeat();
            b.Runner.Tick(0.1f);

            Assert.AreEqual(AreaEncounterState.Defeated, b.Runner.State, "順が逆でも死亡が優先される。");
            Assert.AreEqual(0, b.Area.ClearedEncounterRecordCount);

            // 死亡しても、撃破した瞬間の徳は取り消さない（§8.5）。
            Assert.AreEqual(22, b.Progress.State.Virtue, "撃破済みの報酬は残る。");
        }

        /// <summary>
        /// P5-E19：撃破報酬は<b>勝利より先</b>に確定し、敵ごとに 1 回ずつ入る（§8.4 手順 2、§8.5）。
        /// 10＋12＝22。GrantOnce は RewardId 単位のまま変えない。
        /// </summary>
        [Test]
        public void EnemyRewards_PrecedeVictoryAndRespectExistingGrantOnce()
        {
            EncounterRig rig = MakeEncounterRig();

            var virtueWhenCleared = new List<int>();
            rig.Session.StateChanged += s =>
            {
                if (s == CombatSessionState.Victory)
                {
                    virtueWhenCleared.Add(rig.Progress.State.Virtue);
                }
            };

            Assert.IsTrue(rig.Runner.TryStart().Started);
            Assert.AreEqual(0, rig.Progress.State.Virtue);

            rig.Spawner.Enemies[0].Defeat(); // 近接 10
            Assert.AreEqual(10, rig.Progress.State.Virtue, "撃破ごとに入る。");

            // 同じ撃破をもう一度通知しても増えない（Session が重複を弾く）。
            rig.Spawner.Enemies[0].Publish();
            Assert.AreEqual(10, rig.Progress.State.Virtue, "重複通知では二重に付かない。");

            rig.Spawner.Enemies[1].Defeat(); // 遠距離 12
            Assert.AreEqual(22, rig.Progress.State.Virtue, "初回完勝で 22（§8.5）。");

            rig.Runner.Tick(0.1f);
            Assert.AreEqual(AreaEncounterState.Cleared, rig.Runner.State);
            Assert.AreEqual(1, virtueWhenCleared.Count, "勝利は 1 回。");
            Assert.AreEqual(22, virtueWhenCleared[0], "勝利の時点で報酬は確定済み（順序が逆ではない）。");
            Assert.AreEqual(2, rig.Rewards.GrantedCount);

            // ---- 同じ RewardData を 2 体が共有し、GrantOnce=false なら敵ごとに付く（§8.5）----
            EncounterRig shared = MakeEncounterRig(EnemyMelee.Value, EnemyMelee.Value);
            Assert.IsTrue(shared.Runner.TryStart().Started);
            RewardData sharedReward = NewReward("reward_shared_melee", 10, grantOnce: false);
            shared.Spawner.Enemies[0].Reward = sharedReward;
            shared.Spawner.Enemies[1].Reward = sharedReward;

            shared.Spawner.Enemies[0].Defeat();
            shared.Spawner.Enemies[1].Defeat();
            Assert.AreEqual(20, shared.Progress.State.Virtue, "GrantOnce=false は敵ごとに付く。");

            // ---- GrantOnce=true は RewardId 単位で一度だけ（既存の意味を変えない）----
            EncounterRig once = MakeEncounterRig(EnemyMelee.Value, EnemyMelee.Value);
            Assert.IsTrue(once.Runner.TryStart().Started);
            RewardData onceReward = NewReward("reward_once_melee", 10, grantOnce: true);
            once.Spawner.Enemies[0].Reward = onceReward;
            once.Spawner.Enemies[1].Reward = onceReward;

            once.Spawner.Enemies[0].Defeat();
            once.Spawner.Enemies[1].Defeat();
            Assert.AreEqual(10, once.Progress.State.Virtue, "GrantOnce=true は RewardId 単位で一度だけ。");
            Assert.AreEqual(1, once.Rewards.AlreadyGrantedCount);
        }
    }
}
