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
    public sealed class CompanionPlaceholderPresenter : MonoBehaviour, ICompanionStateListener
    {
        [Tooltip("表示対象の仲間（未設定なら親から自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("本体シルエットの描画先。状態色で着色する。")]
        [SerializeField] private SpriteRenderer _body;

        [Tooltip("足元へ寝かせる方向インジケータ。論理前方へ回転させる。")]
        [SerializeField] private SpriteRenderer _directionArrow;

        [Tooltip("方向インジケータを浮かせる高さ（m）。地面との Z ファイティングを避ける。")]
        [SerializeField, Min(0f)] private float _arrowHeight = 0.02f;

        private CompanionActor _subscribedActor;
        private CompanionState _appliedState = CompanionState.Event; // 初回に必ず反映させるための番兵。

        /// <summary>表示対象（配線確認・Validator・テスト用）。</summary>
        public CompanionActor Actor => _actor;

        /// <summary>本体シルエット（配線確認・テスト用）。</summary>
        public SpriteRenderer Body => _body;

        /// <summary>方向インジケータ（配線確認・テスト用）。</summary>
        public SpriteRenderer DirectionArrow => _directionArrow;

        /// <summary>直近に反映した状態（テスト用）。</summary>
        public CompanionState AppliedState => _appliedState;

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
        }

        private void LateUpdate()
        {
            ResolveActor();
            if (!ReferenceEquals(_subscribedActor, _actor))
            {
                Subscribe();
            }

            ApplyState();   // 通知を取りこぼしても表示がずれ続けないよう、毎フレーム安全に整合させる。
            ApplyFacing();
        }

        /// <inheritdoc />
        public void OnCompanionStateChanged(in CompanionStateChanged change)
        {
            ApplyStateColor(change.Current);
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
            bool visible = CompanionStateColors.IsVisible(state);
            Color color = CompanionStateColors.Resolve(state);

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
        }

        private void Unsubscribe()
        {
            _subscribedActor?.States.RemoveListener(this);
            _subscribedActor = null;
        }
    }
}
