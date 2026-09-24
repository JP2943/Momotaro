using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Enemy;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// EnemyId → 敵 Prefab の明示表（P5-07。仕様書 v1.1 §8.1 末尾）。
    ///
    /// <b>表示名や Prefab 名で解決しない。</b> どちらも人が気軽に変える文字列で、
    /// 変えた瞬間に「生成できない」が静かに起きる。実行時はこの表だけを引き、
    /// <b>全 Prefab を検索して逆引きしない</b>（§8.1 末尾）。
    ///
    /// <b>同じ敵が構成に複数居ることは許す。</b> それは EnemyIds 側の重複であって、
    /// 表のキーの重複とは別のこと（§8.1 末尾）。表のキーが重複していたらどちらが引かれるか
    /// 読めなくなるので、そちらは検査で落とす。
    /// </summary>
    [Serializable]
    public sealed class EnemyPrefabTable
    {
        [SerializeField] private List<Entry> _entries = new List<Entry>();

        /// <summary>登録数（Validator・診断用）。</summary>
        public int Count => _entries != null ? _entries.Count : 0;

        /// <summary>1 行ぶんの対応（EnemyId と Prefab）。</summary>
        [Serializable]
        public struct Entry
        {
            [Tooltip("敵の安定 ID。Prefab に配線された EnemyArchetypeData.Id と一致させる。")]
            public StableId EnemyId;

            [Tooltip("生成する Prefab。")]
            public GameObject Prefab;
        }

        /// <summary>表を差し替える（Builder・テスト）。</summary>
        public void SetEntries(IEnumerable<Entry> entries)
        {
            _entries = new List<Entry>();
            if (entries == null)
            {
                return;
            }

            foreach (Entry e in entries)
            {
                _entries.Add(e);
            }
        }

        /// <summary>ID から Prefab を引く。</summary>
        public bool TryResolve(StableId enemyId, out GameObject prefab)
        {
            prefab = null;
            if (enemyId.IsEmpty || _entries == null)
            {
                return false;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].EnemyId.Equals(enemyId) && _entries[i].Prefab != null)
                {
                    prefab = _entries[i].Prefab;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 表そのものを検査する（§13.3）。キーの重複・null・書式不正と、
        /// <b>Prefab に配線された <see cref="EnemyArchetypeData"/> の ID との不一致</b>を挙げる。
        /// </summary>
        public void Validate(List<string> issues)
        {
            if (issues == null)
            {
                return;
            }

            if (_entries == null || _entries.Count == 0)
            {
                issues.Add("EnemyId→Prefab 表が空です。");
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                string label = "EnemyId→Prefab 表[" + i + "]";

                if (!e.EnemyId.IsValid)
                {
                    issues.Add(label + "：EnemyId '" + e.EnemyId.Value + "' の書式が不正です。");
                    continue;
                }

                if (!seen.Add(e.EnemyId.Value))
                {
                    issues.Add(label + "：EnemyId '" + e.EnemyId.Value + "' が重複しています。");
                }

                if (e.Prefab == null)
                {
                    issues.Add(label + "：'" + e.EnemyId.Value + "' の Prefab が未割当です。");
                    continue;
                }

                var actor = e.Prefab.GetComponentInChildren<EnemyActor>(true);
                if (actor == null)
                {
                    issues.Add(label + "：'" + e.EnemyId.Value + "' の Prefab に EnemyActor がありません。");
                    continue;
                }

                if (actor.Archetype == null)
                {
                    issues.Add(label + "：'" + e.EnemyId.Value + "' の Prefab に EnemyArchetypeData が未割当です。");
                    continue;
                }

                if (!actor.Archetype.Id.Equals(e.EnemyId))
                {
                    issues.Add(label + "：表の '" + e.EnemyId.Value + "' と Prefab の '"
                        + actor.Archetype.Id.Value + "' が一致しません。");
                }
            }
        }
    }
}
