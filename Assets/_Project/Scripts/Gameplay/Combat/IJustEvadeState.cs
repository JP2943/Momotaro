namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾側のジャスト回避（Just Evade）受付状態を表す読み取り＋成功通知の最小契約（Phase3.5 P3.5-09）。ジャストガードの「回避版」。
    /// ステップ回避の開始直後のタイトな窓（<see cref="CanJustEvade"/>）で無敵中の命中を受けたとき、命中解決側（<see cref="IDamageable"/>
    /// 実装）が反撃（体幹反射＋近接攻撃者への強制ひるみ）と専用フィードバックを起動し、<see cref="NotifyJustEvadeSuccess"/> で
    /// 当該ステップの受付窓を閉じる（1 ステップ 1 回）。ガード不能攻撃への「回避が正解」の報酬として機能する。
    ///
    /// 受付タイミングの管理は Player 側（<c>PlayerStateController</c> が <c>StepState</c> を駆動）に閉じる。
    /// Input System / Animator / Scene には依存しない。実際の無敵可否（I-frame）は <see cref="IEvadeState"/> が別途供給し、
    /// 命中解決側は「無敵中（<see cref="IEvadeState.IsInvincible"/>）かつジャスト窓中（<see cref="CanJustEvade"/>）」で成立と判定する。
    /// </summary>
    public interface IJustEvadeState
    {
        /// <summary>いまジャスト回避を受け付けているか（ステップ開始直後のタイト窓が開いているか）。</summary>
        bool CanJustEvade { get; }

        /// <summary>ジャスト回避成立時に攻撃者の体幹（Poise）へ反射する固定ダメージ量。</summary>
        float JustEvadeCounterPoise { get; }

        /// <summary>ジャスト回避成立を通知する（当該ステップの受付窓をクローズ。1 ステップ 1 回）。</summary>
        void NotifyJustEvadeSuccess();
    }
}
