using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-04：照準解決 <see cref="EnemyAimingResolver"/> の現在位置型・予測位置型・追尾型を検証する（§6.1/Table 9）。純粋・再現可能。
    /// </summary>
    public sealed class EnemyAimingResolverTests
    {
        [Test]
        public void CurrentPosition_AimsAtTargetNow()
        {
            Vector3 d = EnemyAimingResolver.Resolve(EnemyAimingMode.CurrentPosition, Vector3.zero,
                new Vector3(0, 0, 5f), Vector3.zero, 0.3f);
            Assert.AreEqual(new Vector3(0, 0, 1f), d, "対象方向（+Z）へ正規化。");
        }

        [Test]
        public void PredictedPosition_LeadsByVelocityTimesPredict()
        {
            Vector3 d = EnemyAimingResolver.Resolve(EnemyAimingMode.PredictedPosition, Vector3.zero,
                new Vector3(0, 0, 5f), new Vector3(5f, 0, 0f), 0.5f);
            // 予測点 (2.5, 0, 5) 方向 → +X 成分を持つ。
            Assert.Greater(d.x, 0f, "対象の移動方向へ先読み。");
            Assert.AreEqual(1f, d.magnitude, 1e-4f, "正規化。");
        }

        [Test]
        public void Tracking_UsesCurrentPosition()
        {
            Vector3 d = EnemyAimingResolver.Resolve(EnemyAimingMode.Tracking, Vector3.zero,
                new Vector3(3f, 0, 0f), new Vector3(0, 0, 9f), 0.3f);
            Assert.AreEqual(new Vector3(1f, 0, 0f), d, "追尾は現在位置狙い（旋回停止は Machine が管理）。");
        }

        [Test]
        public void DegenerateDirection_ReturnsForward()
        {
            Vector3 d = EnemyAimingResolver.Resolve(EnemyAimingMode.CurrentPosition, Vector3.zero, Vector3.zero,
                Vector3.zero, 0.3f);
            Assert.AreEqual(Vector3.forward, d);
        }
    }
}
