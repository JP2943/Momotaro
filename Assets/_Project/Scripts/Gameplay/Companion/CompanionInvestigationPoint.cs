using Momotaro.Core.Identification;
using Momotaro.Data.Exploration;
using Momotaro.Gameplay.Companion.Investigation;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 調査地点（P4-07A。v1.0 §10.1）。Scene に置き、主人公が近づいて Interact したときの依頼先になる。
    ///
    /// 持つのは「どこで・誰が・何を」だけ。調査済みかどうかは Scene 単位の記録（<see cref="InvestigationRecord"/>）が持つので、
    /// この地点を Disable／再 Enable しても記録は消えない（§10.3）。有効な間だけレジストリに登録する。
    ///
    /// <b>PointId は配置ごとの StableId</b>（Prefab／SO 共有 ID や GetInstanceID で代用しない）。同じ ID が Scene に 2 つあれば
    /// Validator がエラーにする。表示（マーカー・短文）は Presentation 側が本コンポーネントを読んで描く。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionInvestigationPoint : MonoBehaviour, IInvestigationPoint
    {
        [Header("Identity")]
        [Tooltip("配置された地点固有の StableId（小文字 snake_case）。")]
        [SerializeField] private StableId _pointId;

        [Tooltip("この地点を調べられる仲間の StableId（犬丸なら CompanionData の Id）。")]
        [SerializeField] private StableId _requiredCompanion;

        [Tooltip("発見内容の識別子（報酬そのものではない。後続の受け手が読む）。")]
        [SerializeField] private StableId _discoveryId;

        [Header("Approach")]
        [Tooltip("調査位置（未設定なら自分の位置）。")]
        [SerializeField] private Transform _approachAnchor;

        [Tooltip("調査時の向き（ゼロなら地点の方を向く）。")]
        [SerializeField] private Vector3 _approachFacing;

        [Header("Settings")]
        [Tooltip("距離・時間の設定 Data。受付時に Snapshot として写す。")]
        [SerializeField] private InvestigationSettingsData _settings;

        [Header("Text (仮 UI)")]
        [SerializeField] private string _prompt = "調べる";
        [SerializeField] private string _missingCompanionHint = "犬がいれば何か見つかりそうだ";
        [SerializeField] private string _completedText = "ここは調べ終えた";

        /// <inheritdoc />
        public StableId PointId => _pointId;

        /// <inheritdoc />
        public StableId RequiredCompanion => _requiredCompanion;

        /// <inheritdoc />
        public StableId DiscoveryId => _discoveryId;

        /// <inheritdoc />
        public Vector3 Position => transform.position;

        /// <inheritdoc />
        public Vector3 ApproachPosition => _approachAnchor != null ? _approachAnchor.position : transform.position;

        /// <inheritdoc />
        public Vector3 ApproachFacing => _approachFacing;

        /// <inheritdoc />
        public bool IsAvailable => this != null && isActiveAndEnabled && _pointId.IsValid && _settings != null;

        /// <inheritdoc />
        public InvestigationSettings Settings => InvestigationSettings.From(_settings);

        /// <inheritdoc />
        public string Prompt => _prompt;

        /// <inheritdoc />
        public string MissingCompanionHint => _missingCompanionHint;

        /// <inheritdoc />
        public string CompletedText => _completedText;

        /// <summary>設定 Data（Scene 検査用）。</summary>
        public InvestigationSettingsData SettingsData => _settings;

        /// <summary>調査位置の Transform（Scene 検査用。未設定なら null）。</summary>
        public Transform ApproachAnchor => _approachAnchor;

        /// <summary>配置時の設定（Scene 構築・テスト）。</summary>
        public void Configure(
            StableId pointId, StableId requiredCompanion, StableId discoveryId, InvestigationSettingsData settings,
            Transform approachAnchor = null, Vector3 approachFacing = default)
        {
            _pointId = pointId;
            _requiredCompanion = requiredCompanion;
            _discoveryId = discoveryId;
            _settings = settings;
            _approachAnchor = approachAnchor;
            _approachFacing = approachFacing;
        }

        /// <summary>短文の設定（Scene 構築・テスト）。</summary>
        public void ConfigureText(string prompt, string missingCompanionHint, string completedText)
        {
            _prompt = prompt ?? string.Empty;
            _missingCompanionHint = missingCompanionHint ?? string.Empty;
            _completedText = completedText ?? string.Empty;
        }

        private void OnEnable()
        {
            InvestigationPointRegistry.Register(this);
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱でレジストリに参照を残さない（§2.3 後始末）。記録は消さない（§10.3）。
            InvestigationPointRegistry.Unregister(this);
        }

        private void OnDrawGizmos()
        {
            // Scene ビュー用の補助（実機の表示は Presentation のマーカーが担う。Gizmo だけに依存しない）。
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.4f);
            Vector3 approach = ApproachPosition;
            Gizmos.DrawLine(transform.position + Vector3.up * 0.2f, approach + Vector3.up * 0.2f);
            Gizmos.DrawWireCube(approach + Vector3.up * 0.1f, new Vector3(0.3f, 0.2f, 0.3f));
        }
    }
}
