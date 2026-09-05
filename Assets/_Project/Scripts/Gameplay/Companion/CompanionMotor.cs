using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の移動実行（P4-02）。<c>EnemyMotor</c> と同じ方針で、XZ 平面を Rigidbody の linearVelocity で動かす。
    /// ルートは接地と Collider の安定のため全回転を固定し、Y 位置も固定する（押し出しによる浮き上がりを防ぐ）。
    /// 壁（Default）とは衝突するため物理が停止・接線滑りを解決し、主人公・敵・仲間同士はすり抜ける（<c>CombatLayers</c>）。
    ///
    /// 「どこへ向かうか」「ワープすべきか」の判断は持たない。判断は <see cref="CompanionFollowModel"/>、指示は
    /// <see cref="CompanionFollowController"/> が行い、本コンポーネントは実行だけを担う。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CompanionMotor : MonoBehaviour
    {
        private const RigidbodyConstraints GroundedConstraints =
            RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

        private Rigidbody _body;
        private float _moveSpeed = 4.5f;
        private float _stopRadius = 0.05f;
        private Vector3 _moveTarget;
        private bool _hasMoveTarget;

        /// <summary>移動指示を受けているか（テスト・診断用）。</summary>
        public bool HasMoveTarget => _hasMoveTarget;

        /// <summary>これまでに実行したワープ回数（テスト・診断用）。</summary>
        public int WarpCount { get; private set; }

        /// <summary>現在の移動速度設定（m/s）。</summary>
        public float MoveSpeed => _moveSpeed;

        private void Awake()
        {
            EnsureBody();
        }

        private void EnsureBody()
        {
            if (_body != null)
            {
                return;
            }

            _body = GetComponent<Rigidbody>();
            if (_body == null)
            {
                return;
            }

            _body.useGravity = false;
            _body.constraints = GroundedConstraints;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>移動速度と停止半径を設定する（Data 由来）。停止半径は下限 0.01。</summary>
        public void Configure(float moveSpeed, float stopRadius)
        {
            _moveSpeed = moveSpeed < 0f ? 0f : moveSpeed;
            _stopRadius = Mathf.Max(0.01f, stopRadius);
        }

        /// <summary>目標地点へ移動する（XZ）。停止半径以内では速度を出さない。</summary>
        public void SetMoveTarget(Vector3 target)
        {
            _moveTarget = target;
            _hasMoveTarget = true;
        }

        /// <summary>移動を止める（速度ゼロ）。二重呼び出し安全。</summary>
        public void Stop()
        {
            _hasMoveTarget = false;
            EnsureBody();
            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 指定位置へ瞬間移動する（距離超過・経路失敗からの復帰）。高さは現在値を保ち（接地を崩さない）、
        /// 速度と移動指示を必ず消す（ワープ直後に残速度で滑らない）。
        /// </summary>
        public void WarpTo(Vector3 position)
        {
            EnsureBody();
            Vector3 destination = new Vector3(position.x, transform.position.y, position.z);

            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.position = destination;
            }

            transform.position = destination;
            _hasMoveTarget = false;
            WarpCount++;
        }

        private void FixedUpdate()
        {
            EnsureBody();
            if (_body == null)
            {
                return;
            }

            if (!_hasMoveTarget)
            {
                _body.linearVelocity = Vector3.zero;
                return;
            }

            Vector3 delta = _moveTarget - _body.position;
            delta.y = 0f;

            if (delta.sqrMagnitude <= _stopRadius * _stopRadius)
            {
                _body.linearVelocity = Vector3.zero;
                return;
            }

            _body.linearVelocity = delta.normalized * _moveSpeed;
        }

        private void OnDisable()
        {
            // Disable・Scene 離脱で速度と指示を残さない（§2.3 後始末）。
            _hasMoveTarget = false;
            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
            }
        }
    }
}
