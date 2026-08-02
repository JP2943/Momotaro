using Momotaro.Gameplay.Enemy.Perception;

namespace Momotaro.Gameplay.Enemy.Threat
{
    /// <summary>
    /// ヘイト（脅威）評価の対象契約（Phase3 P3-06。§7）。<see cref="IPerceptionTarget"/> を継承し、認識で用いる
    /// Identity・Faction・位置・有効性に加えて、脅威評価に必要な「基礎ヘイト」「獲得ヘイト補正」「撃破/ダウン中か」を公開する。
    /// Phase 3 の実対象は主人公のみだが、Phase 4 の犬・猿・雉を <b>敵 AI を書き換えずに</b> 候補へ追加できるよう汎用化する
    /// （§7.1／§15）。各対象は自らの脅威プロファイル（基礎ヘイト・獲得倍率）を宣言し、敵側は行動の重み（§7.1 加算表）だけを持つ。
    /// </summary>
    public interface IThreatTarget : IPerceptionTarget
    {
        /// <summary>
        /// ダウン／撃破などで脅威対象として無効か（§7.2「非活動・Down 対象は 0」「即時切替」）。
        /// true の間は脅威 0 とみなし、現在対象なら即時に選択を切り替える。
        /// </summary>
        bool IsDown { get; }

        /// <summary>
        /// 基礎ヘイト（§7.1 対象補正「主人公 基礎ヘイト+50」）。減衰の対象外で、対象が有効な限り常に維持される（下限）。
        /// 主人公=50、将来の仲間は 0 を基本とし、Inspector／Data で調整する。
        /// </summary>
        float BaseThreat { get; }

        /// <summary>
        /// 獲得ヘイトへ掛ける対象補正（§7.1「犬×1.5／猿×1.2／雉×0.5」）。行動由来の加算にのみ乗じ、基礎ヘイトには影響しない。
        /// 主人公=1.0 を基本とする。
        /// </summary>
        float AcquiredThreatMultiplier { get; }
    }
}
