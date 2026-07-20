using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player Prefab（PF_Player_Momotaro）の Root 階層の参照点となる薄いコンポーネント（仕様書 15.6 / Phase1 P1-01）。
    /// Visual・Collider・Physics・Shadow の責務を分離し、後続の移動・表示・戦闘が安全に参照できる構造を提供する。
    ///
    /// Phase 1 の P1-01 では参照保持と構造検証のみを担い、移動・入力・Animator・戦闘は持たない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRoot : MonoBehaviour
    {
        [Header("Physics")]
        [Tooltip("Player 全体を動かす Rigidbody（CharacterRoot 上）。回転は固定する。")]
        [SerializeField] private Rigidbody _body;

        [Tooltip("衝突形状。PhysicsRoot 側に置き、Visual と分離する。")]
        [SerializeField] private CapsuleCollider _collider;

        [Header("Sub Roots")]
        [Tooltip("衝突・物理に関わるノード（Collider を保持）。")]
        [SerializeField] private Transform _physicsRoot;

        [Tooltip("見た目を載せるノード。Collider とは別 GameObject にする。")]
        [SerializeField] private Transform _visualRoot;

        [Tooltip("影表現を載せるノード。")]
        [SerializeField] private Transform _shadowRoot;

        /// <summary>Player 本体の Rigidbody。</summary>
        public Rigidbody Body => _body;

        /// <summary>衝突形状。</summary>
        public CapsuleCollider Collider => _collider;

        /// <summary>見た目ノード。Presentation 層がここへ Visual を接続する。</summary>
        public Transform VisualRoot => _visualRoot;

        /// <summary>影ノード。</summary>
        public Transform ShadowRoot => _shadowRoot;

        /// <summary>
        /// Prefab 構造が P1-01 の要件を満たすか検証する。Editor 検査・自動テストから利用する。
        /// </summary>
        /// <param name="error">不備があればその説明。無ければ空文字。</param>
        /// <returns>構造が妥当なら true。</returns>
        public bool HasValidStructure(out string error)
        {
            if (_body == null)
            {
                error = "Rigidbody (_body) is not assigned.";
                return false;
            }

            if (_collider == null)
            {
                error = "CapsuleCollider (_collider) is not assigned.";
                return false;
            }

            if (_physicsRoot == null || _visualRoot == null || _shadowRoot == null)
            {
                error = "PhysicsRoot / VisualRoot / ShadowRoot must all be assigned.";
                return false;
            }

            const RigidbodyConstraints freezeRotation =
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
            if ((_body.constraints & freezeRotation) != freezeRotation)
            {
                error = "Rigidbody rotation must be frozen on X, Y and Z.";
                return false;
            }

            // Collider と Visual が別 GameObject に分離されていること。
            if (_collider.gameObject == _visualRoot.gameObject)
            {
                error = "Collider and VisualRoot must be on separate GameObjects.";
                return false;
            }

            error = string.Empty;
            return true;
        }
    }
}
