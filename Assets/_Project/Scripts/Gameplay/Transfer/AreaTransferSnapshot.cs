using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion;

namespace Momotaro.Gameplay.Transfer
{
    /// <summary>
    /// 遷移中だけ使う<b>不変の値コピー</b>（P5-03b。仕様書 v1.1 §4.4）。
    ///
    /// <b>第 2 の正本にしない。</b> 活動中の Actor 値と並行更新されるものではなく、
    /// 「採取した瞬間の値」を到着まで運ぶだけ。到着して適用したら役目は終わる。
    ///
    /// 中身は P5-03a で作った単位ごとの Snapshot の合成で、ここが新しい値を持つことはない
    /// （同じ CD を複数の欄へ複製しない。§4.5）。位置は含めない — 到着位置は目的地の入口と
    /// 安全な隊列位置へ<b>置換</b>されるため（§4.4）。
    /// </summary>
    public readonly struct AreaTransferSnapshot
    {
        /// <summary>主人公の値を含むか。</summary>
        public bool HasPlayer { get; }

        /// <summary>主人公の HP とスタミナ。</summary>
        public PlayerVitalsTransferSnapshot PlayerVitals { get; }

        /// <summary>主人公の被弾後無敵。</summary>
        public HitReactionTransferSnapshot PlayerHitReaction { get; }

        /// <summary>仲間の値を含むか。</summary>
        public bool HasCompanion { get; }

        /// <summary>引渡し対象の仲間 ID（§4.4）。</summary>
        public StableId CompanionId { get; }

        /// <summary>仲間の HP・Down・復帰残り・被弾後無敵・ひるみ。</summary>
        public CompanionVitalsTransferSnapshot CompanionVitals { get; }

        /// <summary>仲間の攻撃 CD（中断完了後に採取したもの）。</summary>
        public CompanionCombatTransferSnapshot CompanionCombat { get; }

        /// <summary>仲間の構え・回避 CD。</summary>
        public CompanionDefenseTransferSnapshot CompanionDefense { get; }

        /// <summary>仲間の守護 CD。</summary>
        public CompanionGuardianTransferSnapshot CompanionGuardian { get; }

        /// <summary>仲間の配置状態（§4.6 の復元表）。</summary>
        public CompanionState CompanionState { get; }

        /// <summary>復旧用の元エリア（§4.4）。読込失敗で戻る先。</summary>
        public StableId OriginAreaId { get; }

        /// <summary>復旧用の元入口。</summary>
        public StableId OriginEntryId { get; }

        public AreaTransferSnapshot(
            bool hasPlayer,
            PlayerVitalsTransferSnapshot playerVitals,
            HitReactionTransferSnapshot playerHitReaction,
            bool hasCompanion,
            StableId companionId,
            CompanionVitalsTransferSnapshot companionVitals,
            CompanionCombatTransferSnapshot companionCombat,
            CompanionDefenseTransferSnapshot companionDefense,
            CompanionGuardianTransferSnapshot companionGuardian,
            CompanionState companionState,
            StableId originAreaId,
            StableId originEntryId)
        {
            HasPlayer = hasPlayer;
            PlayerVitals = playerVitals;
            PlayerHitReaction = playerHitReaction;
            HasCompanion = hasCompanion;
            CompanionId = companionId;
            CompanionVitals = companionVitals;
            CompanionCombat = companionCombat;
            CompanionDefense = companionDefense;
            CompanionGuardian = companionGuardian;
            CompanionState = companionState;
            OriginAreaId = originAreaId;
            OriginEntryId = originEntryId;
        }

        /// <summary>何も運ばない既定値（初回入場・直開き）。</summary>
        public bool IsEmpty => !HasPlayer && !HasCompanion;
    }
}
