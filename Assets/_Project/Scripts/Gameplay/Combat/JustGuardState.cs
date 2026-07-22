namespace Momotaro.Gameplay.Combat
{
    /// <summary>ジャストガードの入力状態（Phase2 P2-08。仕様書 §3.3 / §9.1）。明示的に管理する。</summary>
    public enum JustGuardPhase
    {
        /// <summary>通常。押下で通常受付窓を開く。</summary>
        Normal = 0,

        /// <summary>連打ペナルティ中。解除直後の一定時間で、この間の押下→解除では JG 受付を開かない。</summary>
        ReleasePenalty = 1,

        /// <summary>連続成功猶予中。JG 成功で 1 回付与。次の解除では連打ペナルティを発生させず消費する。</summary>
        SuccessGrace = 2,
    }

    /// <summary>
    /// ジャストガードの受付タイミングを管理する純粋状態機械（Phase2 P2-08。仕様書 §3.3 / §9.1）。deltaTime を外部から受け取り
    /// テスト可能。前方判定・スタミナ・体幹反射は扱わず、「今 JG を受け付けているか（<see cref="CanJustGuard"/>）」だけを提供する。
    ///
    /// 規則：ガード押下で通常受付窓（<see cref="_pressWindowSeconds"/>、既定 0.15 秒）を開く。ガード解除の直後にも受付窓を残す
    /// （<see cref="_releaseWindowSeconds"/>、既定 0.075 秒）。解除時は連打ペナルティ（<see cref="_penaltySeconds"/>、既定 0.20 秒）へ入り、
    /// ペナルティ中の押下→解除では受付窓を開かない（Spam 防止。通常ガードは可能）。JG 成功（<see cref="NotifySuccess"/>）で連続成功猶予を
    /// 1 回付与し、猶予中の解除は連打ペナルティを発生させず消費する（多段攻撃を一段ごとに JG できる）。猶予は重複しない。
    /// 多段攻撃は一段ごとに新しい押下が必要（成功で現在の受付窓は閉じる）。通常ガード中の再押下による窓更新は行わない。
    /// </summary>
    public sealed class JustGuardState
    {
        private readonly float _pressWindowSeconds;
        private readonly float _releaseWindowSeconds;
        private readonly float _penaltySeconds;

        private float _windowRemaining;
        private float _penaltyRemaining;
        private bool _graceAvailable;
        private bool _guardHeld;

        public JustGuardState(
            float pressWindowSeconds = 0.15f,
            float releaseWindowSeconds = 0.075f,
            float penaltySeconds = 0.20f)
        {
            _pressWindowSeconds = pressWindowSeconds < 0f ? 0f : pressWindowSeconds;
            _releaseWindowSeconds = releaseWindowSeconds < 0f ? 0f : releaseWindowSeconds;
            _penaltySeconds = penaltySeconds < 0f ? 0f : penaltySeconds;
        }

        /// <summary>現在の受付窓の残り秒（0 なら受付なし）。</summary>
        public float WindowRemaining => _windowRemaining;

        /// <summary>連打ペナルティの残り秒。</summary>
        public float PenaltyRemaining => _penaltyRemaining;

        /// <summary>連続成功猶予を保持しているか。</summary>
        public bool HasSuccessGrace => _graceAvailable;

        /// <summary>いま JG を受け付けているか。</summary>
        public bool CanJustGuard => _windowRemaining > 0f;

        /// <summary>明示管理する入力状態（通常／連打ペナルティ／連続成功猶予）。</summary>
        public JustGuardPhase Phase
        {
            get
            {
                if (_graceAvailable)
                {
                    return JustGuardPhase.SuccessGrace;
                }

                return _penaltyRemaining > 0f ? JustGuardPhase.ReleasePenalty : JustGuardPhase.Normal;
            }
        }

        /// <summary>
        /// ガード押下（立ち上がり）。通常／猶予中は通常受付窓を開く。連打ペナルティ中（かつ猶予なし）は窓を開かない（Spam 防止）。
        /// すでに押下中なら何もしない（通常ガード中の再押下では窓更新しない）。
        /// </summary>
        public void Press()
        {
            if (_guardHeld)
            {
                return;
            }

            _guardHeld = true;

            // 猶予中／通常は通常窓、連打ペナルティ中（猶予なし）は開かない。
            if (_graceAvailable || _penaltyRemaining <= 0f)
            {
                _windowRemaining = _pressWindowSeconds;
            }
            else
            {
                _windowRemaining = 0f;
            }
        }

        /// <summary>
        /// ガード解除（立ち下がり）。猶予中は連打ペナルティを発生させず解除後窓を開いて猶予を消費する。通常は解除後窓を開いて
        /// 連打ペナルティへ入る。連打ペナルティ中の解除（Spam）は窓を開かない。押下していなければ何もしない。
        /// </summary>
        public void Release()
        {
            if (!_guardHeld)
            {
                return;
            }

            _guardHeld = false;

            if (_graceAvailable)
            {
                _graceAvailable = false;      // 猶予を消費
                _penaltyRemaining = 0f;       // 連打ペナルティを発生させない
                _windowRemaining = _releaseWindowSeconds;
            }
            else if (_penaltyRemaining > 0f)
            {
                _windowRemaining = 0f;        // Spam：受付なし（ペナルティは延長しない）
            }
            else
            {
                _windowRemaining = _releaseWindowSeconds;
                _penaltyRemaining = _penaltySeconds;
            }
        }

        /// <summary>時間を進める。受付窓・連打ペナルティを減算する（猶予は時間で消えない）。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (_windowRemaining > 0f)
            {
                _windowRemaining -= deltaTime;
                if (_windowRemaining < 0f)
                {
                    _windowRemaining = 0f;
                }
            }

            if (_penaltyRemaining > 0f)
            {
                _penaltyRemaining -= deltaTime;
                if (_penaltyRemaining < 0f)
                {
                    _penaltyRemaining = 0f;
                }
            }
        }

        /// <summary>
        /// JG 成功を通知する。連続成功猶予を 1 回付与（重複なし）し、連打ペナルティを解除、現在の受付窓を閉じる
        /// （多段は一段ごとに新しい押下が必要）。
        /// </summary>
        public void NotifySuccess()
        {
            _graceAvailable = true;
            _penaltyRemaining = 0f;
            _windowRemaining = 0f;
        }

        /// <summary>入力状態を初期化する。</summary>
        public void Reset()
        {
            _windowRemaining = 0f;
            _penaltyRemaining = 0f;
            _graceAvailable = false;
            _guardHeld = false;
        }
    }
}
