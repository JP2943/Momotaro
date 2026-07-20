using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の現在の表示方向を保持・更新する（Phase1 P1-04）。入力から <see cref="FacingResolver"/> で
    /// 4 方向を決定する。<see cref="IsLocked"/> が true の間は方向を固定する（ガード中: P1-07）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFacing : MonoBehaviour
    {
        [Range(0f, 1f)]
        [Tooltip("この大きさ未満の入力では向きを変えない（震え防止）。")]
        [SerializeField] private float _deadzone = 0.2f;

        [SerializeField] private FacingDirection _initial = FacingDirection.Down;

        private IPlayerInput _input;

        /// <summary>現在の表示方向。</summary>
        public FacingDirection Current { get; private set; }

        /// <summary>true の間は向きを固定する（ガード中・攻撃中など）。</summary>
        public bool IsLocked { get; set; }

        private void Awake()
        {
            Current = _initial;
        }

        /// <summary>
        /// 与えられた移動入力から 4 方向を明示的に確定する（Phase2 P2-02 受入修正）。
        /// 攻撃開始時に「その時点の Move 入力」で向きを固定するために用いる。<see cref="Update"/> と
        /// 呼び出し順に依存せず結果が定まるよう、ロック状態に関係なく確定し、以降は <see cref="IsLocked"/> で保持する。
        /// Deadzone 未満の入力では現在の向きを維持する。
        /// </summary>
        /// <param name="move">確定に用いる移動入力。</param>
        public void ConfirmFromInput(Vector2 move)
        {
            Current = FacingResolver.Resolve(move, Current, _deadzone);
        }

        private void Update()
        {
            if (_input == null)
            {
                _input = PlayerInputProvider.Current;
            }

            if (_input == null)
            {
                return;
            }

            Current = FacingUpdate.Resolve(IsLocked, _input.Move, Current, _deadzone);
        }
    }
}
