using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Locomotion
{
    /// <summary>
    /// 敵の移動実行（Phase3 §5/§2.2）。XZ 平面を Rigidbody の linearVelocity で動かす。壁は Phase 1 の衝突規則
    /// （Enemy↔Default 維持）で物理が停止・接線滑りを解決し、Player↔Enemy はすり抜ける。経路不能（指示に対し実移動が
    /// 乏しい）を検出して <see cref="IsBlocked"/> で通知する（Brain が停止・Debug 理由を出す）。
    /// 向きはルート Transform を回さず、論理値として <see cref="EnemyActor.SetFacing"/> に渡す（認識コーン・攻撃照準・4 方向
    /// スプライトが参照）。ルートは接地・Collider 安定のため回転を全固定（転倒・押し出しによる姿勢崩れ防止）し、さらに Y 位置を
    /// 固定して押し出し由来の浮き上がり（Collider が地面から離れ、主人公攻撃が空振りする不具合）を防ぐ。通常移動に Transform
    /// 直接書換えは行わない。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class EnemyMotor : MonoBehaviour
    {
        // 全回転固定＋Y 位置固定（FreezeRotationX|Y|Z=112 + FreezePositionY=4 = 116）。
        private const RigidbodyConstraints GroundedConstraints =
            RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

        private Rigidbody _body;
        private EnemyActor _actor;
        private float _moveSpeed = 3.5f;
        private float _stopRadius = 0.05f;

        private Vector3 _moveTarget;
        private bool _hasMoveTarget;
        private bool _charging;
        private float _chargeSpeed;
        private Vector3 _facing;
        private bool _hasFacing;

        private Vector3 _lastPos;
        private float _blockedTimer;

        /// <summary>指示に対して実移動が乏しい（壁等で詰まっている）か。</summary>
        public bool IsBlocked { get; private set; }

        /// <summary>突進中か（Debug/テスト用）。</summary>
        public bool IsCharging => _charging;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _actor = GetComponent<EnemyActor>();
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
            // ルートは接地基準：全回転を固定し、Y 位置も固定する（押し出しによる浮き上がりを防ぎ Collider を地面に保つ）。
            _body.constraints = GroundedConstraints;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        /// <summary>
        /// 移動・停止半径のパラメータを設定する（アーキタイプ由来）。<paramref name="turnSpeedDeg"/> はルートを回さなく
        /// なったため未使用だが、呼び出し側（Brain）の契約維持のため引数は残す（向きは論理値で即時反映する）。
        /// </summary>
        public void Configure(float moveSpeed, float turnSpeedDeg, float stopRadius)
        {
            _moveSpeed = moveSpeed;
            _ = turnSpeedDeg;
            _stopRadius = Mathf.Max(0.01f, stopRadius);
        }

        /// <summary>目標地点へ移動する（XZ）。停止半径以内では停止する。</summary>
        public void SetMoveTarget(Vector3 target)
        {
            _moveTarget = target;
            _hasMoveTarget = true;
            _charging = false;
        }

        /// <summary>
        /// 突進する（Phase3 P3-09。§9.3）。指定方向へ <paramref name="speed"/> で前進する。壁は Enemy↔Default 衝突で停止し貫通しない。
        /// 進行方向は攻撃側で早期固定した狙い方向を渡す。<see cref="Stop"/> で解除する。
        /// </summary>
        public void SetCharge(Vector3 target, float speed)
        {
            _moveTarget = target;
            _hasMoveTarget = true;
            _charging = true;
            _chargeSpeed = speed < 0f ? 0f : speed;
        }

        /// <summary>移動・突進を停止する（速度ゼロ）。</summary>
        public void Stop()
        {
            _hasMoveTarget = false;
            _charging = false;
        }

        /// <summary>向けたいワールド方向（XZ）。停止中も対象へ向き続けるために使う。ルートは回さず論理向きへ反映する。</summary>
        public void SetFacing(Vector3 worldDirection)
        {
            worldDirection.y = 0f;
            if (worldDirection.sqrMagnitude > 1e-6f)
            {
                _facing = worldDirection.normalized;
                _hasFacing = true;
                ApplyFacing(_facing); // 即時に論理向きへ反映（表示・照準の追従を遅らせない）。
            }
        }

        private void ApplyFacing(Vector3 dir)
        {
            if (_actor == null)
            {
                _actor = GetComponent<EnemyActor>();
            }

            _actor?.SetFacing(dir);
        }

        private void FixedUpdate()
        {
            if (_body == null)
            {
                return;
            }

            Vector3 pos = _body.position;
            float speed = _charging ? _chargeSpeed : _moveSpeed;
            Vector3 velocity = _hasMoveTarget
                ? ApproachCalculator.DesiredVelocity(pos, _moveTarget, speed, _stopRadius)
                : Vector3.zero;

            // XZ のみ駆動し Y 速度は 0（Y 位置は Rigidbody 制約でも固定。押し出しによる浮き上がりを二重に防ぐ）。
            _body.linearVelocity = new Vector3(velocity.x, 0f, velocity.z);

            // 向き：移動中は進行方向、停止中は指定 Facing。ルート Transform は回さず論理向きだけを更新する。
            Vector3 faceDir = velocity.sqrMagnitude > 1e-6f ? velocity : (_hasFacing ? _facing : Vector3.zero);
            if (faceDir.sqrMagnitude > 1e-6f)
            {
                ApplyFacing(new Vector3(faceDir.x, 0f, faceDir.z));
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
