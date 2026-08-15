using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-11／P3-10 受入修正：実コンポーネント同士で、ガード不能な危険を観測して回避し分けることを検証する。実 <see cref="PhysicsEnemyDangerSense"/>
    /// が攻撃側の <see cref="IAttackThreatSource"/> を物理越しに読み、<see cref="EnemyDangerStimulus.Unblockable"/> を伝える。両能力持ちの
    /// <see cref="EnemyDefenseController"/> は、ガード不能な危険には回避、通常の危険にはガードで対処する（Fake 注入なしの実観測経路）。
    /// </summary>
    public sealed class EnemyDangerUnblockablePlayTests
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

        private sealed class FakeAttacker : MonoBehaviour, ICombatActor, IAttackThreatSource
        {
            public bool Attacking;
            public bool Unblockable;
            public CombatFaction Faction => CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
            public bool IsThreateningAttack => Attacking;
            public bool IsUnblockableThreat => Unblockable;
            public Vector3 ThreatForward => transform.forward;
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

        private FakeAttacker MakeAttacker(Vector3 pos)
        {
            var go = new GameObject("Attacker");
            _spawned.Add(go);
            go.transform.position = pos;
            go.AddComponent<BoxCollider>().size = Vector3.one;
            return go.AddComponent<FakeAttacker>();
        }

        private EnemyDefenseController MakeDefender(bool canGuard, bool canEvade, Vector3 pos)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 100);
            SetField(arch, "_canGuard", canGuard);
            SetField(arch, "_canEvade", canEvade);
            SetField(arch, "_guardCooldownSeconds", 3f);
            SetField(arch, "_evadeCooldownSeconds", 4f);

            var go = new GameObject("Defender");
            _spawned.Add(go);
            go.transform.position = pos;
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            actor.SetFacing(Vector3.forward);
            return go.AddComponent<EnemyDefenseController>();
        }

        [UnityTest]
        public IEnumerator DangerSense_ReadsUnblockable_FromAttackContract()
        {
            var sense = new PhysicsEnemyDangerSense(radius: 2.5f);
            FakeAttacker atk = MakeAttacker(new Vector3(0, 0, 1.5f));
            yield return new WaitForFixedUpdate();

            atk.Attacking = true;
            atk.Unblockable = false;
            EnemyDangerStimulus blockable = sense.Sense(Vector3.zero, Vector3.forward, 1);
            Assert.IsTrue(blockable.HasDanger, "攻撃中を危険として観測。");
            Assert.IsFalse(blockable.Unblockable, "通常攻撃はガード可能。");

            atk.Unblockable = true;
            EnemyDangerStimulus unblock = sense.Sense(Vector3.zero, Vector3.forward, 1);
            Assert.IsTrue(unblock.HasDanger);
            Assert.IsTrue(unblock.Unblockable, "必殺技（ガード不能）を観測して伝える。");
        }

        [UnityTest]
        public IEnumerator BothAbilities_EvadeOnUnblockable_GuardOnBlockable()
        {
            // ガード不能：両能力持ちは回避する（原点付近）。
            EnemyDefenseController evadeCase = MakeDefender(canGuard: true, canEvade: true, Vector3.zero);
            FakeAttacker atkU = MakeAttacker(new Vector3(0, 0, 1.2f));
            atkU.Attacking = true;
            atkU.Unblockable = true;
            yield return new WaitForFixedUpdate();

            evadeCase.TickDefense(0.016f);
            Assert.IsTrue(evadeCase.IsEvadeInvulnerable, "ガード不能な危険には回避で対処。");
            Assert.IsFalse(evadeCase.IsGuarding, "ガードは構えない。");

            // 通常：両能力持ちはガードで受ける（別地点＝互いの危険観測に干渉しない）。
            EnemyDefenseController guardCase = MakeDefender(canGuard: true, canEvade: true, new Vector3(100f, 0, 0));
            FakeAttacker atkB = MakeAttacker(new Vector3(100f, 0, 1.2f));
            atkB.Attacking = true;
            atkB.Unblockable = false;
            yield return new WaitForFixedUpdate();

            guardCase.TickDefense(0.016f);
            Assert.IsTrue(guardCase.IsGuarding, "通常の危険にはガードで対処。");
            Assert.IsFalse(guardCase.IsEvadeInvulnerable, "回避は使わない。");
        }
    }
}
