using System.Collections.Generic;

namespace Momotaro.Gameplay.Enemy.Slots
{
    /// <summary>
    /// Encounter 単位の包囲（Surround）調停（Phase3 P3-07。§8.1「Slot なしの敵は包囲・距離調整」）。交戦中の敵を登録順で保持し、
    /// 各敵に連番インデックスと総数を与える。<see cref="SurroundRing"/> がこれを用いて対象周囲へ均等配置し、待機敵が単縦列で並ばず
    /// 取り囲むようにする（攻撃 Slot は 1 体でも、周囲を囲んで順番待ちする）。純粋・決定的で Unity 非依存（EditMode 再現可能）。
    /// </summary>
    public sealed class SurroundCoordinator
    {
        private readonly List<int> _members = new List<int>(8);

        /// <summary>現在の包囲参加数。</summary>
        public int Count => _members.Count;

        /// <summary>交戦中の敵を登録する（重複は無視）。</summary>
        public void Register(int ownerId)
        {
            if (ownerId != 0 && !_members.Contains(ownerId))
            {
                _members.Add(ownerId);
            }
        }

        /// <summary>包囲参加から外す（帰還・無効化）。</summary>
        public void Unregister(int ownerId) => _members.Remove(ownerId);

        /// <summary>この敵の包囲インデックス（登録順。均等配置の角度に用いる）。未登録は false。</summary>
        public bool TryGetIndex(int ownerId, out int index)
        {
            index = _members.IndexOf(ownerId);
            return index >= 0;
        }

        /// <summary>全参加を消去する（Encounter 終了・再試行）。</summary>
        public void Clear() => _members.Clear();
    }
}
