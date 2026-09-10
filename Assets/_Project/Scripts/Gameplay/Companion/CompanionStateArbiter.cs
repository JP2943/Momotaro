using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の状態要求を受け取る<b>唯一の窓口</b>（P4-FIX F02b）。
    ///
    /// これまでは追従・戦闘・防御・守護・被弾の 5 か所がそれぞれ <see cref="CompanionActor.RequestState"/> と
    /// <see cref="CompanionActor.ForceHitState"/> を直接呼んでいた。状態機（<see cref="CompanionStateMachine"/>）が
    /// 不正遷移を弾いてはいるが、<b>「誰の行動か」を持っていない</b>ため次が止められない。
    ///
    /// <list type="number">
    /// <item><description><b>古い終了通知が新しい状態を壊す。</b>攻撃中にひるんで中断されたあと、
    /// 遅れて届いた「攻撃終了」が Stagger を Chase へ書き換えてしまう。状態機から見れば
    /// Stagger→Chase は理由 <c>AttackFinished</c> の不正遷移として弾かれるが、
    /// Down→…→復帰後のように現在状態が変わっていれば通ってしまう。</description></item>
    /// <item><description><b>弱い行動が強い行動を奪う。</b>順位表は「状態」の強さしか見ないので、
    /// 同じ強さの状態へ別の持ち主が入れ替わることを止められない。</description></item>
    /// </list>
    ///
    /// そこで行動に<b>引換券</b>（<see cref="CompanionActionHandle"/> ＝ 持ち主 × 実行 ID）を持たせ、
    /// 「段を進める」「正常終了する」はその券が今の行動と一致するときだけ受理する。
    ///
    /// 受け付ける操作は 4 種類に分ける。混ぜると、どれをどこまで許すかが表現できなくなる。
    /// <list type="bullet">
    /// <item><description><b>開始</b>（<see cref="TryBegin"/>）… 新しい行動を始める。</description></item>
    /// <item><description><b>割込み</b>（<see cref="TryInterrupt"/>）… 進行中の行動を止めて別の行動へ移る。</description></item>
    /// <item><description><b>正常終了</b>（<see cref="TryComplete"/>）… 券が一致するときだけ。
    /// <b>下位状態への復帰を順位で拒否しない</b>（AttackActive→Recovery、Stagger 明け→Follow は正常）。</description></item>
    /// <item><description><b>被弾由来の強制</b>（<see cref="ForceHit"/>／<see cref="ForceRecover"/>）… 同期的に確定し、
    /// <b>進行中の券をその場で無効にする</b>。次の Update を待たない。</description></item>
    /// </list>
    ///
    /// 所有権（誰が強いか）と<b>状態ごとの可否</b>（v1.0 §8.3 の表＝<see cref="CompanionActionRules"/>）は
    /// 別の話なので、両方を見る入口を <see cref="TryStartAction"/> に用意した（F02c）。所有権だけでは
    /// 「振っている最中に自動ガードを始めない」を表現できない（防御のほうが強い持ち主だから通ってしまう）。
    /// 逆に表だけでも「弱い持ち主が強い持ち主から奪う」を止められない。両方要る。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionStateArbiter : MonoBehaviour
    {
        [Tooltip("状態の保持先（未設定なら同じ GameObject から自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        private int _runId;

        /// <summary>いま行動を持っている側（テスト・診断用）。</summary>
        public CompanionActionOwner CurrentOwner { get; private set; } = CompanionActionOwner.None;

        /// <summary>いまの実行 ID（テスト・診断用）。</summary>
        public int CurrentRunId => _runId;

        /// <summary>受理しなかった要求の数（テスト・診断用。古い終了通知の検出に使う）。</summary>
        public int RejectedCount { get; private set; }

        /// <summary>許可表（<see cref="CompanionActionRules"/>）に拒否された回数（テスト・診断用）。</summary>
        public int DeniedByRuleCount { get; private set; }

        /// <summary>直近に <see cref="TryStartAction"/> が表から得た答え（テスト・診断用）。</summary>
        public CompanionActionVerdict LastVerdict { get; private set; } = CompanionActionVerdict.Allowed;

        /// <summary>Actor を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor)
        {
            if (actor != null)
            {
                _actor = actor;
            }
        }

        /// <summary>この券が今の行動を指しているか。</summary>
        public bool IsCurrent(in CompanionActionHandle handle)
        {
            return handle.IsValid
                && handle.Owner == CurrentOwner
                && handle.RunId == _runId;
        }

        /// <summary>
        /// 新しい行動を開始する。強い持ち主が行動中なら受理しない。
        /// 受理したら実行 ID が新しくなり、<b>それまでに配った券はすべて無効</b>になる。
        /// </summary>
        public bool TryBegin(
            CompanionActionOwner owner, CompanionState state, CompanionStateChangeReason reason,
            out CompanionActionHandle handle)
        {
            return TryTake(owner, state, reason, out handle);
        }

        /// <summary>
        /// <b>行動として</b>始める（F02c）。<see cref="CompanionActionRules"/> に「この状態でこの行動を
        /// 始めてよいか」を尋ね、その答えに従って開始・割込み・拒否へ振り分ける。
        ///
        /// 駆動側が状態を条件式で見ないための入口。条件式を各駆動へ散らすと必ず食い違う
        /// （実際「防御中は攻撃しない」はあるのに「攻撃中は防御しない」がどこにも無かった）。
        /// </summary>
        /// <param name="owner">誰の行動か（所有権の強さ）。</param>
        /// <param name="kind">何を始めるか（表の行）。</param>
        /// <param name="state">始まった結果として入る状態。</param>
        /// <param name="reason">遷移理由。</param>
        /// <param name="handle">受理したときの引換券。</param>
        public bool TryStartAction(
            CompanionActionOwner owner, CompanionActionKind kind, CompanionState state,
            CompanionStateChangeReason reason, out CompanionActionHandle handle)
        {
            handle = default;

            EnsureActor();
            if (_actor == null)
            {
                return false;
            }

            LastVerdict = CompanionActionRules.Evaluate(kind, _actor.State);

            switch (LastVerdict)
            {
                case CompanionActionVerdict.Allowed:
                    return TryBegin(owner, state, reason, out handle);

                case CompanionActionVerdict.Interrupt:
                    return TryInterrupt(owner, state, reason, out handle);

                default:
                    // 表が禁じた。所有権も状態も動かさない。
                    DeniedByRuleCount++;
                    RejectedCount++;
                    return false;
            }
        }

        /// <summary>
        /// 進行中の行動を止めて別の行動へ移る（守護の成立・イベントなど）。
        ///
        /// 所有権の規則は <see cref="TryBegin"/> と同じ（強い持ち主からは奪えない）。
        /// 分けてあるのは、<see cref="TryStartAction"/> が表の答え（Allowed／Interrupt）で呼び分けるため。
        /// </summary>
        public bool TryInterrupt(
            CompanionActionOwner owner, CompanionState state, CompanionStateChangeReason reason,
            out CompanionActionHandle handle)
        {
            return TryTake(owner, state, reason, out handle);
        }

        /// <summary>
        /// 同じ行動の中で段を進める（AttackPrepare→Active→Recovery）。券が一致しなければ受理しない。
        /// 実行 ID は据え置く（同じ行動のままなので、配った券は有効なまま）。
        /// </summary>
        public bool TryAdvance(
            in CompanionActionHandle handle, CompanionState state, CompanionStateChangeReason reason)
        {
            if (!IsCurrent(handle))
            {
                RejectedCount++;
                return false;
            }

            EnsureActor();
            return _actor != null && _actor.RequestState(state, reason);
        }

        /// <summary>
        /// 行動を正常に終えて次の状態へ移る。券が一致しなければ受理しない（＝古い終了通知を無視する）。
        /// 受理した場合は所有権を手放す。
        ///
        /// <b>順位では拒否しない。</b>正常終了は下位状態へ戻るのが普通で（攻撃終了→Chase、
        /// ひるみ明け→Follow）、ここで順位を見ると「終わったのに終われない」状態になる。
        /// </summary>
        public bool TryComplete(
            in CompanionActionHandle handle, CompanionState next, CompanionStateChangeReason reason)
        {
            if (!IsCurrent(handle))
            {
                RejectedCount++;
                return false;
            }

            EnsureActor();
            bool applied = _actor != null && _actor.RequestState(next, reason);

            // 状態遷移が通らなくても、行動そのものは終わっている。所有権は必ず返す
            //（返さないと、以後この持ち主しか行動できなくなる）。
            CurrentOwner = CompanionActionOwner.None;
            return applied;
        }

        /// <summary>行動を打ち切って所有権だけ返す（状態は変えない）。券が一致するときだけ効く。</summary>
        public bool Release(in CompanionActionHandle handle)
        {
            if (!IsCurrent(handle))
            {
                return false;
            }

            CurrentOwner = CompanionActionOwner.None;
            return true;
        }

        /// <summary>
        /// 被弾由来の強制状態（Stagger／Down）を<b>同期的に</b>確定する。
        /// 所有権に関わらず通り、進行中の券をその場で無効にする。次の Update を待たない。
        /// </summary>
        public bool ForceHit(CompanionState state, CompanionStateChangeReason reason)
        {
            EnsureActor();
            if (_actor == null || !_actor.ForceHitState(state, reason))
            {
                return false;
            }

            InvalidateOutstanding();
            return true;
        }

        /// <summary>
        /// 被弾からの復帰（ひるみ明け・ダウンからの復帰）を確定する。
        /// これも進行中の券を無効にする（倒れているあいだに残っていた行動を復活させない）。
        /// </summary>
        public bool ForceRecover(CompanionState state, CompanionStateChangeReason reason)
        {
            EnsureActor();
            if (_actor == null || !_actor.RequestState(state, reason))
            {
                return false;
            }

            InvalidateOutstanding();
            return true;
        }

        /// <summary>所有権と実行 ID を初期化する（加入・Retry・Scene 再構築）。</summary>
        public void ResetArbitration()
        {
            InvalidateOutstanding();
            RejectedCount = 0;
            DeniedByRuleCount = 0;
            LastVerdict = CompanionActionVerdict.Allowed;
        }

        // ---- 内部 ----

        private bool TryTake(
            CompanionActionOwner owner, CompanionState state, CompanionStateChangeReason reason,
            out CompanionActionHandle handle)
        {
            handle = default;

            if (owner == CompanionActionOwner.None)
            {
                return false;
            }

            // 強い持ち主が行動中なら割り込まない。
            if (CurrentOwner != CompanionActionOwner.None && owner < CurrentOwner)
            {
                RejectedCount++;
                return false;
            }

            EnsureActor();
            if (_actor == null)
            {
                return false;
            }

            if (!_actor.RequestState(state, reason))
            {
                // 状態機が拒否した（不正遷移・同一状態）。所有権は動かさない。
                RejectedCount++;
                return false;
            }

            _runId++;
            CurrentOwner = owner;
            handle = new CompanionActionHandle(owner, _runId);
            return true;
        }

        /// <summary>配ってある券をすべて無効にし、所有権を空にする。</summary>
        private void InvalidateOutstanding()
        {
            _runId++;
            CurrentOwner = CompanionActionOwner.None;
        }

        private void EnsureActor()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で所有権を残さない（§2.3 後始末）。
            InvalidateOutstanding();
        }
    }
}
