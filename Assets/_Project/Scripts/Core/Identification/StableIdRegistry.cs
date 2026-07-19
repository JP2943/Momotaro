using System.Collections.Generic;
using Momotaro.Core.Logging;

namespace Momotaro.Core.Identification
{
    /// <summary>
    /// Stable ID の一意性を検証するレジストリ。登録時に不正書式と重複を検出し、
    /// Validation カテゴリのエラーとして報告する（仕様書 11.9）。
    /// プロジェクト全体の検査（P0-12）や読込時の重複検出に用いる。
    /// </summary>
    public sealed class StableIdRegistry
    {
        // id 値 -> 所有者（Asset 名など）
        private readonly Dictionary<string, string> _owners = new Dictionary<string, string>();

        /// <summary>登録済み ID 数。</summary>
        public int Count => _owners.Count;

        /// <summary>
        /// ID を登録する。書式不正または重複の場合は登録せず false を返し、理由を <paramref name="error"/> に格納する。
        /// </summary>
        /// <param name="id">登録する ID。</param>
        /// <param name="owner">所有者の識別（Asset 名など）。</param>
        /// <param name="error">失敗理由。成功時は null。</param>
        /// <returns>登録に成功した場合 true。</returns>
        public bool TryRegister(StableId id, string owner, out string error)
        {
            if (!id.IsValid)
            {
                error = "Invalid stable id format: '" + id.Value + "' (owner: " + owner + ")";
                GameLog.Error(LogCategory.Validation, error, id: id.Value);
                return false;
            }

            if (_owners.TryGetValue(id.Value, out string existing))
            {
                error = "Duplicate stable id '" + id.Value + "' (owner: " + owner + ", existing: " + existing + ")";
                GameLog.Error(LogCategory.Validation, error, id: id.Value);
                return false;
            }

            _owners.Add(id.Value, owner);
            error = null;
            return true;
        }

        /// <summary>指定 ID が登録済みか。</summary>
        public bool Contains(StableId id)
        {
            return _owners.ContainsKey(id.Value);
        }

        /// <summary>登録内容をすべて消去する。</summary>
        public void Clear()
        {
            _owners.Clear();
        }
    }
}
