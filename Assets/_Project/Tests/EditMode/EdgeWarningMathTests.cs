using Momotaro.Gameplay.Enemy.Screen;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-08 受入修正：画面端警告の純粋計算（§9.2）。Viewport→Screen 変換（カメラ背面の反転）、画面端クランプ、中心からの方向、
    /// 発射者までの距離を決定的に検証する。
    /// </summary>
    public sealed class EdgeWarningMathTests
    {
        private const float W = 1920f;
        private const float H = 1080f;

        [Test]
        public void ScreenPoint_FrontCamera_MapsDirectly()
        {
            Vector2 p = EdgeWarningMath.ScreenPointFromViewport(new Vector3(0.75f, 0.25f, 5f), W, H);
            Assert.AreEqual(0.75f * W, p.x, 1e-2f);
            Assert.AreEqual(0.25f * H, p.y, 1e-2f);
        }

        [Test]
        public void ScreenPoint_BehindCamera_IsMirroredAroundCenter()
        {
            // 背面（z<0）は中心対称に反転して「発射者のいる向き」を安定に示す。
            Vector2 p = EdgeWarningMath.ScreenPointFromViewport(new Vector3(0.75f, 0.25f, -5f), W, H);
            Assert.AreEqual(0.25f * W, p.x, 1e-2f, "x は中心対称に反転。");
            Assert.AreEqual(0.75f * H, p.y, 1e-2f, "y は中心対称に反転。");
        }

        [Test]
        public void ClampInside_KeepsWithinMargin()
        {
            var c = EdgeWarningMath.ClampInside(new Vector2(-500f, 5000f), W, H, 24f);
            Assert.AreEqual(24f, c.x, 1e-3f, "左端は margin。");
            Assert.AreEqual(H - 24f, c.y, 1e-3f, "上端は height-margin。");
        }

        [Test]
        public void ClampInside_NoOp_WhenInside()
        {
            var c = EdgeWarningMath.ClampInside(new Vector2(960f, 540f), W, H, 24f);
            Assert.AreEqual(960f, c.x, 1e-3f);
            Assert.AreEqual(540f, c.y, 1e-3f);
        }

        [Test]
        public void DirectionFromCenter_PointsToward()
        {
            Vector2 d = EdgeWarningMath.DirectionFromCenter(new Vector2(W, H * 0.5f), W, H);
            Assert.AreEqual(1f, d.x, 1e-3f, "右端は +X 方向。");
            Assert.AreEqual(0f, d.y, 1e-3f);
        }

        [Test]
        public void ApproxDistance_UsesBothPositions()
        {
            float dist = EdgeWarningMath.ApproxDistance(new Vector3(0, 0, 0), new Vector3(3, 0, 4));
            Assert.AreEqual(5f, dist, 1e-4f, "source と target の距離。");
        }
    }
}
