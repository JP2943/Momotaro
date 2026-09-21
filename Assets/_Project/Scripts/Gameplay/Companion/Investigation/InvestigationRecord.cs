using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>調査済み記録の読み取り契約（v1.0 §10.3「記録へのアクセスを狭い契約で分離」）。</summary>
    public interface IInvestigationRecord
    {
        bool IsInvestigated(StableId pointId);
    }

    /// <summary>
    /// 純粋 C# の Scene 単位の調査済み記録（P4-07A。v1.0 §10.3）。
    /// 地点の Disable／再 Enable では消えない。Scene 再読込（Retry）で新しく作られる＝初期化される。
    /// P5 で外部の進行 State へ注入先を変えるときは、<see cref="IInvestigationRecord"/> の実装を差し替える。
    /// </summary>
    public sealed class InvestigationRecord : IInvestigationRecord
    {
        private readonly HashSet<StableId> _investigated = new HashSet<StableId>();

        public int Count => _investigated.Count;

        public bool IsInvestigated(StableId pointId) => _investigated.Contains(pointId);

        /// <summary>調査済みにする。既に済んでいれば false（完了は 1 回だけ。§6.3）。</summary>
        public bool TryMarkInvestigated(StableId pointId)
        {
            if (pointId.IsEmpty)
            {
                return false;
            }

            return _investigated.Add(pointId);
        }

        public void Clear() => _investigated.Clear();
    }
}
