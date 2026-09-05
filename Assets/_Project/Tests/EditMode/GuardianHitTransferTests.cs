using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-01：肩代わり時の命中再構築（<see cref="GuardianHitTransfer"/>）を検証する。攻撃 Snapshot 値（ダメージ・
    /// ガード可否・ステップ可否・<see cref="HitId"/>・<see cref="HitReaction"/>）が維持されること、対象・接触点・
    /// 攻撃方向だけが守護者基準へ差し替わること、方向を決められない場合に押し出しを起こさない 0 になることを固定する。
    /// </summary>
    public sealed class GuardianHitTransferTests
    {
        private sealed class FakeAttacker : ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition { get; set; }
            public Vector3 Forward => Vector3.forward;
        }

        private sealed class FakeGuardian : IGuardianReceiver
        {
            public int DamageableId => 4242;
            public Vector3 WorldPosition { get; set; }
            public bool CanTakeOver { get; set; } = true;
            public HitInfo? Received;

            public void ReceiveHit(in HitInfo hit) => Received = hit;
        }

        private sealed class FakePlayer : IDamageable
        {
            public int DamageableId => 1;
            public void ReceiveHit(in HitInfo hit) { }
        }

        private static HitInfo MakeHit(ICombatActor attacker, IDamageable target)
        {
            var hit = new HitInfo(
                attacker, target,
                Vector3.forward, new Vector3(0f, 1f, 0f),
                new HitDamage(30f, 12f, 5f),
                guardStaminaDamage: 9f, justGuardPoiseDamage: 7f,
                guardable: false, justGuardable: false, isJustGuardCounter: false,
                defenseIgnoreRatio: 0.25f, stunHpMultiplierOverride: 1.5f,
                steppable: true, hitId: new HitId(11, 2));

            return hit.WithReaction(new HitReaction(1.2f, 0.18f, 0.4f, isProjectile: true));
        }

        [Test]
        public void Rebuild_ReplacesTargetPointAndDirection()
        {
            var attacker = new FakeAttacker { WorldPosition = new Vector3(0f, 0f, 0f) };
            var guardian = new FakeGuardian { WorldPosition = new Vector3(3f, 0f, 0f) };
            HitInfo original = MakeHit(attacker, new FakePlayer());

            HitInfo transferred = GuardianHitTransfer.Rebuild(original, guardian);

            Assert.AreSame(guardian, transferred.Target, "対象は守護者へ差し替わる。");
            Assert.AreEqual(guardian.WorldPosition, transferred.HitPoint, "接触点は守護者位置（Damage VFX が主人公位置に出ない）。");
            Assert.AreEqual(Vector3.right, transferred.AttackDirection, "攻撃方向は攻撃者→守護者で再計算する。");
        }

        [Test]
        public void Rebuild_PreservesAttackSnapshotValues()
        {
            var attacker = new FakeAttacker { WorldPosition = new Vector3(-2f, 0f, 0f) };
            var guardian = new FakeGuardian { WorldPosition = new Vector3(2f, 0f, 0f) };
            HitInfo original = MakeHit(attacker, new FakePlayer());

            HitInfo t = GuardianHitTransfer.Rebuild(original, guardian);

            Assert.AreEqual(original.HitId, t.HitId, "HitId は維持する（受け手側の重複排除の鍵）。");
            Assert.AreSame(original.Attacker, t.Attacker);
            Assert.AreEqual(original.Damage.Hp, t.Damage.Hp, 1e-4f);
            Assert.AreEqual(original.Damage.Poise, t.Damage.Poise, 1e-4f);
            Assert.AreEqual(original.Damage.Flinch, t.Damage.Flinch, 1e-4f);
            Assert.AreEqual(original.GuardStaminaDamage, t.GuardStaminaDamage, 1e-4f);
            Assert.AreEqual(original.JustGuardPoiseDamage, t.JustGuardPoiseDamage, 1e-4f);
            Assert.AreEqual(original.Guardable, t.Guardable);
            Assert.AreEqual(original.JustGuardable, t.JustGuardable);
            Assert.AreEqual(original.IsJustGuardCounter, t.IsJustGuardCounter);
            Assert.AreEqual(original.DefenseIgnoreRatio, t.DefenseIgnoreRatio, 1e-4f);
            Assert.AreEqual(original.StunHpMultiplierOverride, t.StunHpMultiplierOverride, 1e-4f);
            Assert.AreEqual(original.Steppable, t.Steppable);
            Assert.AreEqual(original.Reaction.HitbackDistance, t.Reaction.HitbackDistance, 1e-4f);
            Assert.AreEqual(original.Reaction.HitbackSeconds, t.Reaction.HitbackSeconds, 1e-4f);
            Assert.AreEqual(original.Reaction.GuardbackDistance, t.Reaction.GuardbackDistance, 1e-4f);
            Assert.AreEqual(original.Reaction.IsProjectile, t.Reaction.IsProjectile);
        }

        [Test]
        public void Rebuild_IgnoresHeightDifference_InDirection()
        {
            var attacker = new FakeAttacker { WorldPosition = new Vector3(0f, 5f, 0f) };
            var guardian = new FakeGuardian { WorldPosition = new Vector3(0f, 0f, 4f) };
            HitInfo original = MakeHit(attacker, new FakePlayer());

            HitInfo t = GuardianHitTransfer.Rebuild(original, guardian);

            Assert.AreEqual(0f, t.AttackDirection.y, 1e-4f, "方向は World XZ 平面で求める。");
            Assert.AreEqual(Vector3.forward, t.AttackDirection);
        }

        [Test]
        public void Rebuild_WithoutAttacker_HasZeroDirection()
        {
            var guardian = new FakeGuardian { WorldPosition = new Vector3(1f, 0f, 1f) };
            HitInfo original = MakeHit(null, new FakePlayer());

            HitInfo t = GuardianHitTransfer.Rebuild(original, guardian);

            Assert.AreEqual(Vector3.zero, t.AttackDirection, "攻撃者不明なら方向 0＝ヒットバックを起こさない。");
            Assert.AreEqual(original.Damage.Hp, t.Damage.Hp, 1e-4f, "ダメージ自体は適用する。");
        }

        [Test]
        public void Rebuild_WithSamePosition_HasZeroDirection()
        {
            var attacker = new FakeAttacker { WorldPosition = new Vector3(2f, 0f, 2f) };
            var guardian = new FakeGuardian { WorldPosition = new Vector3(2f, 3f, 2f) }; // XZ が同一。
            HitInfo original = MakeHit(attacker, new FakePlayer());

            HitInfo t = GuardianHitTransfer.Rebuild(original, guardian);

            Assert.AreEqual(Vector3.zero, t.AttackDirection, "方向不定なら 0（不定方向へ吹き飛ばさない）。");
        }

        [Test]
        public void Rebuild_WithNullGuardian_ReturnsOriginal()
        {
            var attacker = new FakeAttacker();
            var player = new FakePlayer();
            HitInfo original = MakeHit(attacker, player);

            HitInfo t = GuardianHitTransfer.Rebuild(original, null);

            Assert.AreSame(player, t.Target, "守護者が無ければ原本のまま（呼び出し側で弾く前提）。");
        }

        [Test]
        public void ResolveDirection_IsNormalized()
        {
            var attacker = new FakeAttacker { WorldPosition = Vector3.zero };

            Vector3 d = GuardianHitTransfer.ResolveDirection(attacker, new Vector3(3f, 0f, 4f));

            Assert.AreEqual(1f, d.magnitude, 1e-4f);
            Assert.AreEqual(0.6f, d.x, 1e-4f);
            Assert.AreEqual(0.8f, d.z, 1e-4f);
        }
    }
}
