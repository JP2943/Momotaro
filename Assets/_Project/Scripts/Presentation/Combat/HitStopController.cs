using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// ヒットストップ（一瞬の時間停止）を <see cref="Time.timeScale"/> の一時低下で表現する Presentation 制御（Phase3.5 P3.5-05B）。
    /// 命中フィードバックの <c>Cue.HitStopSeconds</c> を <see cref="Request"/> で受け、要求秒だけ timeScale を落として復帰する。
    /// 計測は unscaled 時間で行い（停止中も進む）、多重要求は長い方を採用する（ジャストガード等の強調を弱めない）。
    ///
    /// Pause（timeScale 0）との競合を避けるため、未停止かつ既に timeScale が 0 の間は要求を無視する。Disable・Scene 離脱・
    /// Retry では <see cref="CancelImmediately"/> で必ず元の timeScale へ戻し、停止状態を残さない。Gameplay ロジックには干渉しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitStopController : MonoBehaviour
    {
        [Tooltip("停止中の timeScale（0=完全停止）。")]
        [SerializeField] private float _frozenTimeScale = 0f;

        [Tooltip("1 回のヒットストップ上限（秒）。暴走・体感過多を防ぐ安全上限。")]
        [SerializeField] private float _maxHitStopSeconds = 0.25f;

        private float _remaining;
        private bool _stopping;
        private float _restoreTimeScale = 1f;

        /// <summary>ヒットストップ中か（テスト・検証用）。</summary>
        public bool IsStopping => _stopping;

        /// <summary>残りの停止時間（秒。テスト・検証用）。</summary>
        public float Remaining => _remaining;

        /// <summary>
        /// ヒットストップを要求する。<paramref name="seconds"/> は上限で丸め、0 以下は無視。既に停止中なら残り時間の長い方を採用する。
        /// Pause 中（未停止で timeScale 0）は競合回避のため無視する。
        /// </summary>
        public void Request(float seconds)
        {
            if (seconds <= 0f)
            {
                return;
            }

            seconds = Mathf.Min(seconds, _maxHitStopSeconds);

            if (!_stopping)
            {
                if (Time.timeScale <= 0f)
                {
                    return; // Pause 中は掛けない（復帰スケールを誤らないため）。
                }

                _restoreTimeScale = Time.timeScale;
                _stopping = true;
                Time.timeScale = _frozenTimeScale < 0f ? 0f : _frozenTimeScale;
            }

            _remaining = Mathf.Max(_remaining, seconds);
        }

        /// <summary>時間を進める（unscaled 時間で呼ぶ。停止中も進み、満了で timeScale を復帰）。</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!_stopping)
            {
                return;
            }

            _remaining -= unscaledDeltaTime < 0f ? 0f : unscaledDeltaTime;
            if (_remaining <= 0f)
            {
                CancelImmediately();
            }
        }

        /// <summary>即時に停止を解除し、元の timeScale へ戻す（Disable・Scene 離脱・Retry・満了）。timeScale を残さない。</summary>
        public void CancelImmediately()
        {
            _remaining = 0f;
            if (_stopping)
            {
                Time.timeScale = _restoreTimeScale;
                _stopping = false;
            }
        }

        private void OnDisable()
        {
            CancelImmediately();
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }
    }
}
