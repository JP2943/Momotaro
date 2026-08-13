using System.Collections.Generic;
using Momotaro.Data.Characters;
using Momotaro.Data.Progression;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 撃破時に 1 回だけ発行する型付き報酬要求（Phase3 P3-10。§9「型付き Defeated／Reward Request を 1 回発行」）。徳・Item の
    /// 実付与は本 Task 対象外で、要求（誰が・役割・任意の <see cref="RewardData"/>・位置）を通知するだけ。受け手（Phase 4 以降）が付与する。
    /// </summary>
    public readonly struct EnemyRewardRequest
    {
        /// <summary>撃破された敵の Actor 同定 ID。</summary>
        public int EnemyId { get; }

        /// <summary>敵の役割（近接／遠距離／強敵）。</summary>
        public EnemyRole Role { get; }

        /// <summary>付与すべき報酬 Data（未設定なら null。付与は受け手の責務）。</summary>
        public RewardData Reward { get; }

        /// <summary>撃破位置（World）。</summary>
        public Vector3 Position { get; }

        public EnemyRewardRequest(int enemyId, EnemyRole role, RewardData reward, Vector3 position)
        {
            EnemyId = enemyId;
            Role = role;
            Reward = reward;
            Position = position;
        }
    }

    /// <summary>撃破（Down 確定）の型付きイベント（Phase3 P3-10）。撃破された敵と、同時に発行する報酬要求を伴う。</summary>
    public readonly struct EnemyDefeatedEvent
    {
        /// <summary>撃破された敵の Actor 同定 ID。</summary>
        public int EnemyId { get; }

        /// <summary>報酬要求（1 回性）。</summary>
        public EnemyRewardRequest Reward { get; }

        public EnemyDefeatedEvent(int enemyId, EnemyRewardRequest reward)
        {
            EnemyId = enemyId;
            Reward = reward;
        }
    }

    /// <summary>撃破イベントの受信契約（HUD・進行・Reward 付与側が購読）。</summary>
    public interface IEnemyDefeatListener
    {
        /// <summary>敵が撃破された（1 回だけ）ときに呼ばれる。</summary>
        void OnEnemyDefeated(in EnemyDefeatedEvent defeated);
    }

    /// <summary>撃破イベントの配信チャネル（<see cref="EnemyStateChannel"/> と同系統。発火中の増減に安全）。</summary>
    public sealed class EnemyDefeatChannel
    {
        private readonly List<IEnemyDefeatListener> _listeners = new List<IEnemyDefeatListener>();

        /// <summary>購読者数。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>購読を追加する（重複登録はしない）。</summary>
        public void AddListener(IEnemyDefeatListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(IEnemyDefeatListener listener) => _listeners.Remove(listener);

        /// <summary>全購読者へ通知する（発火中の増減に備えスナップショット反復）。</summary>
        public void Publish(in EnemyDefeatedEvent defeated)
        {
            if (_listeners.Count == 0)
            {
                return;
            }

            var snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnEnemyDefeated(defeated);
            }
        }
    }
}
