using System;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// ヒットストップ（一瞬の時間停止）を <see cref="Time.timeScale"/> の一時低下で表現する Presentation 制御（Phase3.5 P3.5-05B）。
    /// 命中フィードバックの <c>Cue.HitStopSeconds</c> を <see cref="Request"/> で受け、要求秒だけ timeScale を落として復帰する。
    /// 計測は unscaled 時間で行い（停止中も進む）、多重要求は長い方を採用する（ジャストガード等の強調を弱めない）。
    ///
    /// Pause との協調：Pause 側が timeScale を所有する上位権限とみなす。<see cref="PausedQuery"/> に Pause 判定を注入すると、
    /// (1) Pause 中はヒットストップを開始しない、(2) 停止満了・解除時に Pause 中なら timeScale を戻さず Pause へ委ねる（誤って解除しない）、
    /// (3) 停止継続中に Pause が解除されたら停止スケールを再適用する（凍結を取りこぼさない）。<see cref="PausedQuery"/> 未注入時は
    /// timeScale が 0 以下かどうかを Pause の代替判定に使う（従来動作）。Disable・Scene 離脱・Retry では <see cref="CancelImmediately"/>
    /// で元の timeScale へ戻し停止状態を残さない（Pause 中は委譲）。Gameplay ロジックには干渉しない。
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

        /// <summary>
        /// Pause 判定の外部注入（P3.5-06 で Pause 管理へ接続）。true を返す間は Pause 中とみなし、timeScale の所有を Pause へ委ねる。
        /// 未設定（null）の間は「未 Pause」とみなす（timeScale の 0 以下判定を代替に使う）。
        /// </summary>
        public Func<bool> PausedQuery { get; set; }

        /// <summary>ヒットストップ中か（テスト・検証用）。</summary>
        public bool IsStopping => _stopping;

        /// <summary>残りの停止時間（秒。テスト・検証用）。</summary>
        public float Remaining => _remaining;

        private bool IsPaused()
        {
            return PausedQuery != null && PausedQuery();
        }

        private float FrozenScale()
        {
            return _frozenTimeScale < 0f ? 0f : _frozenTimeScale;
        }

        /// <summary>
        /// ヒットストップを要求する。<paramref name="seconds"/> は上限で丸め、0 以下は無視。既に停止中なら残り時間の長い方を採用する。
        /// Pause 中（<see cref="PausedQuery"/> が true、または未注入で timeScale が 0 以下）は競合回避のため無視する。
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
                if (IsPaused() || Time.timeScale <= 0f)
                {
                    return; // Pause 中は掛けない（復帰スケールの誤り・timeScale の二重所有を避ける）。
                }

                _restoreTimeScale = Time.timeScale;
                _stopping = true;
                Time.timeScale = FrozenScale();
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
                _remaining = 0f;
                _stopping = false;
                if (!IsPaused())
                {
                    Time.timeScale = _restoreTimeScale; // 非 Pause のみ復帰。Pause 中は解除せず委ねる。
                }

                return;
            }

            // 継続中：停止中に Pause が解除された場合（Pause 側が timeScale を戻した後）でも凍結を取りこぼさないよう再適用する。
            if (!IsPaused())
            {
                Time.timeScale = FrozenScale();
            }
        }

        /// <summary>即時に停止を解除する（Disable・Scene 離脱・Retry・満了）。非 Pause 時は元の timeScale へ戻し、Pause 中は委ねる。</summary>
        public void CancelImmediately()
        {
            _remaining = 0f;
            if (_stopping)
            {
                _stopping = false;
                if (!IsPaused())
                {
                    Time.timeScale = _restoreTimeScale;
                }
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
