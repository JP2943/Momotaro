using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// Scene 上の入口 1 つ（P5-02。仕様書 v1.1 §3.1）。<b>到着位置と代替配置候補だけ</b>を持つ。
    ///
    /// 向きは Data（<c>AreaEntryDefinition</c>）が正本で、ここには置かない。
    /// 座標を Data と Scene の両方に持つと、地形を動かしたときに黙って食い違うため
    /// 「座標は Scene、ID と向きは Data」で分ける。
    ///
    /// 代替配置候補は、到着位置が塞がっているときだけ使う（§6.3）。
    /// <b>同じ Floor の指定候補だけ</b>を使い、Vector3.zero・壁内・別 Floor へ無条件に配置しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaEntryPoint : MonoBehaviour
    {
        [Tooltip("入口の安定 ID。Data の AreaEntryDefinition と一致させる。")]
        [SerializeField] private StableId _entryId;

        [Tooltip("到着位置が塞がっているときの代替配置候補（固定順に検査する。§6.3）。")]
        [SerializeField] private List<Transform> _alternatePlacements = new List<Transform>();

        /// <summary>入口の安定 ID。</summary>
        public StableId EntryId => _entryId;

        /// <summary>到着位置。</summary>
        public Vector3 ArrivalPosition => transform.position;

        /// <summary>代替配置候補（読み取り専用。固定順）。</summary>
        public IReadOnlyList<Transform> AlternatePlacements => _alternatePlacements;

#if UNITY_EDITOR
        /// <summary>Builder から組み立てるための設定入口（Editor 専用）。</summary>
        public void EditorSet(StableId entryId, List<Transform> alternates)
        {
            _entryId = entryId;
            _alternatePlacements = alternates ?? new List<Transform>();
        }
#endif
    }
}
