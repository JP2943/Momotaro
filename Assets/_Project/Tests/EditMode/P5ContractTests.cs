using System.Collections.Generic;
using Momotaro.Core.Identification;
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
    public sealed class P5ContractTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
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
    }
}
