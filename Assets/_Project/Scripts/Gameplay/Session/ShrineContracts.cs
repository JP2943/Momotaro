using Momotaro.Core.Identification;
using Momotaro.Gameplay.Interaction;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// お地蔵様の操作の受け口（P6A-03。仕様 §5）。Scene 側のお地蔵様（<see cref="ShrinePoint"/>）はこの狭い契約だけを見る。
    /// 実体は常駐の campaign サービス（Infrastructure）で、カタログ・遷移・保存と結ぶ。
    /// </summary>
    public interface IShrineOperations
    {
        /// <summary>調べた（登録・保存・メニューを開く）。回復はしない。</summary>
        AreaInteractionOutcome OnShrineInteracted(StableId shrineId);
    }

    /// <summary>お地蔵様の操作の供給点（既存の Provider と同じ形。解除は所有者一致）。</summary>
    public static class ShrineOperationsProvider
    {
        /// <summary>現在の受け口（未設定なら null＝P6 campaign ではない）。</summary>
        public static IShrineOperations Current { get; set; }

        /// <summary>自分が差したものだけを外す。</summary>
        public static void ReleaseIfOwner(IShrineOperations owned)
        {
            if (owned != null && ReferenceEquals(Current, owned))
            {
                Current = null;
            }
        }
    }

    /// <summary>休息の対象（主人公と仲間）。<c>AreaActorTransferPort</c> が実装する。</summary>
    public interface IRestTarget
    {
        /// <summary>全回復（HP・スタミナ最大、Down 復帰、ひるみ等の解消、CD 解除）。場所は変えない。</summary>
        void RestoreForRest();

        /// <summary>
        /// 成長の効果一式を置き直す（P6B 01。何度呼んでも同じ結果）。基礎値と取得 ID から作った合計を渡す。
        /// 現在値は回復しない（上限超過分だけ切り詰める）。
        /// </summary>
        void ApplyGrowthEffects(in Momotaro.Gameplay.Progression.GrowthEffects effects);
    }

    /// <summary>活動中の Area で普通敵をその場で作り直す口。<c>AreaFieldEnemyDirector</c> が実装する。</summary>
    public interface IFieldEnemyRebuild
    {
        void RebuildNow();
    }
}
