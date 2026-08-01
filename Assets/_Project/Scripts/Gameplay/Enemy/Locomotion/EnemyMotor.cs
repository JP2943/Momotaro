using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Locomotion
{
    /// <summary>
    /// 敵の移動実行（Phase3 §5/§2.2）。XZ 平面を Rigidbody の linearVelocity で動かし、Y 軸回頭で向きを変える（認識コーンが
    /// 向きに追従）。壁は Phase 1 の衝突規則（Enemy↔Default 維持）で物理が停止・接線滑りを解決し、Player↔Enemy はすり抜ける。
    /// 経路不能（指示に対し実移動が乏しい）を検出して <see cref="IsBlocked"/> で通知する（Brain が停止・Debug 理由を出す）。
    /// 回転は X/Z を固定（転倒防止）し Y のみ許可する。通常移動に Transform 直接書換えは行わない。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class EnemyMotor : MonoBehaviour
    {
        private Rigidbody _body;
        private float _moveSpeed = 3.5f;
        private float _turnSpeedDeg = 360f;
        private float _stopRadius = 0.05f;

        private Vector3 _moveTarget;
        private bool _hasMoveTarget;
        private Vector3 _facing;
        private bool _hasFacing;

        private Vector3 _lastPos;
        private float _blockedTimer;

        /// <summary>指示に対して実移動が乏しい（壁等で詰まっている）か。</summary>
        public bool IsBlocked { get; private set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            ConfigureBody();
            _lastPos = _body != null ? _body.position : transform.position;
        }

        private void ConfigureBody()
        {
            if (_body == null)
            {
                return;
            }

            _body.useGravity = false;
            // 転倒防止：X/Z 回転を固定し、Y（回頭）だけ許可する。
            _body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>移動・回頭・停止半径のパラメータを設定する（アーキタイプ由来）。</summary>
        public void Configure(float moveSpeed, float turnSpeedDeg, float stopRadius)
        {
            _moveSpeed = moveSpeed;
            _turnSpeedDeg = turnSpeedDeg;
            _stopRadius = Mathf.Max(0.01f, stopRadius);
        }

        /// <summary>目標地点へ移動する（XZ）。停止半径以内では停止する。</summary>
        public void SetMoveTarget(Vector3 target)
        {
            _moveTarget = target;
            _hasMoveTarget = true;
        }

        /// <summary>移動を停止する（速度ゼロ）。</summary>
        public void Stop()
        {
            _hasMoveTarget = false;
        }

        /// <summary>向けたいワールド方向（XZ）。停止中も対象へ向き続けるために使う。</summary>
        public void SetFacing(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude > 1e-6f)
            {
                _facing = worldDirection.normalized;
                _hasFacing = true;
            }
        }

        private void FixedUpdate()
        {
            if (_body == null)
            {
                return;
            }

            Vector3 pos = _body.position;
            Vector3 velocity = _hasMoveTarget
                ? ApproachCalculator.DesiredVelocity(pos, _moveTarget, _moveSpeed, _stopRadius)
                : Vector3.zero;

            _body.linearVelocity = new Vector3(velocity.x, _body.linearVelocity.y, velocity.z);

            // 回頭：移動中は進行方向、停止中は指定 Facing へ。Y のみ回頭する。
            Vector3 faceDir = velocity.sqrMagnitude > 1e-6f ? velocity : (_hasFacing ? _facing : Vector3.zero);
            if (faceDir.sqrMagnitude > 1e-6f)
            {
                Quaternion target = Quaternion.LookRotation(new Vector3(faceDir.x, 0f, faceDir.z), Vector3.up);
                Quaternion next = Quaternion.RotateTowards(_body.rotation, target, _turnSpeedDeg * Time.fixedDeltaTime);
                _body.MoveRotation(next);
            }

            UpdateBlocked(pos, velocity);
        }

        private void UpdateBlocked(Vector3 previousPos, Vector3 commandedVelocity)
        {
            float moved = (_body.position - _lastPos).magnitude;
            _lastPos = _body.position;

            bool commanded = commandedVelocity.sqrMagnitude > 1e-4f;
            float expected = commandedVelocity.magnitude * Time.fixedDeltaTime;
            if (commanded && moved < expected * 0.25f)
            {
                _blockedTimer += Time.fixedDeltaTime;
            }
            else
            {
                _blockedTimer = 0f;
            }

            IsBlocked = _blockedTimer > 0.5f;
        }
    }
}
