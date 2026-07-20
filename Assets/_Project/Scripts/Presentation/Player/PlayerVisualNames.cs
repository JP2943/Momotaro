using Momotaro.Gameplay.Player;

namespace Momotaro.Presentation.Player
{
    /// <summary>
    /// Gameplay 状態・向きから Animation Clip 名を解決する（Phase1 P1-09）。名前解決は Presentation 側に閉じ、
    /// Gameplay Logic は Sprite 名・Clip 名・フレーム数へ依存しない。
    /// Guard 系状態は Guard クリップへ、それ以外は Idle/Move へ写像する。
    /// </summary>
    public static class PlayerVisualNames
    {
        /// <summary>状態と向きに対応するクリップ名（例: AN_Player_Idle_Down）。</summary>
        public static string ClipName(PlayerState state, FacingDirection facing)
        {
            string statePart;
            switch (state)
            {
                case PlayerState.Move:
                    statePart = "Move";
                    break;
                case PlayerState.GuardIdle:
                case PlayerState.GuardMove:
                    statePart = "Guard";
                    break;
                default:
                    statePart = "Idle";
                    break;
            }

            string dirPart;
            switch (facing)
            {
                case FacingDirection.Up:
                    dirPart = "Up";
                    break;
                case FacingDirection.Left:
                    dirPart = "Left";
                    break;
                case FacingDirection.Right:
                    dirPart = "Right";
                    break;
                default:
                    dirPart = "Down";
                    break;
            }

            return "AN_Player_" + statePart + "_" + dirPart;
        }
    }
}
