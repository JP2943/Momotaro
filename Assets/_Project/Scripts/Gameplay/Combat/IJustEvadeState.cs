namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾側のジャスト回避（Just Evade）受付状態を表す読み取り＋成功通知の最小契約（Phase3.5 P3.5-09。P6C で報酬を置換）。
    ///
    /// 命中解決側（<see cref="IDamageable"/> 実装）は「敵の攻撃（<see cref="HitInfo.IsEnemyAttack"/>）・ステップ回避可能
    /// （<see cref="HitInfo.Steppable"/>）・ステップ無敵中（<see cref="IEvadeState.IsInvincible"/>）・受付中（<see cref="CanJustEvade"/>）」の
    /// すべてが揃ったときだけ成功と判定し、<see cref="NotifyJustEvadeSuccess"/> を<b>一度だけ</b>呼ぶ。
    ///
    /// P6C：成功の報酬は「次に開始した通常攻撃の一段の HP 強化」だけ。攻撃者への体幹反射・強制ひるみは<b>行わない</b>（旧 P3.5-09 の報酬を置換）。
    /// 受付時間・倍率・有効時間は <c>StepData</c> が正本。受付と強化の管理は Player 側に閉じる。
    /// </summary>
    public interface IJustEvadeState
    {
        /// <summary>いまジャスト回避を受け付けているか（ステップ中・このステップで未成功・受付終端より前）。</summary>
        bool CanJustEvade { get; }

        /// <summary>
        /// ジャスト回避成立を通知する。当該ステップの受付を閉じ（1 ステップ 1 回）、反撃強化を付与する。
        /// 命中解決が原因を確定した後に 1 回だけ呼ぶ。購読先（表示）から報酬を再実行しない。
        /// </summary>
        void NotifyJustEvadeSuccess();
    }
}
