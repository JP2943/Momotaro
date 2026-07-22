using Momotaro.Gameplay.Player;

namespace Momotaro.Presentation.Player
{
    /// <summary>
    /// Gameplay 状態・向きから Animation Clip 名を解決する（Phase1 P1-09・Phase2 P2-03A）。名前解決は Presentation 側に閉じ、
    /// Gameplay Logic は Sprite 名・Clip 名・フレーム数へ依存しない。
    /// Guard 系状態は Guard クリップへ、Attack 状態は段番号（1..3）付きの Attack クリップへ、それ以外は Idle/Move へ写像する。
    /// </summary>
    public static class PlayerVisualNames
    {
        /// <summary>状態と向きに対応するクリップ名（例: AN_Player_Idle_Down）。Attack は段 1 として解決する。</summary>
        public static string ClipName(PlayerState state, FacingDirection facing)
        {
            return ClipName(state, facing, 1);
        }

        /// <summary>
        /// 状態・向き・攻撃段（Attack 時のみ使用）に対応するクリップ名を返す（例: AN_Player_Attack1_Down）。
        /// <paramref name="attackStage"/> は 1..3 にクランプする。P2-03A では段は常に 1、段送りは P2-03B。
        /// </summary>
        public static string ClipName(PlayerState state, FacingDirection facing, int attackStage)
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
                case PlayerState.Attack:
                    int stage = attackStage < 1 ? 1 : (attackStage > 3 ? 3 : attackStage);
                    statePart = "Attack" + stage;
                    break;
                case PlayerState.GuardBreak:
                    // 仮対応（Phase2 P2-07）。完成 Animation の接続・再生制御は対象外。名前解決のみ用意する。
                    statePart = "GuardBreak";
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
