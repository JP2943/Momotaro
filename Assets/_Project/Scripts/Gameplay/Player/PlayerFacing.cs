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

        /// <summary>true の間は向きを固定する（ガード中など）。</summary>
        public bool IsLocked { get; set; }

        private void Awake()
        {
            Current = _initial;
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
