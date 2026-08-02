using Momotaro.Gameplay.Enemy.Slots;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-07：包囲（Surround）の検証（§8.1「待機敵は棒立ちでなく包囲・位置調整」）。<see cref="SurroundCoordinator"/> の登録・
    /// インデックス・解除と、<see cref="SurroundRing"/> の均等配置（対象周囲に分散して単縦列を防ぐ）を決定的に確認する。純粋・再現可能。
    /// </summary>
    public sealed class SurroundTests
    {
        private static readonly Vector3 Center = new Vector3(3f, 0f, 4f);

        [Test]
        public void Coordinator_RegisterIsIdempotent_AndCounts()
        {
            var c = new SurroundCoordinator();
            c.Register(10);
            c.Register(10); // 重複無視
            c.Register(20);
            Assert.AreEqual(2, c.Count);
            Assert.IsTrue(c.TryGetIndex(10, out int i0));
            Assert.IsTrue(c.TryGetIndex(20, out int i1));
            Assert.AreEqual(0, i0, "登録順にインデックス。");
            Assert.AreEqual(1, i1);
        }

        [Test]
        public void Coordinator_Unregister_RemovesAndReindexes()
        {
            var c = new SurroundCoordinator();
            c.Register(10);
            c.Register(20);
            c.Register(30);
            c.Unregister(10);
            Assert.AreEqual(2, c.Count);
            Assert.IsFalse(c.TryGetIndex(10, out _), "解除した敵は非参加。");
            Assert.IsTrue(c.TryGetIndex(20, out int i));
            Assert.AreEqual(0, i, "解除後は詰め直す。");
        }

        [Test]
        public void Coordinator_IgnoresZeroId()
        {
            var c = new SurroundCoordinator();
            c.Register(0);
            Assert.AreEqual(0, c.Count);
        }

        [Test]
        public void Ring_DistributesEvenly_OnRadius()
        {
            // 3 体を均等配置：各点が中心から半径一定で、互いに離れている（単縦列でない）。
            const float radius = 2f;
            Vector3 p0 = SurroundRing.RingPosition(Center, radius, 0, 3);
            Vector3 p1 = SurroundRing.RingPosition(Center, radius, 1, 3);
            Vector3 p2 = SurroundRing.RingPosition(Center, radius, 2, 3);

            Assert.AreEqual(radius, PlanarDist(p0, Center), 1e-3f);
            Assert.AreEqual(radius, PlanarDist(p1, Center), 1e-3f);
            Assert.AreEqual(radius, PlanarDist(p2, Center), 1e-3f);

            // 120°間隔 → 互いの距離は radius*√3 ≈ 3.464。単縦列（同一点）でないこと。
            float expected = radius * Mathf.Sqrt(3f);
            Assert.AreEqual(expected, PlanarDist(p0, p1), 1e-2f, "均等に離れて囲む。");
            Assert.AreEqual(expected, PlanarDist(p1, p2), 1e-2f);
            Assert.AreEqual(expected, PlanarDist(p2, p0), 1e-2f);
        }

        [Test]
        public void Ring_SingleCount_PlacesAtReferenceAngle()
        {
            Vector3 p = SurroundRing.RingPosition(Center, 2f, 0, 1);
            Assert.AreEqual(2f, PlanarDist(p, Center), 1e-3f);
        }

        [Test]
        public void Ring_ClampsNonPositiveCount()
        {
            Vector3 p = SurroundRing.RingPosition(Center, 2f, 0, 0);
            Assert.AreEqual(2f, PlanarDist(p, Center), 1e-3f, "count<=0 は 1 とみなす。");
        }

        private static float PlanarDist(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
