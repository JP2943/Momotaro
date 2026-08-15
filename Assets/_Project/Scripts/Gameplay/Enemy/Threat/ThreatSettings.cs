using System;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Threat
{
    /// <summary>
    /// ヘイト評価の調整値（Phase3 P3-06。§7.1 加算表・§7.2 再評価/減衰）。行動ごとの重み・再評価間隔・切替閾値・減衰を保持する
    /// 直列化可能な設定。コード直書きを避け Inspector（<see cref="EnemyThreatTracker"/>）で調整できるようにし、EditMode テストは
    /// <see cref="Default"/> または任意値を注入して決定的に検証する。対象側の基礎ヘイト・獲得倍率は <see cref="IThreatTarget"/> が持つ。
    /// </summary>
    [Serializable]
    public struct ThreatSettings
    {
        [Tooltip("HP ダメージ 1 につき加算する脅威（§7.1: +1）。")]
        [SerializeField] private float _hpDamageWeight;

        [Tooltip("体幹ダメージ 1 につき加算する脅威（§7.1: +0.5）。")]
        [SerializeField] private float _poiseDamageWeight;

        [Tooltip("ひるみ 1 回につき加算する脅威（§7.1: +20）。")]
        [SerializeField] private float _flinchThreat;

        [Tooltip("ジャストガード 1 回につき加算する脅威（§7.1: +30）。")]
        [SerializeField] private float _justGuardThreat;

        [Tooltip("将来：犬の挑発の脅威（§7.1: +100。本 Phase では未発行）。")]
        [SerializeField] private float _dogTauntThreat;

        [Tooltip("将来：回復・強化術の脅威（§7.1: +20。本 Phase では未発行）。")]
        [SerializeField] private float _supportSkillThreat;

        [Tooltip("将来：敵弱体術の脅威（§7.1: +40。本 Phase では未発行）。")]
        [SerializeField] private float _debuffSkillThreat;

        [Tooltip("ターゲット再評価の間隔（秒）。§7.2: 1 秒ごと。")]
        [SerializeField] private float _reevaluateInterval;

        [Tooltip("対象切替に必要な比率。新対象が現対象の この倍 以上で切替。§7.2: 1.25（25% 高い）。")]
        [SerializeField] private float _switchThresholdRatio;

        [Tooltip("最後の獲得から減衰を開始するまでの遅延（秒）。§7.2: 3 秒。")]
        [SerializeField] private float _decayDelaySeconds;

        [Tooltip("減衰開始後、獲得ヘイトを毎秒この割合だけ減らす（基礎ヘイトは維持）。§7.2: 0.20（20%/秒）。")]
        [SerializeField] private float _decayRatePerSecond;

        /// <summary>HP ダメージ 1 あたりの脅威（§7.1: +1）。</summary>
        public float HpDamageWeight => _hpDamageWeight;
        /// <summary>体幹ダメージ 1 あたりの脅威（§7.1: +0.5）。</summary>
        public float PoiseDamageWeight => _poiseDamageWeight;
        /// <summary>ひるみ 1 回の脅威（§7.1: +20）。</summary>
        public float FlinchThreat => _flinchThreat;
        /// <summary>ジャストガード 1 回の脅威（§7.1: +30）。</summary>
        public float JustGuardThreat => _justGuardThreat;
        /// <summary>将来：犬の挑発の脅威（§7.1: +100）。</summary>
        public float DogTauntThreat => _dogTauntThreat;
        /// <summary>将来：支援術の脅威（§7.1: +20）。</summary>
        public float SupportSkillThreat => _supportSkillThreat;
        /// <summary>将来：弱体術の脅威（§7.1: +40）。</summary>
        public float DebuffSkillThreat => _debuffSkillThreat;
        /// <summary>再評価間隔（秒。§7.2: 1）。</summary>
        public float ReevaluateInterval => _reevaluateInterval;
        /// <summary>切替閾値比率（§7.2: 1.25）。</summary>
        public float SwitchThresholdRatio => _switchThresholdRatio;
        /// <summary>減衰開始遅延（秒。§7.2: 3）。</summary>
        public float DecayDelaySeconds => _decayDelaySeconds;
        /// <summary>減衰率（毎秒。§7.2: 0.20）。</summary>
        public float DecayRatePerSecond => _decayRatePerSecond;

        /// <summary>全項目を指定して生成する（テスト用）。</summary>
        public ThreatSettings(
            float hpDamageWeight, float poiseDamageWeight, float flinchThreat, float justGuardThreat,
            float dogTauntThreat, float supportSkillThreat, float debuffSkillThreat,
            float reevaluateInterval, float switchThresholdRatio, float decayDelaySeconds, float decayRatePerSecond)
        {
            _hpDamageWeight = hpDamageWeight;
            _poiseDamageWeight = poiseDamageWeight;
            _flinchThreat = flinchThreat;
            _justGuardThreat = justGuardThreat;
            _dogTauntThreat = dogTauntThreat;
            _supportSkillThreat = supportSkillThreat;
            _debuffSkillThreat = debuffSkillThreat;
            _reevaluateInterval = reevaluateInterval;
            _switchThresholdRatio = switchThresholdRatio;
            _decayDelaySeconds = decayDelaySeconds;
            _decayRatePerSecond = decayRatePerSecond;
        }

        /// <summary>仕様書 §7.1／§7.2 の試作値。</summary>
        public static ThreatSettings Default => new ThreatSettings(
            hpDamageWeight: 1f,
            poiseDamageWeight: 0.5f,
            flinchThreat: 20f,
            justGuardThreat: 30f,
            dogTauntThreat: 100f,
            supportSkillThreat: 20f,
            debuffSkillThreat: 40f,
            reevaluateInterval: 1f,
            switchThresholdRatio: 1.25f,
            decayDelaySeconds: 3f,
            decayRatePerSecond: 0.20f);

        /// <summary>由来種別に対応する 1 回あたりの基礎重みを返す（HP／体幹は「1 あたり」の係数）。</summary>
        public float WeightFor(ThreatSource source)
        {
            switch (source)
            {
                case ThreatSource.HpDamage: return _hpDamageWeight;
                case ThreatSource.PoiseDamage: return _poiseDamageWeight;
                case ThreatSource.Flinch: return _flinchThreat;
                case ThreatSource.JustGuard: return _justGuardThreat;
                case ThreatSource.DogTaunt: return _dogTauntThreat;
                case ThreatSource.SupportSkill: return _supportSkillThreat;
                case ThreatSource.DebuffSkill: return _debuffSkillThreat;
                default: return 0f;
            }
        }
    }
}
