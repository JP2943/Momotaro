using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-10：必殺技命中を実際の <see cref="CombatDummy.ReceiveHit"/> 経路で検証する。非スタンは倍率 1.0、スタン中は固有 1.5
    /// （1.25 と乗算しない）、防御一部無視、そして小型敵ノックバック拡張点（ボス無効）を確認する。
    /// </summary>
    public sealed class SpecialHitTests
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

        private static void SetField(object t, string n, object v)
        {
            System.Type ty = t.GetType();
            FieldInfo f = null;
            while (ty != null && f == null)
            {
                f = ty.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
                ty = ty.BaseType;
            }

            f.SetValue(t, v);
        }

        private CombatDummy MakeDummy(int maxHp, float defense, float poiseMax = 100f, bool isBoss = false)
        {
            var e = ScriptableObject.CreateInstance<EnemyData>();
            _spawned.Add(e);
            SetField(e, "_maxHp", maxHp);
            SetField(e, "_defense", defense);
            SetField(e, "_poiseMax", poiseMax);
            SetField(e, "_flinchResistance", 60f);
            SetField(e, "_isBoss", isBoss);

            var go = new GameObject("Dummy");
            _spawned.Add(go);
            go.SetActive(false);
            var dummy = go.AddComponent<CombatDummy>();
            SetField(dummy, "_data", e);
            go.SetActive(true);
            return dummy;
        }

        // 必殺技命中（防御無視率・スタン倍率上書きを持つ）。preHp は防御適用前の攻撃寄与。
        private static HitInfo SpecialHit(CombatDummy target, float preHp, float defenseIgnore, float stunOverride)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero, new HitDamage(preHp, 0f, 0f),
                0f, 0f, guardable: false, justGuardable: false, isJustGuardCounter: false,
                defenseIgnore, stunOverride, HitId.Single(1));
        }

        [Test]
        public void NonStun_UsesMultiplierOne_NotSpecialStun()
        {
            var dummy = MakeDummy(1000, 0f);
            dummy.ReceiveHit(SpecialHit(dummy, 70f, 0f, 1.5f));
            Assert.AreEqual(930, dummy.CurrentHp, "非スタンは 1.0 倍（70 のみ、1.5 を適用しない）。");
        }

        [Test]
        public void Stun_UsesSpecial15_NotMultipliedWith125()
        {
            var dummy = MakeDummy(1000, 0f);
            // 体幹 100 を削ってスタンさせる（HP は変えない）。
            dummy.ReceiveHit(new HitInfo(null, dummy, Vector3.forward, Vector3.zero, new HitDamage(0f, 100f, 0f), true, true, HitId.Single(9)));
            Assert.IsTrue(dummy.IsStunned);

            dummy.ReceiveHit(SpecialHit(dummy, 70f, 0f, 1.5f));
            Assert.AreEqual(895, dummy.CurrentHp, "スタン中は固有 1.5（70×1.5=105 減）。");
            Assert.AreNotEqual(869, dummy.CurrentHp, "1.25×1.5（=131 減）ではない（非乗算）。");
        }

        [Test]
        public void DefenseIgnore_RaisesDamage()
        {
            var full = MakeDummy(1000, 100f);
            full.ReceiveHit(SpecialHit(full, 70f, 0f, 1.5f)); // 70×(100/200)=35
            Assert.AreEqual(965, full.CurrentHp);

            var ignore = MakeDummy(1000, 100f);
            ignore.ReceiveHit(SpecialHit(ignore, 70f, 0.5f, 1.5f)); // 実効防御50 → 70×(100/150)=47
            Assert.AreEqual(953, ignore.CurrentHp);
        }

        [Test]
        public void Knockback_AppliedToSmallEnemy_DisabledForBoss()
        {
            var small = MakeDummy(1000, 0f, isBoss: false);
            small.ReceiveKnockback(Vector3.forward, 6f);
            Assert.AreEqual(6f, small.LastKnockback, 1e-4f, "小型敵は吹き飛ばされる（拡張点）。");

            var boss = MakeDummy(1000, 0f, isBoss: true);
            boss.ReceiveKnockback(Vector3.forward, 6f);
            Assert.AreEqual(0f, boss.LastKnockback, 1e-4f, "ボスは吹き飛ばし無効。");
        }
    }
}
