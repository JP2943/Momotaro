using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Slots;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-07 統合検証（req10）。同一 Encounter 内の近接 3 体で、同時に AttackPrepare へ入れるのは 1 体まで（近接通常 Slot=1）。
    /// 攻撃の終了・中断（Stagger/Stunned/Down 相当の CancelAttack）・Disable、および Owner 不在の回収（PruneInactive）後に、
    /// 次の敵が Slot を取得できることを公開シームで決定的に確認する。
    /// </summary>
    public sealed class EnemyEncounterSlotIntegrationTests
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

        private EnemyArchetypeData MakeArchetype()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_attackClass", EnemyAttackClass.Normal);
            SetField(d, "_useRange", 2.0f);
            SetField(d, "_useAngle", 120f);
            SetField(d, "_prepareSeconds", 0.25f);
            SetField(d, "_activeSeconds", 0.10f);
            SetField(d, "_recoverySeconds", 0.20f);
            SetField(d, "_trackingStopSeconds", 0.15f);
            SetField(d, "_slotKind", AttackSlotKind.MeleeNormal);
            SetField(d, "_aimingMode", EnemyAimingMode.CurrentPosition);

            var a = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(a);
            SetField(a, "_maxHp", 100);
            SetField(a, "_attacks", new[] { d });
            return a;
        }

        private EnemyEncounter _encounter;
        private EnemyAttackController[] _enemies;

        private void BuildEncounterWithEnemies()
        {
            var encGo = new GameObject("Encounter");
            _spawned.Add(encGo);
            _encounter = encGo.AddComponent<EnemyEncounter>(); // 既定容量 1/1/1。

            _enemies = new EnemyAttackController[3];
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Enemy" + i);
                _spawned.Add(go);
                go.transform.SetParent(encGo.transform); // Encounter 配下（GetComponentInParent で解決）。
                var actor = go.AddComponent<EnemyActor>();
                SetField(actor, "_archetype", MakeArchetype());
                _enemies[i] = go.AddComponent<EnemyAttackController>(); // Awake で Encounter を解決。
            }
        }

        private static readonly Vector3 TargetPos = new Vector3(0, 0, 1.0f);

        [Test]
        public void OnlyOneMeleePrepare_AtOnce()
        {
            BuildEncounterWithEnemies();

            Assert.IsTrue(_enemies[0].TryStartAttack(TargetPos, Vector3.zero), "1 体目は Slot を取得して開始。");
            Assert.IsFalse(_enemies[1].TryStartAttack(TargetPos, Vector3.zero), "2 体目は Slot 上限で開始不可。");
            Assert.IsFalse(_enemies[2].TryStartAttack(TargetPos, Vector3.zero), "3 体目も開始不可。");

            Assert.IsTrue(_enemies[0].IsAttacking);
            Assert.IsFalse(_enemies[1].IsAttacking);
            Assert.IsFalse(_enemies[2].IsAttacking);
            Assert.AreEqual(1, _encounter.Coordinator.ActiveCount(AttackSlotKind.MeleeNormal));
        }

        [Test]
        public void NextAcquires_AfterFirstFinishes()
        {
            BuildEncounterWithEnemies();
            Assert.IsTrue(_enemies[0].TryStartAttack(TargetPos, Vector3.zero));

            for (int i = 0; i < 40 && _enemies[0].IsAttacking; i++)
            {
                _enemies[0].TickAttack(0.05f); // Prepare→Active→Recovery→終了で Slot 解放。
            }

            Assert.IsFalse(_enemies[0].IsAttacking);
            Assert.IsTrue(_enemies[1].TryStartAttack(TargetPos, Vector3.zero), "終了後は次の敵が取得できる。");
        }

        [Test]
        public void NextAcquires_AfterCancel_StaggerStunnedDownInterrupt()
        {
            BuildEncounterWithEnemies();
            Assert.IsTrue(_enemies[0].TryStartAttack(TargetPos, Vector3.zero));

            _enemies[0].CancelAttack(); // Stagger/Stunned/Down 由来の中断 Cleanup と同一経路。
            Assert.IsFalse(_enemies[0].IsAttacking);
            Assert.IsTrue(_enemies[1].TryStartAttack(TargetPos, Vector3.zero), "中断後は次の敵が取得できる。");
        }

        [Test]
        public void NextAcquires_AfterDisable()
        {
            BuildEncounterWithEnemies();
            Assert.IsTrue(_enemies[0].TryStartAttack(TargetPos, Vector3.zero));

            // 実行時は OnDisable→ReleaseSlot で解放する。EditMode はライフサイクル非駆動のため、Owner 不在（isActiveAndEnabled=false）を
            // 回収する PruneInactive 経路で Disable 後の解放を検証する。
            _enemies[0].gameObject.SetActive(false);
            int reclaimed = _encounter.Coordinator.PruneInactive();
            Assert.AreEqual(1, reclaimed, "Disable（Owner 無効）の Slot を回収。");
            Assert.IsTrue(_enemies[1].TryStartAttack(TargetPos, Vector3.zero), "Disable 後は次の敵が取得できる。");
        }

        [Test]
        public void NextAcquires_AfterOwnerDown_ViaPrune()
        {
            BuildEncounterWithEnemies();
            Assert.IsTrue(_enemies[0].TryStartAttack(TargetPos, Vector3.zero));

            // 1 体目を撃破（Down）。Update 非駆動のため中断は走らないが、Owner 不在として回収できる。
            var actor0 = _enemies[0].GetComponent<EnemyActor>();
            actor0.ReceiveHit(new HitInfo(null, actor0, Vector3.forward, actor0.WorldPosition,
                new HitDamage(200f, 0f, 0f), false, false, default(HitId)));
            Assert.IsTrue(actor0.IsDown, "撃破で Down。");

            int reclaimed = _encounter.Coordinator.PruneInactive();
            Assert.AreEqual(1, reclaimed, "Down（Owner 無効）の Slot を回収。");
            Assert.IsTrue(_enemies[1].TryStartAttack(TargetPos, Vector3.zero), "回収後は次の敵が取得できる。");
        }
    }
}
