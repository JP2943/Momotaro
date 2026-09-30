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

        [Tooltip("Interact 1 回で遷移を要求する扉（§6.1 の 2 行目）。駆動側が要求を取りに来る対象。")]
        [SerializeField] private List<Interaction.AreaTransitionDoor> _transitionDoors =
            new List<Interaction.AreaTransitionDoor>();

        [Tooltip("門を開けるレバー（§7.3）。開通したら経路を更新する対象（§10.1）。")]
        [SerializeField] private List<Interaction.AreaFlagLever> _levers = new List<Interaction.AreaFlagLever>();

        [Tooltip("接続口をふさぐ見えない境界（工程 P55-14b）。")]
        [SerializeField] private List<AreaSeamBarrier> _seamBarriers = new List<AreaSeamBarrier>();

        [Tooltip("この Area の遭遇 Trigger（§8.2。入場のたびに範囲内を測り直す相手）。")]
        [SerializeField] private List<Encounter.AreaEncounterTrigger> _encounterTriggers =
            new List<Encounter.AreaEncounterTrigger>();

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

        /// <summary>Interact で遷移を要求する扉（読み取り専用。§6.1）。</summary>
        public IReadOnlyList<Interaction.AreaTransitionDoor> TransitionDoors => _transitionDoors;

        /// <summary>門を開けるレバー（読み取り専用。§7.3）。</summary>
        public IReadOnlyList<Interaction.AreaFlagLever> Levers => _levers;

        /// <summary>接続口をふさぐ見えない境界（工程 P55-14b。§7.3）。</summary>
        public IReadOnlyList<AreaSeamBarrier> SeamBarriers => _seamBarriers;

        /// <summary>
        /// この Area の遭遇 Trigger（§8.2 手順 1。工程 P55-15b）。
        ///
        /// <b>出入口と同じ理由で明示参照を持つ。</b> 入場のたびに「範囲内」を測り直す相手なので、
        /// 探して回るのではなく<b>登録されているもの</b>を回す——
        /// 登録が抜けていれば Scene 検査が落ちる（探す形だと、抜けても静かに何もしない）。
        /// 戦闘の無い Area では空でよい。
        /// </summary>
        public IReadOnlyList<Encounter.AreaEncounterTrigger> EncounterTriggers => _encounterTriggers;

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
            List<Interaction.AreaFlagDoor> doors = null,
            List<Interaction.AreaTransitionDoor> transitionDoors = null,
            List<Interaction.AreaFlagLever> levers = null,
            List<AreaSeamBarrier> seamBarriers = null)
        {
            _definition = definition;
            _entryPoints = entryPoints ?? new List<AreaEntryPoint>();
            _exitGates = exitGates ?? new List<AreaExitGate>();
            _doors = doors ?? new List<Interaction.AreaFlagDoor>();
            _transitionDoors = transitionDoors ?? new List<Interaction.AreaTransitionDoor>();
            _levers = levers ?? new List<Interaction.AreaFlagLever>();
            _seamBarriers = seamBarriers ?? new List<AreaSeamBarrier>();
        }

        /// <summary>
        /// 遭遇 Trigger を登録する（Editor 専用。工程 P55-15b）。
        ///
        /// <b>別の入口にしたのは、作られる時機が違うから。</b> 遭遇 Trigger は
        /// <c>CreateAreaSystems</c> の中で作られるので、<see cref="EditorSet"/> を呼ぶ時点ではまだ居ない。
        /// </summary>
        public void EditorSetEncounterTriggers(List<Encounter.AreaEncounterTrigger> triggers)
        {
            _encounterTriggers = triggers ?? new List<Encounter.AreaEncounterTrigger>();
        }
#endif
    }
}
