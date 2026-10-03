using System;
using Momotaro.Gameplay.Player;

namespace Momotaro.Gameplay.Session
{
    /// <summary>きびだんご使用の時間設定（P6B 03。campaign の Data から。不変）。</summary>
    public readonly struct KibidangoUseConfig
    {
        public KibidangoUseConfig(float useSeconds, float commitSeconds, float moveSpeedMultiplier)
        {
            UseSeconds = useSeconds;
            CommitSeconds = commitSeconds;
            MoveSpeedMultiplier = moveSpeedMultiplier;
        }

        /// <summary>全動作（Gameplay 秒）。</summary>
        public float UseSeconds { get; }

        /// <summary>確定時刻（開始からの Gameplay 秒）。</summary>
        public float CommitSeconds { get; }

        /// <summary>使用中の歩行速度倍率。</summary>
        public float MoveSpeedMultiplier { get; }

        /// <summary>値が成り立つか（0 &lt; 確定 ≤ 全動作、0 ≤ 倍率 ≤ 1）。</summary>
        public bool IsValid =>
            CommitSeconds > 0f && UseSeconds >= CommitSeconds && !float.IsInfinity(UseSeconds)
            && MoveSpeedMultiplier >= 0f && MoveSpeedMultiplier <= 1f;
    }

    /// <summary>きびだんご使用の確定の結果（P6B 03）。</summary>
    public enum KibidangoCommitResult
    {
        Committed = 0,

        /// <summary>確定時に残数が足りない（回復も消費もしない。診断する）。</summary>
        OutOfStock = 1,

        /// <summary>配線が無い・死亡している等（回復も消費もしない）。</summary>
        NotAvailable = 2,
    }

    /// <summary>
    /// きびだんご使用の受け口（P6B 03）。主人公の状態（<see cref="PlayerStateController"/>）が開始可否と確定でだけ使う。
    /// 実体は campaign の残数（<see cref="GameSessionState.Kibidango"/>）と回復量（カタログ）を結ぶ。
    /// </summary>
    public interface IKibidangoUseService
    {
        /// <summary>この campaign で使用できるか（使用動作の時間設定）。P6A・P5 等は false。</summary>
        bool TryGetConfig(out KibidangoUseConfig config);

        /// <summary>残数。</summary>
        int Remaining { get; }

        /// <summary>
        /// 確定する：<b>残数 -1 と回復を同じ 1 更新</b>で行い、保存要求を 1 件出す。残数不足なら何も変えない。
        /// </summary>
        KibidangoCommitResult TryCommit(PlayerVitalsHolder target, out int healed);
    }

    /// <summary>きびだんご使用の受け口の供給点（既存の Provider と同じ形。解除は所有者一致）。</summary>
    public static class KibidangoUseProvider
    {
        /// <summary>現在の受け口（未設定なら null＝使用動作なし）。</summary>
        public static IKibidangoUseService Current { get; set; }

        /// <summary>自分が差したものだけを外す。</summary>
        public static void ReleaseIfOwner(IKibidangoUseService owned)
        {
            if (owned != null && ReferenceEquals(Current, owned))
            {
                Current = null;
            }
        }
    }

    /// <summary>
    /// campaign の Session とカタログで動く受け口（P6B 03）。Session・カタログは<b>呼ばれた時点のもの</b>を引く
    /// （New Game・Continue で Session が差し替わっても古い参照を握らない）。
    /// </summary>
    public sealed class CampaignKibidangoUse : IKibidangoUseService
    {
        private readonly Func<GameSessionState> _session;
        private readonly Func<CampaignCatalog> _campaign;

        public CampaignKibidangoUse(Func<GameSessionState> session, Func<CampaignCatalog> campaign)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _campaign = campaign ?? throw new ArgumentNullException(nameof(campaign));
        }

        /// <summary>確定の回数（診断・テスト用）。</summary>
        public int CommitCount { get; private set; }

        /// <summary>確定時の残数不足（診断・テスト用）。</summary>
        public int OutOfStockCount { get; private set; }

        /// <inheritdoc />
        public bool TryGetConfig(out KibidangoUseConfig config)
        {
            config = default;
            CampaignCatalog campaign = _campaign();
            GameSessionState session = _session();
            if (campaign == null || session == null || !campaign.HasKibidangoUse
                || string.IsNullOrEmpty(session.AdventureId))
            {
                return false;
            }

            config = new KibidangoUseConfig(campaign.KibidangoUseSeconds, campaign.KibidangoCommitSeconds,
                campaign.KibidangoMoveSpeedMultiplier);
            return config.IsValid;
        }

        /// <inheritdoc />
        public int Remaining => _session()?.Kibidango ?? 0;

        /// <inheritdoc />
        public KibidangoCommitResult TryCommit(PlayerVitalsHolder target, out int healed)
        {
            healed = 0;
            GameSessionState session = _session();
            CampaignCatalog campaign = _campaign();
            if (session == null || campaign == null || target == null || target.IsDefeated)
            {
                return KibidangoCommitResult.NotAvailable;
            }

            int amount = campaign.KibidangoHealOf(session.Progress);
            int applied = 0;
            if (!session.TryCommitKibidangoUse(() => applied = target.HealFromItem(amount)))
            {
                OutOfStockCount++;
                Momotaro.Core.Logging.GameLog.Warning(Momotaro.Core.Logging.LogCategory.Combat,
                    "Kibidango commit aborted: no stock left at the commit time (no heal, no consume).");
                return KibidangoCommitResult.OutOfStock;
            }

            healed = applied;
            CommitCount++;
            return KibidangoCommitResult.Committed;
        }
    }
}
