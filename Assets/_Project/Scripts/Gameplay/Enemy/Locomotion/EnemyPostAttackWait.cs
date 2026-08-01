using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Locomotion
{
    /// <summary>
    /// 攻撃後待機の純粋タイマー（Phase3 P3-05。§9.1 / Table 6）。近接敵は攻撃終了後に一定秒（0.7〜1.2 等、アーキタイプの
    /// PostAttackWait 範囲）だけ間を置いてから次の攻撃・間合い調整へ移る。連打で理不尽にならないための待機で、Game Time で
    /// 進める。Unity 非依存で EditMode 再現可能。
    /// </summary>
    public sealed class EnemyPostAttackWait
    {
        private float _remaining;

        /// <summary>残り待機秒。</summary>
        public float Remaining => _remaining;

        /// <summary>待機中か。</summary>
        public bool IsWaiting => _remaining > 0f;

        /// <summary>待機を開始する（負値は 0）。</summary>
        public void Begin(float duration)
        {
            _remaining = Mathf.Max(0f, duration);
        }

        /// <summary>時間を進める。</summary>
        public void Tick(float deltaTime)
        {
            if (_remaining > 0f)
            {
                _remaining = Mathf.Max(0f, _remaining - deltaTime);
            }
        }

        /// <summary>待機を解除する。</summary>
        public void Clear()
        {
            _remaining = 0f;
        }

        /// <summary>[min, max] を t01（0..1）で線形補間した待機秒を返す（範囲の順序が逆でも安全）。</summary>
        public static float PickDuration(float min, float max, float t01)
        {
            float lo = Mathf.Min(min, max);
            float hi = Mathf.Max(min, max);
            return Mathf.Lerp(lo, hi, Mathf.Clamp01(t01));
        }
    }
}
