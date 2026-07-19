using UnityEngine;

namespace Momotaro.Data.Characters
{
    /// <summary>敵の基礎データ雛形（仕様書 6 章）。体幹値やボスフラグを持つ。</summary>
    [CreateAssetMenu(fileName = "SO_Enemy_New", menuName = "Momotaro/Data/Character/Enemy Data", order = 2)]
    public sealed class EnemyData : CharacterData
    {
        [Header("Enemy")]
        [SerializeField] private float _poiseMax = 100f;
        [SerializeField] private bool _isBoss;

        /// <summary>体幹の最大値。</summary>
        public float PoiseMax => _poiseMax;

        /// <summary>ボスか。</summary>
        public bool IsBoss => _isBoss;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_poiseMax <= 0f)
            {
                report.Error(name + ": PoiseMax must be > 0.");
            }
        }
    }
}
