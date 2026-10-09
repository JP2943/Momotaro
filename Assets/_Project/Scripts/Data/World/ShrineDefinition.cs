using System;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.World
{
    /// <summary>
    /// お地蔵様 1 体の定義（P6A。P6A-00 の判断 5）。
    ///
    /// <b>復帰点は入口と同じ「点」で解決する。</b> 各お地蔵様は所属 Area の入口（<see cref="AreaEntryDefinition"/>）を
    /// 1 つ持ち、死亡再開・旅立ち・Continue は既存の (AreaId, EntryId) 経路で着く。
    /// 名前だけの空 EntryId で代用しない（P6 仕様 §6 末尾）。入口の位置は Scene の <c>AreaEntryPoint</c> が持ち、
    /// 安全配置（壁内・未開通側・遭遇戦の自動開始範囲を避ける）は Validator が検査する。
    /// </summary>
    [Serializable]
    public sealed class ShrineDefinition
    {
        [Tooltip("お地蔵様の安定 ID（campaign 内で一意）。")]
        [SerializeField] private StableId _shrineId;

        [Tooltip("所属するエリア。")]
        [SerializeField] private StableId _areaId;

        [Tooltip("復帰点として使う入口（所属エリアの入口であること）。")]
        [SerializeField] private StableId _entryId;

        [Tooltip("旅立ちの行き先一覧に出す名前。")]
        [SerializeField] private string _displayName = string.Empty;

        /// <summary>お地蔵様の安定 ID。</summary>
        public StableId ShrineId => _shrineId;

        /// <summary>所属エリア。</summary>
        public StableId AreaId => _areaId;

        /// <summary>復帰点の入口。</summary>
        public StableId EntryId => _entryId;

        /// <summary>表示名。</summary>
        public string DisplayName => _displayName ?? string.Empty;

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(StableId shrineId, StableId areaId, StableId entryId, string displayName)
        {
            _shrineId = shrineId;
            _areaId = areaId;
            _entryId = entryId;
            _displayName = displayName ?? string.Empty;
        }
#endif
    }
}
