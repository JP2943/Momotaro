using Momotaro.Gameplay.Companion;
using UnityEngine;

namespace Momotaro.Presentation.Companion
{
    /// <summary>
    /// 仮表示の状態色（P4-02）。グレーボックス期は新しい絵を量産せず、単色シルエットの<b>色と透明度</b>で状態を見分ける
    /// （改訂ロードマップ v2.2「モーション差は色・透明度・回転・状態ラベルで表す」）。純粋関数のため EditMode で検証できる。
    ///
    /// 正式素材へ差し替える P10a では本クラスごと不要になる（Gameplay は色を一切参照しない）。
    /// </summary>
    public static class CompanionStateColors
    {
        /// <summary>退場中の色（完全透明。表示自体も止めるため実際には描かれない）。</summary>
        public static readonly Color Away = new Color(1f, 1f, 1f, 0f);

        /// <summary>状態に対応する表示色を返す。未知の状態は待機色。</summary>
        public static Color Resolve(CompanionState state)
        {
            switch (state)
            {
                case CompanionState.Idle: return new Color(0.80f, 0.80f, 0.80f, 1f);   // 灰：待機
                case CompanionState.Follow: return new Color(1.00f, 1.00f, 1.00f, 1f); // 白：追従（既定）
                case CompanionState.Warp: return new Color(0.55f, 0.95f, 1.00f, 0.7f); // 水色・半透明：ワープ
                case CompanionState.Chase: return new Color(1.00f, 0.85f, 0.45f, 1f);  // 黄：接近
                case CompanionState.AttackPrepare: return new Color(1.00f, 0.65f, 0.25f, 1f); // 橙：予兆
                case CompanionState.AttackActive: return new Color(1.00f, 0.35f, 0.20f, 1f); // 赤橙：判定中
                case CompanionState.AttackRecovery: return new Color(0.85f, 0.55f, 0.40f, 1f); // 鈍い橙：後隙
                case CompanionState.Guard: return new Color(0.40f, 0.60f, 1.00f, 1f);  // 青：ガード
                case CompanionState.Evade: return new Color(0.60f, 0.85f, 1.00f, 1f);  // 淡青：回避
                case CompanionState.Protect: return new Color(1.00f, 0.45f, 0.90f, 1f); // 桃：守護（かばう）
                case CompanionState.Stagger: return new Color(0.55f, 0.45f, 0.45f, 1f); // 暗い赤灰：ひるみ
                case CompanionState.Down: return new Color(0.35f, 0.35f, 0.35f, 0.6f);  // 暗灰・半透明：ダウン
                case CompanionState.Recovering: return new Color(0.70f, 0.70f, 0.55f, 0.85f); // 淡黄：復帰待ち
                case CompanionState.Away: return Away;
                case CompanionState.Event: return new Color(0.75f, 0.70f, 0.90f, 1f);  // 藤：イベント
                default: return new Color(0.80f, 0.80f, 0.80f, 1f);
            }
        }

        /// <summary>この状態のとき表示体を描くか（退場中は描かない）。</summary>
        public static bool IsVisible(CompanionState state) => state != CompanionState.Away;
    }
}
