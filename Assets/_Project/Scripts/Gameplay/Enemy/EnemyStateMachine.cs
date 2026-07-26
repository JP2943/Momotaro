using System;
using System.Collections.Generic;

namespace Momotaro.Gameplay.Enemy
{
    /// <summary>
    /// 敵状態の純粋な保持・遷移機（Phase3 §2.4）。優先度（<see cref="EnemyStatePriority"/>）に基づき被弾由来の
    /// Stagger／Stunned／Down を割り込み適用し、任意（AI 由来）の遷移も扱う。不正遷移は黙らせず、Actor ID・旧状態・
    /// 新状態・理由を 1 回だけ記録する（同一署名は再記録しない）。Unity 非依存でロガーは注入可能（EditMode 再現）。
    /// </summary>
    public sealed class EnemyStateMachine
    {
        private readonly int _actorId;
        private readonly Action<EnemyStateChanged> _onChanged;
        private readonly Action<string> _illegalLogger;
        private readonly HashSet<int> _loggedIllegal = new HashSet<int>();

        /// <summary>現在状態。</summary>
        public EnemyState Current { get; private set; }

        /// <summary>直近の遷移理由。</summary>
        public EnemyStateChangeReason LastReason { get; private set; }

        /// <summary>これまでに記録した不正遷移の件数（テスト・診断用）。</summary>
        public int IllegalTransitionCount { get; private set; }

        /// <param name="actorId">Actor 同定 ID。</param>
        /// <param name="initial">初期状態。</param>
        /// <param name="onChanged">遷移確定時の通知（型付き）。</param>
        /// <param name="illegalLogger">不正遷移の記録先（開発 Build のみ接続）。</param>
        public EnemyStateMachine(
            int actorId,
            EnemyState initial = EnemyState.Idle,
            Action<EnemyStateChanged> onChanged = null,
            Action<string> illegalLogger = null)
        {
            _actorId = actorId;
            _onChanged = onChanged;
            _illegalLogger = illegalLogger;
            Current = initial;
            LastReason = EnemyStateChangeReason.Spawned;
        }

        /// <summary>初期化・復活。優先度・不正判定を経ずに状態を設定する（Reset／Revive 用）。</summary>
        public void Reset(EnemyState state = EnemyState.Idle)
        {
            Apply(state, EnemyStateChangeReason.Spawned);
        }

        /// <summary>
        /// 被弾由来の強制状態（Stagger／Stunned／Down）を割り込み適用する。優先度が現在以上のときのみ適用し、
        /// 下位（例：Stunned 中の Stagger）は無視する（不正ではない）。適用したら true。
        /// </summary>
        public bool ForceHitState(EnemyState hitState, EnemyStateChangeReason reason)
        {
            if (!EnemyStatePriority.IsForcedByHit(hitState))
            {
                return false;
            }

            // 既に同等以上（Down 中の Stunned/Stagger、Stunned 中の Stagger 等）はダウングレードしない。
            if (EnemyStatePriority.Rank(hitState) < EnemyStatePriority.Rank(Current))
            {
                return false;
            }

            if (hitState == Current)
            {
                return false;
            }

            Apply(hitState, reason);
            return true;
        }

        /// <summary>
        /// 任意（AI 由来）の遷移を試みる。同一状態は no-op。Down からの離脱は復活理由（Spawned）以外は不正。
        /// Stagger／Stunned からの任意離脱は復帰理由（Recovered/Defeated/ForcedByEvent）以外は不正。
        /// 不正時は 1 回記録して false を返す（黙って無視しない）。
        /// </summary>
        public bool TryTransition(EnemyState next, EnemyStateChangeReason reason)
        {
            if (next == Current)
            {
                return false;
            }

            if (Current == EnemyState.Down && reason != EnemyStateChangeReason.Spawned)
            {
                RecordIllegal(next, reason);
                return false;
            }

            if (EnemyStatePriority.IsForcedByHit(Current) && Current != EnemyState.Down
                && !EnemyStatePriority.IsForcedByHit(next)
                && reason != EnemyStateChangeReason.Recovered
                && reason != EnemyStateChangeReason.Defeated
                && reason != EnemyStateChangeReason.ForcedByEvent)
            {
                RecordIllegal(next, reason);
                return false;
            }

            Apply(next, reason);
            return true;
        }

        private void Apply(EnemyState next, EnemyStateChangeReason reason)
        {
            EnemyState previous = Current;
            Current = next;
            LastReason = reason;
            _onChanged?.Invoke(new EnemyStateChanged(_actorId, previous, next, reason));
        }

        private void RecordIllegal(EnemyState attempted, EnemyStateChangeReason reason)
        {
            int signature = ((int)Current << 8) ^ (int)attempted;
            if (!_loggedIllegal.Add(signature))
            {
                return; // 同一署名は 1 回だけ記録する。
            }

            IllegalTransitionCount++;
            _illegalLogger?.Invoke(
                "EnemyStateMachine illegal transition: actor=" + _actorId
                + " from=" + Current + " to=" + attempted + " reason=" + reason + ".");
        }
    }
}
