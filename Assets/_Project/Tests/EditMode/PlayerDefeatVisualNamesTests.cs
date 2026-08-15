using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Player;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-02：死亡（<see cref="PlayerState.Defeated"/>）が、専用スプライト未用意のため現 Facing の Hurt クリップ
    /// （AN_Player_Hurt_{Dir}）へ写像されることを検証する（仮表示。仕様書 §4.2）。既存クリップを流用するため、
    /// Animator State 不足の警告連打は発生しない（Hurt State は P3.5-01 で登録済み）。
    /// </summary>
    public sealed class PlayerDefeatVisualNamesTests
    {
        [Test]
        public void Defeated_MapsToHurtClip_PerFacing()
        {
            Assert.AreEqual("AN_Player_Hurt_Down", PlayerVisualNames.ClipName(PlayerState.Defeated, FacingDirection.Down));
            Assert.AreEqual("AN_Player_Hurt_Up", PlayerVisualNames.ClipName(PlayerState.Defeated, FacingDirection.Up));
            Assert.AreEqual("AN_Player_Hurt_Left", PlayerVisualNames.ClipName(PlayerState.Defeated, FacingDirection.Left));
            Assert.AreEqual("AN_Player_Hurt_Right", PlayerVisualNames.ClipName(PlayerState.Defeated, FacingDirection.Right));
        }

        [Test]
        public void Defeated_ReusesSameClipAsHurt()
        {
            foreach (FacingDirection d in new[] { FacingDirection.Down, FacingDirection.Up, FacingDirection.Left, FacingDirection.Right })
            {
                Assert.AreEqual(
                    PlayerVisualNames.ClipName(PlayerState.Hurt, d),
                    PlayerVisualNames.ClipName(PlayerState.Defeated, d),
                    "死亡は Hurt クリップを流用する: " + d);
            }
        }
    }
}
