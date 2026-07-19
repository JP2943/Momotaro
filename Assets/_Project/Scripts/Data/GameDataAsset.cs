using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data
{
    /// <summary>
    /// すべての Data Asset（ScriptableObject）の基底。仕様書 15.6 の共通フィールド
    /// （安定ID・表示名・説明・Version・Debug Note）を保持し、基本的な検証を提供する。
    /// Runtime 中に自身（元 Asset）を書き換えず、可変状態は別の Runtime State へ保持すること。
    /// </summary>
    public abstract class GameDataAsset : ScriptableObject, IValidatableData
    {
        [Header("Identity")]
        [SerializeField] private StableId _id;
        [SerializeField] private string _displayName;

        [Header("Meta")]
        [SerializeField, TextArea] private string _description;
        [SerializeField] private int _version = 1;
        [SerializeField, TextArea] private string _debugNote;

        /// <summary>安定ID。保存・参照の正本。</summary>
        public StableId Id => _id;

        /// <summary>表示名（IDとは分離）。</summary>
        public string DisplayName => _displayName;

        /// <summary>データ移行・診断用のバージョン。</summary>
        public int Version => _version;

        /// <inheritdoc />
        public virtual void Validate(DataValidationReport report)
        {
            if (_id.IsEmpty)
            {
                report.Error(name + ": Stable ID is empty.");
            }
            else if (!_id.IsValid)
            {
                report.Error(name + ": Invalid Stable ID format '" + _id.Value + "' (lowercase snake_case required).");
            }

            if (string.IsNullOrWhiteSpace(_displayName))
            {
                report.Warning(name + ": Display Name is empty.");
            }

            if (_version < 1)
            {
                report.Error(name + ": Version must be >= 1.");
            }
        }
    }
}
