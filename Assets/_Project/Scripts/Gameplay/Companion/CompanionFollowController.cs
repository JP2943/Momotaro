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
    /// 実際の戦闘参加・対象選択は P4-03 以降で別コンポーネントが担い、本コンポーネントは触らない。
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

        private readonly CompanionFollowModel _model = new CompanionFollowModel();
        private ICombatActor _leaderActor;
        private bool _leaderActorResolved;
        private CompanionActor _subscribedActor; // 状態通知の購読先（対称管理・重複購読防止）。

        /// <summary>判断モデル（テスト・Debug 用）。</summary>
        public CompanionFollowModel Model => _model;

        /// <summary>現在の追従対象。</summary>
        public Transform Leader => _leader;

        /// <summary>直近の判断（テスト・Debug 用）。</summary>
        public CompanionFollowDecision Decision => _model.Decision;

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
            _motor?.Stop();
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

            _motor?.Stop();
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

            // 退場・ダウン・ひるみ中は追従しない（状態遷移の瞬間は通知で停止済み。ここは継続中の保険）。
            if (IsFollowSuspended(_actor.State))
            {
                _motor.Stop();
                _model.Reset();
                return;
            }

            ApplyMoveSettings();

            var input = new CompanionFollowInput(
                _leader.position, ResolveLeaderForward(), transform.position, _actor.SlotIndex);
            CompanionFollowSettings settings = CompanionFollowSettings.From(_actor.Data);

            switch (_model.Tick(input, settings, Time.deltaTime))
            {
                case CompanionFollowDecision.Move:
                    EnterFollow();
                    _motor.SetMoveTarget(_model.SlotPosition);
                    FaceTowards(_model.SlotPosition - transform.position);
                    break;

                case CompanionFollowDecision.Warp:
                    _actor.RequestState(CompanionState.Warp, CompanionStateChangeReason.Warped);
                    _motor.WarpTo(_model.SlotPosition);
                    break;

                default: // Hold
                    EnterFollow();
                    _motor.Stop();
                    FaceTowards(ResolveLeaderForward()); // 到着後は主人公と同じ向きを向く。
                    break;
            }
        }

        /// <summary>Data の移動速度・停止距離を Motor へ反映する（原本が差し替わっても追従する）。</summary>
        private void ApplyMoveSettings()
        {
            float speed = _actor.Data != null ? _actor.Data.MoveSpeed : 4.5f;
            float stopRadius = _actor.Data != null ? _actor.Data.FollowStopDistance : 0.35f;
            _motor.Configure(speed, stopRadius);
        }

        /// <summary>追従中の状態へ入れる（既に Follow なら何もしない。Warp からの復帰もここを通る）。</summary>
        private void EnterFollow()
        {
            if (_actor.State != CompanionState.Follow)
            {
                _actor.RequestState(CompanionState.Follow, CompanionStateChangeReason.FollowResumed);
            }
        }

        private void FaceTowards(Vector3 direction)
        {
            _actor.SetFacing(direction);
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
        }
    }
}
