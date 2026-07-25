using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-02 受入修正：攻撃開始時の Facing 確定（その時点の Move 入力から 4 方向を決定）と、
    /// Deadzone 未満での現状維持を検証する。実行順に依存しない明示確定であることを担保する。
    /// </summary>
    public sealed class AttackFacingConfirmTests
    {
        private static PlayerFacing MakeFacing()
        {
            var go = new GameObject("FacingTest");
            return go.AddComponent<PlayerFacing>();
        }

        [Test]
        public void ConfirmFromInput_SetsFourDirectionFromMove()
        {
            PlayerFacing facing = MakeFacing();
            try
            {
                facing.ConfirmFromInput(new Vector2(1f, 0f));
                Assert.AreEqual(FacingDirection.Right, facing.Current);

                facing.ConfirmFromInput(new Vector2(0f, 1f));
                Assert.AreEqual(FacingDirection.Up, facing.Current);

                facing.ConfirmFromInput(new Vector2(-0.9f, 0.2f));
                Assert.AreEqual(FacingDirection.Left, facing.Current);

                facing.ConfirmFromInput(new Vector2(0.1f, -0.9f));
                Assert.AreEqual(FacingDirection.Down, facing.Current);
            }
            finally
            {
                Object.DestroyImmediate(facing.gameObject);
            }
        }

        [Test]
        public void ConfirmFromInput_BelowDeadzone_KeepsCurrent()
        {
            PlayerFacing facing = MakeFacing();
            try
            {
                facing.ConfirmFromInput(new Vector2(1f, 0f)); // Right
                facing.ConfirmFromInput(new Vector2(0.05f, 0.05f)); // 微小入力
                Assert.AreEqual(FacingDirection.Right, facing.Current, "Deadzone 未満では向きを維持。");

                facing.ConfirmFromInput(Vector2.zero);
                Assert.AreEqual(FacingDirection.Right, facing.Current, "無入力では向きを維持。");
            }
            finally
            {
                Object.DestroyImmediate(facing.gameObject);
            }
        }

        [Test]
        public void ConfirmFromInput_WorksRegardlessOfLockState()
        {
            // 明示確定はロック状態に依存しない（実行順非依存の担保）。
            PlayerFacing facing = MakeFacing();
            try
            {
                facing.IsLocked = true;
                facing.ConfirmFromInput(new Vector2(0f, 1f));
                Assert.AreEqual(FacingDirection.Up, facing.Current);
            }
            finally
            {
                Object.DestroyImmediate(facing.gameObject);
            }
        }
    }
}
