using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-03A：Attack 状態＋段＋向きから、生成した 12 クリップ（AN_Player_Attack{1..3}_{Dir}）の名前へ
    /// 正しく写像されること、段のクランプ、非 Attack 状態が影響を受けないことを検証する。
    /// </summary>
    public sealed class AttackVisualNamesTests
    {
        [Test]
        public void Attack_MapsToStagedClipNames()
        {
            Assert.AreEqual("AN_Player_Attack1_Down", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Down, 1));
            Assert.AreEqual("AN_Player_Attack2_Left", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Left, 2));
            Assert.AreEqual("AN_Player_Attack3_Up", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Up, 3));
            Assert.AreEqual("AN_Player_Attack1_Right", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Right, 1));
        }

        [Test]
        public void Attack_TwoArgOverload_DefaultsToStageOne()
        {
            Assert.AreEqual("AN_Player_Attack1_Down", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Down));
        }

        [Test]
        public void Attack_StageIsClampedToValidRange()
        {
            Assert.AreEqual("AN_Player_Attack1_Down", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Down, 0));
            Assert.AreEqual("AN_Player_Attack1_Down", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Down, -5));
            Assert.AreEqual("AN_Player_Attack3_Down", PlayerVisualNames.ClipName(PlayerState.Attack, FacingDirection.Down, 9));
        }

        [Test]
        public void NonAttackStates_IgnoreStageArgument()
        {
            Assert.AreEqual("AN_Player_Idle_Down", PlayerVisualNames.ClipName(PlayerState.Idle, FacingDirection.Down, 2));
            Assert.AreEqual("AN_Player_Move_Left", PlayerVisualNames.ClipName(PlayerState.Move, FacingDirection.Left, 3));
            Assert.AreEqual("AN_Player_Guard_Up", PlayerVisualNames.ClipName(PlayerState.GuardIdle, FacingDirection.Up, 2));
        }

        [Test]
        public void AllTwelveStageDirectionCombos_MatchAssetNamingConvention()
        {
            var dirs = new[] { FacingDirection.Down, FacingDirection.Left, FacingDirection.Right, FacingDirection.Up };
            var caps = new[] { "Down", "Left", "Right", "Up" };
            for (int stage = 1; stage <= 3; stage++)
            {
                for (int i = 0; i < dirs.Length; i++)
                {
                    string expected = "AN_Player_Attack" + stage + "_" + caps[i];
                    Assert.AreEqual(expected, PlayerVisualNames.ClipName(PlayerState.Attack, dirs[i], stage));
                }
            }
        }
    }
}
