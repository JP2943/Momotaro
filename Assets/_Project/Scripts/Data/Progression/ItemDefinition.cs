using System;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Data.Progression
{
    /// <summary>
    /// 一般消耗品 1 種の定義（P6A。仕様 §7）。所持数の上限だけを持つ。
    /// 時限強化・属性等の効果は後続（P6B 以降）で足す。保存の検証は「この一覧に無い ID」を拒否する。
    /// </summary>
    [Serializable]
    public sealed class ItemDefinition
    {
        [Tooltip("アイテムの安定 ID（campaign 内で一意）。")]
        [SerializeField] private StableId _itemId;

        [Tooltip("所持数の上限（1 以上）。")]
        [SerializeField] private int _maxStack = 1;

        [Tooltip("表示名。")]
        [SerializeField] private string _displayName = string.Empty;

        /// <summary>アイテムの安定 ID。</summary>
        public StableId ItemId => _itemId;

        /// <summary>所持数の上限。</summary>
        public int MaxStack => _maxStack;

        /// <summary>表示名。</summary>
        public string DisplayName => _displayName ?? string.Empty;

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(StableId itemId, int maxStack, string displayName)
        {
            _itemId = itemId;
            _maxStack = maxStack;
            _displayName = displayName ?? string.Empty;
        }
#endif
    }
}
