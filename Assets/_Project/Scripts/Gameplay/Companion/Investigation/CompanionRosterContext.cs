using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// 試遊用の加入供給元（Scene に 1 つ置き、加入済みの仲間の StableId を列挙する）。
    /// 本編の加入イベント・セーブは作らない（P5〜P6）。欠落していれば調停役が未配線として拒否し、Validator が検出する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionRosterContext : MonoBehaviour, ICompanionAvailability
    {
        [Tooltip("加入済みの仲間の StableId（犬丸なら CompanionData の Id）。")]
        [SerializeField] private StableId[] _recruited = System.Array.Empty<StableId>();

        public int Count => _recruited != null ? _recruited.Length : 0;

        /// <inheritdoc />
        public bool IsRecruited(StableId companionId)
        {
            if (companionId.IsEmpty || _recruited == null)
            {
                return false;
            }

            for (int i = 0; i < _recruited.Length; i++)
            {
                if (_recruited[i].Equals(companionId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>加入済みの一覧を置き換える（Scene 構築・テスト・未加入経路の検証用）。</summary>
        public void SetRecruited(params StableId[] recruited)
        {
            _recruited = recruited ?? System.Array.Empty<StableId>();
        }

        /// <summary>1 体分の加入を取り消す（加入資格消失の検証・E08）。</summary>
        public void Remove(StableId companionId)
        {
            if (_recruited == null)
            {
                return;
            }

            var list = new System.Collections.Generic.List<StableId>(_recruited);
            list.RemoveAll(id => id.Equals(companionId));
            _recruited = list.ToArray();
        }
    }
}
