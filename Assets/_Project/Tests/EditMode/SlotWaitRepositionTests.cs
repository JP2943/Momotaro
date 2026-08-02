using Momotaro.Gameplay.Enemy.Slots;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-07：Slot 待ち時の周回間合い <see cref="SlotWaitReposition"/> の検証（§8.1「棒立ちにせず包囲・距離調整」）。
    /// 周回目標が対象から一定半径にあり、所有者ごとに周回方向が分かれることを確認する。純粋・再現可能。
    /// </summary>
    public sealed class SlotWaitRepositionTests
    {
        private static readonly Vector3 Target = new Vector3(5f, 0f, 5f);

        [Test]
        public void OrbitTarget_KeepsRadius_AndMovesOffCurrent()
        {
            Vector3 self = Target + new Vector3(0f, 0f, -2f);
            Vector3 orbit = SlotWaitReposition.OrbitTarget(self, Target, radius: 2f, sign: 1f, stepDegrees: 30f);
            Assert.AreEqual(2f, SlotWaitReposition.PlanarDistance(orbit, Target), 1e-3f, "対象から半径一定で周回。");
            Assert.AreNotEqual(self, orbit, "棒立ちにせず移動目標を出す。");
        }

        [Test]
        public void OrbitTarget_OppositeSigns_GoOppositeWays()
        {
            Vector3 self = Target + new Vector3(0f, 0f, -2f);
            Vector3 cw = SlotWaitReposition.OrbitTarget(self, Target, 2f, 1f, 30f);
            Vector3 ccw = SlotWaitReposition.OrbitTarget(self, Target, 2f, -1f, 30f);
            Assert.AreNotEqual(cw, ccw, "符号で周回方向が分かれ包囲になる。");
        }

        [Test]
        public void OrbitTarget_Overlap_UsesDefaultDirection()
        {
            Vector3 orbit = SlotWaitReposition.OrbitTarget(Target, Target, radius: 2f, sign: 1f, stepDegrees: 30f);
            Assert.AreEqual(2f, SlotWaitReposition.PlanarDistance(orbit, Target), 1e-3f, "完全重なりでも半径を保つ。");
        }

        [Test]
        public void DirectionSign_SplitsByParity()
        {
            Assert.AreEqual(1f, SlotWaitReposition.DirectionSign(2));
            Assert.AreEqual(-1f, SlotWaitReposition.DirectionSign(3));
        }
    }
}
