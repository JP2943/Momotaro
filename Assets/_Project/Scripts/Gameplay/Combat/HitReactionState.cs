namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾リアクション（Hurt 硬直・被弾後無敵）の Runtime タイマ状態（Phase3.5 P3.5-01。仕様書 §3.2 / Table3）。
    /// 純粋クラスで deltaTime を外部から受け取り、MonoBehaviour 非依存でテストできる（StaminaState / SpecialChargeState と同方針）。
    ///
    /// 規則：<see cref="Begin"/> で Hurt 硬直（既定 0.30 秒）と被弾後無敵（既定 0.50 秒）を同時に開始する。硬直と無敵は独立した
    /// タイマで、無敵の方が長いため「硬直は解けたが無敵は継続」区間が存在する。境界は「残り &gt; 0 の間だけ有効」とし、
    /// 経過が持続時間ちょうどに達した瞬間に終了する（0.30/0.50 秒＝終了、直前＝有効、直後＝終了）。Game Time 前提のため、
    /// Pause（deltaTime 0）では進行しない。
    /// </summary>
    public sealed class HitReactionState
    {
        private readonly float _hurtSeconds;
        private readonly float _invincibleSeconds;

        private float _hurtRemaining;
        private float _invincibleRemaining;

        public HitReactionState(float hurtSeconds = 0.30f, float invincibleSeconds = 0.50f)
        {
            _hurtSeconds = hurtSeconds < 0f ? 0f : hurtSeconds;
            _invincibleSeconds = invincibleSeconds < 0f ? 0f : invincibleSeconds;
        }

        /// <summary>Hurt 硬直（強制行動不能）中か。</summary>
        public bool IsHurt => _hurtRemaining > 0f;

        /// <summary>被弾後無敵（通常 Damage を無効化）中か。硬直より長く継続しうる。</summary>
        public bool IsInvincible => _invincibleRemaining > 0f;

        /// <summary>Hurt 硬直の残り秒（検証・HUD 用）。</summary>
        public float HurtRemaining => _hurtRemaining;

        /// <summary>被弾後無敵の残り秒（検証用）。</summary>
        public float InvincibleRemaining => _invincibleRemaining;

        /// <summary>Hurt 硬直の設定秒。</summary>
        public float HurtSeconds => _hurtSeconds;

        /// <summary>被弾後無敵の設定秒。</summary>
        public float InvincibleSeconds => _invincibleSeconds;

        /// <summary>被弾を受けて Hurt 硬直と被弾後無敵を最大から開始する（再被弾で上書きリフレッシュ）。</summary>
        public void Begin()
        {
            _hurtRemaining = _hurtSeconds;
            _invincibleRemaining = _invincibleSeconds;
        }

        /// <summary>時間を進める。Pause 中（deltaTime 0 以下）は進行しない。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (_hurtRemaining > 0f)
            {
                _hurtRemaining -= deltaTime;
                if (_hurtRemaining < 0f)
                {
                    _hurtRemaining = 0f;
                }
            }

            if (_invincibleRemaining > 0f)
            {
                _invincibleRemaining -= deltaTime;
                if (_invincibleRemaining < 0f)
                {
                    _invincibleRemaining = 0f;
                }
            }
        }

        /// <summary>硬直・無敵を即時に解除する（Disable / Scene 離脱 / Retry の後始末。仕様書 §2.3）。</summary>
        public void Reset()
        {
            _hurtRemaining = 0f;
            _invincibleRemaining = 0f;
        }
    }
}
