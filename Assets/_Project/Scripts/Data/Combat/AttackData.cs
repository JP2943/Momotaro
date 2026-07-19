using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 攻撃データ雛形（仕様書 6.7）。判定・時間・数値・防御関連・予兆種別を保持する。
    /// AI コードへ固有値を直書きせず、本データへ集約する。詳細値は後続 Phase で拡張。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Attack_New", menuName = "Momotaro/Data/Combat/Attack Data", order = 0)]
    public sealed class AttackData : GameDataAsset
    {
        [Header("Timing / Range")]
        [SerializeField] private float _cooldownSeconds;
        [SerializeField] private float _useRange = 1.5f;
        [SerializeField] private float _useAngle = 60f;

        [Header("Numbers")]
        [SerializeField] private float _hpMultiplier = 1f;
        [SerializeField] private float _poiseDamage = 10f;
        [SerializeField] private float _flinchPower;
        [SerializeField] private float _guardStaminaCost = 10f;

        [Header("Defense / Telegraph")]
        [SerializeField] private bool _guardable = true;
        [SerializeField] private bool _justGuardable = true;
        [SerializeField] private bool _stepAvoidable = true;
        [SerializeField] private AttackTelegraph _telegraph = AttackTelegraph.Normal;

        /// <summary>HP技倍率。</summary>
        public float HpMultiplier => _hpMultiplier;

        /// <summary>予兆種別。</summary>
        public AttackTelegraph Telegraph => _telegraph;

        /// <summary>通常ガード可能か。</summary>
        public bool Guardable => _guardable;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_hpMultiplier < 0f)
            {
                report.Error(name + ": HpMultiplier must be >= 0.");
            }

            if (_cooldownSeconds < 0f)
            {
                report.Error(name + ": CooldownSeconds must be >= 0.");
            }

            if (_telegraph == AttackTelegraph.Unblockable && _guardable)
            {
                report.Warning(name + ": Unblockable telegraph but Guardable is true.");
            }
        }
    }
}
