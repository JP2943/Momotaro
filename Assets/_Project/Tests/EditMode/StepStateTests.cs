using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-09：ステップ回避の Runtime 状態（<see cref="StepState"/>）を検証する。方向確定（正規化）、動作/後硬直の時間、
    /// 無敵区間の境界（0.05 含む〜0.20 含まない）、移動速度（距離/移動秒）、終了直前の連続入力窓、Reset を確認する。
    /// </summary>
    public sealed class StepStateTests
    {
        // 距離3・移動0.20・後硬直0.10・無敵0.05〜0.20・連続窓0.12（総 0.30）。
        private static StepState Make() => new StepState(3f);

        [Test]
        public void Begin_NormalizesDirection_XZ()
        {
            var s = Make();
            s.Begin(new Vector3(0f, 5f, 3f)); // Y 無視、+Z
            Assert.IsTrue(s.IsActive);
            Assert.AreEqual(Vector3.forward, s.Direction);
        }

        [Test]
        public void Begin_ZeroDirection_DoesNotStart()
        {
            var s = Make();
            s.Begin(Vector3.zero);
            Assert.IsFalse(s.IsActive);
        }

        [Test]
        public void Diagonal_IsNormalized()
        {
            var s = Make();
            s.Begin(new Vector3(1f, 0f, 1f));
            Assert.AreEqual(1f, s.Direction.magnitude, 1e-4f, "斜めも大きさ 1 に正規化。");
            Assert.AreEqual(Vector3.forward.z * 0.70710677f, s.Direction.z, 1e-4f);
        }

        [Test]
        public void Invincible_WindowBoundaries_StartInclusive_EndExclusive()
        {
            var s = Make();
            s.Begin(Vector3.forward);
            Assert.IsFalse(s.IsInvincible, "開始直後(0)は無敵ではない。");

            s.Tick(0.05f); // 0.05
            Assert.IsTrue(s.IsInvincible, "0.05 は無敵（開始含む）。");

            s.Tick(0.14f); // 0.19
            Assert.IsTrue(s.IsInvincible, "0.19 は無敵。");

            s.Tick(0.01f); // 0.20
            Assert.IsFalse(s.IsInvincible, "0.20 は無敵ではない（終了含まない）。");
        }

        [Test]
        public void Velocity_MoveThenRecovery()
        {
            var s = Make();
            s.Begin(Vector3.forward);
            // 移動フェーズ（<0.20）：距離3 / 移動0.20 = 15。
            Assert.IsTrue(s.IsMoving);
            Assert.AreEqual(15f, s.CurrentVelocity.z, 1e-3f);

            s.Tick(0.20f); // 後硬直へ（0.20）
            Assert.IsFalse(s.IsMoving, "移動フェーズ終了。");
            Assert.AreEqual(Vector3.zero, s.CurrentVelocity, "後硬直中は速度ゼロ。");
            Assert.IsTrue(s.IsActive, "後硬直も動作中。");
        }

        [Test]
        public void CanChain_OnlyNearEnd()
        {
            var s = Make();
            s.Begin(Vector3.forward);
            s.Tick(0.17f); // 0.17 < 0.18（境界手前）
            Assert.IsFalse(s.CanChain, "終了直前窓(残0.12)より前は不可。");
            s.Tick(0.02f); // 0.19 > 0.18（境界を明確に越える。ちょうど 0.18 は float 誤差を避ける）
            Assert.IsTrue(s.CanChain, "総0.30 の残0.12（=0.18 以降）で連続入力可。");
        }

        [Test]
        public void EndsAfterTotalDuration()
        {
            var s = Make();
            s.Begin(Vector3.forward);
            s.Tick(0.30f);
            Assert.IsFalse(s.IsActive, "総動作 0.30 で終了。");
            Assert.IsFalse(s.IsInvincible);
            Assert.AreEqual(Vector3.zero, s.CurrentVelocity);
        }

        [Test]
        public void Reset_ClearsState()
        {
            var s = Make();
            s.Begin(Vector3.forward);
            s.Tick(0.1f);
            s.Reset();
            Assert.IsFalse(s.IsActive);
            Assert.IsFalse(s.IsInvincible);
            Assert.AreEqual(Vector3.zero, s.Direction);
        }
    }
}
