using Momotaro.Presentation.Enemy;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-08 受入修正：矢の 4 方向表示（§9.2）。進行方向の 4 方向量子化（対角の固定規則）と、方向→スプライト選択を検証する。
    /// Gameplay Root は回転させず方向別スプライトで表現する方針の中核ロジック。
    /// </summary>
    public sealed class ProjectileVisualTests
    {
        [Test]
        public void FromForward_QuantizesCardinals()
        {
            Assert.AreEqual(EnemyVisualFacing.Up, EnemyFacingResolver.FromForward(new Vector3(0, 0, 1)));
            Assert.AreEqual(EnemyVisualFacing.Down, EnemyFacingResolver.FromForward(new Vector3(0, 0, -1)));
            Assert.AreEqual(EnemyVisualFacing.Right, EnemyFacingResolver.FromForward(new Vector3(1, 0, 0)));
            Assert.AreEqual(EnemyVisualFacing.Left, EnemyFacingResolver.FromForward(new Vector3(-1, 0, 0)));
        }

        [Test]
        public void FromForward_DiagonalRule_IsFixed_PrefersXOnTie()
        {
            // |x| >= |z| で左右を優先（対角の量子化規則を固定）。
            Assert.AreEqual(EnemyVisualFacing.Right, EnemyFacingResolver.FromForward(new Vector3(1, 0, 1)));
            Assert.AreEqual(EnemyVisualFacing.Right, EnemyFacingResolver.FromForward(new Vector3(1, 0, -1)));
            Assert.AreEqual(EnemyVisualFacing.Left, EnemyFacingResolver.FromForward(new Vector3(-1, 0, 1)));
            Assert.AreEqual(EnemyVisualFacing.Left, EnemyFacingResolver.FromForward(new Vector3(-1, 0, -1)));
        }

        [Test]
        public void FromForward_YIsIgnored()
        {
            Assert.AreEqual(EnemyVisualFacing.Up, EnemyFacingResolver.FromForward(new Vector3(0, 9, 1)), "Y は無視（XZ 平面）。");
        }

        [Test]
        public void Pick_SelectsSpritePerFacing()
        {
            var tex = new Texture2D(1, 1);
            Sprite d = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f));
            Sprite u = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f));
            Sprite l = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f));
            Sprite r = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0f));

            Assert.AreSame(d, EnemyProjectileVisualAdapter.Pick(EnemyVisualFacing.Down, d, u, l, r));
            Assert.AreSame(u, EnemyProjectileVisualAdapter.Pick(EnemyVisualFacing.Up, d, u, l, r));
            Assert.AreSame(l, EnemyProjectileVisualAdapter.Pick(EnemyVisualFacing.Left, d, u, l, r));
            Assert.AreSame(r, EnemyProjectileVisualAdapter.Pick(EnemyVisualFacing.Right, d, u, l, r));

            Object.DestroyImmediate(d); Object.DestroyImmediate(u);
            Object.DestroyImmediate(l); Object.DestroyImmediate(r); Object.DestroyImmediate(tex);
        }
    }
}
