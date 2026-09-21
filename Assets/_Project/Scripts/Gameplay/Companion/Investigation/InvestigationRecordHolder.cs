using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// Scene に 1 つ置く記録の保持先。探索の調停役（<see cref="InvestigationCoordinator"/>）が
    /// 明示参照で読む（万能 static にしない）。
    ///
    /// <b>既定は Scene ローカル記録</b>（P4 と同じ挙動。Retry で Scene ごと作り直され、記録は自然に初期化される）。
    /// P5 では <see cref="Bind"/> で Session の Area 記録（<c>AreaRuntimeState.Investigation</c>）へ差し替え、
    /// エリアを往復しても調査済みが残るようにする（仕様書 v1.1 §4.3）。差し替えても調停役から見える契約は
    /// <see cref="IInvestigationRecordSink"/> のままで、巨大な全世界 State は渡らない。
    ///
    /// Bind の保護は <see cref="Momotaro.Gameplay.Progression.PlayerProgressHolder"/> と同じ考え方：
    /// 同一参照の再 Bind は冪等、<b>この Holder を通して記録を書いたあとの別参照への差し替えは拒否</b>する。
    /// 記録の読み取り（表示・候補選択）では使用済みにしない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvestigationRecordHolder : MonoBehaviour
    {
        private readonly InvestigationRecord _local = new InvestigationRecord();

        private IInvestigationRecordSink _bound;
        private bool _used;

        /// <summary>調停役・表示が読む記録。未注入なら Scene ローカル記録（P4 互換）。</summary>
        public IInvestigationRecordSink Record => _bound ?? _local;

        /// <summary>外部記録が注入済みか（配線確認・Validator・テスト用）。</summary>
        public bool IsBound => _bound != null;

        /// <summary>この Holder を通して記録を書いたか（Bind 保護の判定。読み取りでは立たない）。</summary>
        public bool IsUsed => _used;

        /// <summary>Scene ローカル記録の実体（診断・テスト用。注入中でも中身を確認できる）。</summary>
        public InvestigationRecord LocalRecord => _local;

        /// <summary>
        /// 外部の調査記録を注入する。Area 初期化が、Actor の活動開始より前に呼ぶ（§5.1 手順 4）。
        /// </summary>
        /// <returns>注入が成立したか。拒否した場合は既存の記録を保持する。</returns>
        public bool Bind(IInvestigationRecordSink record)
        {
            if (record == null)
            {
                GameLog.WarningOnce(LogCategory.Scene, "investigation_record_bind_null",
                    "調査記録の注入に null が渡されたため無視しました（既存の記録を保持します）。");
                return false;
            }

            // 同一参照の再 Bind は冪等。Scene 再読込後の再配線で記録を捨てない。
            if (ReferenceEquals(_bound, record))
            {
                return true;
            }

            if (_used)
            {
                GameLog.WarningOnce(LogCategory.Scene, "investigation_record_bind_after_use",
                    "この Holder は既に記録の書込みに使われているため、別の調査記録へ差し替えませんでした。"
                    + "注入は Actor の活動開始より前に行ってください（仕様書 §5.1）。");
                return false;
            }

            _bound = record;
            return true;
        }

        /// <summary>
        /// 調査済みとして確定する。調停役はここを通す（<see cref="Record"/> を直接書かない）。
        /// 既に済んでいれば false で、呼び出し元は完了通知を発行しない（§6.3、P5-E04）。
        /// </summary>
        public bool TryMarkInvestigated(StableId pointId)
        {
            _used = true;
            return Record.TryMarkInvestigated(pointId);
        }
    }
}
