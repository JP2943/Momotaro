using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// Scene に 1 つ置く記録の保持先。Retry で Scene ごと作り直されるため、記録は自然に初期化される。
    /// 探索の調停役（<see cref="InvestigationCoordinator"/>）が明示参照で読む（万能 static にしない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvestigationRecordHolder : MonoBehaviour
    {
        public InvestigationRecord Record { get; } = new InvestigationRecord();
    }
}
