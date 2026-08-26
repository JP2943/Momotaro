using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// カメラ（または指定 Transform）を短時間だけ揺らす Presentation 効果（Phase3.5 P3.5-05B）。<see cref="Shake"/> で強さ・秒数を要求し、
    /// 減衰しながら局所座標へオフセットを与える。多重要求は強い方・長い方を採用する（ジャストガード等の強調を弱めない）。
    ///
    /// 揺れ方向は決定的な疑似乱数（xorshift・シード設定可）で生成し、テストの再現性を保つ。時間は <see cref="Tick"/> で外部から与える
    /// （unscaled 前提。ヒットストップ中も揺れる）。停止・Disable で基準座標へ確実に戻し残さない。表示専用（Gameplay 非干渉）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraShakePresenter : MonoBehaviour
    {
        [Tooltip("揺らす対象。未割当なら自分の Transform を揺らす。")]
        [SerializeField] private Transform _target;

        [Tooltip("揺れ幅の上限（m）。過剰要求を丸める安全上限。")]
        [SerializeField] private float _maxMagnitude = 0.4f;

        [Tooltip("疑似乱数シード（決定的。0 は既定値へ丸め）。")]
        [SerializeField] private uint _seed = 2463534242u;

        private bool _shaking;
        private float _elapsed;
        private float _duration;
        private float _magnitude;
        private Vector3 _base;
        private uint _rng;

        /// <summary>揺れ中か（テスト・検証用）。</summary>
        public bool IsShaking => _shaking;

        /// <summary>揺らす対象（Scene 構築 P3.5-06・テストが設定）。未割当なら自分の Transform。</summary>
        public Transform Target { get => _target; set => _target = value; }

        private Transform Shaken => _target != null ? _target : transform;

        /// <summary>揺れを要求する（強さ・秒数。0 以下は無視。多重要求は強い方・長い方を採用）。</summary>
        public void Shake(float magnitude, float seconds)
        {
            if (magnitude <= 0f || seconds <= 0f)
            {
                return;
            }

            magnitude = Mathf.Min(magnitude, _maxMagnitude);

            if (!_shaking)
            {
                _base = Shaken.localPosition; // 揺れ前の基準を捕捉（再要求時は上書きしない）。
                _rng = _seed == 0u ? 2463534242u : _seed;
                _shaking = true;
                _magnitude = 0f;
                _duration = 0f;
                _elapsed = 0f;
            }

            float remaining = _duration - _elapsed;
            _duration = Mathf.Max(remaining, seconds);
            _magnitude = Mathf.Max(_magnitude, magnitude);
            _elapsed = 0f;
        }

        /// <summary>時間を進めて揺れを更新する（unscaled 前提）。満了で基準座標へ戻す。</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!_shaking)
            {
                return;
            }

            _elapsed += unscaledDeltaTime < 0f ? 0f : unscaledDeltaTime;
            float dur = _duration <= 0f ? 0.0001f : _duration;
            if (_elapsed >= dur)
            {
                Stop();
                return;
            }

            float amp = _magnitude * (1f - _elapsed / dur); // 減衰。
            Shaken.localPosition = _base + new Vector3(NextUnit() * amp, NextUnit() * amp, 0f);
        }

        /// <summary>揺れを止め基準座標へ戻す（Disable・Scene 離脱・Retry・満了）。残留を残さない。</summary>
        public void Stop()
        {
            if (_shaking)
            {
                Shaken.localPosition = _base;
                _shaking = false;
            }

            _magnitude = 0f;
            _duration = 0f;
            _elapsed = 0f;
        }

        private float NextUnit()
        {
            // xorshift32：決定的な [-1,1)。
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return (_rng / 4294967295f) * 2f - 1f;
        }

        private void OnDisable()
        {
            Stop();
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }
    }
}
