using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 追従の駆動（P4-02）。判断（<see cref="CompanionFollowModel"/>）・実行（<see cref="CompanionMotor"/>）・
    /// 状態（<see cref="CompanionActor"/>）を結線するだけの薄い層で、判断規則そのものは持たない。
    ///
    /// 主人公の論理前方は <see cref="ICombatActor.Forward"/> があればそれを使い、無ければ Transform の forward を使う
    /// （具象 <c>PlayerStateController</c> に依存しない）。追従対象が未設定・破棄済みのときは何もせず、例外も出さない。
    ///
    /// Down／Stagger／Away の間は追従を止め、判断もリセットする（復帰後に古い停滞時間や前回距離を引きずらない）。
    /// 停止は <see cref="Update"/> を待たず、状態遷移の通知（<see cref="CompanionStateChannel"/>）を購読して<b>その場で</b>行う。
    /// 物理ステップは Update とは独立に回るため、次の Update まで移動指示が残ると退場・ダウンの直後に数 cm 滑ってしまう。
    ///
    /// 戦闘中（<see cref="ICompanionEngagementSource.IsEngaged"/>）は移動を戦闘側へ譲り、本コンポーネントは
    /// <see cref="CompanionMotor"/> へ一切指示しない（P4-03）。両方が毎フレーム移動先を書くと、隊列位置と敵の間で震える。
    /// 誰を狙うか・どう攻撃するかは戦闘側の責務で、本コンポーネントは触らない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionFollowController : MonoBehaviour, ICompanionStateListener
    {
        [Tooltip("追従対象（主人公）。Scene 構築または Bind で注入する。未設定の間は何もしない。")]
        [SerializeField] private Transform _leader;

        [Tooltip("同一 GameObject 上の仲間 Actor（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("同一 GameObject 上の移動実行（未設定なら自動取得）。")]
        [SerializeField] private CompanionMotor _motor;

        [Tooltip("移動と向きの書き手（未設定なら自動取得）。追従は意図を出すだけで、Motor へは直接書かない。")]
        [SerializeField] private CompanionMovementArbiter _arbiter;

        [Tooltip("状態要求の唯一の窓口（未設定なら自動取得）。Actor へは直接書かない。")]
        [SerializeField] private CompanionStateArbiter _states;

        private readonly CompanionFollowModel _model = new CompanionFollowModel();
        private ICombatActor _leaderActor;
        private bool _leaderActorResolved;
        private CompanionActor _subscribedActor; // 状態通知の購読先（対称管理・重複購読防止）。
        private ICompanionEngagementSource _engagement; // 戦闘側（同一 GameObject。未装備なら null のまま）。

        /// <summary>判断モデル（テスト・Debug 用）。</summary>
        public CompanionFollowModel Model => _model;

        /// <summary>現在の追従対象。</summary>
        public Transform Leader => _leader;

        /// <summary>直近の判断（テスト・Debug 用）。</summary>
        public CompanionFollowDecision Decision => _model.Decision;

        /// <summary>戦闘側へ移動を譲っているか（テスト・診断用）。</summary>
        public bool IsYieldingToCombat => ResolveEngagement() != null && _engagement.IsEngaged;

        /// <summary>
        /// 追従より強い持ち主が移動を握っているか（探索・防御など。テスト・診断用）。
        /// 「誰が握っているか」は F02a の調停役が既に持っている。追従側で数え直さない。
        /// </summary>
        public bool IsYieldingToStrongerMovementOwner =>
            _arbiter != null && _arbiter.Owner > CompanionMovementOwner.Follow;

        /// <summary>追従対象・Actor・Motor を注入する（Scene 構築・テスト。null は無視して既存を保つ）。</summary>
        public void Bind(Transform leader, CompanionActor actor = null, CompanionMotor motor = null)
        {
            if (leader != null && !ReferenceEquals(_leader, leader))
            {
                _leader = leader;
                _leaderActor = null;
                _leaderActorResolved = false;
            }

            if (actor != null && !ReferenceEquals(_actor, actor))
            {
                _actor = actor;
                if (isActiveAndEnabled)
                {
                    SubscribeState(); // Actor を差し替えたら購読も張り替える。
                }
            }

            if (motor != null)
            {
                _motor = motor;
            }
        }

        private void OnEnable()
        {
            ResolveComponents();
            SubscribeState();
            _model.Reset(); // 有効化のたびに停滞時間・前回距離を引き継がない。
        }

        private void OnDisable()
        {
            UnsubscribeState();
            ForceStopMovement();
            ReleaseMovement();
            _model.Reset();
        }

        /// <inheritdoc />
        /// <remarks>
        /// 退場・ダウン・ひるみへ入った瞬間に移動を止める。Update を待つと、その間に回る物理ステップで移動指示が
        /// 生き残り、止まるべき場面で滑ってしまう（実測で約 15mm／1 フレーム）。
        /// </remarks>
        public void OnCompanionStateChanged(in CompanionStateChanged change)
        {
            if (!IsFollowSuspended(change.Current))
            {
                return;
            }

            ForceStopMovement();
            ReleaseMovement();
            _model.Reset();
        }

        /// <summary>この状態の間は追従しないか（退場・ダウン・ひるみ）。</summary>
        private static bool IsFollowSuspended(CompanionState state)
        {
            return state == CompanionState.Away || state == CompanionState.Down || state == CompanionState.Stagger;
        }

        private void SubscribeState()
        {
            if (ReferenceEquals(_subscribedActor, _actor))
            {
                return;
            }

            UnsubscribeState();
            _subscribedActor = _actor;
            _subscribedActor?.States.AddListener(this);
        }

        private void UnsubscribeState()
        {
            _subscribedActor?.States.RemoveListener(this);
            _subscribedActor = null;
        }

        private void Update()
        {
            TickFollow(Time.deltaTime);
        }

        /// <summary>
        /// 追従を 1 Tick 進める（Update から呼ばれるが、テストは決定的に直接呼べる）。
        ///
        /// <b>停止の判断はここに置く。</b>Update 側だけで返しても、別の駆動から直接呼ばれれば素通りしてしまう
        /// （P4-FIX F05。追従にはそもそもゲートが無く、Pause 中も歩き続けていた）。
        /// </summary>
        public void TickFollow(float deltaTime)
        {
            ResolveComponents();

            // 自動取得で Actor が後から解決された場合にも購読を張る（Bind 経由でない Scene 構成の保険）。
            if (!ReferenceEquals(_subscribedActor, _actor))
            {
                SubscribeState();
            }

            if (_actor == null || _motor == null || _leader == null)
            {
                return; // 未配線でも例外を出さずに何もしない。
            }

            CompanionActivity activity = CompanionActivityProvider.Activity;
            if (!activity.ClocksRun)
            {
                // Pause・会話・イベント中は歩かない。速度も残さない（timeScale に頼らない）。
                ForceStopMovement();

                if (activity.DiscardOngoing)
                {
                    _model.Reset(); // 復帰時に古い停滞時間・前回距離を引きずらない。
                }

                return;
            }

            // 退場・ダウン・ひるみ中は追従しない（状態遷移の瞬間は通知で停止済み。ここは継続中の保険）。
            if (IsFollowSuspended(_actor.State))
            {
                ForceStopMovement();
                ReleaseMovement();
                _model.Reset();
                return;
            }

            // 戦闘中は移動を戦闘側へ譲る（Motor へ触れない。停止も戦闘側が必要に応じて行う）。
            if (IsYieldingToCombat)
            {
                _model.Reset(); // 復帰時に古い停滞時間・前回距離を引きずらない。
                ReleaseMovement(); // 所有権を返す。Motor そのものには触れない（戦闘側が握る）。
                return;
            }

            // 追従より強い持ち主が移動を握っているなら、判断そのものを止める（P4-07A）。
            //
            // 意図を出しても調停役が捨ててくれるので「動いてしまう」ことは無いが、それだけでは足りない。
            // 判断は走り続けるので、探索で隊列から離れているあいだに<b>距離超過のワープが成立</b>し、
            // 調べに行った先から隊列へ引き戻される。譲るときは判断ごと止める。
            if (IsYieldingToStrongerMovementOwner)
            {
                _model.Reset();
                return;
            }

            // Data 由来の移動値。Motor へは調停役が渡すので、ここでは意図に載せるだけ。
            float speed = _actor.Data != null ? _actor.Data.MoveSpeed : 4.5f;
            float stopRadius = _actor.Data != null ? _actor.Data.FollowStopDistance : 0.35f;

            var input = new CompanionFollowInput(
                _leader.position, ResolveLeaderForward(), transform.position, _actor.SlotIndex);
            CompanionFollowSettings settings = CompanionFollowSettings.From(_actor.Data);

            switch (_model.Tick(input, settings, deltaTime))
            {
                case CompanionFollowDecision.Move:
                    EnterFollow();
                    SubmitMove(CompanionMoveRequest.MoveFacing(
                        _model.SlotPosition, speed, stopRadius, _model.SlotPosition - transform.position));
                    break;

                case CompanionFollowDecision.Warp:
                    BeginFollowAction(CompanionState.Warp, CompanionStateChangeReason.Warped);
                    SubmitMove(CompanionMoveRequest.Warp(_model.SlotPosition));
                    break;

                default: // Hold
                    EnterFollow();
                    // 到着後は主人公と同じ向きを向く。
                    SubmitMove(CompanionMoveRequest.StopFacing(ResolveLeaderForward()));
                    break;
            }
        }

        /// <summary>追従中の状態へ入れる（既に Follow なら何もしない。Warp・戦闘からの復帰もここを通る）。</summary>
        private void EnterFollow()
        {
            if (_actor.State != CompanionState.Follow)
            {
                BeginFollowAction(CompanionState.Follow, CompanionStateChangeReason.FollowResumed);
            }
        }

        /// <summary>追従としての状態要求を出す（受理されるかは調停役が決める。P4-FIX F02b）。</summary>
        private void BeginFollowAction(CompanionState state, CompanionStateChangeReason reason)
        {
            ResolveComponents();
            _states?.TryBegin(CompanionActionOwner.Follow, state, reason, out _);
        }

        private Vector3 ResolveLeaderForward()
        {
            if (!_leaderActorResolved)
            {
                _leaderActor = _leader != null ? _leader.GetComponentInParent<ICombatActor>() : null;
                _leaderActorResolved = true;
            }

            return _leaderActor != null ? _leaderActor.Forward : _leader.forward;
        }

        /// <summary>
        /// 戦闘側（同一 GameObject の <see cref="ICompanionEngagementSource"/>）を解決する。未装備の構成
        /// （追従だけの仲間・テスト）では null のままで、その場合は従来どおり常に追従する。
        /// interface 参照は Unity の null 演算子が効かないため、破棄済み Object を明示的に捨てて取り直す。
        /// </summary>
        private ICompanionEngagementSource ResolveEngagement()
        {
            if (_engagement is Object destroyed && destroyed == null)
            {
                _engagement = null;
            }

            if (_engagement == null)
            {
                _engagement = GetComponent<ICompanionEngagementSource>();
            }

            return _engagement;
        }

        private void ResolveComponents()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_motor == null)
            {
                _motor = GetComponent<CompanionMotor>();
            }

            if (_arbiter == null)
            {
                _arbiter = GetComponent<CompanionMovementArbiter>();
            }

            if (_states == null)
            {
                _states = GetComponent<CompanionStateArbiter>();
            }
        }

        /// <summary>追従としての移動意図を出す（受理されるかは調停役が決める。P4-FIX F02a）。</summary>
        private void SubmitMove(in CompanionMoveRequest request)
        {
            ResolveComponents();
            _arbiter?.Submit(CompanionMovementOwner.Follow, request);
        }

        /// <summary>
        /// 強制的に止める（ひるみ・ダウン・退場・活動停止）。所有権に関わらず通り、同じフレームの
        /// 通常の移動決定より優先される。Update を待つと、その間に回る物理ステップで滑る（実測で約 15mm／1 フレーム）。
        /// </summary>
        private void ForceStopMovement()
        {
            ResolveComponents();
            _arbiter?.ForceStop();
        }

        /// <summary>追従の所有権を手放す（既に戦闘へ移っていれば何も起きない）。</summary>
        private void ReleaseMovement()
        {
            ResolveComponents();
            _arbiter?.Release(CompanionMovementOwner.Follow);
        }
    }
}
