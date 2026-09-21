using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using UnityEngine;

namespace Momotaro.Presentation.Companion
{
    /// <summary>
    /// 探索用の表示代理と進行ラベルの描画（P4-07B。v1.0 §5.2・§11）。
    ///
    /// 探索駆動（<see cref="CompanionInvestigationController"/>）が持つ代理の位置・向き・段を<b>読むだけ</b>で描く。
    /// 代理が出ている間は通常表示（<see cref="CompanionPlaceholderPresenter"/>）を抑制し、同時に 2 体描かない。
    /// 引き渡しが終わったら抑制を解き、表示の所有権を通常表示へ返す（もともと Down 等で非表示なら、そのまま）。
    ///
    /// 代理は戦闘 Actor・HP・Hurtbox・ヘイト・判定を一切持たない（本体と同じ仮素材を SpriteRenderer で描くだけ）。
    /// 進行段は色だけに頼らず文字（移動／調査中／帰還）で示す。本体で調べているときも同じラベルを頭上へ出す。
    /// 素材・参照が未割当でも無表示・無例外で継続する（既存方針。必須参照の欠落は Validator が拾う）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionInvestigationProxyPresenter : MonoBehaviour
    {
        [Tooltip("読む探索駆動（未設定なら同じ GameObject から自動取得）。")]
        [SerializeField] private CompanionInvestigationController _driver;

        [Tooltip("通常表示。代理が出ている間だけ抑制する（未設定なら同じ GameObject から自動取得）。")]
        [SerializeField] private CompanionPlaceholderPresenter _normal;

        [Tooltip("代理の置き場（この Transform を代理の位置へ動かす。配下に Billboard と本体シルエットを置く）。")]
        [SerializeField] private Transform _proxyAnchor;

        [Tooltip("代理の本体シルエット（本体と同じ仮素材）。")]
        [SerializeField] private SpriteRenderer _proxyBody;

        [Tooltip("代理の方向インジケータ（足元へ寝かせる）。")]
        [SerializeField] private SpriteRenderer _proxyArrow;

        [Tooltip("進行段のラベル（移動／調査中／帰還）。親の Transform を頭上へ動かす。")]
        [SerializeField] private TextMesh _label;

        [Tooltip("ラベルの高さ（m）。")]
        [SerializeField, Min(0f)] private float _labelHeight = 1.45f;

        [Tooltip("方向インジケータを浮かせる高さ（m）。")]
        [SerializeField, Min(0f)] private float _arrowHeight = 0.02f;

        private bool _shown;

        /// <summary>代理を描いているか（テスト・診断用）。</summary>
        public bool ProxyShown => _shown;

        /// <summary>いま出しているラベル文（空なら非表示。テスト用）。</summary>
        public string LabelText => _label != null && _label.gameObject.activeSelf ? _label.text : string.Empty;

        /// <summary>読んでいる探索駆動（配線確認用）。</summary>
        public CompanionInvestigationController Driver => _driver;

        /// <summary>抑制する通常表示（配線確認用）。</summary>
        public CompanionPlaceholderPresenter Normal => _normal;

        /// <summary>代理の置き場（配線確認用）。</summary>
        public Transform ProxyAnchor => _proxyAnchor;

        /// <summary>代理の本体（配線確認用）。</summary>
        public SpriteRenderer ProxyBody => _proxyBody;

        /// <summary>代理の方向インジケータ（配線確認用）。</summary>
        public SpriteRenderer ProxyArrow => _proxyArrow;

        /// <summary>進行ラベル（配線確認用）。</summary>
        public TextMesh Label => _label;

        /// <summary>参照を注入する（Prefab 構築・テスト。null は無視して既存を保つ）。</summary>
        public void Bind(
            CompanionInvestigationController driver, CompanionPlaceholderPresenter normal,
            Transform proxyAnchor = null, SpriteRenderer proxyBody = null, SpriteRenderer proxyArrow = null, TextMesh label = null)
        {
            if (driver != null)
            {
                _driver = driver;
            }

            if (normal != null)
            {
                _normal = normal;
            }

            if (proxyAnchor != null)
            {
                _proxyAnchor = proxyAnchor;
            }

            if (proxyBody != null)
            {
                _proxyBody = proxyBody;
            }

            if (proxyArrow != null)
            {
                _proxyArrow = proxyArrow;
            }

            if (label != null)
            {
                _label = label;
            }
        }

        private void OnEnable()
        {
            Resolve();
            Refresh();
        }

        private void OnDisable()
        {
            // 無効化で表示の所有権を必ず返す（代理を出したまま消えない）。
            SetProxyVisible(false);
            SetLabel(string.Empty, Vector3.zero);
            if (_normal != null)
            {
                _normal.Suppressed = false;
            }
        }

        private void LateUpdate()
        {
            Refresh();
        }

        /// <summary>駆動の状態を表示へ反映する（LateUpdate から呼ばれるが、テストは決定的に直接呼べる）。</summary>
        public void Refresh()
        {
            Resolve();
            if (_driver == null)
            {
                SetProxyVisible(false);
                SetLabel(string.Empty, Vector3.zero);
                return;
            }

            bool proxy = _driver.ProxyVisible && _driver.Mode == InvestigationMode.Proxy;

            // 通常表示の抑制は代理が出ている間だけ。解除しても通常表示側が状態（Down 等）に従って描く。
            if (_normal != null)
            {
                _normal.Suppressed = proxy;
            }

            SetProxyVisible(proxy);
            if (proxy)
            {
                Vector3 position = _driver.ProxyPosition;
                Transform anchor = _proxyAnchor != null ? _proxyAnchor : (_proxyBody != null ? _proxyBody.transform : null);
                if (anchor != null)
                {
                    anchor.position = position;
                }

                if (_proxyArrow != null)
                {
                    Transform arrow = _proxyArrow.transform;
                    arrow.position = new Vector3(position.x, position.y + _arrowHeight, position.z);
                    arrow.rotation = Quaternion.LookRotation(Vector3.up, _driver.ProxyFacing);
                }
            }

            // 進行ラベル：代理が出ていれば代理の頭上、本体で調べていれば本体の頭上。依頼が無ければ消す。
            if (_driver.IsBusy)
            {
                Vector3 anchor = proxy
                    ? _driver.ProxyPosition
                    : (_driver.BoundActor != null ? _driver.BoundActor.WorldPosition : transform.position);
                SetLabel(InvestigationTexts.PhaseLabel(_driver.Phase), anchor + Vector3.up * _labelHeight);
            }
            else
            {
                SetLabel(string.Empty, Vector3.zero);
            }
        }

        private void SetProxyVisible(bool visible)
        {
            _shown = visible;
            if (_proxyBody != null)
            {
                _proxyBody.enabled = visible;
                if (visible)
                {
                    _proxyBody.color = CompanionStateColors.Resolve(CompanionState.Investigate);
                }
            }

            if (_proxyArrow != null)
            {
                _proxyArrow.enabled = visible;
            }
        }

        private void SetLabel(string text, Vector3 worldPosition)
        {
            if (_label == null)
            {
                return;
            }

            bool show = !string.IsNullOrEmpty(text);
            if (_label.text != text)
            {
                _label.text = text;
            }

            if (_label.gameObject.activeSelf != show)
            {
                _label.gameObject.SetActive(show);
            }

            if (show)
            {
                Transform anchor = _label.transform.parent != null ? _label.transform.parent : _label.transform;
                anchor.position = worldPosition;
            }
        }

        private void Resolve()
        {
            if (_driver == null)
            {
                _driver = GetComponent<CompanionInvestigationController>();
            }

            if (_normal == null)
            {
                _normal = GetComponent<CompanionPlaceholderPresenter>();
            }
        }
    }
}
