using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy.Combat;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵攻撃予兆の仮表示アダプタ（Phase3 P3-04。§6.3）。<see cref="EnemyAttackController.Telegraph"/> の型付きイベントを購読し、
    /// 予兆（Prepare）は扇形、発射／判定（Active）は直線で Gizmo 表示する（種別ごとに色分け：通常=白／強=黄／ガード不能=赤／
    /// 投射=水色）。End／Cancel で消灯する。表示専用で Gameplay の時間・判定・結果には一切関与しない（イベントを描画するだけ）。
    /// Gizmo は Editor の Scene／Game ビュー（Gizmos 有効時）に描かれ、手動受入で予兆・判定・後隙・中断消去を目視できる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyTelegraphView : MonoBehaviour, IEnemyTelegraphListener
    {
        [Tooltip("予兆を購読する攻撃制御（未指定なら同 GameObject から取得）。")]
        [SerializeField] private EnemyAttackController _controller;

        [Tooltip("扇形・直線の表示半径（m）。")]
        [SerializeField] private float _displayRadius = 2f;

        [Tooltip("扇形の半角（度）。")]
        [SerializeField] private float _fanHalfAngle = 45f;

        private bool _showing;
        private EnemyTelegraphPhase _phase;
        private AttackTelegraph _kind;
        private Vector3 _position;
        private Vector3 _aim;

        /// <summary>予兆を表示中か（Prepare/Active 中。テスト・Debug 用）。</summary>
        public bool IsShowing => _showing;

        /// <summary>現在表示中の予兆段階。</summary>
        public EnemyTelegraphPhase CurrentPhase => _phase;

        /// <summary>現在表示中の攻撃種別。</summary>
        public AttackTelegraph CurrentKind => _kind;

        private void Awake()
        {
            if (_controller == null)
            {
                _controller = GetComponent<EnemyAttackController>();
            }
        }

        private void OnEnable()
        {
            if (_controller == null)
            {
                _controller = GetComponent<EnemyAttackController>();
            }

            _controller?.Telegraph.AddListener(this);
        }

        private void OnDisable()
        {
            _controller?.Telegraph.RemoveListener(this);
            _showing = false;
        }

        /// <inheritdoc />
        public void OnTelegraph(in EnemyTelegraphEvent telegraph)
        {
            _phase = telegraph.Phase;
            _kind = telegraph.Kind;
            _position = telegraph.Position;
            _aim = telegraph.AimDirection;
            // Prepare（Begin）・Active（Fire）は表示、End／Cancel で消灯（後隙明け・中断で予兆消去）。
            _showing = telegraph.Phase == EnemyTelegraphPhase.Begin || telegraph.Phase == EnemyTelegraphPhase.Fire;
        }

        /// <summary>種別ごとの表示色（色だけに依存しないが、識別を助ける）。</summary>
        public static Color KindColor(AttackTelegraph kind)
        {
            switch (kind)
            {
                case AttackTelegraph.Heavy: return new Color(1f, 0.85f, 0.1f); // 黄
                case AttackTelegraph.Unblockable: return new Color(1f, 0.2f, 0.2f); // 赤
                case AttackTelegraph.Projectile: return new Color(0.3f, 0.85f, 1f); // 水色
                case AttackTelegraph.AreaOfEffect: return new Color(1f, 0.4f, 1f); // 紫
                default: return Color.white; // 通常
            }
        }

        private void OnDrawGizmos()
        {
            if (!_showing)
            {
                return;
            }

            Vector3 origin = _position + Vector3.up * 0.5f;
            Vector3 fwd = _aim.sqrMagnitude > 1e-6f ? new Vector3(_aim.x, 0f, _aim.z).normalized : transform.forward;
            Gizmos.color = KindColor(_kind);

            if (_phase == EnemyTelegraphPhase.Begin)
            {
                // 予兆：扇形（危険範囲を線で示す）。
                Vector3 left = Quaternion.AngleAxis(-_fanHalfAngle, Vector3.up) * fwd;
                Vector3 right = Quaternion.AngleAxis(_fanHalfAngle, Vector3.up) * fwd;
                Gizmos.DrawLine(origin, origin + left * _displayRadius);
                Gizmos.DrawLine(origin, origin + right * _displayRadius);
                Gizmos.DrawLine(origin, origin + fwd * _displayRadius);
            }
            else if (_phase == EnemyTelegraphPhase.Fire)
            {
                // 発射／判定：直線＋着弾点。
                Gizmos.DrawLine(origin, origin + fwd * _displayRadius);
                Gizmos.DrawWireSphere(origin + fwd * _displayRadius, 0.3f);
            }
        }
    }
}
