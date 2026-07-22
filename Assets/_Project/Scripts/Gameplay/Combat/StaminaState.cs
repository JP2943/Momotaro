namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 主人公スタミナとガードブレイクの Runtime 状態（Phase2 P2-07。仕様書 §3.2 / §3.2.1）。純粋クラスで deltaTime を
    /// 外部から受け取りテスト可能。SO 原本は保持せず Runtime 値のみを持つ。状態境界を跨いだ余剰 deltaTime は同じ Tick で
    /// 後続へ流し、大きな Tick でも小分け Tick と概ね一致させる（P2-05 と同方針）。
    ///
    /// 規則：ガードで固定量を消費（<see cref="Consume"/>）。最後の消費から <see cref="_regenDelay"/> 秒後に毎秒
    /// <see cref="_regenPerSecond"/> 回復。スタミナ 0 になった消費では待機を <see cref="_zeroRegenDelay"/> 秒へ延長する。
    /// スタミナ 0 でガードブレイク（<see cref="_breakSeconds"/> 秒の行動不能。被 HP ダメージ <see cref="BreakHpMultiplier"/> 倍）。
    /// ブレイク終了時に最大値の <see cref="_breakRestoreRatio"/> まで回復する。回復停止条件（ガード中・被弾・チャージ中・
    /// ダウン中）は呼び出し側が <c>regenBlocked</c> で与える。ブレイク中は内部的に常に回復停止。通常攻撃中は回復可。
    /// </summary>
    public sealed class StaminaState
    {
        private readonly float _max;
        private readonly float _regenPerSecond;
        private readonly float _regenDelay;
        private readonly float _zeroRegenDelay;
        private readonly float _breakSeconds;
        private readonly float _breakRestoreRatio; // 0..1
        private readonly float _breakHpMultiplier;

        private float _current;
        private float _regenDelayRemaining;
        private float _breakRemaining;

        public StaminaState(
            float max,
            float regenPerSecond = 25f,
            float regenDelay = 1.0f,
            float zeroRegenDelay = 1.5f,
            float breakSeconds = 1.5f,
            float breakRestoreRatio = 0.25f,
            float breakHpMultiplier = 1.25f)
        {
            _max = max <= 0f ? 1f : max;
            _regenPerSecond = regenPerSecond < 0f ? 0f : regenPerSecond;
            _regenDelay = regenDelay < 0f ? 0f : regenDelay;
            _zeroRegenDelay = zeroRegenDelay < 0f ? 0f : zeroRegenDelay;
            _breakSeconds = breakSeconds < 0f ? 0f : breakSeconds;
            _breakRestoreRatio = breakRestoreRatio < 0f ? 0f : (breakRestoreRatio > 1f ? 1f : breakRestoreRatio);
            _breakHpMultiplier = breakHpMultiplier;
            _current = _max;
        }

        /// <summary>最大スタミナ。</summary>
        public float Max => _max;

        /// <summary>現在スタミナ。</summary>
        public float Current => _current;

        /// <summary>ガードブレイク（行動不能）中か。</summary>
        public bool IsBroken => _breakRemaining > 0f;

        /// <summary>ブレイク残り秒。</summary>
        public float BreakRemaining => _breakRemaining;

        /// <summary>ブレイク中の被 HP ダメージ倍率（それ以外は 1.0）。</summary>
        public float BreakHpMultiplier => IsBroken ? _breakHpMultiplier : 1f;

        /// <summary>
        /// ガードで固定スタミナを消費する。ブレイク中は消費しない（行動不能でガードできない）。実際に減った量を返す。
        /// 残量を超える消費でも 0 で止まり、0 到達でガードブレイクへ移行する（その一撃自体の防御成否は呼び出し側で判定済み）。
        /// </summary>
        public float Consume(float amount)
        {
            if (amount <= 0f || IsBroken)
            {
                return 0f;
            }

            float before = _current;
            _current -= amount;
            if (_current < 0f)
            {
                _current = 0f;
            }

            // 0 到達なら待機延長。その後ブレイクへ（ブレイク中は回復停止なので待機値は復帰後に上書きされる）。
            _regenDelayRemaining = _current <= 0f ? _zeroRegenDelay : _regenDelay;

            if (_current <= 0f)
            {
                _breakRemaining = _breakSeconds;
            }

            return before - _current;
        }

        /// <summary>
        /// 時間を進める。ブレイク・回復待機・回復を更新する。<paramref name="regenBlocked"/> が true の間は回復を停止する
        /// （ガード中・被弾・チャージ中・ダウン中など。攻撃中は false を渡し回復を許可）。ブレイク中は内部的に常に停止。
        /// </summary>
        public void Tick(float deltaTime, bool regenBlocked)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            // ブレイク：終了を跨いだ余剰は復帰後の回復処理へ流す。
            if (IsBroken)
            {
                if (deltaTime < _breakRemaining)
                {
                    _breakRemaining -= deltaTime;
                    return; // 行動不能中は回復しない
                }

                deltaTime -= _breakRemaining; // 余剰
                _breakRemaining = 0f;
                _current = _max * _breakRestoreRatio; // 復帰（最大の 25% 等）
                _regenDelayRemaining = _regenDelay;   // 復帰後は通常待機（>0 なので延長不要）
            }

            if (regenBlocked)
            {
                return; // 回復停止（待機は据え置き）
            }

            if (_current < _max)
            {
                if (deltaTime < _regenDelayRemaining)
                {
                    _regenDelayRemaining -= deltaTime;
                }
                else
                {
                    float regenSeconds = deltaTime - _regenDelayRemaining; // 待機超過分
                    _regenDelayRemaining = 0f;
                    _current += _regenPerSecond * regenSeconds;
                    if (_current > _max)
                    {
                        _current = _max;
                    }
                }
            }
        }

        /// <summary>スタミナ・ブレイクを初期状態へ戻す。</summary>
        public void Reset()
        {
            _current = _max;
            _regenDelayRemaining = 0f;
            _breakRemaining = 0f;
        }
    }
}
