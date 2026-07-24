using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>ステップ回避のパラメータ雛形（仕様書 3.4 / Phase2 P2-09）。</summary>
    [CreateAssetMenu(fileName = "SO_Step_New", menuName = "Momotaro/Data/Combat/Step Data", order = 2)]
    public sealed class StepData : GameDataAsset
    {
        [Header("Step")]
        [Tooltip("移動距離（Data 化。仕様書 3.4）。")]
        [SerializeField] private float _distance = 3f;
        [Tooltip("移動フェーズ秒（この間に距離を移動）。総動作 0.30 のうち移動 0.20。")]
        [SerializeField] private float _moveSeconds = 0.20f;
        [Tooltip("後硬直秒。移動後の硬直。仕様書 3.4（0.10）。")]
        [SerializeField] private float _recoverySeconds = 0.10f;
        [Tooltip("無敵開始秒（開始からの経過）。仕様書 3.4（0.05）。")]
        [SerializeField] private float _invincibleStartSeconds = 0.05f;
        [Tooltip("無敵終了秒（開始からの経過）。仕様書 3.4（0.20）。")]
        [SerializeField] private float _invincibleEndSeconds = 0.20f;
        [Tooltip("消費スタミナ。仕様書 3.4（25）。")]
        [SerializeField] private float _staminaCost = 25f;
        [Tooltip("終了直前の先行入力窓秒（連続ステップ／通常攻撃 1 段目への接続）。")]
        [SerializeField] private float _chainBufferSeconds = 0.12f;

        /// <summary>移動距離。</summary>
        public float Distance => _distance;

        /// <summary>移動フェーズ秒。</summary>
        public float MoveSeconds => _moveSeconds;

        /// <summary>後硬直秒。</summary>
        public float RecoverySeconds => _recoverySeconds;

        /// <summary>総動作秒（移動＋後硬直）。</summary>
        public float TotalSeconds => _moveSeconds + _recoverySeconds;

        /// <summary>無敵開始秒。</summary>
        public float InvincibleStartSeconds => _invincibleStartSeconds;

        /// <summary>無敵終了秒。</summary>
        public float InvincibleEndSeconds => _invincibleEndSeconds;

        /// <summary>消費スタミナ。</summary>
        public float StaminaCost => _staminaCost;

        /// <summary>終了直前の先行入力窓秒。</summary>
        public float ChainBufferSeconds => _chainBufferSeconds;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_distance < 0f)
            {
                report.Error(name + ": Distance must be >= 0.");
            }

            if (_moveSeconds <= 0f)
            {
                report.Error(name + ": MoveSeconds must be > 0.");
            }

            if (_recoverySeconds < 0f)
            {
                report.Error(name + ": RecoverySeconds must be >= 0.");
            }

            if (_invincibleStartSeconds < 0f || _invincibleEndSeconds < _invincibleStartSeconds)
            {
                report.Error(name + ": Invincible window must satisfy 0 <= start <= end.");
            }

            if (_staminaCost < 0f)
            {
                report.Error(name + ": StaminaCost must be >= 0.");
            }
        }
    }
}
