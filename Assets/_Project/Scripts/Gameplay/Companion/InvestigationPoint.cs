using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 調査地点の契約（P4-07A）。仲間が「気になる」と判断して調べに行ける場所。
    ///
    /// <b>何が起きるかは地点側が決める</b>。仲間は「行って・調べた」と伝えるだけで、宝箱が開くのか
    /// 隠し通路が出るのかは知らない。P5 のマップ・探索基盤が本物の地点を用意するときに、
    /// この契約を実装するだけで仲間側は触らずに済む。
    ///
    /// 調査にかかる秒数は<b>地点ではなく仲間の Data</b>（<see cref="Data.Characters.CompanionData.InvestigateSeconds"/>）
    /// に置いてある。犬と猿で調べる速さが違うのは仲間の性質で、地点の性質ではないため。
    /// 地点ごとに難度を持たせたくなったら、そのときに地点側へ足す（先回りしては作らない）。
    /// </summary>
    public interface IInvestigationPoint
    {
        /// <summary>地点の同定 ID。</summary>
        int PointId { get; }

        /// <summary>現在位置。</summary>
        Vector3 Position { get; }

        /// <summary>いま調べに行ける状態か（未調査で、有効で、封鎖されていない）。</summary>
        bool IsAvailable { get; }

        /// <summary>
        /// 調べ終えたことを伝える。何が起きるかは地点側が決める。
        /// <paramref name="companionActorId"/> は誰が調べたか（犬にしか分からない匂い、といった分岐のため）。
        /// </summary>
        void OnInvestigated(int companionActorId);
    }

    /// <summary>
    /// 調査地点のレジストリ（<c>Find*</c> を使わないための登録所。索敵の
    /// <see cref="Enemy.Perception.PerceptionTargetRegistry"/> と同じ形にしてある）。
    ///
    /// 地点が自己登録し、仲間は最寄りの調査可能な地点を得る。静的だがテストから <see cref="Clear"/> できる。
    /// 走査は登録数（少数）に比例し、毎フレームの確保を行わない。
    /// </summary>
    public static class InvestigationPointRegistry
    {
        private static readonly List<IInvestigationPoint> _points = new List<IInvestigationPoint>();

        /// <summary>登録数。</summary>
        public static int Count => _points.Count;

        /// <summary>地点を登録する（重複登録はしない）。</summary>
        public static void Register(IInvestigationPoint point)
        {
            if (point != null && !_points.Contains(point))
            {
                _points.Add(point);
            }
        }

        /// <summary>地点の登録を解除する。</summary>
        public static void Unregister(IInvestigationPoint point) => _points.Remove(point);

        /// <summary>全登録を消去する（テスト・Scene 再構築用）。</summary>
        public static void Clear() => _points.Clear();

        /// <summary>
        /// 調べに行ける最寄りの地点を返す（XZ 平面距離）。見つからなければ false。
        /// </summary>
        /// <param name="from">探す側の位置（仲間）。</param>
        /// <param name="maxRange">この距離以内だけを候補にする（m）。0 以下なら候補なし。</param>
        /// <param name="leashOrigin">紐の起点（主人公）。</param>
        /// <param name="leashDistance">紐の長さ（m）。起点からこれを超える地点は候補にしない。0 以下なら候補なし。</param>
        /// <param name="nearest">見つかった地点。</param>
        public static bool TryGetNearestAvailable(
            Vector3 from, float maxRange, Vector3 leashOrigin, float leashDistance, out IInvestigationPoint nearest)
        {
            nearest = null;

            // どちらかが 0 以下なら「探索しない設定」。無制限扱いにはしない。
            // 無制限にすると、値を入れ忘れた Data で仲間が Scene の端まで走っていく。
            if (maxRange <= 0f || leashDistance <= 0f)
            {
                return false;
            }

            float best = float.MaxValue;
            for (int i = 0; i < _points.Count; i++)
            {
                IInvestigationPoint p = _points[i];
                if (p == null || !p.IsAvailable)
                {
                    continue;
                }

                Vector3 position = p.Position;
                if (FormationSlot.HorizontalDistance(leashOrigin, position) > leashDistance)
                {
                    continue; // 主人公から離れすぎ。置き去りとワープを誘発する。
                }

                float d = FormationSlot.HorizontalDistance(from, position);
                if (d > maxRange || d >= best)
                {
                    continue;
                }

                best = d;
                nearest = p;
            }

            return nearest != null;
        }
    }
}
