using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-10：<see cref="EnemyDefenseController"/> が観測可能な危険（注入した <see cref="IEnemyDangerSense"/>）に反応してガード／回避を
    /// 駆動することを決定的に検証する（§9）。入力ではなく「危険刺激の有無」で発火し、能力 Data 無効な敵は防御しない。Cooldown で連続回避を抑える。
    /// </summary>
    public sealed class EnemyDefenseControllerTests
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

        private sealed class FakeDanger : IEnemyDangerSense
        {
            public EnemyDangerStimulus Stimulus = EnemyDangerStimulus.None;
            public EnemyDangerStimulus Sense(Vector3 selfPosition, Vector3 selfForward, int selfDamageableId) => Stimulus;
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

        private (EnemyDefenseController ctrl, EnemyActor actor, FakeDanger danger) Make(bool canGuard, bool canEvade)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 100);
            SetField(arch, "_canGuard", canGuard);
            SetField(arch, "_canEvade", canEvade);
            SetField(arch, "_guardCooldownSeconds", 3f);
            SetField(arch, "_evadeCooldownSeconds", 4f);

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            actor.SetFacing(Vector3.forward);
            var ctrl = go.AddComponent<EnemyDefenseController>();
            var danger = new FakeDanger();
            ctrl.SetDangerSense(danger);
            return (ctrl, actor, danger);
        }

        private static EnemyDangerStimulus FrontDanger()
        {
            // 危険源→自分＝-Z（危険源は +Z 前方）。ガード方向（+Z）に対して前方。
            return new EnemyDangerStimulus(new Vector3(0, 0, 2f), new Vector3(0, 0, -1f), unblockable: false);
        }

        [Test]
        public void Evade_TriggersOnObservedDanger_NotWithout()
        {
            var (ctrl, actor, danger) = Make(canGuard: false, canEvade: true);

            danger.Stimulus = EnemyDangerStimulus.None;
            ctrl.TickDefense(0.016f);
            Assert.IsFalse(ctrl.IsEvadeInvulnerable, "危険が無ければ回避しない（入力では動かない）。");

            danger.Stimulus = FrontDanger();
            ctrl.TickDefense(0.016f);
            Assert.IsTrue(ctrl.IsEvadeInvulnerable, "観測可能な危険に反応して回避（無敵）。");
            Assert.AreEqual(EnemyState.Evade, actor.State);
        }

        [Test]
        public void Evade_NoConsecutive_DuringCooldown()
        {
            var (ctrl, _, danger) = Make(canGuard: false, canEvade: true);
            danger.Stimulus = FrontDanger();
            ctrl.TickDefense(0.016f);
            Assert.IsTrue(ctrl.IsEvadeInvulnerable);

            ctrl.TickDefense(0.5f); // 無敵終了→Cooldown。危険は継続。
            Assert.IsFalse(ctrl.IsEvadeInvulnerable);
            ctrl.TickDefense(0.016f);
            Assert.IsFalse(ctrl.IsEvadeInvulnerable, "Cooldown 中は連続回避しない。");

            ctrl.TickDefense(4f); // Cooldown 明け。
            ctrl.TickDefense(0.016f);
            Assert.IsTrue(ctrl.IsEvadeInvulnerable, "Cooldown 明けで再回避。");
        }

        [Test]
        public void Guard_RaisesOnDanger_ReleasesWhenGone()
        {
            var (ctrl, actor, danger) = Make(canGuard: true, canEvade: false);

            danger.Stimulus = FrontDanger();
            ctrl.TickDefense(0.016f);
            Assert.IsTrue(ctrl.IsGuarding, "前方の危険にガードを構える。");
            Assert.AreEqual(EnemyState.Guard, actor.State);

            danger.Stimulus = EnemyDangerStimulus.None;
            ctrl.TickDefense(0.016f);
            Assert.IsFalse(ctrl.IsGuarding, "危険が消えたら構えを解く。");
        }

        [Test]
        public void NoAbility_DoesNotDefend()
        {
            var (ctrl, _, danger) = Make(canGuard: false, canEvade: false);
            danger.Stimulus = FrontDanger();
            ctrl.TickDefense(0.016f);
            Assert.IsFalse(ctrl.IsDefending, "能力 Data 無効な敵は防御しない。");
        }
    }
}
