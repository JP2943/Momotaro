using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// エリア Scene の根（P5-02。仕様書 v1.1 §5.1「AreaRoot：AreaId、入口・カメラ領域・仕掛け・Encounter 定義の明示参照」）。
    ///
    /// P5-02 の時点で持つのは <b>AreaId と入口の明示参照</b>まで。
    /// カメラ領域は P5-06、仕掛けは P5-04、Encounter 定義は P5-07 で足す。
    /// 先回りして空の配線を置かない（`CLAUDE.md`「未使用機能の実処理は先回りして作らない」）。
    ///
    /// <b>Find* を使わない。</b> 入口は Inspector の明示参照で集める（`CLAUDE.md` の設計の約束）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaRoot : MonoBehaviour
    {
        [Tooltip("このエリアの定義（Data）。AreaId・Scene パス・入口一覧の正本。")]
        [SerializeField] private AreaDefinition _definition;

        [Tooltip("Scene 上の入口。Data の入口一覧と 1 対 1 で対応させる。")]
        [SerializeField] private List<AreaEntryPoint> _entryPoints = new List<AreaEntryPoint>();

        [Tooltip("開放出入口（§6.1）。到着時に一度武装解除する対象。")]
        [SerializeField] private List<AreaExitGate> _exitGates = new List<AreaExitGate>();

        [Tooltip("Flag で恒久開通する門（§7.3）。到着時に記録から開通状態を復元する対象。")]
        [SerializeField] private List<Interaction.AreaFlagDoor> _doors = new List<Interaction.AreaFlagDoor>();

        /// <summary>このエリアの定義。</summary>
        public AreaDefinition Definition => _definition;

        /// <summary>このエリアの安定 ID（定義が未配線なら空）。</summary>
        public StableId AreaId => _definition != null ? _definition.Id : default;

        /// <summary>Scene 上の入口（読み取り専用）。</summary>
        public IReadOnlyList<AreaEntryPoint> EntryPoints => _entryPoints;

        /// <summary>開放出入口（読み取り専用）。</summary>
        public IReadOnlyList<AreaExitGate> ExitGates => _exitGates;

        /// <summary>恒久開通する門（読み取り専用）。到着時の復元対象（§4.3／§7.3）。</summary>
        public IReadOnlyList<Interaction.AreaFlagDoor> Doors => _doors;

        /// <summary>指定 ID の入口を Scene から引く。</summary>
        public bool TryGetEntryPoint(StableId entryId, out AreaEntryPoint point)
        {
            for (int i = 0; i < _entryPoints.Count; i++)
            {
                AreaEntryPoint p = _entryPoints[i];
                if (p != null && p.EntryId.Equals(entryId))
                {
                    point = p;
                    return true;
                }
            }

            point = null;
            return false;
        }

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(
            AreaDefinition definition,
            List<AreaEntryPoint> entryPoints,
            List<AreaExitGate> exitGates = null,
            List<Interaction.AreaFlagDoor> doors = null)
        {
            _definition = definition;
            _entryPoints = entryPoints ?? new List<AreaEntryPoint>();
            _exitGates = exitGates ?? new List<AreaExitGate>();
            _doors = doors ?? new List<Interaction.AreaFlagDoor>();
        }
#endif
    }
}
