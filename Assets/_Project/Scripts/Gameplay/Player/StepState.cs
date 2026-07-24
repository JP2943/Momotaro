using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// ステップ回避の Runtime 状態（Phase2 P2-09。仕様書 §3.4 / §10）。純粋クラスで deltaTime を外部から受け取りテスト可能。
    /// SO 原本は保持せず Runtime 値のみを持つ。
    ///
    /// 規則：開始時に方向を確定し（入力方向、無入力なら後方を呼び出し側が渡す）、移動フェーズ <see cref="_moveSeconds"/> 秒の間だけ
    /// 距離 <see cref="_distance"/> を一定速度（distance/moveSeconds）で移動する。移動後は後硬直 <see cref="_recoverySeconds"/> 秒。
    /// 無敵は <see cref="_invincibleStartSeconds"/>〜<see cref="_invincibleEndSeconds"/> 秒の区間（境界は開始含む・終了含まない）。
    /// 終了直前の <see cref="_chainBufferSeconds"/> 秒は先行入力で連続ステップ／通常攻撃 1 段目へ接続できる窓（<see cref="CanChain"/>）。
    /// 敵すり抜け・壁停止は物理／Layer 側で解決し、本クラスは速度と無敵状態のみを提供する。
    /// </summary>
    public sealed class StepState
    {
        private readonly float _distance;
        private readonly float _moveSeconds;
        private readonly float _recoverySeconds;
        private readonly float _invincibleStartSeconds;
        private readonly float _invincibleEndSeconds;
        private readonly float _chainBufferSeconds;

        private Vector3 _direction;
        private float _elapsed;
        private bool _active;

        public StepState(
            float distance,
            float moveSeconds = 0.20f,
            float recoverySeconds = 0.10f,
            float invincibleStartSeconds = 0.05f,
            float invincibleEndSeconds = 0.20f,
            float chainBufferSeconds = 0.12f)
        {
            _distance = distance < 0f ? 0f : distance;
            _moveSeconds = moveSeconds <= 0f ? 0.0001f : moveSeconds;
            _recoverySeconds = recoverySeconds < 0f ? 0f : recoverySeconds;
            _invincibleStartSeconds = invincibleStartSeconds < 0f ? 0f : invincibleStartSeconds;
            _invincibleEndSeconds = invincibleEndSeconds < 0f ? 0f : invincibleEndSeconds;
            _chainBufferSeconds = chainBufferSeconds < 0f ? 0f : chainBufferSeconds;
        }

        /// <summary>ステップの総時間（移動＋後硬直）。</summary>
        public float TotalSeconds => _moveSeconds + _recoverySeconds;

        /// <summary>ステップ中（移動＋後硬直を含む）か。</summary>
        public bool IsActive => _active;

        /// <summary>無敵区間中か（開始含む・終了含まない。Damageable が明示状態として評価する）。</summary>
        public bool IsInvincible => _active && _elapsed >= _invincibleStartSeconds && _elapsed < _invincibleEndSeconds;

        /// <summary>移動フェーズ中か（速度を適用する区間）。後硬直中は false。</summary>
        public bool IsMoving => _active && _elapsed < _moveSeconds;

        /// <summary>確定したステップ方向（World XZ・正規化）。</summary>
        public Vector3 Direction => _direction;

        /// <summary>現在の XZ 速度（移動フェーズのみ非ゼロ）。壁は物理が停止させる。</summary>
        public Vector3 CurrentVelocity => IsMoving ? _direction * (_distance / _moveSeconds) : Vector3.zero;

        /// <summary>終了直前の先行入力窓中か（連続ステップ／通常攻撃 1 段目へ接続可能）。</summary>
        public bool CanChain => _active && _elapsed >= (TotalSeconds - _chainBufferSeconds);

        /// <summary>経過秒（検証用）。</summary>
        public float Elapsed => _elapsed;

        /// <summary>ステップを開始する。方向は XZ 平面へ射影して正規化する。ゼロ方向なら開始しない。</summary>
        public void Begin(Vector3 direction)
        {
            Vector3 flat = new Vector3(direction.x, 0f, direction.z);
            if (flat.sqrMagnitude < 1e-6f)
            {
                return;
            }

            _direction = flat.normalized;
            _elapsed = 0f;
            _active = true;
        }

        /// <summary>時間を進める。総時間に達したら終了する。</summary>
        public void Tick(float deltaTime)
        {
            if (!_active || deltaTime <= 0f)
            {
                return;
            }

            _elapsed += deltaTime;
            if (_elapsed >= TotalSeconds)
            {
                _active = false;
            }
        }

        /// <summary>ステップを初期化する。</summary>
        public void Reset()
        {
            _active = false;
            _elapsed = 0f;
            _direction = Vector3.zero;
        }
    }
}
