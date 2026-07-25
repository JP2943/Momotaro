using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 通常攻撃コンボの構成データ（Phase2 P2-03B）。段順に <see cref="AttackData"/> を並べ、先行入力（Buffer）
    /// 保持秒を持つ。挙動は Gameplay 側が本データを Snapshot 化して駆動し、SO 原本は実行時に書き換えない。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Player_AttackCombo", menuName = "Momotaro/Data/Combat/Player Attack Combo", order = 5)]
    public sealed class PlayerAttackComboData : GameDataAsset
    {
        [Tooltip("段順の攻撃データ（1 段目→…）。通常攻撃は 3 段。")]
        [SerializeField] private AttackData[] _stages;

        [Tooltip("次段の先行入力を保持する秒（試作 0.30、仕様書 §4.2）。")]
        [SerializeField] private float _bufferSeconds = 0.30f;

        /// <summary>段数。</summary>
        public int StageCount => _stages == null ? 0 : _stages.Length;

        /// <summary>先行入力保持秒。</summary>
        public float BufferSeconds => _bufferSeconds;

        /// <summary>段データ（読み取り専用）。</summary>
        public IReadOnlyList<AttackData> Stages => _stages;

        /// <summary>指定段（0 始まり）の攻撃データ。</summary>
        public AttackData Stage(int index) => _stages[index];

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);

            if (_stages == null || _stages.Length == 0)
            {
                report.Error(name + ": Combo must contain at least one stage.");
                return;
            }

            for (int i = 0; i < _stages.Length; i++)
            {
                if (_stages[i] == null)
                {
                    report.Error(name + ": Stage " + (i + 1) + " AttackData is not assigned.");
                }
            }

            if (_bufferSeconds <= 0f)
            {
                report.Error(name + ": BufferSeconds must be > 0.");
            }
        }
    }
}
