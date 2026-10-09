using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.Story;

namespace Momotaro.Gameplay.Story
{
    /// <summary>章ごとの経路の記録（読み取り用の値。P7。仕様 §7）。</summary>
    public readonly struct ChapterRouteRecord
    {
        public ChapterRouteRecord(bool standardReached, bool standardCompleted, bool hardReached, bool hardCompleted,
            StoryRoute lastRoute, StoryRoute bossRoute)
        {
            StandardReached = standardReached;
            StandardCompleted = standardCompleted;
            HardReached = hardReached;
            HardCompleted = hardCompleted;
            LastRoute = lastRoute;
            BossRoute = bossRoute;
        }

        public bool StandardReached { get; }
        public bool StandardCompleted { get; }
        public bool HardReached { get; }
        public bool HardCompleted { get; }

        /// <summary>合流へ入った直前の経路（未記録は None）。</summary>
        public StoryRoute LastRoute { get; }

        /// <summary>ボス前へ通常の移動で着いたときの経路（未記録は None）。</summary>
        public StoryRoute BossRoute { get; }

        public bool IsEmpty => !StandardReached && !StandardCompleted && !HardReached && !HardCompleted
            && LastRoute == StoryRoute.None && BossRoute == StoryRoute.None;

        public bool Reached(StoryRoute route) =>
            route == StoryRoute.Standard ? StandardReached : route == StoryRoute.Hard && HardReached;

        public bool Completed(StoryRoute route) =>
            route == StoryRoute.Standard ? StandardCompleted : route == StoryRoute.Hard && HardCompleted;
    }

    /// <summary>
    /// 会話・依頼・章の進行のうち、既存の記録に無いもの（P7。<see cref="Session.GameSessionState"/> の配下）。
    /// 必須イベントの完了・章ごとの経路・章クリア（クリア時の経路）を持つ。依頼は既存のクエスト段階を使うのでここに無い。
    ///
    /// <b>変更は Session の確定入口からだけ。</b> 版の更新と保存要求は Session が行う（ここは純粋な値だけ）。
    /// </summary>
    public sealed class StoryProgressState
    {
        private sealed class RouteState
        {
            public bool StandardReached;
            public bool StandardCompleted;
            public bool HardReached;
            public bool HardCompleted;
            public StoryRoute LastRoute;
            public StoryRoute BossRoute;

            public ChapterRouteRecord ToRecord() =>
                new ChapterRouteRecord(StandardReached, StandardCompleted, HardReached, HardCompleted, LastRoute, BossRoute);
        }

        private readonly HashSet<string> _events = new HashSet<string>();
        private readonly Dictionary<string, RouteState> _routes = new Dictionary<string, RouteState>();
        private readonly Dictionary<string, StoryRoute> _clearedChapters = new Dictionary<string, StoryRoute>();

        public int CompletedEventCount => _events.Count;
        public int ClearedChapterCount => _clearedChapters.Count;

        public bool IsEventCompleted(StableId eventId) => !eventId.IsEmpty && _events.Contains(eventId.Value);

        public bool IsChapterCleared(StableId chapterId) => !chapterId.IsEmpty && _clearedChapters.ContainsKey(chapterId.Value);

        /// <summary>章クリア時に固定した経路（未クリア・未記録は None）。</summary>
        public StoryRoute ClearedRouteOf(StableId chapterId) =>
            !chapterId.IsEmpty && _clearedChapters.TryGetValue(chapterId.Value, out StoryRoute route) ? route : StoryRoute.None;

        public ChapterRouteRecord RouteOf(StableId chapterId) =>
            !chapterId.IsEmpty && _routes.TryGetValue(chapterId.Value, out RouteState s) ? s.ToRecord() : default;

        internal bool TryCompleteEvent(StableId eventId) => !eventId.IsEmpty && _events.Add(eventId.Value);

