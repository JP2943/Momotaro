using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// 加入資格の読み取り契約（P4-07A。v1.0 §5.1）。
    /// <b>戦闘状態とは分離する。</b><c>CompanionActor</c> が Scene に居るか、HP が正か、レジストリに居るかを加入判定に使わない。
    /// 加入済みなら Down／控え／離脱 CD 中でも探索できる（§4.3、E03）。
    /// </summary>
    public interface ICompanionAvailability
    {
        bool IsRecruited(StableId companionId);
    }
}
