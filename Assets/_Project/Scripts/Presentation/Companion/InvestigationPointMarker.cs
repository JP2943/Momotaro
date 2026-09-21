using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using UnityEngine;

namespace Momotaro.Presentation.Companion
{
    /// <summary>
    /// 調査地点の仮マーカー（P4-07B。v1.0 §11「Gizmos や色だけに依存しない」）。
    ///
    /// 地面に寝かせた輪（手続き生成の Sprite。新しい素材を作らない）と、頭上の文字で状態を示す。
    /// 文字は<b>調査済み＝「済」</b>、<b>候補（いま Interact すると対象になる地点）＝地点の案内文</b>、
    /// <b>候補だが拒否される＝理由</b>、<b>調査中＝「調査中」</b>、それ以外＝「？」。輪の色は補助。
    ///
    /// 候補かどうかは自分では判定しない（地点ごとに選定を回すと N 回の評価になる）。
    /// <see cref="Momotaro.Presentation.Hud.InvestigationPromptHud"/> が 1 フレーム 1 回の Peek 結果を
    /// <see cref="SetCandidate"/> で配る。HUD が無い Scene では「済／？／調査中」だけを自力で出す。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvestigationPointMarker : MonoBehaviour, IInvestigationListener
    {
        [Tooltip("表示する地点（未設定なら同じ GameObject から自動取得）。")]
        [SerializeField] private CompanionInvestigationPoint _point;

        [Tooltip("Scene の調査済み記録（「済」の判定に読む）。")]
        [SerializeField] private InvestigationRecordHolder _record;

        [Tooltip("通知の購読先（調査中の表示のため）。")]
        [SerializeField] private InvestigationCoordinator _coordinator;

        [Tooltip("地面の輪。")]
        [SerializeField] private SpriteRenderer _ring;

        [Tooltip("頭上の文字。")]
        [SerializeField] private TextMesh _label;

        [Tooltip("文字の高さ（m）。")]
        [SerializeField, Min(0f)] private float _labelHeight = 1.1f;

        [Tooltip("輪を浮かせる高さ（m）。")]
        [SerializeField, Min(0f)] private float _ringHeight = 0.03f;

        private static readonly Color RingIdle = new Color(0.45f, 0.75f, 1f, 0.85f);
        private static readonly Color RingCandidate = new Color(1f, 1f, 1f, 1f);
        private static readonly Color RingRejected = new Color(1f, 0.55f, 0.35f, 0.9f);
        private static readonly Color RingBusy = new Color(0.55f, 0.9f, 0.55f, 0.95f);
        private static readonly Color RingDone = new Color(0.5f, 0.5f, 0.5f, 0.6f);

        private bool _candidate;
        private InvestigationRejectReason _candidateReason;
        private string _candidateText;
        private int _activeRequestId;
        private InvestigationCoordinator _subscribed;

        /// <summary>表示する地点（配線確認用）。</summary>
        public CompanionInvestigationPoint Point => _point;

        /// <summary>輪（配線確認用）。</summary>
        public SpriteRenderer Ring => _ring;

        /// <summary>文字（配線確認用）。</summary>
        public TextMesh Label => _label;

        /// <summary>いま出している文字（テスト用）。</summary>
        public string LabelText => _label != null ? _label.text : string.Empty;

        /// <summary>この地点が調査中か（受理〜完了／中断）。</summary>
        public bool IsBeingInvestigated => _activeRequestId != 0;

        /// <summary>参照を注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(
            CompanionInvestigationPoint point, InvestigationRecordHolder record, InvestigationCoordinator coordinator,
            SpriteRenderer ring = null, TextMesh label = null)
        {
            if (point != null)
            {
                _point = point;
            }

            if (record != null)
            {
                _record = record;
            }

            if (coordinator != null)
            {
                _coordinator = coordinator;
                if (isActiveAndEnabled)
                {
                    Subscribe();
                }
            }

            if (ring != null)
            {
                _ring = ring;
            }

            if (label != null)
            {
                _label = label;
            }
        }

        /// <summary>
        /// HUD から「いま Interact すると対象になる地点か」と、その結果を受け取る（1 フレーム 1 回）。
        /// 候補でなければ <paramref name="candidate"/> false。
        /// </summary>
        public void SetCandidate(bool candidate, InvestigationRejectReason reason, string text)
        {
            _candidate = candidate;
            _candidateReason = reason;
            _candidateText = text;
        }

        private void OnEnable()
        {
            Resolve();
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        /// <summary>状態を表示へ反映する（LateUpdate から呼ばれるが、テストは決定的に直接呼べる）。</summary>
        public void Refresh()
        {
            Resolve();
            if (_point == null)
            {
                return;
            }

            bool investigated = _record != null && _point.PointId.IsValid && _record.Record.IsInvestigated(_point.PointId);

            string text;
            Color ring;
            if (investigated)
            {
                text = InvestigationTexts.InvestigatedMark;
                ring = RingDone;
            }
            else if (IsBeingInvestigated)
            {
                text = InvestigationTexts.PhaseLabel(InvestigationPhase.Investigating);
                ring = RingBusy;
            }
            else if (_candidate)
            {
                if (_candidateReason == InvestigationRejectReason.None)
                {
                    text = InvestigationTexts.Prompt(_point.Prompt);
                    ring = RingCandidate;
                }
                else
                {
                    string reject = InvestigationTexts.Reject(_candidateReason, _candidateText);
                    text = string.IsNullOrEmpty(reject) ? InvestigationTexts.UnknownMark : reject;
                    ring = RingRejected;
                }
            }
            else
            {
                text = InvestigationTexts.UnknownMark;
                ring = RingIdle;
            }

            Vector3 position = _point.Position;
            if (_ring != null)
            {
                Transform t = _ring.transform;
                t.position = new Vector3(position.x, position.y + _ringHeight, position.z);
                t.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                _ring.color = ring;
            }

            if (_label != null)
            {
                if (_label.text != text)
                {
                    _label.text = text;
                }

                Transform anchor = _label.transform.parent != null ? _label.transform.parent : _label.transform;
                anchor.position = position + Vector3.up * _labelHeight;
            }
        }

        /// <inheritdoc />
        public void OnInvestigationAccepted(in InvestigationAccepted accepted)
        {
            if (_point != null && accepted.PointId.Equals(_point.PointId))
            {
                _activeRequestId = accepted.RequestId;
            }
        }

        /// <inheritdoc />
        public void OnInvestigationRejected(in InvestigationRejected rejected)
        {
        }

        /// <inheritdoc />
        public void OnInvestigationCompleted(in InvestigationCompleted completed)
        {
            if (completed.RequestId == _activeRequestId)
            {
                _activeRequestId = 0;
            }
        }

        /// <inheritdoc />
        public void OnInvestigationInterrupted(in InvestigationInterrupted interrupted)
        {
            if (interrupted.RequestId == _activeRequestId)
            {
                _activeRequestId = 0;
            }
        }

        private void Resolve()
        {
            if (_point == null)
            {
                _point = GetComponent<CompanionInvestigationPoint>();
            }
        }

        private void Subscribe()
        {
            if (ReferenceEquals(_subscribed, _coordinator))
            {
                return;
            }

            Unsubscribe();
            _subscribed = _coordinator;
            _subscribed?.Events.AddListener(this);
        }

        private void Unsubscribe()
        {
            _subscribed?.Events.RemoveListener(this);
            _subscribed = null;
        }
    }
}
