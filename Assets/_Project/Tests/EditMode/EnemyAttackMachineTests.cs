using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-04：攻撃段階機 <see cref="EnemyAttackMachine"/> の時間境界・追尾停止窓・Hitbox 窓・中断を検証する（§6.3）。純粋・再現可能。
    /// </summary>
    public sealed class EnemyAttackMachineTests
    {
        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private static EnemyAttackSnapshot MakeSnapshot(float prepare, float active, float recovery, float trackingStop)
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            SetField(d, "_prepareSeconds", prepare);
            SetField(d, "_activeSeconds", active);
            SetField(d, "_recoverySeconds", recovery);
            SetField(d, "_trackingStopSeconds", trackingStop);
            EnemyAttackSnapshot s = EnemyAttackSnapshot.From(d);
            Object.DestroyImmediate(d);
            return s;
        }

        [Test]
        public void Phases_AdvanceAtTimeBoundaries()
        {
            var m = new EnemyAttackMachine();
            m.Begin(MakeSnapshot(0.30f, 0.10f, 0.20f, 0.15f));
            Assert.AreEqual(EnemyAttackMachine.Phase.Prepare, m.Current);

            var r1 = m.Tick(0.30f); // → Active
            Assert.IsTrue(r1.EnteredActive);
            Assert.AreEqual(EnemyAttackMachine.Phase.Active, m.Current);
            Assert.IsTrue(m.IsHitboxActive, "Active で Hitbox 窓が開く。");

            var r2 = m.Tick(0.10f); // → Recovery（0.40）
            Assert.IsTrue(r2.EnteredRecovery);
            Assert.AreEqual(EnemyAttackMachine.Phase.Recovery, m.Current);
            Assert.IsFalse(m.IsHitboxActive, "Recovery で Hitbox 窓は閉じる。");

            var r3 = m.Tick(0.20f); // → 終了（0.60）
            Assert.IsTrue(r3.Finished);
            Assert.AreEqual(EnemyAttackMachine.Phase.None, m.Current);
            Assert.IsFalse(m.IsAttacking);
        }

        [Test]
        public void TrackingActive_OnlyDuringPrepareBeforeStop()
        {
            var m = new EnemyAttackMachine();
            m.Begin(MakeSnapshot(0.30f, 0.10f, 0.20f, 0.15f));
            Assert.IsTrue(m.IsTrackingActive, "Prepare 開始直後は追尾可。");

            m.Tick(0.16f); // 追尾停止(0.15)を超え、まだ Prepare(0.30未満)
            Assert.AreEqual(EnemyAttackMachine.Phase.Prepare, m.Current);
            Assert.IsFalse(m.IsTrackingActive, "追尾停止時刻を過ぎたら固定。");
        }

        [Test]
        public void Cancel_StopsImmediately()
        {
            var m = new EnemyAttackMachine();
            m.Begin(MakeSnapshot(0.30f, 0.10f, 0.20f, 0.15f));
            m.Tick(0.30f);
            Assert.IsTrue(m.IsHitboxActive);

            m.Cancel();
            Assert.AreEqual(EnemyAttackMachine.Phase.None, m.Current, "中断で即 None。");
            Assert.IsFalse(m.IsAttacking);
            Assert.IsFalse(m.IsHitboxActive);
        }
    }
}
