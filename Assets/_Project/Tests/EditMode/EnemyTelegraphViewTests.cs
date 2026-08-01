using System.Collections.Generic;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy.Combat;
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
    }
}
