namespace Momotaro.Gameplay.Combat
{
    /// <summary>必殺技チャージの解放結果（Phase2 P2-10）。</summary>
    public enum SpecialReleaseResult
    {
        /// <summary>最大チャージ未満で離した。不発（発動しない）。</summary>
        NotCharged = 0,

        /// <summary>最大チャージ済みで発動する。</summary>
        Fire = 1,
    }

    /// <summary>
    /// 必殺技のホールドチャージ状態（Phase2 P2-10。仕様書 §3.6）。純粋クラスで deltaTime を外部から受け取りテスト可能。
    ///
    /// 規則：ボタン長押しで開始し、<see cref="_chargeSeconds"/> 秒（既定 2.0）で最大チャージ。最大未満で離すと不発
    /// （<see cref="SpecialReleaseResult.NotCharged"/>）。最大到達後は <see cref="_maxHoldSeconds"/> 秒（既定 0.75）まで保持でき、
    /// 保持限界を超えると自動発動（<see cref="ShouldAutoFire"/>）。チャージ中の移動不可・方向転換のみ・各種キャンセルは駆動側が扱う。
    /// </summary>
    public sealed class SpecialChargeState
    {
        private readonly float _chargeSeconds;
        private readonly float _maxHoldSeconds;

        private float _elapsed;
        private bool _active;

        public SpecialChargeState(float chargeSeconds = 2.0f, float maxHoldSeconds = 0.75f)
        {
            _chargeSeconds = chargeSeconds < 0f ? 0f : chargeSeconds;
            _maxHoldSeconds = maxHoldSeconds < 0f ? 0f : maxHoldSeconds;
        }

        /// <summary>チャージ中（開始〜発動/中断まで）か。</summary>
        public bool IsActive => _active;

        /// <summary>最大チャージ済み（発動可能）か。</summary>
        public bool IsCharged => _active && _elapsed >= _chargeSeconds;

        /// <summary>最大チャージ未満の充填中か。</summary>
        public bool IsCharging => _active && _elapsed < _chargeSeconds;

        /// <summary>保持限界（最大チャージ＋<see cref="_maxHoldSeconds"/>）を超え、自動発動すべきか。</summary>
        public bool ShouldAutoFire => _active && _elapsed >= _chargeSeconds + _maxHoldSeconds;

        /// <summary>経過秒（検証・HUD 用）。</summary>
        public float Elapsed => _elapsed;

        /// <summary>最大チャージ秒。</summary>
        public float ChargeSeconds => _chargeSeconds;

        /// <summary>チャージ開始（長押し開始）。</summary>
        public void Begin()
        {
            _elapsed = 0f;
            _active = true;
        }

        /// <summary>時間を進める。</summary>
        public void Tick(float deltaTime)
        {
            if (!_active || deltaTime <= 0f)
            {
                return;
            }

            _elapsed += deltaTime;
        }

        /// <summary>
        /// ボタンを離したときの解放。最大チャージ済みなら <see cref="SpecialReleaseResult.Fire"/>、未満なら
        /// <see cref="SpecialReleaseResult.NotCharged"/>（不発）。いずれもチャージを終了する。
        /// </summary>
        public SpecialReleaseResult Release()
        {
            if (!_active)
            {
                return SpecialReleaseResult.NotCharged;
            }

            SpecialReleaseResult result = _elapsed >= _chargeSeconds
                ? SpecialReleaseResult.Fire
                : SpecialReleaseResult.NotCharged;
            _active = false;
            return result;
        }

        /// <summary>チャージを中断する（ガード/ステップ/交代/被弾など。後隙なし）。</summary>
        public void Cancel()
        {
            _active = false;
            _elapsed = 0f;
        }

        /// <summary>初期化する。</summary>
        public void Reset()
        {
            _active = false;
            _elapsed = 0f;
        }
    }
}
