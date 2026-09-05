using System;
using System.Collections.Generic;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間状態の純粋な保持・遷移機（P4-01）。優先度（<see cref="CompanionStatePriority"/>）に基づき被弾由来の
    /// Stagger／Down を割り込み適用し、AI・指示・イベント由来の任意遷移も扱う。不正遷移は黙らせず、Actor ID・旧状態・
    /// 新状態・理由を 1 回だけ記録する（同一署名は再記録しない）。Unity 非依存でロガーは注入可能（EditMode 再現）。
    ///
    /// 敵の状態機（<c>EnemyStateMachine</c>）と同じ構造を採るが、仲間固有の 2 点を加える：
    /// <list type="number">
    /// <item><description>Down からの離脱は復帰（<see cref="CompanionStateChangeReason.Recovered"/>）・再配置
    /// （<see cref="CompanionStateChangeReason.Spawned"/>）・退場（<see cref="CompanionStateChangeReason.Left"/>）だけを許す。
    /// 仲間は撃破されても復帰するため、敵と違い Down は終端ではない。</description></item>
    /// <item><description>退場（<see cref="CompanionState.Away"/>）は理由が <see cref="CompanionStateChangeReason.Left"/> であれば
    /// どの状態からでも成立する。Scene 離脱・交代・イベント退場で残留（購読・対象参照・判定）を作らないため。</description></item>
    /// </list>
    /// </summary>
    public sealed class CompanionStateMachine
    {
        private readonly int _actorId;
        private readonly Action<CompanionStateChanged> _onChanged;
        private readonly Action<string> _illegalLogger;
        private readonly HashSet<int> _loggedIllegal = new HashSet<int>();

        /// <summary>現在状態。</summary>
        public CompanionState Current { get; private set; }

        /// <summary>直近の遷移理由。</summary>
        public CompanionStateChangeReason LastReason { get; private set; }

        /// <summary>これまでに記録した不正遷移の件数（テスト・診断用）。</summary>
        public int IllegalTransitionCount { get; private set; }

        /// <param name="actorId">Actor 同定 ID。</param>
        /// <param name="initial">初期状態（未加入から始めるなら <see cref="CompanionState.Away"/>）。</param>
        /// <param name="onChanged">遷移確定時の通知（型付き）。</param>
        /// <param name="illegalLogger">不正遷移の記録先（開発 Build のみ接続）。</param>
        public CompanionStateMachine(
            int actorId,
            CompanionState initial = CompanionState.Idle,
            Action<CompanionStateChanged> onChanged = null,
            Action<string> illegalLogger = null)
        {
            _actorId = actorId;
            _onChanged = onChanged;
            _illegalLogger = illegalLogger;
            Current = initial;
            LastReason = CompanionStateChangeReason.Spawned;
        }

        /// <summary>初期化・再配置。優先度・不正判定を経ずに状態を設定する（Reset／加入 用）。</summary>
        public void Reset(CompanionState state = CompanionState.Idle)
        {
            Apply(state, CompanionStateChangeReason.Spawned);
        }

        /// <summary>
        /// 被弾由来の強制状態（Stagger／Down）を割り込み適用する。優先度が現在以上のときのみ適用し、
        /// 下位（例：Down 中の Stagger）は無視する（不正ではない）。退場中は被弾しないため適用しない。適用したら true。
        /// </summary>
        public bool ForceHitState(CompanionState hitState, CompanionStateChangeReason reason)
        {
            if (!CompanionStatePriority.IsForcedByHit(hitState))
            {
                return false;
            }

            if (Current == CompanionState.Away)
            {
                return false; // 退場中は場に居ないため被弾状態を持たない。
            }

            if (CompanionStatePriority.Rank(hitState) < CompanionStatePriority.Rank(Current))
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
        /// 任意（AI・指示・イベント由来）の遷移を試みる。同一状態は no-op。
        /// 退場（理由 <see cref="CompanionStateChangeReason.Left"/>）はどの状態からでも成立する。
        /// Down からの離脱は Recovered／Spawned／Left のみ、Stagger からの離脱は Recovered／Defeated／ForcedByEvent／Left のみ許す。
        /// 不正時は 1 回記録して false を返す（黙って無視しない）。
        /// </summary>
        public bool TryTransition(CompanionState next, CompanionStateChangeReason reason)
        {
            if (next == Current)
            {
                return false;
            }

            // 退場は最優先で常に成立させる（残留を作らないため）。
            if (next == CompanionState.Away && reason == CompanionStateChangeReason.Left)
            {
                Apply(next, reason);
                return true;
            }

            if (Current == CompanionState.Down
                && reason != CompanionStateChangeReason.Recovered
                && reason != CompanionStateChangeReason.Spawned
                && reason != CompanionStateChangeReason.Left)
            {
                RecordIllegal(next, reason);
                return false;
            }

            if (Current == CompanionState.Stagger
                && !CompanionStatePriority.IsForcedByHit(next)
                && reason != CompanionStateChangeReason.Recovered
                && reason != CompanionStateChangeReason.Defeated
                && reason != CompanionStateChangeReason.ForcedByEvent
                && reason != CompanionStateChangeReason.Left)
            {
                RecordIllegal(next, reason);
                return false;
            }

            Apply(next, reason);
            return true;
        }

        private void Apply(CompanionState next, CompanionStateChangeReason reason)
        {
            CompanionState previous = Current;
            Current = next;
            LastReason = reason;
            _onChanged?.Invoke(new CompanionStateChanged(_actorId, previous, next, reason));
        }

        private void RecordIllegal(CompanionState attempted, CompanionStateChangeReason reason)
        {
            int signature = ((int)Current << 8) ^ (int)attempted;
            if (!_loggedIllegal.Add(signature))
            {
                return; // 同一署名は 1 回だけ記録する。
            }

            IllegalTransitionCount++;
            _illegalLogger?.Invoke(
                "CompanionStateMachine illegal transition: actor=" + _actorId
                + " from=" + Current + " to=" + attempted + " reason=" + reason + ".");
        }
    }
}
