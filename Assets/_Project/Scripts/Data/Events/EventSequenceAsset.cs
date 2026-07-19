using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.Events
{
    /// <summary>
    /// 複合イベント（メイン・サブ・加入・ボス・幕間等）の定義雛形（仕様書 13.1）。
    /// 型付き EventStepData の列は Ch.13 実装 Phase で追加する。Phase 0 では枠のみ。
    /// </summary>
    [CreateAssetMenu(fileName = "SO_Event_New", menuName = "Momotaro/Data/Events/Event Sequence Asset", order = 1)]
    public sealed class EventSequenceAsset : GameDataAsset
    {
        [Header("Event Sequence")]
        [SerializeField] private int _chapter;
        [SerializeField] private string _sceneName;
        [SerializeField] private StableId _debugStartStepId;
        [SerializeField] private StableId _completedFlagId;

        /// <summary>対象章番号。</summary>
        public int Chapter => _chapter;

        /// <summary>完了フラグの Stable ID。</summary>
        public StableId CompletedFlagId => _completedFlagId;

        /// <inheritdoc />
        public override void Validate(DataValidationReport report)
        {
            base.Validate(report);
            if (_chapter < 0)
            {
                report.Error(name + ": Chapter must be >= 0.");
            }

            if (!_completedFlagId.IsEmpty && !_completedFlagId.IsValid)
            {
                report.Error(name + ": CompletedFlagId has invalid format '" + _completedFlagId.Value + "'.");
            }
        }
    }
}
