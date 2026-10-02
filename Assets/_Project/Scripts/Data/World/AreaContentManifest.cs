using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.World
{
    /// <summary>
    /// エリアに置かれた「保存される対象」の ID 一覧（P6A-02。仕様 §10「未知 ID を拒否する」）。
    ///
    /// <b>保存の検証は Scene を読まずに行う。</b> Continue の時点では目的地の Scene はまだ無いので、
    /// 「このエリアにそんな遭遇戦・配置・門は無い」を Data だけで判断できるよう、Builder が Scene と同時にここを書く。
    /// Scene と一覧の一致は P6 の Validator が検査する（片方だけ直された状態を受入にしない）。
    /// </summary>
    [Serializable]
    public sealed class AreaContentManifest
    {
        [SerializeField] private List<StableId> _encounters = new List<StableId>();
        [SerializeField] private List<StableId> _bosses = new List<StableId>();
        [SerializeField] private List<StableId> _fieldPlacements = new List<StableId>();
        [SerializeField] private List<StableId> _pickups = new List<StableId>();
        [SerializeField] private List<StableId> _flags = new List<StableId>();
        [SerializeField] private List<StableId> _investigationPoints = new List<StableId>();

        /// <summary>遭遇戦 ID。</summary>
        public IReadOnlyList<StableId> Encounters => _encounters;

        /// <summary>ボス対象 ID（P6A の仮ボスは遭遇戦 ID と同じ）。</summary>
        public IReadOnlyList<StableId> Bosses => _bosses;

        /// <summary>普通敵の配置 ID。</summary>
        public IReadOnlyList<StableId> FieldPlacements => _fieldPlacements;

        /// <summary>配置物・宝箱の ID。</summary>
        public IReadOnlyList<StableId> Pickups => _pickups;

        /// <summary>仕掛けの FlagId。</summary>
        public IReadOnlyList<StableId> Flags => _flags;

        /// <summary>調査点の ID。</summary>
        public IReadOnlyList<StableId> InvestigationPoints => _investigationPoints;

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(List<StableId> encounters, List<StableId> bosses, List<StableId> fieldPlacements,
            List<StableId> pickups, List<StableId> flags, List<StableId> investigationPoints)
        {
            _encounters = encounters ?? new List<StableId>();
            _bosses = bosses ?? new List<StableId>();
            _fieldPlacements = fieldPlacements ?? new List<StableId>();
            _pickups = pickups ?? new List<StableId>();
            _flags = flags ?? new List<StableId>();
            _investigationPoints = investigationPoints ?? new List<StableId>();
        }
#endif
    }
}
