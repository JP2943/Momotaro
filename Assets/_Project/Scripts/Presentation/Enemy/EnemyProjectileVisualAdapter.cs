using Momotaro.Gameplay.Enemy.Combat.Projectile;
using UnityEngine;

namespace Momotaro.Presentation.Enemy
{
    /// <summary>
    /// 敵 Projectile の 4 方向スプライト表示（Phase3 P3-08 受入修正）。Gameplay Root を 3D 回転させず、進行方向を
    /// <see cref="EnemyFacingResolver"/> で down／left／right／up に量子化し、対応するスプライトへ切り替える。板状 Sprite の
    /// カメラ正対は VisualRoot の <see cref="Characters.CameraFacingBillboard"/> が担う。負 Scale・flipX・Collider 回転は使わない
    /// （方向は方向別スプライトで表現）。読み取りのみで Gameplay へ干渉しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyProjectileVisualAdapter : MonoBehaviour
    {
        [Tooltip("方向を読む Projectile（未指定なら親から取得）。")]
        [SerializeField] private EnemyProjectile _projectile;

        [Tooltip("表示先 SpriteRenderer（VisualRoot 配下）。")]
        [SerializeField] private SpriteRenderer _renderer;

        [Tooltip("正面（手前/down）スプライト。")]
        [SerializeField] private Sprite _down;
        [Tooltip("奥（up）スプライト。")]
        [SerializeField] private Sprite _up;
        [Tooltip("左（left）スプライト。")]
        [SerializeField] private Sprite _left;
        [Tooltip("右（right）スプライト。")]
        [SerializeField] private Sprite _right;

        private EnemyVisualFacing _current = (EnemyVisualFacing)(-1);

        private void Awake()
        {
            if (_projectile == null)
            {
                _projectile = GetComponentInParent<EnemyProjectile>();
            }
        }

        private void LateUpdate()
        {
            if (_projectile == null || _renderer == null)
            {
                return; // Presentation 欠落でも Gameplay は進行する。
            }

            EnemyVisualFacing facing = EnemyFacingResolver.FromForward(_projectile.Direction);
            if (facing == _current)
            {
                return; // 変化時のみ差し替え。
            }

            _current = facing;
            Sprite s = Pick(facing, _down, _up, _left, _right);
            if (s != null)
            {
                _renderer.sprite = s;
            }
        }

        /// <summary>方向量子化（<see cref="EnemyFacingResolver.FromForward"/>）に対応するスプライトを選ぶ純粋関数（テスト用）。</summary>
        public static Sprite Pick(EnemyVisualFacing facing, Sprite down, Sprite up, Sprite left, Sprite right)
        {
            switch (facing)
            {
                case EnemyVisualFacing.Up: return up;
                case EnemyVisualFacing.Left: return left;
                case EnemyVisualFacing.Right: return right;
                default: return down;
            }
        }
    }
}
