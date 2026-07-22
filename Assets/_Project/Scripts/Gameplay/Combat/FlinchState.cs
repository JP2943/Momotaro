namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// ひるみ蓄積の Runtime 状態（Phase2 P2-05。仕様書 §3.12 / §3.12.1 / §3.12.2）。純粋クラスで deltaTime を
    /// 外部から受け取りテスト可能。体幹・スタンとは独立系統（§3.13 / Table 15）。
    ///
    /// 規則：ひるませ値を蓄積し、耐性以上でひるみ発生（発生時に蓄積 0）。最後の蓄積から <see cref="_holdSeconds"/> 秒
    /// 保持し、再蓄積でタイマー更新。<see cref="_holdSeconds"/> 秒経過で蓄積を一括 0（徐々に減らさない）。ひるみ中は蓄積しない。
    /// ひるみ時間 <see cref="_flinchSeconds"/> 秒、ひるみ終了後 <see cref="_immunitySeconds"/> 秒は新たなひるみを無効化する。
    /// </summary>
    public sealed class FlinchState
    {
        private readonly float _resistance;
        private readonly float _holdSeconds;
        private readonly float _flinchSeconds;
        private readonly float _immunitySeconds;

        private float _accumulation;
        private float _holdRemaining;
        private float _flinchRemaining;
        private float _immunityRemaining;

        public FlinchState(float resistance, float holdSeconds = 1.5f, float flinchSeconds = 0.5f, float immunitySeconds = 0.5f)
        {
            _resistance = resistance <= 0f ? 1f : resistance;
            _holdSeconds = holdSeconds < 0f ? 0f : holdSeconds;
            _flinchSeconds = flinchSeconds < 0f ? 0f : flinchSeconds;
            _immunitySeconds = immunitySeconds < 0f ? 0f : immunitySeconds;
        }

        /// <summary>ひるみ耐性値。</summary>
        public float Resistance => _resistance;

        /// <summary>現在の蓄積量。</summary>
        public float Accumulation => _accumulation;

        /// <summary>ひるみ中か。</summary>
        public bool IsFlinching => _flinchRemaining > 0f;

        /// <summary>ひるみ終了後の免疫中か。</summary>
        public bool InImmunity => _immunityRemaining > 0f;

        /// <summary>
        /// ひるませ値を加算する。ひるみ中は蓄積しない（0 を返す）。加算により耐性以上へ達し、かつ免疫中でなければ
        /// ひるみを発生させ蓄積を 0 に戻す。実際に蓄積へ加わった量を返す。
        /// </summary>
        public float AddFlinch(float amount)
        {
            if (amount <= 0f || IsFlinching)
            {
                return 0f;
            }

            _accumulation += amount;
            _holdRemaining = _holdSeconds;

            if (_immunityRemaining <= 0f && _accumulation >= _resistance)
            {
                _accumulation = 0f;
                _flinchRemaining = _flinchSeconds;
            }

            return amount;
        }

        /// <summary>時間を進める。ひるみ・免疫・蓄積保持を更新する。</summary>
        public void Tick(float deltaTime)
        {
            if (IsFlinching)
            {
                _flinchRemaining -= deltaTime;
                if (_flinchRemaining <= 0f)
                {
                    _flinchRemaining = 0f;
                    _immunityRemaining = _immunitySeconds;
                }

                return;
            }

            if (_immunityRemaining > 0f)
            {
                _immunityRemaining -= deltaTime;
                if (_immunityRemaining < 0f)
                {
                    _immunityRemaining = 0f;
                }
            }

            if (_holdRemaining > 0f)
            {
                _holdRemaining -= deltaTime;
                if (_holdRemaining <= 0f)
                {
                    _holdRemaining = 0f;
                    _accumulation = 0f; // 保持切れで一括 0
                }
            }
        }

        /// <summary>ひるみ状態を初期化する。</summary>
        public void Reset()
        {
            _accumulation = 0f;
            _holdRemaining = 0f;
            _flinchRemaining = 0f;
            _immunityRemaining = 0f;
        }
    }
}
