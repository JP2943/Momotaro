using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Progression;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 本編型 Session の純粋 State（P5-01。仕様書 v1.1 §4.1／§4.2）。UnityEngine・Scene API に依存しない。
    ///
    /// <b>徳の正本はここが持つ 1 個の <see cref="PlayerProgressState"/> だけ。</b>
    /// World 状態用のクラスへ徳を複製しない（§4.2）。Scene 上の <see cref="PlayerProgressHolder"/> は
    /// この参照を Bind されて窓口になるだけで、自分の State を並行して持たない。
    ///
    /// Scene 由来の Actor・Presenter を <c>DontDestroyOnLoad</c> で運ばない（§4.1 末尾）。ここに入るのは
    /// 「Scene が入れ替わっても意味を保つ純粋な値」に限る。生成・破棄の権限は Session の所有者
    /// （Bootstrap／Game Session 層）だけが持ち、明示的な New Game が新しい実体を作る（§9.2）。
    /// 本クラスに全体 Reset を置かないのは、Scene 側から共有 State を初期化できないようにするため（§4.2）。
    /// </summary>
    public sealed class GameSessionState
    {
        private readonly PlayerProgressState _progress = new PlayerProgressState();
        private readonly Dictionary<StableId, AreaRuntimeState> _areas = new Dictionary<StableId, AreaRuntimeState>();
        private readonly HashSet<StableId> _visitedAreas = new HashSet<StableId>();
        private readonly HashSet<StableId> _recruited = new HashSet<StableId>();

        /// <summary>徳・GrantOnce 記録の唯一の正本（§4.2）。</summary>
        public PlayerProgressState Progress => _progress;

        /// <summary>
        /// 通常 Encounter の再出現周期。本編型死亡再開のたびに 1 進み、過去のクリア記録を無効化する（§9.1）。
        /// 恒久進行（徳・調査・開通・加入）はこの値に影響されない。
        /// </summary>
        public int RespawnCycle { get; private set; }

        /// <summary>State を作成済みのエリア数（診断・テスト用）。</summary>
        public int AreaCount => _areas.Count;

        /// <summary>訪問済みエリア数（診断・テスト用）。</summary>
        public int VisitedAreaCount => _visitedAreas.Count;

        /// <summary>加入済み仲間の数（診断・テスト用）。</summary>
        public int RecruitedCount => _recruited.Count;

        /// <summary>指定エリアの State を取得する。無ければ作る（初回入場）。</summary>
        public AreaRuntimeState GetOrCreateArea(StableId areaId)
        {
            if (_areas.TryGetValue(areaId, out AreaRuntimeState existing))
            {
                return existing;
            }

            var created = new AreaRuntimeState(areaId);
            _areas.Add(areaId, created);
            return created;
        }

        /// <summary>指定エリアの State を取得する（未作成なら false）。作成の副作用を起こさない読み取り。</summary>
        public bool TryGetArea(StableId areaId, out AreaRuntimeState area)
        {
            return _areas.TryGetValue(areaId, out area);
        }

        /// <summary>訪問済みか。</summary>
        public bool HasVisited(StableId areaId) => !areaId.IsEmpty && _visitedAreas.Contains(areaId);

        /// <summary>訪問済みにする。<b>初回だけ true</b>（再訪で初回入場の演出を再発火させないため）。</summary>
        public bool MarkVisited(StableId areaId)
        {
            return !areaId.IsEmpty && _visitedAreas.Add(areaId);
        }

        /// <summary>加入済みか。</summary>
        public bool IsRecruited(StableId companionId) => !companionId.IsEmpty && _recruited.Contains(companionId);

        /// <summary>加入させる。<b>初回だけ true</b>（加入演出・通知の二重発火を防ぐ）。</summary>
        public bool Recruit(StableId companionId)
        {
            return !companionId.IsEmpty && _recruited.Add(companionId);
        }

        /// <summary>
        /// 本編型死亡再開のときに呼ぶ（§9.1 手順 5）。再出現周期を 1 進め、
        /// <b>全エリアの通常 Encounter クリア記録だけ</b>を初期化する。
        ///
        /// 徳・GrantOnce 記録・調査済み・門の開通・訪問済み・加入は<b>変更しない</b>（§4.1 の表）。
        /// 再開要求 1 件につき 1 回だけ呼ぶこと。連打や読込失敗で複数回進めない（§9.1 末尾、P5-E21）。
        /// </summary>
        public void AdvanceRespawnCycle()
        {
            RespawnCycle++;
            foreach (KeyValuePair<StableId, AreaRuntimeState> pair in _areas)
            {
                pair.Value.ClearNormalEncounterRecords();
            }
        }
    }
}
