using System.Collections.Generic;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中結果（<see cref="HitResult"/>）を購読者（<see cref="IHitResultListener"/>）へ配信する通知チャネル
    /// （P2-01 受入修正）。Gameplay 側の命中解決（後続 Task）が <see cref="Publish"/> し、Presentation が
    /// <see cref="AddListener"/> で購読する。public static な万能マネージャは作らず、インスタンスとして所有する。
    ///
    /// 通知はスナップショット走査で行い、通知中の購読追加・解除で例外を出さない（既存 GameMode 通知の P0.5-A 方針に倣う）。
    /// P2-01 では配信の器のみを提供し、結果を生む解決ロジックは実装しない。
    /// </summary>
    public sealed class HitResultChannel
    {
        private readonly List<IHitResultListener> _listeners = new List<IHitResultListener>();

        /// <summary>購読者数（診断・テスト用）。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>購読者を追加する（null・重複は無視）。</summary>
        public void AddListener(IHitResultListener listener)
        {
            if (listener == null || _listeners.Contains(listener))
            {
                return;
            }

            _listeners.Add(listener);
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(IHitResultListener listener)
        {
            if (listener == null)
            {
                return;
            }

            _listeners.Remove(listener);
        }

        /// <summary>結果を全購読者へ配信する。通知中の購読変更に耐えるためスナップショットを走査する。</summary>
        public void Publish(in HitResult result)
        {
            if (_listeners.Count == 0)
            {
                return;
            }

            IHitResultListener[] snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnHitResult(result);
            }
        }
    }
}
