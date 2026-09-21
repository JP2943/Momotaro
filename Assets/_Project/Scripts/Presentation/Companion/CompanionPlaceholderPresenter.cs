using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Companion;
using UnityEngine;

namespace Momotaro.Presentation.Companion
{
    /// <summary>
    /// 仲間の仮表示（P4-02）。単色シルエット 1 枚と方向インジケータ 1 枚だけで、状態と向きを見分けられるようにする
    /// グレーボックス専用の Presentation。<b>Gameplay へは一切干渉せず</b>、状態と論理前方を読むだけで描画を決める。
    ///
    /// 状態は色と透明度で表す（<see cref="CompanionStateColors"/>）。向きは足元へ寝かせた矢印の回転で表す
    /// （見下ろし視点では頭上の矢印より接地した矢印のほうが 4 方向を判別しやすく、Billboard の回転とも干渉しない）。
    /// 退場中は表示体ごと消す。
    ///
    /// 素材・参照が未割当でも無表示・無例外で継続する（既存方針）。正式素材の統合（P10a）で本コンポーネントは役目を終える。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionPlaceholderPresenter : MonoBehaviour, ICompanionStateListener, IGuardianTransferListener
    {
        [Tooltip("表示対象の仲間（未設定なら親から自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("本体シルエットの描画先。状態色で着色する。")]
        [SerializeField] private SpriteRenderer _body;

        [Tooltip("足元へ寝かせる方向インジケータ。論理前方へ回転させる。")]
        [SerializeField] private SpriteRenderer _directionArrow;

        [Tooltip("方向インジケータを浮かせる高さ（m）。地面との Z ファイティングを避ける。")]
        [SerializeField, Min(0f)] private float _arrowHeight = 0.02f;

        [Tooltip("守護成立の短い表示時間（秒）。Protect は状態としては一瞬なので、通知を受けて表示側が持つ（v1.0 §8.5）。")]
        [SerializeField, Min(0f)] private float _protectFlashSeconds = 0.35f;

        private CompanionActor _subscribedActor;
        private CompanionGuardianController _subscribedGuardian;
        private CompanionState _appliedState = CompanionState.Event; // 初回に必ず反映させるための番兵。
        private float _protectFlashRemaining;
        private bool _suppressed;

        /// <summary>表示対象（配線確認・Validator・テスト用）。</summary>
        public CompanionActor Actor => _actor;

        /// <summary>本体シルエット（配線確認・テスト用）。</summary>
        public SpriteRenderer Body => _body;

        /// <summary>方向インジケータ（配線確認・テスト用）。</summary>
        public SpriteRenderer DirectionArrow => _directionArrow;

        /// <summary>直近に反映した状態（テスト用）。</summary>
        public CompanionState AppliedState => _appliedState;

        /// <summary>
        /// 通常表示を一時的に抑制しているか（探索の表示代理が出ている間。v1.0 §5.2「通常表示と探索表示を同時に出さない」）。
        /// 解除しても無条件に表示へ戻さず、そのときの状態（Down 等）に従う。
        /// </summary>
        public bool Suppressed
        {
            get => _suppressed;
            set
            {
                if (_suppressed == value)
                {
                    return;
                }

                _suppressed = value;
                _appliedState = CompanionState.Event; // 次の整合で必ず描き直す。
                ApplyState();
            }
        }

        /// <summary>守護成立の表示が残っているか（テスト・診断用）。</summary>
        public bool IsProtectFlashing => _protectFlashRemaining > 0f;

        /// <summary>表示対象と描画先を注入する（Prefab 構築・テスト。null は無視して既存を保つ）。</summary>
        public void Bind(CompanionActor actor, SpriteRenderer body = null, SpriteRenderer directionArrow = null)
        {
            if (actor != null && !ReferenceEquals(_actor, actor))
            {
                _actor = actor;
                if (isActiveAndEnabled)
                {
                    Subscribe();
                }
            }

            if (body != null)
            {
                _body = body;
            }

            if (directionArrow != null)
            {
                _directionArrow = directionArrow;
            }
        }

        private void OnEnable()
        {
            ResolveActor();
            Subscribe();
            _appliedState = CompanionState.Event;
            ApplyState();   // 有効化時点の状態を必ず一度反映する。
            ApplyFacing();
        }

        private void OnDisable()
        {
            Unsubscribe();
            _protectFlashRemaining = 0f;
        }

        private void LateUpdate()
        {
            ResolveActor();
            if (!ReferenceEquals(_subscribedActor, _actor))
            {
                Subscribe();
            }

            TickProtectFlash(Time.deltaTime);
            ApplyState();   // 通知を取りこぼしても表示がずれ続けないよう、毎フレーム安全に整合させる。
            ApplyFacing();
        }

        /// <inheritdoc />
        public void OnCompanionStateChanged(in CompanionStateChanged change)
        {
            ApplyStateColor(change.Current);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 守護の成立は状態としては一瞬（Protect → 即 Follow）なので、通知を受けた表示側が短く桃色を保つ（v1.0 §8.5）。
        /// HitStop・カメラ揺れは付けない（§11）。
        /// </remarks>
        public void OnGuardianTransfer(in GuardianTransferEvent transfer)
        {
            _protectFlashRemaining = _protectFlashSeconds;
            _appliedState = CompanionState.Event; // 次の整合で色を描き直す。
            ApplyState();
        }

        /// <summary>守護表示の残り時間を進める（LateUpdate から呼ばれるが、テストは決定的に直接呼べる）。</summary>
        public void TickProtectFlash(float deltaTime)
        {
            if (_protectFlashRemaining <= 0f)
            {
                return;
            }

            _protectFlashRemaining -= deltaTime;
            if (_protectFlashRemaining <= 0f)
            {
                _protectFlashRemaining = 0f;
                _appliedState = CompanionState.Event; // 表示を通常色へ戻す。
            }
        }

        /// <summary>現在状態を表示へ反映する（変化が無ければ何もしない）。</summary>
        private void ApplyState()
        {
            if (_actor == null)
            {
                return;
            }

            CompanionState state = _actor.State;
            if (state == _appliedState)
            {
                return;
            }

            ApplyStateColor(state);
        }

        private void ApplyStateColor(CompanionState state)
        {
            _appliedState = state;

            // 探索の表示代理が出ている間は通常表示を消す（同時に 2 体描かない）。
            // 守護成立の直後は実状態（Follow 等）に関わらず短く守護色を見せる。
            bool visible = !_suppressed && CompanionStateColors.IsVisible(state);
            Color color = _protectFlashRemaining > 0f
                ? CompanionStateColors.Resolve(CompanionState.Protect)
                : CompanionStateColors.Resolve(state);

            if (_body != null)
            {
                _body.enabled = visible;
                _body.color = color;
            }

            if (_directionArrow != null)
            {
                _directionArrow.enabled = visible;
            }
        }

        /// <summary>方向インジケータを足元へ寝かせ、論理前方へ向ける。</summary>
        private void ApplyFacing()
        {
            if (_actor == null || _directionArrow == null)
            {
                return;
            }

            Transform arrow = _directionArrow.transform;
            Vector3 basePosition = _actor.transform.position;
            arrow.position = new Vector3(basePosition.x, basePosition.y + _arrowHeight, basePosition.z);

            // 面の法線を真上へ、矢印の指す向き（Sprite の +Y）を論理前方へ合わせる。
            arrow.rotation = Quaternion.LookRotation(Vector3.up, _actor.Forward);
        }

        private void ResolveActor()
        {
            if (_actor == null)
            {
                _actor = GetComponentInParent<CompanionActor>();
            }
        }

        private void Subscribe()
        {
            if (ReferenceEquals(_subscribedActor, _actor))
            {
                return;
            }

            Unsubscribe();
            _subscribedActor = _actor;
            _subscribedActor?.States.AddListener(this);

            // 守護の成立通知は Guardian が持つ（同じ Body に載っている。無ければ守護表示は出ないだけ）。
            _subscribedGuardian = _subscribedActor != null ? _subscribedActor.GetComponent<CompanionGuardianController>() : null;
            _subscribedGuardian?.Transfers.AddListener(this);
        }

        private void Unsubscribe()
        {
            _subscribedActor?.States.RemoveListener(this);
            _subscribedActor = null;
            _subscribedGuardian?.Transfers.RemoveListener(this);
            _subscribedGuardian = null;
        }
    }
}
