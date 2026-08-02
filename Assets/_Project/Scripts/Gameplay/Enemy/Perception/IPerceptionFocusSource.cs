namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// 認識の注視対象の供給契約（Phase3 P3-06 受入修正 req1）。敵が「今どの対象を認識・追跡・攻撃すべきか」の決定を、
    /// ヘイト（脅威）選択（<see cref="Threat.EnemyThreatTracker"/>）へ委ねるための入口。<see cref="EnemyPerception"/> はこの供給元が
    /// 対象を返す間はその対象へ視覚を評価し（最寄りではなく Threat 最大対象を追う）、供給が無い場合は従来どおり最寄り敵対対象へ
    /// フォールバックする（刺激調査・最終確認位置・Return などの既存経路は維持する。req5）。
    /// </summary>
    public interface IPerceptionFocusSource
    {
        /// <summary>現在注視すべき対象を返す（無ければ false）。</summary>
        bool TryGetFocusTarget(out IPerceptionTarget target);
    }
}
