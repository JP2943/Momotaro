using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-04 修正1：照準3方式の分離を検証する（§6.1）。CurrentPosition／PredictedPosition は開始時に固定し更新しない、
    /// Tracking は Prepare 中に角速度制限で漸進旋回する。<see cref="EnemyAimingResolver.RotateToward"/> の純粋検証と、
    /// <see cref="EnemyAttackController"/> の照準固定／漸進を公開シームで確認する。
    /// </summary>
    public sealed class EnemyAimingRotationTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            PerceptionTargetRegistry.Clear();
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        // ---- RotateToward（純粋）----

        [Test]
        public void RotateToward_LimitsToMaxDegrees()
        {
            Vector3 cur = Vector3.forward;             // +Z
            Vector3 desired = Vector3.right;           // +X（90°）
            Vector3 r = EnemyAimingResolver.RotateToward(cur, desired, 10f);
            float moved = Vector3.Angle(cur, r);
            Assert.AreEqual(10f, moved, 0.5f, "1 ステップの回頭は最大角度以下。");
            Assert.Less(moved, 90f, "瞬時に目標へ向かない。");
        }

        [Test]
        public void RotateToward_LargeBudget_ReachesDesired()
        {
            Vector3 r = EnemyAimingResolver.RotateToward(Vector3.forward, Vector3.right, 360f);
            Assert.Less(Vector3.Angle(Vector3.right, r), 1e-2f, "十分な角度なら目標へ到達。");
        }

        // ---- Controller（照準固定／漸進）----

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

        private sealed class FakeTarget : IPerceptionTarget
        {
            public int ActorId => 1;
            public CombatFaction Faction => CombatFaction.Player;
            public Vector3 Position { get; set; }
            public bool IsActive => true;
        }

        private EnemyAttackController MakeController(EnemyAimingMode mode)
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_useRange", 3f);
            SetField(d, "_useAngle", 120f);
            SetField(d, "_prepareSeconds", 0.60f);
            SetField(d, "_activeSeconds", 0.10f);
            SetField(d, "_recoverySeconds", 0.20f);
            SetField(d, "_trackingStopSeconds", 0.50f);
            SetField(d, "_cooldownSeconds", 0f); // 連続攻撃検証のため Cooldown なし。
            SetField(d, "_aimingMode", mode);
            SetField(d, "_trackingAngularSpeed", 180f);

            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_attacks", new[] { d });

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            return go.AddComponent<EnemyAttackController>();
        }

        [Test]
        public void CurrentPosition_FixesAimAtStart_DoesNotFollowMovingTarget()
        {
            var target = new FakeTarget { Position = new Vector3(0, 0, 1.5f) };
            PerceptionTargetRegistry.Register(target);
            var c = MakeController(EnemyAimingMode.CurrentPosition);

            Assert.IsTrue(c.TryStartAttack(target.Position, Vector3.zero));
            Vector3 aimAtStart = c.AimDirection;

            target.Position = new Vector3(1.5f, 0, 0); // 対象が真横へ移動
            c.TickAttack(0.1f); // まだ Prepare

            Assert.Less(Vector3.Angle(aimAtStart, c.AimDirection), 1e-3f, "現在位置型は開始時方向で固定（追尾しない）。");
        }

        [Test]
        public void Tracking_RotatesGradually_NotInstant()
        {
            var target = new FakeTarget { Position = new Vector3(0, 0, 1.5f) };
            PerceptionTargetRegistry.Register(target);
            var c = MakeController(EnemyAimingMode.Tracking);

            // 追尾先は開始時に確定した照準対象（req2/3）。最寄り再取得はしないため対象を明示的に渡す。
            Assert.IsTrue(c.TryStartAttack(target, target.Position, Vector3.zero));
            Vector3 aimAtStart = c.AimDirection; // ほぼ +Z

            target.Position = new Vector3(1.5f, 0, 0); // 真横（90°）へ移動
            c.TickAttack(0.05f); // 追尾停止前。角速度180×0.05=9°まで

            float moved = Vector3.Angle(aimAtStart, c.AimDirection);
            Assert.Greater(moved, 0.1f, "追尾は対象方向へ旋回する。");
            Assert.Less(moved, 20f, "角速度制限で瞬時に90°転換しない。");
        }

        private sealed class FakeThreatTarget : Momotaro.Gameplay.Enemy.Threat.IThreatTarget
        {
            public int ActorId { get; set; } = 1;
            public CombatFaction Faction => CombatFaction.Player;
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat { get; set; } = 50f;
            public float AcquiredThreatMultiplier { get; set; } = 1f;
        }

        [Test]
        public void Tracking_DoesNotFollow_DownLockedTarget()
        {
            // 固定対象が Down（IsActive は true のまま）になったら、その位置を追尾せず現在方向を保持する（別対象へ急旋回しない。req1/4）。
            var target = new FakeThreatTarget { ActorId = 7, Position = new Vector3(0, 0, 1.5f) };
            var c = MakeController(EnemyAimingMode.Tracking);
            Assert.IsTrue(c.TryStartAttack(target, target.Position, Vector3.zero));
            Vector3 aimAtStart = c.AimDirection; // ほぼ +Z

            target.IsDown = true;                     // Down（EnemyThreatTable では即時無効化される）。
            target.Position = new Vector3(1.5f, 0, 0); // 真横へ移動
            c.TickAttack(0.05f);                       // 追尾停止前だが、Down 対象は追尾しない。

            Assert.Less(Vector3.Angle(aimAtStart, c.AimDirection), 1e-3f, "Down 対象の位置は Tracking しない（現在方向を保持）。");
            Assert.AreEqual(target.ActorId, c.AttackTargetId, "照準対象の固定は攻撃終了まで維持（AttackTargetId 不変。req2）。");
        }

        [Test]
        public void NextAttack_SelectsNewThreatTarget_AfterFinish()
        {
            // 攻撃終了で照準対象の固定が解け、次の攻撃は（Brain が渡す）新しい Threat 対象を狙う（req5）。
            var first = new FakeThreatTarget { ActorId = 11, Position = new Vector3(0, 0, 1.5f) };
            var next = new FakeThreatTarget { ActorId = 22, Position = new Vector3(0, 0, 1.5f) };
            var c = MakeController(EnemyAimingMode.Tracking);

            Assert.IsTrue(c.TryStartAttack(first, first.Position, Vector3.zero));
            Assert.AreEqual(first.ActorId, c.AttackTargetId);

            for (int i = 0; i < 60 && c.IsAttacking; i++)
            {
                c.TickAttack(0.05f); // 攻撃を最後まで進める。
            }

            Assert.IsFalse(c.IsAttacking);
            Assert.AreEqual(0, c.AttackTargetId, "終了で固定が解ける。");

            Assert.IsTrue(c.TryStartAttack(next, next.Position, Vector3.zero));
            Assert.AreEqual(next.ActorId, c.AttackTargetId, "次回攻撃は新しい Threat 対象を狙う。");
        }
    }
}
