using System.Collections.Generic;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の状態遷移の型付きイベント（P4-01）。Actor 同定・旧状態・新状態・理由を伴う。Presentation（仮表示の色替え・
    /// 状態ラベル）、Debug HUD、将来の仲間切替（P8）が読み取り専用で購読する（内部状態を書き換えない）。
    /// </summary>
    public readonly struct CompanionStateChanged
    {
        /// <summary>Actor 同定 ID。</summary>
        public int ActorId { get; }

        /// <summary>遷移前の状態。</summary>
        public CompanionState Previous { get; }

        /// <summary>遷移後の状態。</summary>
        public CompanionState Current { get; }

        /// <summary>遷移理由（型付き）。</summary>
        public CompanionStateChangeReason Reason { get; }

        /// <summary>型付き遷移イベントを生成する。</summary>
        public CompanionStateChanged(int actorId, CompanionState previous, CompanionState current,
            CompanionStateChangeReason reason)
        {
            ActorId = actorId;
            Previous = previous;
            Current = current;
            Reason = reason;
        }
    }

    /// <summary>仲間の状態遷移の受信契約。</summary>
    public interface ICompanionStateListener
    {
        /// <summary>状態が遷移したときに呼ばれる。</summary>
        void OnCompanionStateChanged(in CompanionStateChanged change);
    }

    /// <summary>
    /// 仲間の状態遷移の通知チャネル（<c>EnemyStateChannel</c> と同系統。発火中の購読増減に安全なスナップショット反復）。
    /// static な万能マネージャは作らず、各仲間がインスタンスとして所有する。
    /// </summary>
    public sealed class CompanionStateChannel
    {
        private readonly List<ICompanionStateListener> _listeners = new List<ICompanionStateListener>();

        /// <summary>購読者数。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>購読を追加する（null・重複は無視）。</summary>
        public void AddListener(ICompanionStateListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(ICompanionStateListener listener)
        {
            _listeners.Remove(listener);
        }

        /// <summary>全購読者へ通知する（発火中の増減に備えスナップショット反復）。</summary>
        public void Publish(in CompanionStateChanged change)
        {
            if (_listeners.Count == 0)
            {
                return;
            }

            ICompanionStateListener[] snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnCompanionStateChanged(change);
            }
        }
    }
}
