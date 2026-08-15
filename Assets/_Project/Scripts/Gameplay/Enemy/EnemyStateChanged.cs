using System.Collections.Generic;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵状態遷移の型付きイベント（Phase3 §2.4）。Actor 同定・旧状態・新状態・理由を伴う。
    /// Presentation／Debug／将来の仲間 AI（Phase 4）は読み取り専用でこれを購読する（内部状態を書き換えない）。
    /// </summary>
    public readonly struct EnemyStateChanged
    {
        /// <summary>Actor 同定 ID。</summary>
        public int ActorId { get; }

        /// <summary>遷移前の状態。</summary>
        public EnemyState Previous { get; }

        /// <summary>遷移後の状態。</summary>
        public EnemyState Current { get; }

        /// <summary>遷移理由（型付き）。</summary>
        public EnemyStateChangeReason Reason { get; }

        /// <summary>型付き遷移イベントを生成する。</summary>
        public EnemyStateChanged(int actorId, EnemyState previous, EnemyState current, EnemyStateChangeReason reason)
        {
            ActorId = actorId;
            Previous = previous;
            Current = current;
            Reason = reason;
        }
    }

    /// <summary>敵状態遷移の受信契約。</summary>
    public interface IEnemyStateListener
    {
        /// <summary>状態が遷移したときに呼ばれる。</summary>
        void OnEnemyStateChanged(in EnemyStateChanged change);
    }

    /// <summary>
    /// 敵状態遷移の通知チャネル（<see cref="HitResultChannel"/> と同系統。Snapshot 反復で購読中の増減に安全）。
    /// </summary>
    public sealed class EnemyStateChannel
    {
        private readonly List<IEnemyStateListener> _listeners = new List<IEnemyStateListener>();

        /// <summary>購読者数。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>購読を追加する（重複登録はしない）。</summary>
        public void AddListener(IEnemyStateListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(IEnemyStateListener listener)
        {
            _listeners.Remove(listener);
        }

        /// <summary>全購読者へ通知する（発火中の増減に備えスナップショット反復）。</summary>
        public void Publish(in EnemyStateChanged change)
        {
            int count = _listeners.Count;
            if (count == 0)
            {
                return;
            }

            var snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnEnemyStateChanged(change);
            }
        }
    }
}
