using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// 主人公の行動（ステップ・通常攻撃・必殺技チャージ・必殺技発動）から型付き音刺激を発行する（Phase3 §4.2）。
    /// <see cref="PlayerStateController"/> の公開状態のみを読み取り、Presentation／Input／Animator へ依存しない。
    /// 立ち上がりエッジで 1 回だけ発行し、半径は <see cref="NoiseCatalog"/>（Table 8）に従う。Pause／会話中は発行しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNoiseEmitter : MonoBehaviour
    {
        [Tooltip("観測する主人公（未指定なら同 GameObject から取得）。")]
        [SerializeField] private PlayerStateController _player;

        private bool _wasStepping;
        private bool _wasCharging;
        private bool _wasActivating;
        private bool _wasAttacking;
        private int _lastAttackStage = -1;

        private void Awake()
        {
            if (_player == null)
            {
                _player = GetComponent<PlayerStateController>();
            }
        }

        private void Update()
        {
            if (_player == null || !IsGameplayActive())
            {
                return;
            }

            Vector3 pos = _player.WorldPosition;
            int sourceId = _player.gameObject.GetInstanceID();

            bool stepping = _player.IsStepping;
            if (stepping && !_wasStepping)
            {
                Emit(NoiseKind.Step, pos, sourceId);
            }

            _wasStepping = stepping;

            bool charging = _player.IsSpecialCharging;
            if (charging && !_wasCharging)
            {
                Emit(NoiseKind.SpecialCharge, pos, sourceId);
            }

            _wasCharging = charging;

            bool activating = _player.IsSpecialAttacking;
            if (activating && !_wasActivating)
            {
                Emit(NoiseKind.SpecialActivate, pos, sourceId);
            }

            _wasActivating = activating;

            // 通常攻撃：Attack へ入った時、および段が進むたびに 1 回（コンボ各段が音を出す）。
            bool attacking = _player.Current == PlayerState.Attack;
            if (attacking && (!_wasAttacking || _player.AttackStage != _lastAttackStage))
            {
                Emit(NoiseKind.Attack, pos, sourceId);
            }

            _wasAttacking = attacking;
            _lastAttackStage = _player.AttackStage;
        }

        private static void Emit(NoiseKind kind, Vector3 position, int sourceId)
        {
            NoiseChannel channel = NoiseBus.Channel;
            float radius = NoiseCatalog.Radius(kind);
            if (radius <= 0f)
            {
                return; // Movement 等、半径なしは発行しない。
            }

            channel.Publish(new NoiseStimulus(
                channel.NextStimulusId(), sourceId, position, radius, Time.time, kind, shareGeneration: 0));
        }

        private static bool IsGameplayActive()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return true; // モード未初期化（単体テスト等）は発行を許可する。
            }

            GameMode m = modes.Current;
            return m == GameMode.Exploration || m == GameMode.Combat;
        }
    }
}
