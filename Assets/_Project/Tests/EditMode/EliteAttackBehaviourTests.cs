using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-09 受入：強敵の攻撃選択挙動を <see cref="EnemyAttackController"/> の公開シームで決定的に検証する（EditMode。Update/物理は走らない）。
    /// (1) 突進のみ Chase（間合いの外＝停止距離より遠い 3〜5m）から開始でき、通常/強/ガード不能は接近開始経路から始まらない（§9.3）。
    /// (2) ガード不能が多数回の選択で 20% を超えず（上限）、かつ 0% にならない（下限。§9.3）。
    /// </summary>
    public sealed class EliteAttackBehaviourTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private static void SetField(object target, string name, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private EnemyAttackData MakeAttack(EnemyAttackClass cls, float range, float angle, float baseScore, float cooldown)
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_attackClass", cls);
            SetField(d, "_useRange", range);
            SetField(d, "_useAngle", angle);
            SetField(d, "_cooldownSeconds", cooldown);
            SetField(d, "_baseScore", baseScore);
            SetField(d, "_prepareSeconds", 0.30f);
            SetField(d, "_activeSeconds", 0.10f);
            SetField(d, "_recoverySeconds", 0.20f);
            SetField(d, "_trackingStopSeconds", 0.15f);
            SetField(d, "_aimingMode", EnemyAimingMode.CurrentPosition);
            SetField(d, "_hitboxHalfExtents", new Vector3(0.6f, 0.5f, 0.6f));
            return d;
        }

        private EnemyAttackController MakeController(EnemyAttackData[] attacks, int seed)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 200);
            SetField(arch, "_attackPower", 50f);
            SetField(arch, "_moveSpeed", 3f);
            SetField(arch, "_attacks", attacks);

            var go = new GameObject("Elite");
            _spawned.Add(go);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            var controller = go.AddComponent<EnemyAttackController>();
            SetField(controller, "_seed", seed);
            return controller;
        }

        // ---- (1) 突進は Chase（間合いの外）から開始できる。通常近接は接近開始経路から始まらない。----

        [Test]
        public void ApproachAttack_StartsChargeFromFarRange_NotNormal()
        {
            // 通常（射程2.2）＋突進（射程5）。停止距離より遠い 4m は本来 Hold に入らないが、突進だけは接近中に開始する。
            var c = MakeController(new[]
            {
                MakeAttack(EnemyAttackClass.Normal, 2.2f, 60f, 10f, 0f),
                MakeAttack(EnemyAttackClass.Charge, 5.0f, 40f, 9f, 0f),
            }, seed: 1);

            Assert.IsTrue(c.HasApproachAttack, "突進を持つ強敵は接近開始攻撃を持つ。");

            bool started = c.TryStartApproachAttack(null, new Vector3(0, 0, 4.0f), Vector3.zero);
            Assert.IsTrue(started, "3〜5m（4m）から突進を開始できる。");
            Assert.AreEqual(EnemyAttackClass.Charge, c.CurrentAttackClass, "接近開始は突進のみ。");
        }

        [Test]
        public void ApproachAttack_ExcludesNormal_EvenWhenNormalInRange()
        {
            // 2.0m は通常（射程2.2）の射程内だが、接近開始経路では通常を除外し突進のみ許可する（回帰）。
            var c = MakeController(new[]
            {
                MakeAttack(EnemyAttackClass.Normal, 2.2f, 60f, 100f, 0f), // Score を最大にしても選ばれてはいけない。
                MakeAttack(EnemyAttackClass.Charge, 5.0f, 40f, 9f, 0f),
            }, seed: 1);

            bool started = c.TryStartApproachAttack(null, new Vector3(0, 0, 2.0f), Vector3.zero);
            Assert.IsTrue(started, "突進は 2.0m でも射程内。");
            Assert.AreEqual(EnemyAttackClass.Charge, c.CurrentAttackClass,
                "通常が射程内・高 Score でも、接近開始経路からは通常を始めない。");
        }

        [Test]
        public void ApproachAttack_ReturnsFalse_WhenNoChargeAttack()
        {
            // 通常のみ（突進なし）の敵は接近開始攻撃を持たず、Chase からは何も始めない。
            var c = MakeController(new[]
            {
                MakeAttack(EnemyAttackClass.Normal, 2.2f, 60f, 10f, 0f),
            }, seed: 1);

            Assert.IsFalse(c.HasApproachAttack, "突進なしの敵は接近開始攻撃を持たない。");
            Assert.IsFalse(c.TryStartApproachAttack(null, new Vector3(0, 0, 1.5f), Vector3.zero),
                "接近開始経路では突進系以外は開始しない。");
        }

        // ---- (2) ガード不能は ≤20% かつ >0%。----

        [Test]
        public void Unblockable_ShareWithinCap_AndUsedAtLeastOnce()
        {
            // 強敵相当の 4 攻撃。全て停止帯（1.5m）で使用可能・Cooldown 無し。通常/強/突進の Score をガード不能より高くしても、
            // 上限枠でガード不能が確実に出現し（>0%）、かつ 20% を超えない。
            var c = MakeController(new[]
            {
                MakeAttack(EnemyAttackClass.Normal, 3.0f, 90f, 10f, 0f),
                MakeAttack(EnemyAttackClass.Heavy, 3.0f, 90f, 12f, 0f),
                MakeAttack(EnemyAttackClass.Unblockable, 3.0f, 90f, 8f, 0f), // 最低 Score。Score 方式なら 0% になる。
                MakeAttack(EnemyAttackClass.Charge, 3.0f, 90f, 9f, 0f),
            }, seed: 12345);

            const int n = 40;
            int unblockable = 0;
            var target = new Vector3(0, 0, 1.5f);
            for (int i = 0; i < n; i++)
            {
                Assert.IsTrue(c.TryStartAttack(target, Vector3.zero), "停止帯で毎回いずれかの攻撃を開始する。");
                if (c.CurrentAttackClass == EnemyAttackClass.Unblockable)
                {
                    unblockable++;
                }

                c.CancelAttack(); // 次の選択へ（governor は開始時に記録済み）。
            }

            Assert.LessOrEqual(unblockable, n / 5, "ガード不能は全選択の 20% 以下（" + unblockable + "/" + n + "）。");
            Assert.Greater(unblockable, 0, "ガード不能が 0%（全く使われない）にならない。");
        }
    }
}
