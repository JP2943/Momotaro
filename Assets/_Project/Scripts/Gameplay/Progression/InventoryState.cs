using System.Collections.Generic;
using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Progression
{
    /// <summary>所持品の変更結果（P6 仕様 §7）。<see cref="Applied"/> 以外は全体無変更。</summary>
    public enum InventoryChangeResult
    {
        /// <summary>反映した。</summary>
        Applied = 0,

        /// <summary>ID が空・書式不正。</summary>
        InvalidItem = 1,

        /// <summary>個数が 0 以下。</summary>
        InvalidCount = 2,

        /// <summary>上限を超える（取得は全体拒否。旧案の「余剰破棄して初回権利も消費」は採らない）。</summary>
        OverCapacity = 3,

        /// <summary>足りない（消費）。</summary>
        NotEnough = 4,
    }

    /// <summary>
    /// 一般消耗品の所持数（P6A-01。仕様 §7）。純粋 C#。
    ///
    /// <b>P6A は所持数と原子的な増減の契約まで。</b> 時限強化・属性の効果、店、ドロップ経済は後続（仕様 §2）。
    /// 消費は個数を減らすだけで、休息・死亡で戻さない（コアループ「使用した消耗品は敗北しても戻らない」）。
    /// 上限は定義側（Data）が持ち、ここは渡された上限で判定する。
    /// </summary>
    public sealed class InventoryState
    {
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        /// <summary>種類数（診断・テスト用）。</summary>
        public int KindCount => _counts.Count;

        /// <summary>変化した（取得・消費・復元を除く）。Session が版を進める。</summary>
        internal System.Action Changed;

        /// <summary>所持数（未所持は 0）。</summary>
        public int CountOf(in StableId itemId)
        {
            return !itemId.IsEmpty && _counts.TryGetValue(itemId.Value, out int count) ? count : 0;
        }

        /// <summary>
        /// 加える。<b>上限を超えるなら何も変えない</b>（取得全体を拒否し、配置物は未取得のまま残す。仕様 §7）。
        /// </summary>
        public InventoryChangeResult CanAdd(in StableId itemId, int count, int maxStack)
        {
            if (itemId.IsEmpty || !itemId.IsValid)
            {
                return InventoryChangeResult.InvalidItem;
            }

            if (count <= 0)
            {
                return InventoryChangeResult.InvalidCount;
            }

            long next = (long)CountOf(itemId) + count;
            if (maxStack <= 0 || next > maxStack)
            {
                return InventoryChangeResult.OverCapacity;
            }

            return InventoryChangeResult.Applied;
        }

        /// <summary>加える（<see cref="CanAdd"/> が通るときだけ反映）。</summary>
        public InventoryChangeResult TryAdd(in StableId itemId, int count, int maxStack)
        {
            InventoryChangeResult check = CanAdd(itemId, count, maxStack);
            if (check != InventoryChangeResult.Applied)
            {
                return check;
            }

            _counts[itemId.Value] = CountOf(itemId) + count;
            Changed?.Invoke();
            return InventoryChangeResult.Applied;
        }

        /// <summary>消費する。足りなければ何も変えない。</summary>
        public InventoryChangeResult TryConsume(in StableId itemId, int count)
        {
            if (itemId.IsEmpty || !itemId.IsValid)
            {
                return InventoryChangeResult.InvalidItem;
            }

            if (count <= 0)
            {
                return InventoryChangeResult.InvalidCount;
            }

            int have = CountOf(itemId);
            if (have < count)
            {
                return InventoryChangeResult.NotEnough;
            }

            if (have == count)
            {
                _counts.Remove(itemId.Value);
            }
            else
            {
                _counts[itemId.Value] = have - count;
            }

            Changed?.Invoke();
            return InventoryChangeResult.Applied;
        }

        /// <summary>保存用に写す。</summary>
        public void CopyTo(List<KeyValuePair<string, int>> buffer)
        {
            buffer.Clear();
            foreach (KeyValuePair<string, int> pair in _counts)
            {
                buffer.Add(pair);
            }
        }

        /// <summary>保存から置く（検証済みの値。通知しない）。</summary>
        internal void RestoreFrom(IReadOnlyList<KeyValuePair<string, int>> items)
        {
            _counts.Clear();
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Value > 0)
                {
                    _counts[items[i].Key] = items[i].Value;
                }
            }
        }
    }
}
