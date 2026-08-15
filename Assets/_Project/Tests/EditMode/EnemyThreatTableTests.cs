using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-06：ヘイト・ターゲット選択 <see cref="EnemyThreatTable"/> の検証（§7）。§7.1 の全加算・対象補正、基礎ヘイト維持、
    /// 3 秒遅延＋毎秒 20% 減衰、24.99%／25% 切替境界、同点規則、対象 Down／離脱／範囲外の即時切替、近接攻撃中の切替固定、
    /// 戦闘終了 Reset を、Fake 対象（主人公／犬／猿／雉）で決定的に検証する。純粋・再現可能（時間は dt 注入）。
    /// </summary>
    public sealed class EnemyThreatTableTests
    {
        private sealed class FakeThreatTarget : IThreatTarget
        {
            public int ActorId { get; set; }
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat { get; set; }
            public float AcquiredThreatMultiplier { get; set; } = 1f;
        }

        private static FakeThreatTarget Player(int id = 1) => new FakeThreatTarget
        {
            ActorId = id, Faction = CombatFaction.Player, BaseThreat = 50f, AcquiredThreatMultiplier = 1f,
        };

        private static EnemyThreatTable Table() => new EnemyThreatTable(ThreatSettings.Default);

        private static List<IThreatTarget> List(params IThreatTarget[] ts) => new List<IThreatTarget>(ts);

        // ---- §7.1 加算値・対象補正 ----

        [Test]
        public void Additions_HpPoiseFlinchJustGuard_UseSpecWeights()
        {
            var t = Table();
            var p = Player();
            t.AddThreat(p, ThreatSource.HpDamage, 12f);   // +12
            t.AddThreat(p, ThreatSource.PoiseDamage, 8f); // +0.5*8 = 4
            t.AddThreat(p, ThreatSource.Flinch);          // +20
            t.AddThreat(p, ThreatSource.JustGuard);       // +30
            Assert.AreEqual(66f, t.GetAcquired(p.ActorId), 1e-4f);
            Assert.AreEqual(50f + 66f, t.GetThreat(p), 1e-4f, "基礎50＋獲得66。");
        }

        [Test]
        public void AcquiredMultiplier_AppliesPerSpecies_NotToBase()
        {
            var t = Table();
            var dog = new FakeThreatTarget { ActorId = 2, Faction = CombatFaction.Ally, BaseThreat = 0f, AcquiredThreatMultiplier = 1.5f };
            var monkey = new FakeThreatTarget { ActorId = 3, Faction = CombatFaction.Ally, BaseThreat = 0f, AcquiredThreatMultiplier = 1.2f };
            var pheasant = new FakeThreatTarget { ActorId = 4, Faction = CombatFaction.Ally, BaseThreat = 0f, AcquiredThreatMultiplier = 0.5f };

            t.AddThreat(dog, ThreatSource.HpDamage, 10f);      // 10*1.5 = 15
            t.AddThreat(monkey, ThreatSource.HpDamage, 10f);   // 10*1.2 = 12
            t.AddThreat(pheasant, ThreatSource.HpDamage, 10f); // 10*0.5 = 5

            Assert.AreEqual(15f, t.GetAcquired(dog.ActorId), 1e-4f);
            Assert.AreEqual(12f, t.GetAcquired(monkey.ActorId), 1e-4f);
            Assert.AreEqual(5f, t.GetAcquired(pheasant.ActorId), 1e-4f);
        }

        [Test]
        public void BaseHate_PresentWithoutAnyAction()
        {
            var t = Table();
            var p = Player();
            Assert.AreEqual(50f, t.GetThreat(p), 1e-4f, "行動前でも基礎ヘイトで選ばれる（単独 Player の安定）。");
        }

        // ---- §7.2 減衰（3 秒遅延・毎秒 20%・基礎維持） ----

        [Test]
        public void Decay_StartsAfter3s_Reduces20PercentPerSecond()
        {
            var t = Table();
            var p = Player();
            t.AddThreat(p, ThreatSource.HpDamage, 100f); // 獲得100
            var cands = List(p);

            t.UpdateSelection(cands, 1f, false); // 1s
            t.UpdateSelection(cands, 1f, false); // 2s
            Assert.AreEqual(100f, t.GetAcquired(p.ActorId), 1e-4f, "3 秒までは減衰しない。");

            t.UpdateSelection(cands, 1f, false); // 3s → 20% 減
            Assert.AreEqual(80f, t.GetAcquired(p.ActorId), 1e-4f);

            t.UpdateSelection(cands, 1f, false); // 4s → さらに 20%
            Assert.AreEqual(64f, t.GetAcquired(p.ActorId), 1e-4f);
        }

        [Test]
        public void Decay_MaintainsBaseHate()
        {
            var t = Table();
            var p = Player();
            t.AddThreat(p, ThreatSource.HpDamage, 100f);
            var cands = List(p);
            for (int i = 0; i < 100; i++)
            {
                t.UpdateSelection(cands, 1f, false);
            }

            Assert.GreaterOrEqual(t.GetThreat(p), 50f, "基礎ヘイトは減衰後も維持される。");
        }

        [Test]
        public void Decay_ResetTimerOnNewGain()
        {
            var t = Table();
            var p = Player();
            var cands = List(p);
            t.AddThreat(p, ThreatSource.HpDamage, 100f);
            t.UpdateSelection(cands, 2.9f, false); // 遅延手前
            t.AddThreat(p, ThreatSource.HpDamage, 0f); // 0 は無視（獲得は増えないが…）
            // 実加算で遅延をリセット。
            t.AddThreat(p, ThreatSource.HpDamage, 1f); // 獲得 101、タイマ 0
            t.UpdateSelection(cands, 2.9f, false);     // まだ 3 秒未満
            Assert.AreEqual(101f, t.GetAcquired(p.ActorId), 1e-4f, "獲得で減衰待ちがリセットされる。");
        }

        // ---- §7.2 25% 切替境界 ----

        [Test]
        public void Switch_RequiresAtLeast25PercentHigher()
        {
            var t = Table();
            var cur = new FakeThreatTarget { ActorId = 1, BaseThreat = 100f };
            var cands = List(cur);
            t.UpdateSelection(cands, 0.1f, false); // cur を選択
            Assert.AreEqual(1, t.CurrentTargetId);

            var challenger = new FakeThreatTarget { ActorId = 2, BaseThreat = 124.99f };
            cands.Add(challenger);
            t.UpdateSelection(cands, 1f, false); // 再評価：124.99 < 125 → 維持
            Assert.AreEqual(1, t.CurrentTargetId, "24.99% では切替えない。");

            challenger.BaseThreat = 125f;
            t.UpdateSelection(cands, 1f, false); // 125 >= 125 → 切替
            Assert.AreEqual(2, t.CurrentTargetId, "ちょうど 25% で切替。");
        }

        [Test]
        public void Reevaluate_OnlyEverySecond()
        {
            var t = Table();
            var cur = new FakeThreatTarget { ActorId = 1, BaseThreat = 100f };
            var cands = List(cur);
            t.UpdateSelection(cands, 0.1f, false);

            var big = new FakeThreatTarget { ActorId = 2, BaseThreat = 1000f };
            cands.Add(big);
            t.UpdateSelection(cands, 0.5f, false); // 1 秒未満：再評価しない
            Assert.AreEqual(1, t.CurrentTargetId, "1 秒未満は再評価しない（両者有効）。");
            t.UpdateSelection(cands, 0.5f, false); // 累計 1 秒：再評価 → 切替
            Assert.AreEqual(2, t.CurrentTargetId);
        }

        // ---- §7.2 同点規則 ----

        [Test]
        public void Tie_KeepsCurrent_AndInitialSelectionPrefersLowestId()
        {
            var t = Table();
            var a = new FakeThreatTarget { ActorId = 2, BaseThreat = 50f };
            var b = new FakeThreatTarget { ActorId = 1, BaseThreat = 50f };
            var cands = List(a, b);
            t.UpdateSelection(cands, 0.1f, false); // 同点初期選択 → ActorId 昇順で 1
            Assert.AreEqual(1, t.CurrentTargetId, "同点初期選択は ActorId 最小。");

            // 現在 (id1) と同点の id2 は切替対象にならない（25% 未満）。
            t.UpdateSelection(cands, 1f, false);
            Assert.AreEqual(1, t.CurrentTargetId, "同点は現対象維持。");
        }

        // ---- §7.2 即時無効化（Down／離脱／範囲外） ----

        [Test]
        public void CurrentDown_SwitchesImmediately()
        {
            var t = Table();
            var cur = new FakeThreatTarget { ActorId = 1, BaseThreat = 100f };
            var other = new FakeThreatTarget { ActorId = 2, BaseThreat = 10f };
            var cands = List(cur, other);
            t.UpdateSelection(cands, 0.1f, false);
            Assert.AreEqual(1, t.CurrentTargetId);

            cur.IsDown = true;
            t.UpdateSelection(cands, 0.01f, false); // 1 秒未満でも即時切替
            Assert.AreEqual(2, t.CurrentTargetId, "Down は即時切替（再評価待ちなし）。");
        }

        [Test]
        public void CurrentLeaves_Candidates_SwitchesImmediately()
        {
            var t = Table();
            var cur = new FakeThreatTarget { ActorId = 1, BaseThreat = 100f };
            var other = new FakeThreatTarget { ActorId = 2, BaseThreat = 10f };
            var cands = List(cur, other);
            t.UpdateSelection(cands, 0.1f, false);
            Assert.AreEqual(1, t.CurrentTargetId);

            cands.Remove(cur); // 範囲外／離脱で候補から外れる
            t.UpdateSelection(cands, 0.01f, false);
            Assert.AreEqual(2, t.CurrentTargetId, "離脱・範囲外は即時切替。");
        }

        [Test]
        public void NoEligibleCandidate_ClearsTarget()
        {
            var t = Table();
            var cur = new FakeThreatTarget { ActorId = 1, BaseThreat = 100f };
            var cands = List(cur);
            t.UpdateSelection(cands, 0.1f, false);
            cur.IsActive = false;
            t.UpdateSelection(cands, 0.01f, false);
            Assert.AreEqual(EnemyThreatTable.NoTarget, t.CurrentTargetId, "有効候補なしで対象なし。");
            Assert.AreEqual(0f, t.GetThreat(cur), 1e-4f, "非活動は脅威 0。");
        }

        // ---- §7.2 近接攻撃中の切替固定 ----

        [Test]
        public void AttackLocked_HoldsPreferenceSwitch_ButAllowsInvalidation()
        {
            var t = Table();
            var cur = new FakeThreatTarget { ActorId = 1, BaseThreat = 100f };
            var big = new FakeThreatTarget { ActorId = 2, BaseThreat = 1000f };
            var cands = List(cur);
            t.UpdateSelection(cands, 0.1f, false); // cur を選択
            Assert.AreEqual(1, t.CurrentTargetId);

            cands.Add(big);
            t.UpdateSelection(cands, 1f, true); // 攻撃中：嗜好切替を保留
            Assert.AreEqual(1, t.CurrentTargetId, "近接攻撃中は 25% 切替を保留。");

            cur.IsDown = true; // 攻撃中でも現対象無効化は即時切替
            t.UpdateSelection(cands, 0.01f, true);
            Assert.AreEqual(2, t.CurrentTargetId, "攻撃中でも無効化は即時切替。");
        }

        [Test]
        public void AttackLocked_Released_ReevaluatesAndSwitches()
        {
            var t = Table();
            var cur = new FakeThreatTarget { ActorId = 1, BaseThreat = 100f };
            var big = new FakeThreatTarget { ActorId = 2, BaseThreat = 1000f };
            var cands = List(cur);
            t.UpdateSelection(cands, 0.1f, false); // cur を選択
            cands.Add(big);
            t.UpdateSelection(cands, 1f, true);  // 攻撃中保留
            Assert.AreEqual(1, t.CurrentTargetId);
            t.UpdateSelection(cands, 1f, false); // 解除後の再評価で切替
            Assert.AreEqual(2, t.CurrentTargetId);
        }

        // ---- Fake 主人公＋仲間の選択 ----

        [Test]
        public void Selection_PlayerBaseHateBeatsUninjuredAllies()
        {
            var t = Table();
            var player = Player(1);
            var dog = new FakeThreatTarget { ActorId = 2, Faction = CombatFaction.Ally, AcquiredThreatMultiplier = 1.5f };
            var monkey = new FakeThreatTarget { ActorId = 3, Faction = CombatFaction.Ally, AcquiredThreatMultiplier = 1.2f };
            var pheasant = new FakeThreatTarget { ActorId = 4, Faction = CombatFaction.Ally, AcquiredThreatMultiplier = 0.5f };
            var cands = List(player, dog, monkey, pheasant);

            // 各仲間が同じ生ダメージを与えても、主人公の基礎50が優越（不必要な揺れなし）。
            t.AddThreat(dog, ThreatSource.HpDamage, 10f);
            t.AddThreat(monkey, ThreatSource.HpDamage, 10f);
            t.AddThreat(pheasant, ThreatSource.HpDamage, 10f);
            t.UpdateSelection(cands, 0.1f, false);
            Assert.AreEqual(1, t.CurrentTargetId, "基礎ヘイトで主人公を選択。");
        }

        [Test]
        public void Selection_AllySwitch_WhenAcquiredExceedsThreshold()
        {
            var t = Table();
            var player = Player(1); // 基礎50
            var dog = new FakeThreatTarget { ActorId = 2, Faction = CombatFaction.Ally, AcquiredThreatMultiplier = 1.5f };
            var cands = List(player, dog);
            t.UpdateSelection(cands, 0.1f, false);
            Assert.AreEqual(1, t.CurrentTargetId);

            // 犬が挑発相当の大ヘイト（>= 50*1.25=62.5）を獲得 → 切替。42*1.5=63。
            t.AddThreat(dog, ThreatSource.HpDamage, 42f);
            t.UpdateSelection(cands, 1f, false);
            Assert.AreEqual(2, t.CurrentTargetId, "獲得が閾値超で仲間へ切替。");
        }

        // ---- Reset・入力ガード ----

        [Test]
        public void Reset_ClearsThreatAndSelection()
        {
            var t = Table();
            var p = Player();
            var cands = List(p);
            t.AddThreat(p, ThreatSource.HpDamage, 50f);
            t.UpdateSelection(cands, 0.1f, false);
            Assert.AreEqual(1, t.CurrentTargetId);

            t.Reset();
            Assert.AreEqual(EnemyThreatTable.NoTarget, t.CurrentTargetId);
            Assert.AreEqual(0f, t.GetAcquired(p.ActorId), 1e-4f);
            Assert.AreEqual(0, t.TrackedCount);
        }

        [Test]
        public void AddThreat_IgnoresNullAndNonPositive()
        {
            var t = Table();
            var p = Player();
            t.AddThreat(null, ThreatSource.HpDamage, 10f);
            t.AddThreat(p, ThreatSource.HpDamage, 0f);
            t.AddThreat(p, ThreatSource.HpDamage, -5f);
            Assert.AreEqual(0f, t.GetAcquired(p.ActorId), 1e-4f);
            Assert.AreEqual(0, t.TrackedCount);
        }

        [Test]
        public void GetThreat_ZeroForDownOrInactive()
        {
            var t = Table();
            var p = Player();
            t.AddThreat(p, ThreatSource.HpDamage, 30f);
            p.IsDown = true;
            Assert.AreEqual(0f, t.GetThreat(p), 1e-4f);
            p.IsDown = false;
            p.IsActive = false;
            Assert.AreEqual(0f, t.GetThreat(p), 1e-4f);
        }
    }
}
