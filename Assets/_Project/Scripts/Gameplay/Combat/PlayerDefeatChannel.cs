using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// プレイヤー死亡（致死確定）の型付きイベント（Phase3.5 P3.5-02）。同一命中解決内で一度だけ発行し、CombatSessionController
    /// （P3.5-03）が購読して Session を Defeat へ一度だけ遷移させる。撃破側 <see cref="EnemyDefeatChannel"/> と同系統。
    /// </summary>
    public readonly struct PlayerDefeatedEvent
    {
        /// <summary>死亡したプレイヤーの被弾同定 ID（<see cref="IDamageable.DamageableId"/>）。</summary>
        public int PlayerId { get; }

        /// <summary>致死時の World 位置。</summary>
        public Vector3 Position { get; }

        public PlayerDefeatedEvent(int playerId, Vector3 position)
        {
            PlayerId = playerId;
            Position = position;
        }
    }

    /// <summary>プレイヤー死亡イベントの受信契約（Session・HUD・Feedback が購読）。</summary>
    public interface IPlayerDefeatListener
    {
        /// <summary>プレイヤーが死亡した（1 回だけ）ときに呼ばれる。</summary>
        void OnPlayerDefeated(in PlayerDefeatedEvent defeated);
    }

    /// <summary>
    /// プレイヤー死亡イベントの配信チャネル（<see cref="EnemyDefeatChannel"/> / <see cref="HitResultChannel"/> と同系統）。
    /// static 万能マネージャは作らずインスタンスとして所有し、発火中の購読増減に耐えるためスナップショット反復する。
    /// </summary>
    public sealed class PlayerDefeatChannel
    {
        private readonly List<IPlayerDefeatListener> _listeners = new List<IPlayerDefeatListener>();

        /// <summary>購読者数（診断・テスト用）。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>購読を追加する（null・重複は無視）。</summary>
        public void AddListener(IPlayerDefeatListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(IPlayerDefeatListener listener) => _listeners.Remove(listener);

        /// <summary>全購読者へ通知する（発火中の増減に備えスナップショット反復）。</summary>
        public void Publish(in PlayerDefeatedEvent defeated)
        {
            if (_listeners.Count == 0)
            {
                return;
            }

            IPlayerDefeatListener[] snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnPlayerDefeated(defeated);
            }
        }
    }
}
