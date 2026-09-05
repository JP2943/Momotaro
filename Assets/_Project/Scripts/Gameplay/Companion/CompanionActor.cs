using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の Identity・所属・状態の窓口（P4-02）。<see cref="ICompanionActor"/> の実体で、<c>EnemyActor</c> と同じ形
    /// （Runtime 状態機の遅延生成、型付き通知、論理前方の保持、レイヤー方針の適用）を採る。
    ///
    /// 本 Task では追従に必要な範囲だけを担う。被弾（<c>IDamageable</c>）・攻撃（<c>ICombatActor</c>）・ヘイト候補
    /// （<c>IThreatTarget</c>）・肩代わり（<c>IGuardianReceiver</c>）は、それぞれ P4-03／P4-04／P4-05 で本コンポーネントか
    /// 併設コンポーネントが実装する。先回りして空実装を置かない。
    ///
    /// 向きはルート Transform を回さず論理値として保持する（敵と同じ理由：接地と Collider の安定のためルートは回さない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionActor : MonoBehaviour, ICompanionActor
    {
        [Tooltip("仲間の基礎データ（役割・ヘイト補正・追従・守護の数値）。未割当でも既定値で安全に動く。")]
        [SerializeField] private CompanionData _data;

        [Tooltip("隊列番号（0 始まり）。0=後方やや左、1=後方やや右、2=さらに後方中央。")]
        [SerializeField] private int _slotIndex;

        [Tooltip("初期状態。未加入から始める場合は Away を選ぶ。")]
        [SerializeField] private CompanionState _initialState = CompanionState.Follow;

        private CompanionStateMachine _machine;
        private Vector3 _facing = Vector3.forward;

        /// <summary>状態遷移の通知チャネル（表示・切替・Debug が購読）。</summary>
        public CompanionStateChannel States { get; } = new CompanionStateChannel();

        /// <inheritdoc />
        public int ActorId => GetInstanceID();

        /// <inheritdoc />
        public CombatFaction Faction => CombatFaction.Ally;

        /// <inheritdoc />
        public CompanionRole Role => _data != null ? _data.Role : CompanionRole.Dog;

        /// <inheritdoc />
        public CompanionData Data => _data;

        /// <inheritdoc />
        public CompanionState State
        {
            get { EnsureRuntime(); return _machine.Current; }
        }

        /// <inheritdoc />
        public Vector3 WorldPosition => transform.position;

        /// <inheritdoc />
        public bool IsDown => State == CompanionState.Down;

        /// <inheritdoc />
        public bool IsAway => State == CompanionState.Away;

        /// <summary>隊列番号（0 始まり）。切替・加入順に応じて外部が変更できる（負値は 0 として扱う）。</summary>
        public int SlotIndex
        {
            get => _slotIndex < 0 ? 0 : _slotIndex;
            set => _slotIndex = value < 0 ? 0 : value;
        }

        /// <summary>直近の遷移理由（Debug・テスト用）。</summary>
        public CompanionStateChangeReason LastReason
        {
            get { EnsureRuntime(); return _machine.LastReason; }
        }

        /// <summary>不正遷移の記録数（Debug・テスト用）。</summary>
        public int IllegalTransitionCount
        {
            get { EnsureRuntime(); return _machine.IllegalTransitionCount; }
        }

        /// <summary>
        /// 論理的な前方（XZ 平面）。ルート Transform は回さないため、向きはこの論理値で保持する
        /// （表示の 4 方向・将来の攻撃照準が参照する）。既定は +Z。
        /// </summary>
        public Vector3 Forward => _facing.sqrMagnitude > 1e-6f ? _facing : Vector3.forward;

        /// <summary>基礎データを注入する（Scene 構築・テスト。null は無視して既存を保つ）。</summary>
        public void SetData(CompanionData data)
        {
            if (data != null)
            {
                _data = data;
            }
        }

        /// <summary>論理的な前方（XZ）を設定する。ルート Transform は回さない。</summary>
        public void SetFacing(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-6f)
            {
                _facing = direction.normalized;
            }
        }

        /// <summary>
        /// AI・指示・イベント由来の状態遷移を要求する（優先度・不正判定は状態機に従う）。適用できたら true。
        /// </summary>
        public bool RequestState(CompanionState state, CompanionStateChangeReason reason)
        {
            EnsureRuntime();
            return _machine.TryTransition(state, reason);
        }

        /// <summary>被弾由来の強制状態（Stagger／Down）を割り込み適用する（P4-04／P4-06 が使う）。適用できたら true。</summary>
        public bool ForceHitState(CompanionState state, CompanionStateChangeReason reason)
        {
            EnsureRuntime();
            return _machine.ForceHitState(state, reason);
        }

        /// <summary>状態を初期化する（加入・再配置・検証の再試行）。優先度・不正判定を経ずに適用する。</summary>
        public void ResetState(CompanionState state = CompanionState.Follow)
        {
            EnsureRuntime();
            _machine.Reset(state);
            _facing = Vector3.forward;
        }

        private void Awake()
        {
            EnsureRuntime();
            // 仲間は Ally レイヤーへ（主人公・敵・仲間同士はすり抜け、壁では止まる。P4-02）。
            CombatLayers.ConfigureAlly(gameObject);
        }

        private void EnsureRuntime()
        {
            if (_machine == null)
            {
                _machine = new CompanionStateMachine(
                    GetInstanceID(),
                    _initialState,
                    change => States.Publish(change),
                    IllegalTransitionLogger());
            }
        }

        private static System.Action<string> IllegalTransitionLogger()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return message => Debug.LogWarning(message);
#else
            return null;
#endif
        }
    }
}
