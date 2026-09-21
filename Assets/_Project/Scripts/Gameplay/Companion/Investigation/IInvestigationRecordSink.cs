using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// 調査済み記録への<b>狭い書込み契約</b>（P5-01。仕様書 v1.1 §4.3）。
    ///
    /// <see cref="IInvestigationRecord"/> は読み取りだけを持つため、調停役が完了を確定する経路は
    /// これまで具象 <see cref="InvestigationRecord"/> に固定されていた。P5 では記録の実体が
    /// Scene ローカルから Session の Area 記録（<c>AreaRuntimeState</c>）へ替わるので、
    /// <b>差し替え可能な最小の書込み口</b>をここに切り出す。
    ///
    /// <b>意図的に狭い。</b> 巨大な全世界 State を <see cref="InvestigationCoordinator"/> へ渡さないための
    /// 契約であり（§4.3）、Area・徳・門・Encounter といった他の世界状態はここから触れない。
    /// </summary>
    public interface IInvestigationRecordSink : IInvestigationRecord
    {
        /// <summary>調査済みの件数（診断・テスト用）。</summary>
        int Count { get; }

        /// <summary>
        /// 調査済みにする。<b>既に済んでいれば false</b>（完了は 1 地点につき 1 回だけ。v1.0 §6.3）。
        /// 調停役はこの false を完了通知の抑止に使うため、Area 記録を注入した状態で再入場しても
        /// 成功イベントが再発行されない（P5-E04）。
        /// </summary>
        bool TryMarkInvestigated(StableId pointId);
    }
}
