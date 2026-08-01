using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-04 修正3：予兆仮表示アダプタ <see cref="EnemyTelegraphView"/> が型付きイベントを受けて表示状態を更新することを検証する。
    /// Begin／Fire で表示、End／Cancel で消灯、種別で色分け（表示専用で Gameplay 非関与）。
    /// </summary>
    public sealed class EnemyTelegraphViewTests
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

        private EnemyTelegraphView MakeView()
        {
            var go = new GameObject("View");
            _spawned.Add(go);
            return go.AddComponent<EnemyTelegraphView>();
        }

        private static EnemyTelegraphEvent Event(EnemyTelegraphPhase phase, AttackTelegraph kind)
        {
            return new EnemyTelegraphEvent(1, phase, kind, Vector3.zero, Vector3.forward, 0.3f);
        }

        [Test]
        public void Begin_ShowsFan_WithKind()
        {
            var v = MakeView();
            v.OnTelegraph(Event(EnemyTelegraphPhase.Begin, AttackTelegraph.Heavy));
            Assert.IsTrue(v.IsShowing, "予兆開始で表示。");
            Assert.AreEqual(EnemyTelegraphPhase.Begin, v.CurrentPhase);
            Assert.AreEqual(AttackTelegraph.Heavy, v.CurrentKind);
        }

        [Test]
        public void Fire_KeepsShowing()
        {
            var v = MakeView();
            v.OnTelegraph(Event(EnemyTelegraphPhase.Begin, AttackTelegraph.Normal));
            v.OnTelegraph(Event(EnemyTelegraphPhase.Fire, AttackTelegraph.Normal));
            Assert.IsTrue(v.IsShowing);
            Assert.AreEqual(EnemyTelegraphPhase.Fire, v.CurrentPhase);
        }

        [Test]
        public void End_And_Cancel_Hide()
        {
            var v = MakeView();
            v.OnTelegraph(Event(EnemyTelegraphPhase.Begin, AttackTelegraph.Normal));
            v.OnTelegraph(Event(EnemyTelegraphPhase.End, AttackTelegraph.Normal));
            Assert.IsFalse(v.IsShowing, "後隙明けで消灯。");

            v.OnTelegraph(Event(EnemyTelegraphPhase.Begin, AttackTelegraph.Unblockable));
            v.OnTelegraph(Event(EnemyTelegraphPhase.Cancel, AttackTelegraph.Unblockable));
            Assert.IsFalse(v.IsShowing, "中断で予兆消去。");
        }

        [Test]
        public void KindColor_DistinguishesTypes()
        {
            Assert.AreNotEqual(EnemyTelegraphView.KindColor(AttackTelegraph.Normal),
                EnemyTelegraphView.KindColor(AttackTelegraph.Unblockable), "通常とガード不能は別色。");
            Assert.AreNotEqual(EnemyTelegraphView.KindColor(AttackTelegraph.Heavy),
                EnemyTelegraphView.KindColor(AttackTelegraph.Normal), "強と通常は別色。");
        }

        [Test]
        public void RecoveryEvent_Hides_WithoutWaitingForEnd()
        {
            var v = MakeView();
            v.OnTelegraph(Event(EnemyTelegraphPhase.Begin, AttackTelegraph.Normal));
            v.OnTelegraph(Event(EnemyTelegraphPhase.Fire, AttackTelegraph.Normal));
            Assert.IsTrue(v.IsShowing);

            v.OnTelegraph(Event(EnemyTelegraphPhase.Recovery, AttackTelegraph.Normal));
            Assert.IsFalse(v.IsShowing, "Recovery 突入で消灯（End を待たない）。");
        }

        // ---- 制御連携（Recovery 消灯・Tracking 追従）----

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

        private (EnemyAttackController controller, EnemyTelegraphView view) MakeControllerAndView(EnemyAimingMode mode)
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_useRange", 3f);
            SetField(d, "_useAngle", 120f);
            SetField(d, "_prepareSeconds", 0.30f);
            SetField(d, "_activeSeconds", 0.10f);
            SetField(d, "_recoverySeconds", 0.20f);
            SetField(d, "_trackingStopSeconds", 0.50f);
            SetField(d, "_aimingMode", mode);
            SetField(d, "_trackingAngularSpeed", 180f);

            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_attacks", new[] { d });

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            var controller = go.AddComponent<EnemyAttackController>();
            var view = go.AddComponent<EnemyTelegraphView>();
            SetField(view, "_controller", controller);
            controller.Telegraph.AddListener(view); // EditMode では OnEnable が走らないため手動購読。
            return (controller, view);
        }

        [Test]
        public void Controller_RecoveryEntry_HidesBeforeEnd()
        {
            PerceptionTargetRegistry.Clear();
            var target = new FakeTarget { Position = new Vector3(0, 0, 1.5f) };
            PerceptionTargetRegistry.Register(target);

            var (c, v) = MakeControllerAndView(EnemyAimingMode.CurrentPosition);
            Assert.IsTrue(c.TryStartAttack(target.Position, Vector3.zero));
            c.TickAttack(0.30f); // → Active（Fire）
            Assert.IsTrue(v.IsShowing);

            c.TickAttack(0.10f); // → Recovery
            Assert.IsFalse(v.IsShowing, "Recovery 突入時点で表示が消える（Recovery 終了を待たない）。");

            PerceptionTargetRegistry.Clear();
        }

        [Test]
        public void Tracking_DisplayDirection_FollowsController()
        {
            PerceptionTargetRegistry.Clear();
            var target = new FakeTarget { Position = new Vector3(0, 0, 1.5f) };
            PerceptionTargetRegistry.Register(target);

            var (c, v) = MakeControllerAndView(EnemyAimingMode.Tracking);
            c.TryStartAttack(target.Position, Vector3.zero);
            Vector3 startDisplay = v.DisplayDirection;

            target.Position = new Vector3(1.5f, 0, 0); // 真横へ移動
            c.TickAttack(0.05f); // Prepare 中・追尾更新

            Assert.Greater(Vector3.Angle(startDisplay, v.DisplayDirection), 0.1f, "Tracking は表示方向が制御に追従して動く。");
            Assert.Less(Vector3.Angle(c.AimDirection, v.DisplayDirection), 1e-2f, "表示方向は制御の現在照準と一致。");

            PerceptionTargetRegistry.Clear();
        }

        [Test]
        public void CurrentPosition_DisplayDirection_StaysFixed()
        {
            PerceptionTargetRegistry.Clear();
            var target = new FakeTarget { Position = new Vector3(0, 0, 1.5f) };
            PerceptionTargetRegistry.Register(target);

            var (c, v) = MakeControllerAndView(EnemyAimingMode.CurrentPosition);
            c.TryStartAttack(target.Position, Vector3.zero);
            Vector3 startDisplay = v.DisplayDirection;

            target.Position = new Vector3(1.5f, 0, 0);
            c.TickAttack(0.05f);

            Assert.Less(Vector3.Angle(startDisplay, v.DisplayDirection), 1e-3f, "現在位置型は表示方向も固定。");

            PerceptionTargetRegistry.Clear();
        }
    }
}
