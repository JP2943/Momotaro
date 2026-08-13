using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-10：命中パイプラインへの防御反映を <see cref="EnemyActor.ReceiveHit"/> 経由で検証する（§9）。ガード中は前方命中を HP90%軽減・
    /// 被体幹×1.5、背後・Special は貫通（等倍）。回避無敵中は命中を無効化する。防御状態は Fake（<see cref="IEnemyDefenseState"/>）で注入する。
    /// </summary>
    public sealed class EnemyDefenseHitPipelineTests
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

        private sealed class FakeDefense : MonoBehaviour, IEnemyDefenseState
        {
            public bool Guarding;
            public bool EvadeInvuln;
            public bool IsGuarding => Guarding;
            public bool IsEvadeInvulnerable => EvadeInvuln;
            public bool IsDefending => Guarding || EvadeInvuln;
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

        private (EnemyActor actor, FakeDefense def) MakeActor()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 1000);
            SetField(arch, "_defense", 0f);
            SetField(arch, "_poiseMax", 1000f);
            SetField(arch, "_flinchResistance", 100000f); // ひるみを起こさない（HP/体幹の測定に集中）。

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var def = go.AddComponent<FakeDefense>(); // ResolveDefense がこれを拾う。
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            actor.SetFacing(Vector3.forward); // +Z 向き。
            return (actor, def);
        }

        private static HitInfo Hit(EnemyActor target, Vector3 attackDirection, float defenseIgnoreRatio = 0f)
        {
            return new HitInfo(null, target, attackDirection, Vector3.zero, new HitDamage(100f, 20f, 0f),
                0f, 0f, guardable: true, justGuardable: true, isJustGuardCounter: false,
                defenseIgnoreRatio: defenseIgnoreRatio, stunHpMultiplierOverride: 0f, HitId.Single(1));
        }

        [Test]
        public void Guarding_FrontHit_Reduces90PercentHp_AndAmplifiesPoise()
        {
            var (actor, def) = MakeActor();
            def.Guarding = true;
            int hp0 = actor.CurrentHp;
            float poise0 = actor.CurrentPoise;

            actor.ReceiveHit(Hit(actor, new Vector3(0, 0, -1))); // 前方から。

            Assert.AreEqual(10, hp0 - actor.CurrentHp, "HP は 90% 軽減（100→10）。");
            Assert.AreEqual(30f, poise0 - actor.CurrentPoise, 1e-3f, "被体幹は ×1.5（20→30）。");
        }

        [Test]
        public void Guarding_BackHit_Pierces_FullDamage()
        {
            var (actor, def) = MakeActor();
            def.Guarding = true;
            int hp0 = actor.CurrentHp;

            actor.ReceiveHit(Hit(actor, new Vector3(0, 0, 1))); // 背後から。

            Assert.AreEqual(100, hp0 - actor.CurrentHp, "背後は貫通し等倍。");
        }

        [Test]
        public void Guarding_SpecialHit_Pierces_FullDamage()
        {
            var (actor, def) = MakeActor();
            def.Guarding = true;
            int hp0 = actor.CurrentHp;

            actor.ReceiveHit(Hit(actor, new Vector3(0, 0, -1), defenseIgnoreRatio: 0.5f)); // 前方だが Special。

            Assert.AreEqual(100, hp0 - actor.CurrentHp, "Special は正面でも貫通し等倍。");
        }

        [Test]
        public void EvadeInvulnerable_IgnoresHit()
        {
            var (actor, def) = MakeActor();
            def.EvadeInvuln = true;
            int hp0 = actor.CurrentHp;
            float poise0 = actor.CurrentPoise;

            actor.ReceiveHit(Hit(actor, new Vector3(0, 0, -1)));

            Assert.AreEqual(hp0, actor.CurrentHp, "回避無敵は命中を無効化（HP 不変）。");
            Assert.AreEqual(poise0, actor.CurrentPoise, 1e-3f, "体幹も不変。");
        }

        [Test]
        public void NotDefending_TakesFullDamage()
        {
            var (actor, _) = MakeActor();
            int hp0 = actor.CurrentHp;
            actor.ReceiveHit(Hit(actor, new Vector3(0, 0, -1)));
            Assert.AreEqual(100, hp0 - actor.CurrentHp, "非防御は等倍。");
        }
    }
}
