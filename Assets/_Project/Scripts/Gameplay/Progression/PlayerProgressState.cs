using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 主人公の進行データ（P4-00。P6A-01 で累計・使用済み・成長記録へ拡張）。
    /// 徳と、GrantOnce 報酬の付与済み記録と、取得済み成長を保持する純粋 C# の Runtime State。
    /// MonoBehaviour・UnityEngine の時間／Scene API に依存しないため、EditMode テストで決定的に検証できる。
    ///
    /// <b>徳の会計（P6 仕様 §3）。</b> 正本は「累計（<see cref="TotalVirtue"/>）」と「使用済み（<see cref="SpentVirtue"/>）」の 2 つで、
    /// 使用可能な徳は差分から<b>導出する</b>（別の欄に持つと、どちらかの更新漏れで食い違う）。
    /// 常に 0 ≤ 使用済み ≤ 累計。現時点で支出は成長だけなので、使用済み ＝ 成長の実支出合計 が成り立つ。
    /// 将来アイテム購入等へ使うときは、この等式を黙って壊さず契約を更新すること。
    ///
    /// <b><see cref="Virtue"/> は「使用可能」の別名。</b> P4／P5 の HUD・テストは「付与で増える徳」を
    /// <see cref="Virtue"/> で読んでいる。支出が無い間は累計と一致するので、既存の挙動は変わらない。
    ///
    /// 保持スコープ：本編型では常駐 Session が 1 個だけ所有する（<c>GameSessionState.Progress</c>）。
    /// 試遊 Scene の Retry は Scene 再読込で Holder ごと破棄されるため、徳・付与済み記録はともにリセットされる（P4-00 仕様）。
    /// </summary>
    public sealed class PlayerProgressState
    {
        // GrantOnce 報酬の付与済み記録。鍵は RewardData の安定 ID 文字列（Instance ID ではないので Prefab 生成でも一意）。
        private readonly HashSet<string> _grantedRewardIds = new HashSet<string>();

        // 取得済み成長（成長 ID → 実支出額）。振り直しは未実装なので減らない（P6 仕様 §7）。
        private readonly Dictionary<string, int> _growth = new Dictionary<string, int>();

        // 払い戻し権利を追加済みの章（P6B 02。仕様 §5。上限で増加 0 でも処理済みにする）。
        private readonly HashSet<string> _processedChapters = new HashSet<string>();

        /// <summary>払い戻し権利の残り（P6B 02。死亡・休息・Load で初期化しない）。</summary>
        public int RefundRights { get; private set; }

        /// <summary>権利を追加済みの章の数（診断・テスト用）。</summary>
        public int ProcessedChapterCount => _processedChapters.Count;

        /// <summary>その章の権利追加を処理済みか。</summary>
        public bool IsChapterProcessed(in StableId chapterId) =>
            !chapterId.IsEmpty && _processedChapters.Contains(chapterId.Value);

        /// <summary>処理済み章を写す（保存の採取用。順は不定）。</summary>
        public void CopyProcessedChaptersTo(List<string> buffer)
        {
            buffer.Clear();
            foreach (string id in _processedChapters)
            {
                buffer.Add(id);
            }
        }

        /// <summary>New Game の権利を置く（冒険の開始で 1 回だけ使う。P6B 02）。</summary>
        public void SetInitialRefundRights(int rights)
        {
            int value = rights < 0 ? 0 : rights;
            if (value == RefundRights)
            {
                return;
            }

            RefundRights = value;
            NotifyChanged(true);
        }

        /// <summary>
        /// 成長 1 つを払い戻す（P6B 02。仕様 §5）。<b>検証はすべてここより前（呼び出し側）で終えている前提</b>だが、
        /// 未取得・権利 0 はここでも拒否し、そのときは何も変えない。成立したら実支出を使用済みから戻し（累計は変えない）、
        /// 取得記録を消し、権利を 1 つ使う。返した徳を <paramref name="returned"/> へ。
        /// </summary>
        public GrowthRefundResult TryRefundGrowth(in StableId growthId, out int returned)
        {
            returned = 0;
            if (growthId.IsEmpty || !_growth.TryGetValue(growthId.Value, out int spent))
            {
                return GrowthRefundResult.NotAcquired;
            }

            if (RefundRights <= 0)
            {
                return GrowthRefundResult.NoRights;
            }

            SpentVirtue -= spent;
            _growth.Remove(growthId.Value);
            RefundRights--;
            returned = spent;
            NotifyChanged(true);
            return GrowthRefundResult.Refunded;
        }

        /// <summary>
        /// 章クリアの権利追加（P6B 02。仕様 §5）。未処理の章なら min(上限, 現在＋追加) にし、<b>増加 0 でも処理済みにする</b>。
        /// 処理済みなら何もしない（再通知・Load 後の再通知で重複しない）。章 ID の妥当性は呼び出し側（カタログ）が見る。
        /// </summary>
        public ChapterRightsResult TryGrantChapterRefundRights(in StableId chapterId, int perChapter, int max, out int added)
        {
            added = 0;
            if (chapterId.IsEmpty || !chapterId.IsValid)
            {
                return ChapterRightsResult.UnknownChapter;
            }

            if (_processedChapters.Contains(chapterId.Value))
            {
                return ChapterRightsResult.AlreadyProcessed;
            }

            int cap = max < 0 ? 0 : max;
            int target = RefundRights + (perChapter < 0 ? 0 : perChapter);
            if (target > cap)
            {
                target = cap;
            }

            if (target < RefundRights)
            {
                target = RefundRights; // 既に上限超え（ありえないが）でも減らさない。
            }

            added = target - RefundRights;
            RefundRights = target;
            _processedChapters.Add(chapterId.Value);
            NotifyChanged(true);
            return ChapterRightsResult.Processed;
        }

        /// <summary>累計で得た徳（0 以上。int の上限で飽和）。</summary>
        public int TotalVirtue { get; private set; }

        /// <summary>使用済みの徳（0 以上、累計以下）。</summary>
        public int SpentVirtue { get; private set; }

        /// <summary>使用可能な徳（累計 − 使用済み）。</summary>
        public int AvailableVirtue => TotalVirtue - SpentVirtue;

        /// <summary>
        /// 徳（<b>使用可能な徳</b>）。P4／P5 の互換名。支出が無ければ累計と同じ。
        /// </summary>
        public int Virtue => AvailableVirtue;

        /// <summary>付与済みとして記録された GrantOnce 報酬の数（テスト・診断用）。</summary>
        public int GrantedRewardCount => _grantedRewardIds.Count;

        /// <summary>取得済み成長の数（テスト・診断用）。</summary>
        public int GrowthCount => _growth.Count;

        /// <summary>
        /// 進行が変わった（徳・付与記録・成長のどれか）。引数は変化後の使用可能な徳。
        ///
        /// <b>Session の版と保存要求はここから立つ。</b> Holder を経由しない付与（到着報酬・初回クリア等）でも
        /// HUD が追従できるよう、State 自身が通知する。
        /// </summary>
        public event Action<int> Changed;

        /// <summary>指定 ID の GrantOnce 報酬が付与済みか。</summary>
        public bool HasGranted(in StableId rewardId)
        {
            return !rewardId.IsEmpty && _grantedRewardIds.Contains(rewardId.Value);
        }

        /// <summary>指定の成長を取得済みか。</summary>
        public bool HasGrowth(in StableId growthId)
        {
            return !growthId.IsEmpty && _growth.ContainsKey(growthId.Value);
        }

        /// <summary>指定の成長に実際に払った徳（未取得なら 0）。</summary>
        public int GrowthSpent(in StableId growthId)
        {
            return !growthId.IsEmpty && _growth.TryGetValue(growthId.Value, out int spent) ? spent : 0;
        }

        /// <summary>取得済み成長の ID を列挙する（保存・効果再計算用）。順序は呼び出し側で決める。</summary>
        public void CopyGrowthTo(List<KeyValuePair<string, int>> buffer)
        {
            buffer.Clear();
            foreach (KeyValuePair<string, int> pair in _growth)
            {
                buffer.Add(pair);
            }
        }

        /// <summary>付与済み GrantOnce の ID を列挙する（保存用）。</summary>
        public void CopyGrantedTo(List<string> buffer)
        {
            buffer.Clear();
            foreach (string id in _grantedRewardIds)
            {
                buffer.Add(id);
            }
        }

        /// <summary>
        /// 報酬の付与を試みる。GrantOnce の報酬は同じ <see cref="RewardSnapshot.RewardId"/> について 1 度だけ付与する
        /// （敵インスタンス単位の重複排除は上流＝<c>EnemyActor</c> の 1 回発行と <c>CombatSessionController</c> の初回受理が担う）。
        /// </summary>
        /// <param name="reward">付与要求（原本から複製済み）。</param>
        /// <param name="grantedVirtue">実際に加算された徳量（付与しなかった場合は 0）。</param>
        /// <returns>処理結果。</returns>
        public RewardGrantResult TryGrant(in RewardSnapshot reward, out int grantedVirtue)
        {
            grantedVirtue = 0;
            if (!reward.HasReward)
            {
                return RewardGrantResult.NoReward;
            }

            if (!reward.GrantOnce)
            {
                grantedVirtue = AddVirtue(reward.VirtueAmount);
                NotifyChanged(grantedVirtue > 0);
                return RewardGrantResult.Granted;
            }

            // GrantOnce だが鍵が無い（Data 不備）。重複排除はできないが、付与自体は行い結果で区別できるようにする。
            if (reward.RewardId.IsEmpty)
            {
                grantedVirtue = AddVirtue(reward.VirtueAmount);
                NotifyChanged(grantedVirtue > 0);
                return RewardGrantResult.GrantedWithoutId;
            }

            if (!_grantedRewardIds.Add(reward.RewardId.Value))
            {
                return RewardGrantResult.AlreadyGranted;
            }

            grantedVirtue = AddVirtue(reward.VirtueAmount);

            // 徳が 0 の GrantOnce でも「付与済み」になった事実は進行の変化（保存対象）。
            NotifyChanged(true);
            return RewardGrantResult.Granted;
        }

        /// <summary>
        /// 成長を購入する（P6 仕様 §7）。<b>不成立なら何も変えない</b>（全体無変更）。
        /// 成立したら使用済みに実支出を足し、取得記録を残す。効果の反映・休息・保存は呼び出し側（進行側の確定手順）。
        /// </summary>
        /// <param name="growthId">成長 ID（SkillNodeData の StableId）。</param>
        /// <param name="cost">この購入の実支出（Data から複製した値）。</param>
        public GrowthPurchaseResult TryPurchaseGrowth(in StableId growthId, int cost)
        {
            if (growthId.IsEmpty || !growthId.IsValid)
            {
                return GrowthPurchaseResult.UnknownGrowth;
            }

            if (cost < 0)
            {
                return GrowthPurchaseResult.InvalidCost;
            }

            if (_growth.ContainsKey(growthId.Value))
            {
                return GrowthPurchaseResult.AlreadyAcquired;
            }

            if (AvailableVirtue < cost)
            {
                return GrowthPurchaseResult.InsufficientVirtue;
            }

            SpentVirtue += cost;
            _growth.Add(growthId.Value, cost);
            NotifyChanged(true);
            return GrowthPurchaseResult.Purchased;
        }

        /// <summary>徳と付与済み記録・成長を初期化する（新規セッション・検証の再試行用）。</summary>
        public void Reset()
        {
            bool changed = TotalVirtue != 0 || SpentVirtue != 0 || _grantedRewardIds.Count != 0 || _growth.Count != 0
                || RefundRights != 0 || _processedChapters.Count != 0;
            TotalVirtue = 0;
            SpentVirtue = 0;
            _grantedRewardIds.Clear();
            _growth.Clear();
            RefundRights = 0;
            _processedChapters.Clear();
            NotifyChanged(changed);
        }

        /// <summary>
        /// 保存から値を置く（P6A-02 の候補 Session 構築だけが使う）。<b>先に全部を検証し、1 つでも不正なら何も変えない</b>。
        /// 通知は出さない（Load は獲得演出を起こさない。仕様 §10）。
        /// </summary>
        internal bool TryRestore(int total, int spent, IReadOnlyList<string> granted,
            IReadOnlyList<KeyValuePair<string, int>> growth, out string error)
        {
            return TryRestore(total, spent, granted, growth, 0, null, out error);
        }

        /// <summary>
        /// 保存から置く（P6B 02 で払い戻し権利と処理済み章を追加）。値はすべて検証済みのものを渡す前提だが、
        /// 会計の不整合・負の権利・空や重複の章 ID はここでも拒否し、そのときは何も変えない。
        /// </summary>
        internal bool TryRestore(int total, int spent, IReadOnlyList<string> granted,
            IReadOnlyList<KeyValuePair<string, int>> growth, int refundRights, IReadOnlyList<string> processedChapters,
            out string error)
        {
            error = null;
            if (refundRights < 0)
            {
                error = "払い戻し権利が負です（" + refundRights + "）。";
                return false;
            }

            var chapterSet = new HashSet<string>();
            if (processedChapters != null)
            {
                for (int i = 0; i < processedChapters.Count; i++)
                {
                    if (string.IsNullOrEmpty(processedChapters[i]) || !chapterSet.Add(processedChapters[i]))
                    {
                        error = "処理済み章 ID が空か重複しています（" + processedChapters[i] + "）。";
                        return false;
                    }
                }
            }

            if (total < 0 || spent < 0 || spent > total)
            {
                error = "徳の会計が不正です（累計 " + total + "／使用済み " + spent + "）。";
                return false;
            }

            var grantedSet = new HashSet<string>();
            if (granted != null)
            {
                for (int i = 0; i < granted.Count; i++)
                {
                    if (string.IsNullOrEmpty(granted[i]) || !grantedSet.Add(granted[i]))
                    {
                        error = "付与済み報酬 ID が空か重複しています（" + granted[i] + "）。";
                        return false;
                    }
                }
            }

            var growthMap = new Dictionary<string, int>();
            long growthSum = 0;
            if (growth != null)
            {
                for (int i = 0; i < growth.Count; i++)
                {
                    KeyValuePair<string, int> pair = growth[i];
                    if (string.IsNullOrEmpty(pair.Key) || pair.Value < 0 || growthMap.ContainsKey(pair.Key))
                    {
                        error = "成長記録が不正です（" + pair.Key + "／" + pair.Value + "）。";
                        return false;
                    }

                    growthMap.Add(pair.Key, pair.Value);
                    growthSum += pair.Value;
                }
            }

            // 現時点の支出は成長だけ：使用済み ＝ 成長の実支出合計（仕様 §3）。
            if (growthSum != spent)
            {
                error = "使用済み徳（" + spent + "）と成長の実支出合計（" + growthSum + "）が一致しません。";
                return false;
            }

            TotalVirtue = total;
            SpentVirtue = spent;
            _grantedRewardIds.Clear();
            foreach (string id in grantedSet)
            {
                _grantedRewardIds.Add(id);
            }

            _growth.Clear();
            foreach (KeyValuePair<string, int> pair in growthMap)
            {
                _growth.Add(pair.Key, pair.Value);
            }

            RefundRights = refundRights;
            _processedChapters.Clear();
            foreach (string id in chapterSet)
            {
                _processedChapters.Add(id);
            }

            return true;
        }

        /// <summary>徳を加算する（負値は 0 として無視し、int の上限で飽和させる）。実際に加算した量を返す。</summary>
        private int AddVirtue(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            int room = int.MaxValue - TotalVirtue;
            int applied = amount > room ? room : amount;
            TotalVirtue += applied;
            return applied;
        }

        private void NotifyChanged(bool changed)
        {
            if (changed)
            {
                Changed?.Invoke(AvailableVirtue);
            }
        }
    }

    /// <summary>成長購入の結果（P6 仕様 §7）。<see cref="Purchased"/> 以外は全体無変更。</summary>
    /// <summary>払い戻しの結果（P6B 02）。成立以外はすべて全体無変更。</summary>
    public enum GrowthRefundResult
    {
        Refunded = 0,
        UnknownGrowth = 1,
        NotAcquired = 2,
        NoRights = 3,
        HasDependents = 4,
        NotAllowedHere = 5,
    }

    /// <summary>章クリアの権利追加の結果（P6B 02）。</summary>
    public enum ChapterRightsResult
    {
        Processed = 0,
        AlreadyProcessed = 1,
        UnknownChapter = 2,
    }

    public enum GrowthPurchaseResult
    {
        /// <summary>購入した。</summary>
        Purchased = 0,

        /// <summary>未知の ID（空・書式不正・カタログに無い）。</summary>
        UnknownGrowth = 1,

        /// <summary>取得済み。</summary>
        AlreadyAcquired = 2,

        /// <summary>徳が足りない。</summary>
        InsufficientVirtue = 3,

        /// <summary>前提を満たさない（定義側の判定）。</summary>
        PrerequisiteNotMet = 4,

        /// <summary>費用が不正（負）。</summary>
        InvalidCost = 5,

        /// <summary>操作できる場所・状態ではない（お地蔵様の有効な操作コンテキストの外）。</summary>
        NotAllowedHere = 6,
    }
}
