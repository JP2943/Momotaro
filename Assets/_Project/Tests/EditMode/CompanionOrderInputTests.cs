using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using Momotaro.Infrastructure.Input;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-08R：指示の入力（<see cref="CompanionOrderInput"/>）を検証する。
    ///
    /// 入力そのもの（キーが押されたか）は検証の対象にしない。デバイスを直接読む部分は
    /// テストから再現できないし、再現しても得られるものが無い。代わりに<b>押されたあと何をするか</b>を
    /// 公開メソッドとして切り出してあり、ここではそれを叩く。
    ///
    /// 固定するのは「まとめて同じ指示になること」。1 体ずつ違う状態にできると、キー 1 つでは戻せなくなる。
    /// </summary>
    public sealed class CompanionOrderInputTests
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

        private CompanionOrders MakeCompanion(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<CompanionOrders>();
        }

        private CompanionOrderInput MakeInput(params CompanionOrders[] companions)
        {
            var go = new GameObject("CompanionOrderInput");
            _spawned.Add(go);
            var input = go.AddComponent<CompanionOrderInput>();
            foreach (CompanionOrders c in companions)
            {
                input.Bind(c);
            }

            return input;
        }

        [Test]
        public void ToggleAll_SwitchesEveryoneTogether()
        {
            CompanionOrders a = MakeCompanion("A");
            CompanionOrders b = MakeCompanion("B");
            CompanionOrderInput input = MakeInput(a, b);

            input.ToggleAll();

            Assert.AreEqual(CompanionOrder.Wait, a.Current);
            Assert.AreEqual(CompanionOrder.Wait, b.Current);

            input.ToggleAll();

            Assert.AreEqual(CompanionOrder.Follow, a.Current, "もう一度押せば戻る。");
            Assert.AreEqual(CompanionOrder.Follow, b.Current);
            Assert.AreEqual(2, input.ToggleCount);
        }

        /// <summary>
        /// 状態がばらけていたら「全員待機」へ倒す。倒す向きを決めておかないと、
        /// 押すたびに 1 体ずつ入れ替わって全員を揃えられなくなる。
        /// </summary>
        [Test]
        public void ToggleAll_WhenMixed_MakesEveryoneWait()
        {
            CompanionOrders a = MakeCompanion("A");
            CompanionOrders b = MakeCompanion("B");
            b.SetOrder(CompanionOrder.Wait);
            CompanionOrderInput input = MakeInput(a, b);

            input.ToggleAll();

            Assert.AreEqual(CompanionOrder.Wait, a.Current);
            Assert.AreEqual(CompanionOrder.Wait, b.Current, "既に待機の相手はそのまま。");

            input.ToggleAll();

            Assert.AreEqual(CompanionOrder.Follow, a.Current, "揃ったあとは反対側へ倒せる。");
            Assert.AreEqual(CompanionOrder.Follow, b.Current);
        }

        [Test]
        public void SetAll_CountsOnlyRealChanges()
        {
            CompanionOrders a = MakeCompanion("A");
            CompanionOrderInput input = MakeInput(a);

            Assert.IsTrue(input.SetAll(CompanionOrder.Wait));
            Assert.IsFalse(input.SetAll(CompanionOrder.Wait), "同じ指示の再送は変更ではない。");
            Assert.AreEqual(1, input.ToggleCount);
        }

        [Test]
        public void Bind_IgnoresNullAndDuplicates()
        {
            CompanionOrders a = MakeCompanion("A");
            CompanionOrderInput input = MakeInput(a);

            input.Bind(a);
            input.Bind(null);

            Assert.AreEqual(1, input.CompanionCount, "同じ相手を二重に持たない。");

            input.ClearCompanions();
            Assert.AreEqual(0, input.CompanionCount);
        }

        /// <summary>相手が居なくても例外にしない（Scene 構築の途中で叩かれても壊れない）。</summary>
        [Test]
        public void ToggleAll_WithoutCompanions_DoesNothing()
        {
            CompanionOrderInput input = MakeInput();

            Assert.DoesNotThrow(() => input.ToggleAll());
            Assert.AreEqual(0, input.ToggleCount);
        }
    }
}
