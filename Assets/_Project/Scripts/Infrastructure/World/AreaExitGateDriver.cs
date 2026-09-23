using Momotaro.Gameplay.Player;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// 開放出入口を駆動する（P5-03b。仕様書 v1.1 §6.1）。
    ///
    /// 主人公の移動入力を出入口へ配り、0.15 秒の連続入力が成立したら遷移を要求する。
    /// <b>判定そのものは <see cref="AreaExitGate"/> が持つ</b>ので、ここは入力の供給と
    /// 遷移サービスへの橋渡しだけ。Gameplay 時計が止まっていれば Gate 側が自分で何もしない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaExitGateDriver : MonoBehaviour
    {
        [Tooltip("この Area の根（出入口の明示参照を持つ）。")]
        [SerializeField] private AreaRoot _areaRoot;

        [Tooltip("主人公。移動入力と範囲判定に使う。")]
        [SerializeField] private PlayerStateController _player;

        /// <summary>配線する（Area 初期化担当・テストが呼ぶ）。</summary>
        public void Bind(AreaRoot areaRoot, PlayerStateController player)
        {
            _areaRoot = areaRoot;
            _player = player;
        }

        private void Update()
        {
            Tick(Time.deltaTime, ResolveMoveInput());
        }

        /// <summary>時間と入力を注入して進める（テストから直接駆動できるよう分離）。</summary>
        public void Tick(float deltaTime, Vector3 moveInput)
        {
            if (_areaRoot == null)
            {
                return;
            }

            AreaTransitionService transitions = BootstrapServices.Get<AreaTransitionService>();
            if (transitions == null)
            {
                return;
            }

            // Interact で置かれた扉の要求を先に取りに行く（§6.1 の 2 行目）。
            // 押下は 1 フレームの出来事なので、連続入力の溜めより先に見る。
            for (int i = 0; i < _areaRoot.TransitionDoors.Count; i++)
            {
                Momotaro.Gameplay.Interaction.AreaTransitionDoor door = _areaRoot.TransitionDoors[i];
                if (door == null || !door.ConsumePendingRequest())
                {
                    continue;
                }

                transitions.TryTravel(door.DestinationAreaId, door.DestinationEntryId);
                return; // 1 フレームに 1 件だけ。
            }

            for (int i = 0; i < _areaRoot.ExitGates.Count; i++)
            {
                AreaExitGate gate = _areaRoot.ExitGates[i];
                if (gate == null || !gate.Tick(deltaTime, moveInput))
                {
                    continue;
                }

                // 受付条件（§6.1）は遷移サービス側が見る。ここは要求するだけ。
                transitions.TryTravel(gate.DestinationAreaId, gate.DestinationEntryId);
                return; // 1 フレームに 1 件だけ。
            }
        }

        private Vector3 ResolveMoveInput()
        {
            if (_player == null || PlayerInputProvider.Current == null)
            {
                return Vector3.zero;
            }

            Vector2 move = PlayerInputProvider.Current.Move;
            return new Vector3(move.x, 0f, move.y);
        }
    }
}
