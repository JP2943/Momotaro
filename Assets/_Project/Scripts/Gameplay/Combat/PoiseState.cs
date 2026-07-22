namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 体幹（ポイズ）とスタンの Runtime 状態（Phase2 P2-05。仕様書 §3.11 / §3.11.2 / §3.13）。純粋クラスで
    /// deltaTime を外部から受け取りテスト可能。SO 原本は保持せず、Runtime 値のみを持つ。
    ///
    /// 規則：最後の体幹ダメージから <see cref="_recoveryDelay"/> 秒後に毎秒最大値の <see cref="_recoveryRatioPerSecond"/> を回復。
    /// ジャストガード被弾後は回復開始を <see cref="_jgRecoveryDelay"/> 秒後へ遅らせる。体幹 0 でスタン（<see cref="_stunSeconds"/> 秒）。
    /// スタン中は回復しない。スタン終了時に体幹を最大へ全回復し、その後 <see cref="_postStunReductionSeconds"/> 秒間は
    /// 受ける体幹ダメージを <see cref="_postStunReduction"/> 割合ぶん軽減する。スタン中の被 HP ダメージ倍率は <see cref="StunHpMultiplier"/>。
    /// </summary>
    public sealed class PoiseState
    {
        private readonly float _max;
        private readonly float _recoveryDelay;
        private readonly float _recoveryRatioPerSecond;
        private readonly float _jgRecoveryDelay;
        private readonly float _stunSeconds;
        private readonly float _postStunReductionSeconds;
        private readonly float _postStunReduction; // 0..1（0.5 = 50%減）
        private readonly float _stunHpMultiplier;

        private float _current;
        private float _recoveryDelayRemaining;
        private float _stunRemaining;
        private float _postStunReductionRemaining;

        public PoiseState(
            float max,
            float recoveryDelay = 3f,
            float recoveryRatioPerSecond = 0.08f,
            float jgRecoveryDelay = 4f,
            float stunSeconds = 3f,
            float postStunReductionSeconds = 3f,
            float postStunReduction = 0.5f,
            float stunHpMultiplier = 1.25f)
        {
            _max = max <= 0f ? 1f : max;
            _recoveryDelay = recoveryDelay < 0f ? 0f : recoveryDelay;
            _recoveryRatioPerSecond = recoveryRatioPerSecond < 0f ? 0f : recoveryRatioPerSecond;
            _jgRecoveryDelay = jgRecoveryDelay < 0f ? 0f : jgRecoveryDelay;
            _stunSeconds = stunSeconds < 0f ? 0f : stunSeconds;
            _postStunReductionSeconds = postStunReductionSeconds < 0f ? 0f : postStunReductionSeconds;
            _postStunReduction = postStunReduction < 0f ? 0f : (postStunReduction > 1f ? 1f : postStunReduction);
            _stunHpMultiplier = stunHpMultiplier;
            _current = _max;
        }

        /// <summary>最大体幹。</summary>
        public float Max => _max;

        /// <summary>現在体幹。</summary>
        public float Current => _current;

        /// <summary>スタン中か。</summary>
        public bool IsStunned => _stunRemaining > 0f;

        /// <summary>スタン残り秒。</summary>
        public float StunRemaining => _stunRemaining;

        /// <summary>スタン終了後の体幹軽減期間中か。</summary>
        public bool InPostStunReduction => _postStunReductionRemaining > 0f;

        /// <summary>回復開始までの残り待機秒（最後の体幹ダメージ後。JG は 4 秒、通常は 3 秒から減算）。</summary>
        public float RecoveryDelayRemaining => _recoveryDelayRemaining;

        /// <summary>スタン中の被 HP ダメージ倍率（それ以外は 1.0）。</summary>
        public float StunHpMultiplier => IsStunned ? _stunHpMultiplier : 1f;

        /// <summary>
        /// 体幹ダメージを適用する。スタン終了後の軽減期間中は <see cref="_postStunReduction"/> ぶん軽減する。
        /// 体幹 0 でスタンへ移行する。実際に減った体幹量を返す。
        /// </summary>
        /// <param name="amount">状況補正・倍率適用後の体幹ダメージ。</param>
        /// <param name="isJustGuard">ジャストガードによる体幹ダメージなら true（回復開始が遅くなる）。</param>
        public float ApplyPoiseDamage(float amount, bool isJustGuard = false)
        {
            if (amount <= 0f)
            {
                return 0f;
            }

            float effective = InPostStunReduction ? amount * (1f - _postStunReduction) : amount;
            float before = _current;
            _current -= effective;
            if (_current < 0f)
            {
                _current = 0f;
            }

            _recoveryDelayRemaining = isJustGuard ? _jgRecoveryDelay : _recoveryDelay;

            if (_current <= 0f && !IsStunned)
            {
                _stunRemaining = _stunSeconds;
            }

            return before - _current;
        }

        /// <summary>
        /// 時間を進める。スタン・回復・軽減期間を更新する。状態境界を跨いだ余剰 deltaTime は同じ Tick 内で後続へ流し、
        /// 大きな deltaTime でも小分け Tick と概ね同じ結果になるようにする（P2-05 受入修正）。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // スタン：終了を跨いだ余剰は軽減期間の消化へ回す。
            if (IsStunned)
            {
                if (deltaTime < _stunRemaining)
                {
                    _stunRemaining -= deltaTime;
                    return; // スタン中は回復しない
                }

                deltaTime -= _stunRemaining; // 余剰
                _stunRemaining = 0f;
                _current = _max; // スタン終了時に全回復
                _postStunReductionRemaining = _postStunReductionSeconds;
                // 満タンなのでこの後の回復処理は発生しない。余剰は軽減期間の消化に使う。
            }

            if (_postStunReductionRemaining > 0f)
            {
                _postStunReductionRemaining -= deltaTime;
                if (_postStunReductionRemaining < 0f)
                {
                    _postStunReductionRemaining = 0f;
                }
            }

            // 回復：遅延を跨いだ余剰分だけ同じ Tick で回復する。
            if (_current < _max)
            {
                if (deltaTime < _recoveryDelayRemaining)
                {
                    _recoveryDelayRemaining -= deltaTime;
                }
                else
                {
                    float recoverSeconds = deltaTime - _recoveryDelayRemaining; // 遅延超過分
                    _recoveryDelayRemaining = 0f;
                    _current += _recoveryRatioPerSecond * _max * recoverSeconds;
                    if (_current > _max)
                    {
                        _current = _max;
                    }
                }
            }
        }

        /// <summary>体幹・スタンを初期状態へ戻す。</summary>
        public void Reset()
        {
            _current = _max;
            _recoveryDelayRemaining = 0f;
            _stunRemaining = 0f;
            _postStunReductionRemaining = 0f;
        }
    }
}
