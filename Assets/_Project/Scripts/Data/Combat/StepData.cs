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

        [Header("Just Evade (P6C)")]
        [Tooltip("ジャスト回避の受付終端（ステップ開始からの秒。この秒数未満）。成功が起きるのは受付と無敵区間の重なりだけ。P6C 仕様 §3（0.12）。")]
        [SerializeField] private float _justEvadeWindowSeconds = 0.12f;
        [Tooltip("ジャスト回避成功で得る反撃強化の倍率。次に開始した通常攻撃の一段の HP ダメージ系統に 1 回掛ける。P6C 仕様 §3（1.5）。")]
        [SerializeField] private float _justEvadeCounterHpMultiplier = 1.5f;
        [Tooltip("反撃強化の有効時間（成功時からの Gameplay 秒。満了時刻未満に開始した段だけ強化）。P6C 仕様 §3（2.0）。")]
        [SerializeField] private float _justEvadeCounterSeconds = 2.0f;

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

        /// <summary>ジャスト回避の受付終端（ステップ開始からの秒。この秒数未満）。</summary>
        public float JustEvadeWindowSeconds => _justEvadeWindowSeconds;

        /// <summary>反撃強化の倍率（通常攻撃一段の HP 系統）。</summary>
        public float JustEvadeCounterHpMultiplier => _justEvadeCounterHpMultiplier;

        /// <summary>反撃強化の有効時間（Gameplay 秒）。</summary>
        public float JustEvadeCounterSeconds => _justEvadeCounterSeconds;

        /// <summary>
        /// ジャスト回避の設定が成立するか（P6C 仕様 §3）。受付終端は無敵開始より後・無敵終了以下（無敵開始前をジャスト扱いにしない、
        /// 通常無敵を広げない）。時間・倍率は有限、時間は正、倍率は 1 以上。成立しなければ理由を返す。
        /// </summary>
        public static bool TryValidateJustEvade(float invincibleStart, float invincibleEnd, float window, float multiplier,
            float seconds, out string reason)
        {
            if (!IsFinite(window) || !IsFinite(multiplier) || !IsFinite(seconds) || !IsFinite(invincibleStart) || !IsFinite(invincibleEnd))
            {
                reason = "JustEvade values must be finite.";
                return false;
            }

            if (window <= invincibleStart || window > invincibleEnd)
            {
                reason = "JustEvadeWindowSeconds (" + window + ") must satisfy InvincibleStart (" + invincibleStart
                    + ") < window <= InvincibleEnd (" + invincibleEnd + ").";
                return false;
            }

            if (multiplier < 1f)
            {
                reason = "JustEvadeCounterHpMultiplier must be >= 1.";
                return false;
            }

            if (seconds <= 0f)
            {
                reason = "JustEvadeCounterSeconds must be > 0.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

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

            if (!TryValidateJustEvade(_invincibleStartSeconds, _invincibleEndSeconds, _justEvadeWindowSeconds,
                    _justEvadeCounterHpMultiplier, _justEvadeCounterSeconds, out string reason))
            {
                report.Error(name + ": " + reason);
            }
        }
    }
}
