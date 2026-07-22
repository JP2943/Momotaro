using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>主人公（桃太郎）の基礎データ雛形（仕様書 3.8.1）。</summary>
    [CreateAssetMenu(fileName = "SO_Player_New", menuName = "Momotaro/Data/Character/Player Data", order = 0)]
    public sealed class PlayerData : CharacterData
    {
        [Header("Player")]
        [SerializeField] private int _maxStamina = 100;

        [Header("Stamina / Guard Break (Phase2 P2-07)")]
        [Tooltip("スタミナ回復速度（毎秒）。仕様書 3.2.1。")]
        [SerializeField] private float _staminaRegenPerSecond = 25f;
        [Tooltip("最後の消費から回復開始までの待機（秒）。")]
        [SerializeField] private float _staminaRegenDelaySeconds = 1.0f;
        [Tooltip("スタミナ 0 到達時の回復開始待機（秒。延長）。")]
        [SerializeField] private float _staminaZeroRegenDelaySeconds = 1.5f;
        [Tooltip("ガードブレイクの行動不能時間（秒）。仕様書 3.2。")]
        [SerializeField] private float _guardBreakSeconds = 1.5f;
        [Tooltip("ブレイク終了時に回復する最大スタミナ比（0.25 = 25%）。")]
        [SerializeField] private float _guardBreakRestoreRatio = 0.25f;
        [Tooltip("ブレイク中の被 HP ダメージ倍率。")]
        [SerializeField] private float _guardBreakHpMultiplier = 1.25f;

        /// <summary>最大スタミナ。</summary>
        public int MaxStamina => _maxStamina;

        /// <summary>スタミナ回復速度（毎秒）。</summary>
        public float StaminaRegenPerSecond => _staminaRegenPerSecond;

        /// <summary>回復開始までの待機（秒）。</summary>
        public float StaminaRegenDelaySeconds => _staminaRegenDelaySeconds;

        /// <summary>スタミナ 0 時の回復開始待機（秒）。</summary>
        public float StaminaZeroRegenDelaySeconds => _staminaZeroRegenDelaySeconds;

        /// <summary>ガードブレイクの行動不能時間（秒）。</summary>
        public float GuardBreakSeconds => _guardBreakSeconds;

        /// <summary>ブレイク終了時の回復比（0..1）。</summary>
        public float GuardBreakRestoreRatio => _guardBreakRestoreRatio;

        /// <summary>ブレイク中の被 HP ダメージ倍率。</summary>
        public float GuardBreakHpMultiplier => _guardBreakHpMultiplier;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_maxStamina <= 0)
            {
                report.Error(name + ": MaxStamina must be > 0.");
            }

            if (_staminaRegenPerSecond < 0f || _staminaRegenDelaySeconds < 0f || _staminaZeroRegenDelaySeconds < 0f)
            {
                report.Error(name + ": Stamina regen values must be >= 0.");
            }

            if (_guardBreakSeconds < 0f)
            {
                report.Error(name + ": GuardBreakSeconds must be >= 0.");
            }

            if (_guardBreakRestoreRatio < 0f || _guardBreakRestoreRatio > 1f)
            {
                report.Error(name + ": GuardBreakRestoreRatio must be within [0, 1].");
            }
        }
    }
}
