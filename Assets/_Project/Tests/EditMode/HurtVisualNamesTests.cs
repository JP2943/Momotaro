using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-01：被弾（<see cref="PlayerState.Hurt"/>）が 4 方向の Hurt クリップ（AN_Player_Hurt_{Dir}）へ写像されること、
    /// 攻撃段引数の影響を受けないこと、他状態の写像を壊さないことを検証する。名前解決は Presentation に閉じる。
    /// </summary>
    public sealed class HurtVisualNamesTests
    {
        [Test]
        public void Hurt_MapsToFourDirectionClipNames()
        {
            Assert.AreEqual("AN_Player_Hurt_Down", PlayerVisualNames.ClipName(PlayerState.Hurt, FacingDirection.Down));
            Assert.AreEqual("AN_Player_Hurt_Up", PlayerVisualNames.ClipName(PlayerState.Hurt, FacingDirection.Up));
            Assert.AreEqual("AN_Player_Hurt_Left", PlayerVisualNames.ClipName(PlayerState.Hurt, FacingDirection.Left));
            Assert.AreEqual("AN_Player_Hurt_Right", PlayerVisualNames.ClipName(PlayerState.Hurt, FacingDirection.Right));
        }

        [Test]
        public void Hurt_IgnoresAttackStageArgument()
        {
            Assert.AreEqual("AN_Player_Hurt_Down", PlayerVisualNames.ClipName(PlayerState.Hurt, FacingDirection.Down, 2));
            Assert.AreEqual("AN_Player_Hurt_Right", PlayerVisualNames.ClipName(PlayerState.Hurt, FacingDirection.Right, 3));
        }

        [Test]
        public void Hurt_DoesNotAffectOtherStates()
        {
            Assert.AreEqual("AN_Player_Idle_Down", PlayerVisualNames.ClipName(PlayerState.Idle, FacingDirection.Down));
            Assert.AreEqual("AN_Player_GuardBreak_Up", PlayerVisualNames.ClipName(PlayerState.GuardBreak, FacingDirection.Up));
        }

        [Test]
        public void AllFourHurtDirections_MatchNamingConvention()
        {
            var dirs = new[] { FacingDirection.Down, FacingDirection.Left, FacingDirection.Right, FacingDirection.Up };
            var caps = new[] { "Down", "Left", "Right", "Up" };
            for (int i = 0; i < dirs.Length; i++)
            {
                Assert.AreEqual("AN_Player_Hurt_" + caps[i], PlayerVisualNames.ClipName(PlayerState.Hurt, dirs[i]));
            }
        }
    }
}
