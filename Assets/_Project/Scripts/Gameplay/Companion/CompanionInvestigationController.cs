using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 探索行動の駆動（P4-07A）。気になる地点（<see cref="IInvestigationPoint"/>）まで行って調べ、終わったら追従へ戻る。
    ///
    /// <b>これは「暇なときの行動」である。</b>戦闘・防御・守護・被弾のどれが起きても譲る。
    /// 譲り方は条件式を並べるのではなく、F02a〜F02c で作った 2 つの調停役に任せる。
    /// <list type="bullet">
    /// <item><description>行動：<see cref="CompanionStateArbiter.TryStartAction"/> に
    /// <see cref="CompanionActionKind.Investigate"/> として尋ねる。許可表（レビュー §5）が
    /// 「調査中でも戦闘・防御・守護は割り込める」と決めているので、割り込まれたら券が無効になる。
    /// 券が今の行動でなくなったら、それが「誰かに取られた」の合図。</description></item>
    /// <item><description>移動：<see cref="CompanionMovementOwner.Investigate"/> として出す。
    /// 追従より強く、戦闘より弱い。追従より強くしないと、隊列から離れた瞬間に引き戻されて地点へ着けない。</description></item>
    /// </list>
    ///
    /// <b>紐（Leash）を持つ。</b>主人公から <see cref="Data.Characters.CompanionData.InvestigateLeashDistance"/> を
    /// 超える地点へは行かない。行かせると、追従側のワープ距離を超えて隊列へ瞬間移動させられ、
    /// 「調べに行ったのに戻ってくる」という壊れ方をする。Data 検証でも紐 &lt; ワープ距離を要求している。
    ///
    /// 戦闘中かどうかは自分で数えない。<see cref="CompanionActivity.CanInvestigate"/>（活動 Context）が
    /// Wave の幕間や開始待ち Encounter も含めて「いま探索してよいか」を持っている。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionInvestigationController : MonoBehaviour
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("状態要求の唯一の窓口（未設定なら自動取得）。Actor へは直接書かない。")]
        [SerializeField] private CompanionStateArbiter _states;

        [Tooltip("移動と向きの書き手（未設定なら自動取得）。Motor へは直接書かない。")]
        [SerializeField] private CompanionMovementArbiter _arbiter;

        [Tooltip("紐の起点（主人公）を知るための追従（未設定なら自動取得）。")]
        [SerializeField] private CompanionFollowController _follow;

        private CompanionActionHandle _action;
        private IInvestigationPoint _target;
        private float _elapsed;      // 到着後に調べている秒数。
        private float _cooldown;     // 次を探し始めるまでの秒数。
        private bool _arrived;

        /// <summary>いま調べに行っている地点（無ければ null。テスト・診断用）。</summary>
        public IInvestigationPoint CurrentPoint => _target;

        /// <summary>調査中か（移動中も含む。テスト・診断用）。</summary>
        public bool IsInvestigating => _action.IsValid && _target != null;

        /// <summary>地点に着いて調べ始めているか（テスト・診断用）。</summary>
        public bool IsExamining => IsInvestigating && _arrived;

        /// <summary>調べている秒数（テスト・診断用）。</summary>
        public float ExamineElapsed => _elapsed;

        /// <summary>次を探し始めるまでの残り秒（テスト・診断用）。</summary>
        public float CooldownRemaining => _cooldown;

        /// <summary>これまでに調べ終えた回数（テスト・診断用）。</summary>
        public int CompletedCount { get; private set; }

        /// <summary>これまでに中断された回数（テスト・診断用）。</summary>
        public int InterruptedCount { get; private set; }

        /// <summary>Actor・追従を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor, CompanionFollowController follow = null)
        {
            if (actor != null)
            {
                _actor = actor;
            }

            if (follow != null)
            {
                _follow = follow;
            }
        }

        /// <summary>
        /// 探索を 1 Tick 進める（Update から呼ばれるが、テストは決定的に直接呼べる）。
        ///
        /// <b>停止の判断はここに置く。</b>Update 側だけで返しても、別の駆動から直接呼ばれれば素通りする
        /// （F05 で追従・戦闘・防御に同じ直し方をした）。
        /// </summary>
        public void TickInvestigation(float deltaTime)
        {
            ResolveComponents();
            if (_actor == null)
            {
                return;
            }

            CompanionActivity activity = CompanionActivityProvider.Activity;

            if (!activity.ClocksRun)
            {
                // Pause は凍結（時計も進めない）。会話・イベントは進行中の調査を捨てる。
                if (activity.DiscardOngoing)
                {
                    Abandon();
                }

                return;
            }

            float dt = deltaTime < 0f ? 0f : deltaTime;

            // 誰かに行動を取られていたら、それが中断の合図。券が今の行動かどうかだけで判断する
            //（「戦闘中か」「防御中か」を自分で数え直すと、必ずどこかで食い違う）。
            if (_action.IsValid && (_states == null || !_states.IsCurrent(_action)))
            {
                InterruptedCount++;
                ClearAction(releaseOwnership: false); // 券は既に無効。所有権は取った側が持っている。
                return;
            }

            if (!activity.CanInvestigate)
            {
                // 戦闘が始まった（Wave の幕間・開始待ちも含む）。調査は中断して戻る。
                if (_action.IsValid)
                {
                    InterruptedCount++;
                    Abandon();
                }

                return;
            }

            if (_cooldown > 0f)
            {
                _cooldown = Mathf.Max(0f, _cooldown - dt);
            }

            if (_action.IsValid)
            {
                Advance(dt);
                return;
            }

            TryBeginInvestigation();
        }

        /// <summary>進行中の調査を打ち切る（状態は追従へ戻す）。</summary>
        public void Abandon()
        {
            if (!_action.IsValid)
            {
                return;
            }

            if (_states != null && _states.IsCurrent(_action))
            {
                // 打ち切りも「行動が終わった」ことに変わりはない。順位で拒否されない正常終了で返す。
                _states.TryComplete(_action, CompanionState.Follow, CompanionStateChangeReason.InvestigationFinished);
            }

            ClearAction(releaseOwnership: true);
        }

        /// <summary>探索の状態を初期化する（加入・Retry）。</summary>
        public void ResetInvestigation()
        {
            ClearAction(releaseOwnership: true);
            _cooldown = 0f;
            CompletedCount = 0;
            InterruptedCount = 0;
        }

        // ---- 内部 ----

        private void TryBeginInvestigation()
        {
            Data.Characters.CompanionData data = _actor.Data;
            if (data == null || !data.CanInvestigate || _cooldown > 0f)
            {
                return;
            }

            Transform leader = _follow != null ? _follow.Leader : null;
            if (leader == null)
            {
                return; // 紐の起点が分からないうちは動かない（無制限に走らせない）。
            }

            if (!InvestigationPointRegistry.TryGetNearestAvailable(
                    _actor.WorldPosition, data.InvestigateRange,
                    leader.position, data.InvestigateLeashDistance, out IInvestigationPoint point))
            {
                return;
            }

            if (_states == null)
            {
                return; // 調停役が無い構成では探索を始めない（新機能なので移行期の保険は作らない）。
            }

            if (!_states.TryStartAction(
                    CompanionActionOwner.Investigate, CompanionActionKind.Investigate,
                    CompanionState.Investigate, CompanionStateChangeReason.InvestigationStarted, out _action))
            {
                return;
            }

            _target = point;
            _elapsed = 0f;
            _arrived = false;

            // 同じ Tick のうちに移動の所有権も取る。次のフレームまで空けると、
            // そのあいだに追従が移動を握り、探索が始まったのに隊列へ歩き出す 1 フレームができる。
            Advance(0f);
        }

        private void Advance(float deltaTime)
        {
            if (_target == null || !_target.IsAvailable)
            {
                // 別の誰かが先に調べた・地点が消えた。空振りとして戻る（中断ではない）。
                Abandon();
                return;
            }

            Data.Characters.CompanionData data = _actor.Data;
            Vector3 position = _target.Position;
            Vector3 self = _actor.WorldPosition;
            float stopRadius = data != null ? data.FollowStopDistance : 0.35f;

            if (!_arrived)
            {
                if (FormationSlot.HorizontalDistance(self, position) > stopRadius)
                {
                    float speed = data != null ? data.MoveSpeed : 4.5f;
                    Submit(CompanionMoveRequest.MoveFacing(position, speed, stopRadius, position - self));
                    return;
                }

                _arrived = true;
                _elapsed = 0f;
            }

            // 到着後は止まって地点の方を向いたまま調べる。
            Submit(CompanionMoveRequest.StopFacing(position - self));

            _elapsed += deltaTime;
            float required = data != null ? data.InvestigateSeconds : 0f;
            if (_elapsed < required)
            {
                return;
            }

            _target.OnInvestigated(_actor.ActorId);
            CompletedCount++;
            _cooldown = data != null ? data.InvestigateCooldownSeconds : 0f;

            if (_states != null && _states.IsCurrent(_action))
            {
                _states.TryComplete(_action, CompanionState.Follow, CompanionStateChangeReason.InvestigationFinished);
            }

            ClearAction(releaseOwnership: true);
        }

        private void Submit(in CompanionMoveRequest request)
        {
            _arbiter?.Submit(CompanionMovementOwner.Investigate, request);
        }

        private void ClearAction(bool releaseOwnership)
        {
            if (releaseOwnership)
            {
                _arbiter?.Release(CompanionMovementOwner.Investigate);
            }

            _action = default;
            _target = null;
            _elapsed = 0f;
            _arrived = false;
        }

        private void ResolveComponents()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_states == null)
            {
                _states = GetComponent<CompanionStateArbiter>();
            }

            if (_arbiter == null)
            {
                _arbiter = GetComponent<CompanionMovementArbiter>();
            }

            if (_follow == null)
            {
                _follow = GetComponent<CompanionFollowController>();
            }
        }

        private void Update()
        {
            TickInvestigation(Time.deltaTime);
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で行動と移動の所有権を残さない（§2.3 後始末）。
            // 状態は触らない（既に別の駆動が握っているかもしれない）。
            if (_action.IsValid)
            {
                _states?.Release(_action);
            }

            ClearAction(releaseOwnership: true);
            _cooldown = 0f;
        }
    }
}
