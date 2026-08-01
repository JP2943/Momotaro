using System.Collections.Generic;
using Momotaro.Data.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>攻撃予兆の段階（Phase3 §6.3）。表示側が予兆開始・発射・終了・中断を型で扱えるようにする。</summary>
    public enum EnemyTelegraphPhase
    {
        /// <summary>予兆開始（Prepare 入り）。</summary>
        Begin = 0,

        /// <summary>発射／判定発生（Active 入り）。</summary>
        Fire = 1,

        /// <summary>終了（Recovery 明け）。</summary>
        End = 2,

        /// <summary>中断（Stagger／Stunned／Down／Disable）。</summary>
        Cancel = 3,
    }

    /// <summary>
    /// 攻撃予兆の型付きイベント（Phase3 §6.3）。表示（仮 VFX/SE・予兆図形）が購読する。Gameplay 時間・判定の正本にしない。
    /// 色だけに依存せず種別（<see cref="Kind"/>）と段階・時間で識別できるよう情報を持つ。
    /// </summary>
    public readonly struct EnemyTelegraphEvent
    {
        public int ActorId { get; }
        public EnemyTelegraphPhase Phase { get; }
        public AttackTelegraph Kind { get; }
        public Vector3 Position { get; }
        public Vector3 AimDirection { get; }
        public float PrepareSeconds { get; }

        public EnemyTelegraphEvent(int actorId, EnemyTelegraphPhase phase, AttackTelegraph kind, Vector3 position,
            Vector3 aimDirection, float prepareSeconds)
        {
            ActorId = actorId;
            Phase = phase;
            Kind = kind;
            Position = position;
            AimDirection = aimDirection;
            PrepareSeconds = prepareSeconds;
        }
    }

    /// <summary>攻撃予兆の受信契約。</summary>
    public interface IEnemyTelegraphListener
    {
        void OnTelegraph(in EnemyTelegraphEvent telegraph);
    }

    /// <summary>攻撃予兆の配信チャネル（HitResultChannel と同系統。発火中の増減に安全）。</summary>
    public sealed class EnemyTelegraphChannel
    {
        private readonly List<IEnemyTelegraphListener> _listeners = new List<IEnemyTelegraphListener>();

        public int ListenerCount => _listeners.Count;

        public void AddListener(IEnemyTelegraphListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        public void RemoveListener(IEnemyTelegraphListener listener) => _listeners.Remove(listener);

        public void Publish(in EnemyTelegraphEvent telegraph)
        {
            int count = _listeners.Count;
            if (count == 0)
            {
                return;
            }

            var snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnTelegraph(telegraph);
            }
        }
    }
}
