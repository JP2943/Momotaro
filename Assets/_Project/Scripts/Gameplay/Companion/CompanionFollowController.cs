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
        private ICompanionInvestigationState _investigation; // 探索側（同上。探索を持たない仲間では null）。

        /// <summary>判断モデル（テスト・Debug 用）。</summary>
        public CompanionFollowModel Model => _model;

        /// <summary>現在の追従対象。</summary>
        public Transform Leader => _leader;

        /// <summary>直近の判断。</summary>
        public CompanionFollowDecision Decision => _model.Decision;

        // ---- P5-05：長距離追従の経路（§10.1／§10.2）----

        private readonly Navigation.CompanionPathFollowModel _pathModel = new Navigation.CompanionPathFollowModel();
        private Navigation.IPathProvider _pathProvider;
        private Navigation.IWarpCandidateProbe _warpProbe;
        private Investigation.IObstacleProbe _obstacleProbe;
        private readonly System.Collections.Generic.List<Vector3> _warpCandidates =
            new System.Collections.Generic.List<Vector3>();

        /// <summary>経路の判断（診断・テスト用）。</summary>
        public Navigation.CompanionPathFollowModel PathModel => _pathModel;

        /// <summary>経路の供給元が差さっているか（Validator・テスト用）。</summary>
        public bool HasPathProvider => _pathProvider != null;

        /// <summary>ワープ候補を断った回数（診断・テスト用）。安全な場所が無くて止まった回数。</summary>
        public int UnsafeWarpBlockedCount { get; private set; }

        /// <summary>
        /// 経路の供給元を明示注入する（P5-05。§10.1）。
        /// <b>GetComponent だけに頼らない。</b> テストは Fake を差し、実機は NavMesh Adapter を差す。
        /// 未注入なら経路追従は行わず、診断できる形（<see cref="HasPathProvider"/> が false）で止まる。
        /// </summary>
        public void BindPathProvider(Navigation.IPathProvider provider)
        {
            _pathProvider = provider;
            _pathModel.Reset();
        }

        /// <summary>ワープ候補の安全性を調べる供給元を差す（§10.2）。未注入ならワープを止める。</summary>
        public void BindWarpProbe(Navigation.IWarpCandidateProbe probe)
        {
            _warpProbe = probe;
        }

        /// <summary>直線で通れるかの判定を差し替える（テスト。未設定なら壁レイヤーへの物理判定）。</summary>
        public void SetObstacleProbe(Investigation.IObstacleProbe probe)
        {
            _obstacleProbe = probe;
        }

        /// <summary>門の開通など、世界の通行状態が変わったことを伝える（§10.1）。</summary>
        public void NotifyWorldChanged()
        {
            _pathModel.NotifyWorldChanged();
        }

        private Investigation.IObstacleProbe ObstacleProbe =>
            _obstacleProbe ?? (_obstacleProbe = new Investigation.PhysicsObstacleProbe());

        /// <summary>戦闘側へ移動を譲っているか（テスト・診断用）。</summary>
        public bool IsYieldingToCombat => ResolveEngagement() != null && _engagement.IsEngaged;

        /// <summary>
        /// 追従より強い持ち主が移動を握っているか（探索・防御など。テスト・診断用）。
        /// 「誰が握っているか」は F02a の調停役が既に持っている。追従側で数え直さない。
        /// </summary>
        public bool IsYieldingToStrongerMovementOwner =>
            _arbiter != null && _arbiter.Owner > CompanionMovementOwner.Follow;

        /// <summary>
        /// 探索へ譲っているか（R3-03。テスト・診断用）。
        ///
        /// 本体で調べているあいだは移動の所有権が探索にあるので
        /// <see cref="IsYieldingToStrongerMovementOwner"/> でも止まるが、<b>表示代理のあいだは所有権が動かない</b>。
        /// 代理は Down／退場中の本体に触れずに歩くので、Down の自然復帰時刻が来ると本体は Follow へ戻り、
        /// 状態も所有権も「追従してよい」に見える。表示だけは代理に抑制されたままなので、
        /// <b>見えない本体が歩き出す</b>（R3-03 で報告された経路）。探索の利用中状態を直接見て止める。
        /// </summary>
        public bool IsYieldingToInvestigation =>
            ResolveInvestigation() != null && _investigation.IsInvestigationActive;

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
            // 遷移中は Gameplay 時計を進めない。Update からでも直接呼ばれても同じ（§6.2 手順 3、P5-E07）。
            if (Session.GameplayClockProvider.IsFrozen)
            {
                return;
            }

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

            // 探索中は判断ごと止める（R3-03）。本体・表示代理のどちらでも譲る。
            // Motor へは触れない（本体探索では探索が握っており、代理探索では本体は既に止まっている）。
            if (IsYieldingToInvestigation)
            {
                _model.Reset();   // 復帰時に古い停滞時間・前回距離を引きずらない。
                ReleaseMovement(); // 自分が握っていれば返す（探索が握っていれば何も起きない）。
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

            CompanionFollowSettings settings = CompanionFollowSettings.From(_actor.Data);

            // 隊列位置は経路の判断より先に要る（そこが経路の目的地になる）。
            Vector3 slot = FormationSlot.Resolve(
                _leader.position, ResolveLeaderForward(), _actor.SlotIndex, settings.Spacing);

            // 経路の判断（§10.1）。供給元が未注入なら Direct 相当に倒れる（経路追従を行わない）。
            Navigation.PathFollowDecision pathDecision = TickPath(deltaTime, slot);

            var input = new CompanionFollowInput(
                _leader.position, ResolveLeaderForward(), transform.position, _actor.SlotIndex,
                pathDecision == Navigation.PathFollowDecision.MoveToCorner);

            // 経路がまだ得られていない間は動かない（§10.1。届かないと決めつけて壁へ突っ込ませない）。
            if (pathDecision == Navigation.PathFollowDecision.Waiting)
            {
                EnterFollow();
                SubmitMove(CompanionMoveRequest.StopFacing(ResolveLeaderForward()));
                return;
            }

            switch (_model.Tick(input, settings, deltaTime))
            {
                case CompanionFollowDecision.Move:
                    EnterFollow();

                    // 迂回中は角へ、そうでなければ隊列位置へ。<b>書き込むのは同じ 1 本</b>
                    // （調停役 → Motor。NavMeshAgent は置かない。§10.1）。
                    Vector3 target = pathDecision == Navigation.PathFollowDecision.MoveToCorner
                        ? _pathModel.NextCorner
                        : _model.SlotPosition;
                    SubmitMove(CompanionMoveRequest.MoveFacing(
                        target, speed, stopRadius, target - transform.position));
                    break;

                case CompanionFollowDecision.Warp:
                    if (!TryWarpToSafePlace())
                    {
                        // 安全な場所が無い。壁内へ押し込まず、止まって次の評価を待つ（§10.2）。
                        UnsafeWarpBlockedCount++;
                        EnterFollow();
                        SubmitMove(CompanionMoveRequest.StopFacing(ResolveLeaderForward()));
                    }

                    break;

                default: // Hold
                    EnterFollow();
                    // 到着後は主人公と同じ向きを向く。
                    SubmitMove(CompanionMoveRequest.StopFacing(ResolveLeaderForward()));
                    break;
            }
        }

        /// <summary>
        /// 経路の判断を 1 Tick 進める（§10.1）。供給元が無ければ経路追従はしない。
        /// </summary>
        private Navigation.PathFollowDecision TickPath(float deltaTime, Vector3 slot)
        {
            if (_pathProvider == null)
            {
                // 未注入。経路追従は行わない（§10.1。Validator がここを不合格にする）。
                _pathModel.Reset();
                return Navigation.PathFollowDecision.Direct;
            }

            bool clear = ObstacleProbe.IsClear(transform.position, slot);
            var pathInput = new Navigation.PathFollowInput(transform.position, slot, clear);
            return _pathModel.Tick(pathInput, Navigation.PathFollowSettings.Default, _pathProvider, deltaTime);
        }

        /// <summary>
        /// 安全なワープ先へ跳ぶ（§10.2）。候補が無ければ<b>跳ばずに false</b>。
        ///
        /// 候補は主人公近傍の隊列位置を固定順で並べる。近い順に並べ替えない：
        /// 同じ状況で同じ場所へ出ることの方が、見ている側には分かりやすい。
        /// </summary>
        private bool TryWarpToSafePlace()
        {
            Vector3 slot = _model.SlotPosition;

            if (_warpProbe == null)
            {
                // 安全性を確かめる手立てが無い構成（P4 の試遊など）は従来どおり跳ぶ。
                BeginFollowAction(CompanionState.Warp, CompanionStateChangeReason.Warped);
                SubmitMove(CompanionMoveRequest.Warp(slot));
                return true;
            }

            Vector3 leaderPosition = _leader.position;
            Vector3 forward = ResolveLeaderForward();
            _warpCandidates.Clear();
            _warpCandidates.Add(slot);
            _warpCandidates.Add(FormationSlot.Resolve(leaderPosition, forward, _actor.SlotIndex + 1, 1.2f));
            _warpCandidates.Add(leaderPosition - forward * 1.2f);
            _warpCandidates.Add(leaderPosition + Vector3.Cross(Vector3.up, forward) * 1.2f);
            _warpCandidates.Add(leaderPosition - Vector3.Cross(Vector3.up, forward) * 1.2f);

            if (!Navigation.SafeWarpSelector.TrySelect(
                    _warpCandidates, leaderPosition, IsWarpAllowed(), _warpProbe,
                    out Vector3 chosen, out _))
            {
                return false;
            }

            BeginFollowAction(CompanionState.Warp, CompanionStateChangeReason.Warped);
            SubmitMove(CompanionMoveRequest.Warp(chosen));
            _pathModel.Reset();
            return true;
        }

        /// <summary>
        /// 指定範囲の<b>内側へ配置する</b>（P5-07。仕様書 v1.1 §8.2 手順 5、§10.2 末尾）。
        ///
        /// <b>通常 Follow の Warp とは別の配置処理</b>（§10.2 末尾が名指しで分けている）。だから
        /// 状態を <c>Warp</c> へ変えないし、移動所有権の調停も通さない。理由は 2 つある。
        /// <list type="number">
        /// <item><description>Down／Away の犬丸に <c>Warp</c> への遷移を要求すると<b>不正遷移</b>になる。
        /// §10.2 末尾は「Down／Away を含む HP・復帰残り・CD・表示資格を維持したまま」配置せよと言っている。
        /// Away を出撃扱いに変えてもいけない。</description></item>
        /// <item><description>強制停止のラッチや別の所有者が居ると移動要求は握り潰される。
        /// それを「配置できた」と読むと、<b>境界の外に Down の本体を置き去りにしたまま封鎖する</b>
        /// （§10.2 末尾が禁じている形）。</description></item>
        /// </list>
        ///
        /// 候補の選び方は P5-05 の安全ワープ規則をそのまま使う（固定順・危険候補は拒否・
        /// 主人公と繋がっている側だけ）。<b>置いたあとに実位置が内側に収まったことを確かめてから</b>成功を返す。
        /// 範囲内の安全候補が 1 つも無ければ跳ばさない：壁内へ押し込むくらいなら封鎖を諦める方が正しい。
        ///
        /// すでに内側なら何もせず true。
        /// </summary>
        public bool TryRelocateInside(Bounds bounds, out Navigation.SafeWarpRejection rejection)
        {
            rejection = Navigation.SafeWarpRejection.None;
            ResolveComponents();

            if (_actor == null || _leader == null || _motor == null)
            {
                rejection = Navigation.SafeWarpRejection.NotAllowed;
                return false;
            }

            if (IsInside(bounds, _actor.transform.position))
            {
                return true; // すでに内側。動かさない。
            }

            Vector3 leaderPosition = _leader.position;
            Vector3 forward = ResolveLeaderForward();

            _warpCandidates.Clear();
            AddIfInside(bounds, _model.SlotPosition);
            AddIfInside(bounds, leaderPosition - forward * 1.2f);
            AddIfInside(bounds, leaderPosition + Vector3.Cross(Vector3.up, forward) * 1.2f);
            AddIfInside(bounds, leaderPosition - Vector3.Cross(Vector3.up, forward) * 1.2f);
            AddIfInside(bounds, leaderPosition);

            if (_warpCandidates.Count == 0)
            {
                rejection = Navigation.SafeWarpRejection.NoCandidate;
                return false;
            }

            Vector3 chosen;
            if (_warpProbe == null)
            {
                // 安全性を確かめる手立てが無い構成（P4 の試遊など）は先頭候補へ置く。
                chosen = _warpCandidates[0];
            }
            else if (!Navigation.SafeWarpSelector.TrySelect(
                         _warpCandidates, leaderPosition, warpAllowed: true, _warpProbe,
                         out chosen, out rejection))
            {
                return false;
            }

            // 配置は Motor の専用口で行う（状態・所有権に触れない）。
            Vector3 placed = _motor.PlaceAt(chosen);

            // <b>置けたことを実位置で確かめる。</b> 要求を出しただけで成功にすると、
            // 握り潰された場合に境界の外へ置き去りにしたまま封鎖してしまう。
            if (!IsInside(bounds, placed) || !IsInside(bounds, _actor.transform.position))
            {
                rejection = Navigation.SafeWarpRejection.AllUnsafe;
                return false;
            }

            // 経路の途中状態は捨てる（配置後の位置から評価し直す）。状態・値は触らない。
            _pathModel.Reset();
            return true;
        }

        private void AddIfInside(Bounds bounds, Vector3 candidate)
        {
            if (IsInside(bounds, candidate))
            {
                _warpCandidates.Add(candidate);
            }
        }

        private static bool IsInside(Bounds bounds, Vector3 position)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return position.x >= min.x && position.x <= max.x
                && position.z >= min.z && position.z <= max.z;
        }

        /// <summary>
        /// いま通常 Follow のワープをしてよいか（§10.2）。
        /// 攻撃 Active・防御・被弾・Down・Away・探索占有中は禁止。
        /// </summary>
        private bool IsWarpAllowed()
        {
            if (_actor == null)
            {
                return false;
            }

            if (IsFollowSuspended(_actor.State) || IsYieldingToInvestigation || IsYieldingToCombat)
            {
                return false;
            }

            return _actor.State == CompanionState.Follow
                || _actor.State == CompanionState.Idle
                || _actor.State == CompanionState.Warp;
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

        /// <summary>探索側（同一 GameObject の <see cref="ICompanionInvestigationState"/>）を解決する（R3-03）。</summary>
        private ICompanionInvestigationState ResolveInvestigation()
        {
            if (_investigation is Object destroyed && destroyed == null)
            {
                _investigation = null;
            }

            if (_investigation == null)
            {
                _investigation = GetComponent<ICompanionInvestigationState>();
            }

            return _investigation;
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