        /// <summary>
        /// 通常の移動の到着を章の経路へ反映する（仕様 §7）。変化したら true。
        /// 到達＝経路の Area に入った、踏破＝経路の終端（合流へ入る入口）に着いた、ボス到達経路＝ボス前の入口に着いた時点の直前の経路。
        /// 章クリア後はボス到達経路を変えない（クリア時の値は別に固定済み）。
        /// </summary>
        internal bool ApplyArrival(ChapterInfo chapter, StableId areaId, StableId entryId)
        {
            if (chapter == null || areaId.IsEmpty)
            {
                return false;
            }

            string key = chapter.ChapterId.Value;
            _routes.TryGetValue(key, out RouteState s);
            ChapterRouteRecord before = s != null ? s.ToRecord() : default;
            bool touched = chapter.Standard.Contains(areaId) || chapter.Hard.Contains(areaId)
                || chapter.Standard.IsTerminal(areaId, entryId) || chapter.Hard.IsTerminal(areaId, entryId)
                || chapter.IsBossFrontArrival(areaId, entryId);
            if (!touched)
            {
                return false;
            }

            if (s == null)
            {
                s = new RouteState();
                _routes.Add(key, s);
            }

            if (chapter.Standard.Contains(areaId))
            {
                s.StandardReached = true;
            }

            if (chapter.Hard.Contains(areaId))
            {
                s.HardReached = true;
            }

            if (chapter.Standard.IsTerminal(areaId, entryId))
            {
                s.StandardCompleted = true;
                s.LastRoute = StoryRoute.Standard;
            }
            else if (chapter.Hard.IsTerminal(areaId, entryId))
            {
                s.HardCompleted = true;
                s.LastRoute = StoryRoute.Hard;
            }

            if (chapter.IsBossFrontArrival(areaId, entryId) && s.LastRoute != StoryRoute.None
                && !_clearedChapters.ContainsKey(key))
            {
                s.BossRoute = s.LastRoute;
            }

            ChapterRouteRecord after = s.ToRecord();
            return !after.Equals(before);
        }

        /// <summary>章クリアを記録する（その時点のボス到達経路を固定）。既にクリア済みなら false。</summary>
        internal bool TryMarkChapterCleared(StableId chapterId)
        {
            if (chapterId.IsEmpty || _clearedChapters.ContainsKey(chapterId.Value))
            {
                return false;
            }

            StoryRoute route = _routes.TryGetValue(chapterId.Value, out RouteState s) ? s.BossRoute : StoryRoute.None;
            _clearedChapters.Add(chapterId.Value, route);
            return true;
        }

        // ---------------------------------------------------------------- 保存

        /// <summary>完了イベントを列挙する（保存用。順序は呼び出し側で決める）。</summary>
        public void CopyEventsTo(List<string> buffer)
        {
            buffer.Clear();
            buffer.AddRange(_events);
        }

        /// <summary>経路の記録を列挙する（保存用）。空の記録は出さない。</summary>
        public void CopyRoutesTo(List<KeyValuePair<string, ChapterRouteRecord>> buffer)
        {
            buffer.Clear();
            foreach (KeyValuePair<string, RouteState> pair in _routes)
            {
                ChapterRouteRecord record = pair.Value.ToRecord();
                if (!record.IsEmpty)
                {
                    buffer.Add(new KeyValuePair<string, ChapterRouteRecord>(pair.Key, record));
                }
            }
        }

        /// <summary>章クリアを列挙する（保存用。値はクリア時の経路）。</summary>
        public void CopyClearedChaptersTo(List<KeyValuePair<string, StoryRoute>> buffer)
        {
            buffer.Clear();
            buffer.AddRange(_clearedChapters);
        }

        /// <summary>保存から置く（候補 Session の構築だけ。検証済みの値）。</summary>
        internal void RestoreFrom(IEnumerable<string> events, IEnumerable<KeyValuePair<string, ChapterRouteRecord>> routes,
            IEnumerable<KeyValuePair<string, StoryRoute>> cleared)
        {
            _events.Clear();
            _routes.Clear();
            _clearedChapters.Clear();
            foreach (string id in events)
            {
                _events.Add(id);
            }

            foreach (KeyValuePair<string, ChapterRouteRecord> pair in routes)
            {
                ChapterRouteRecord r = pair.Value;
                _routes[pair.Key] = new RouteState
                {
                    StandardReached = r.StandardReached,
                    StandardCompleted = r.StandardCompleted,
                    HardReached = r.HardReached,
                    HardCompleted = r.HardCompleted,
                    LastRoute = r.LastRoute,
                    BossRoute = r.BossRoute,
                };
            }

            foreach (KeyValuePair<string, StoryRoute> pair in cleared)
            {
                _clearedChapters[pair.Key] = pair.Value;
            }
        }
    }
}
