using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Combat.Guardian
{
    /// <summary>
    /// 肩代わり成立の型付き通知（P4-01）。守護対象（主人公）の <see cref="HitResultChannel"/> には
    /// <c>Damage</c>／<c>Guard</c>／<c>Evade</c> などの代用結果を流さず（既存種別へ意味の異なる結果を混ぜない）、
    /// 専用のこのイベントで「誰が誰の被弾を肩代わりしたか」を伝える。
    ///
    /// Presentation はこれを購読して <c>GuardianTransfer</c> の VFX／SE のみを再生する。ヒットストップは持たせない
    /// （肩代わり時の HitStop は守護者側の通常 Damage 由来の 1 回だけ）。
    /// </summary>
    public readonly struct GuardianTransferEvent
    {
        /// <summary>肩代わりした命中の同一性（元の命中と同じ値を保つ）。</summary>
        public HitId HitId { get; }

        /// <summary>攻撃者（不明なら null）。</summary>
        public ICombatActor Attacker { get; }

        /// <summary>守られた側（主人公）。</summary>
        public IDamageable Protected { get; }

        /// <summary>肩代わりした守護者。</summary>
        public IDamageable Guardian { get; }

        /// <summary>守護者が被弾した位置（World。演出の表示位置）。</summary>
        public Vector3 HitPoint { get; }

        public GuardianTransferEvent(HitId hitId, ICombatActor attacker, IDamageable protectedTarget,
            IDamageable guardian, Vector3 hitPoint)
        {
            HitId = hitId;
            Attacker = attacker;
            Protected = protectedTarget;
            Guardian = guardian;
            HitPoint = hitPoint;
        }
    }

    /// <summary>肩代わり通知の受信契約（Presentation・Debug HUD が購読）。</summary>
    public interface IGuardianTransferListener
    {
        /// <summary>肩代わりが成立したときに呼ばれる（1 回の肩代わりにつき 1 回）。</summary>
        void OnGuardianTransfer(in GuardianTransferEvent transfer);
    }

    /// <summary>
    /// 肩代わり通知の配信チャネル（<see cref="HitResultChannel"/> と同系統。発火中の購読増減に安全）。
    /// static な万能マネージャは作らず、守護対象がインスタンスとして所有する。
    /// </summary>
    public sealed class GuardianTransferChannel
    {
        private readonly List<IGuardianTransferListener> _listeners = new List<IGuardianTransferListener>();

        /// <summary>購読者数（診断・テスト用）。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>購読を追加する（null・重複は無視）。</summary>
        public void AddListener(IGuardianTransferListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(IGuardianTransferListener listener)
        {
            if (listener != null)
            {
                _listeners.Remove(listener);
            }
        }

        /// <summary>全購読者へ配信する（発火中の増減に備えスナップショット反復）。</summary>
        public void Publish(in GuardianTransferEvent transfer)
        {
            if (_listeners.Count == 0)
            {
                return;
            }

            IGuardianTransferListener[] snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnGuardianTransfer(transfer);
            }
        }
    }
}
