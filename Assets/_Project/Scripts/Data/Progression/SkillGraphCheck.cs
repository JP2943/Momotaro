using System.Collections.Generic;

namespace Momotaro.Data.Progression
{
    /// <summary>
    /// 成長ノード一覧の構造検査（P6B 01。仕様 §11 P6B 17「StableId・前提循環・未知参照・値検査」）。
    /// Data 検証（<c>AreaCatalogData.Validate</c>）と Gameplay のカタログ構築（<c>CampaignCatalog.Build</c>）が
    /// <b>同じ条件</b>で判定するよう 1 か所に置く。
    /// </summary>
    public static class SkillGraphCheck
    {
        /// <summary>
        /// 前提・排他が一覧の外を指していないか、前提に循環が無いかを調べ、問題を <paramref name="errors"/> へ足す。
        /// 問題が無ければ true。
        /// </summary>
        public static bool Check(IReadOnlyList<SkillNodeData> nodes, List<string> errors)
        {
            int before = errors.Count;
            if (nodes == null)
            {
                return true;
            }

            var members = new HashSet<SkillNodeData>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] != null)
                {
                    members.Add(nodes[i]);
                }
            }

            foreach (SkillNodeData node in members)
            {
                CheckRefs(node, node.Prerequisites, "prerequisite", members, errors);
                CheckRefs(node, node.MutuallyExclusive, "exclusive", members, errors);
                if (ContainsRef(node.Prerequisites, node))
                {
                    errors.Add("Growth '" + node.Id.Value + "' lists itself as a prerequisite.");
                }
            }

            // 循環（白・灰・黒の深さ優先）。
            var state = new Dictionary<SkillNodeData, int>();
            foreach (SkillNodeData node in members)
            {
                if (HasCycle(node, state))
                {
                    errors.Add("Growth prerequisites contain a cycle through '" + node.Id.Value + "'.");
                    break;
                }
            }

            return errors.Count == before;
        }

        private static bool ContainsRef(IReadOnlyList<SkillNodeData> list, SkillNodeData node)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], node))
                {
                    return true;
                }
            }

            return false;
        }

        private static void CheckRefs(SkillNodeData node, IReadOnlyList<SkillNodeData> refs, string label,
            HashSet<SkillNodeData> members, List<string> errors)
        {
            for (int i = 0; i < refs.Count; i++)
            {
                if (refs[i] == null)
                {
                    errors.Add("Growth '" + node.Id.Value + "' has a null " + label + ".");
                }
                else if (!members.Contains(refs[i]))
                {
                    errors.Add("Growth '" + node.Id.Value + "' " + label + " '" + refs[i].Id.Value
                        + "' is not part of this campaign.");
                }
            }
        }

        private static bool HasCycle(SkillNodeData node, Dictionary<SkillNodeData, int> state)
        {
            if (node == null)
            {
                return false;
            }

            if (state.TryGetValue(node, out int s))
            {
                return s == 1; // 灰＝探索中の経路上に戻ってきた。
            }

            state[node] = 1;
            IReadOnlyList<SkillNodeData> pre = node.Prerequisites;
            for (int i = 0; i < pre.Count; i++)
            {
                if (pre[i] != node && HasCycle(pre[i], state))
                {
                    return true;
                }
            }

            state[node] = 2;
            return false;
        }
    }
}
