using System;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using UnityEngine;

namespace Momotaro.Data.World
{
    /// <summary>
    /// エリアの入口 1 つ分の定義（P5-02。仕様書 v1.1 §3.1）。
    ///
    /// <b>座標は持たない。</b> 到着位置と代替配置候補は Scene 側の <c>AreaEntryPoint</c> が Transform として持ち、
    /// ここは「どの ID の入口が存在し、到着時にどちらを向くか」だけを定める。
    /// 座標を Data と Scene の両方に置くと、地形を動かしたときに黙って食い違うため。
    /// </summary>
    [Serializable]
    public sealed class AreaEntryDefinition
    {
        [Tooltip("入口の安定 ID（例 area_p5_a_start）。エリア内で一意。")]
        [SerializeField] private StableId _entryId;

        [Tooltip("到着した Actor が向く方向。復旧時だけ採取した元の向きへ戻す（§3.1）。")]
        [SerializeField] private CardinalDirection _facing = CardinalDirection.North;

        /// <summary>入口の安定 ID。</summary>
        public StableId EntryId => _entryId;

        /// <summary>到着時の向き。</summary>
        public CardinalDirection Facing => _facing;

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(StableId entryId, CardinalDirection facing)
        {
            _entryId = entryId;
            _facing = facing;
        }
#endif
    }
}
