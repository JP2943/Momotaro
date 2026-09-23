using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// いま活動しているエリアの状態（P5-03b。仕様書 v1.1 §5.1 手順 8）。
    ///
    /// <b>AreaReady を持つのが主な仕事。</b> 初期化順（§5.1）の 1〜7 が終わるまで Actor と入力を止め、
    /// 8 で確定させる。Script Execution Order や「1 フレーム待てば大丈夫」に依存しない。
    ///
    /// 常駐させない。Scene と一緒に作られて壊れるので、旧 Scene の Context が新しい Scene へ残らない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaContext : MonoBehaviour
    {
        /// <summary>初期化が完了し、活動と入力を許可してよいか（§5.1 手順 8）。</summary>
        public bool IsAreaReady { get; private set; }

        /// <summary>このエリアの安定 ID。</summary>
        public StableId AreaId { get; private set; }

        /// <summary>到着に使った入口の安定 ID。</summary>
        public StableId EntryId { get; private set; }

        /// <summary>Ready を確定した回数（診断・テスト用。二重確定を検出する）。</summary>
        public int ReadyCount { get; private set; }

        /// <summary>
        /// 初期化の開始を記録する。ここではまだ Ready にしない（§5.1 手順 1〜7 が先）。
        /// </summary>
        public void BeginInitialize(StableId areaId, StableId entryId)
        {
            AreaId = areaId;
            EntryId = entryId;
            IsAreaReady = false;
            IsPrepared = false;
        }

        /// <summary>
        /// 到着側が「準備できた」と報告する（§5.1 手順 1〜7 の完了）。
        ///
        /// <b>これだけでは活動を許可しない。</b> 準備が整っていても、その到着が
        /// 「もう捨てられた遷移のもの」かもしれないため（監視がタイムアウトしたあとに
        /// 遅れて届いた目的地など）。許可の判断は遷移の所有者が行う（GPT レビュー R2）。
        /// </summary>
        public void MarkPrepared()
        {
            PreparedCount++;
            IsPrepared = true;
            Prepared?.Invoke();
        }

        /// <summary>
        /// 準備が整ったことの通知（§5.1 手順 7）。<b>カメラの即時配置はこれを購読して行う。</b>
        ///
        /// 初期化担当（Infrastructure）から Presentation のカメラを直接呼ばせない。
        /// 層の向きが逆になるうえ、カメラを持たない Scene（試遊の起動 Scene・テスト用の最小構成）で
        /// 「未配線なので失敗」と言い出す羽目になる。通知なら、聞いている者だけが反応する。
        ///
        /// 活動許可（<see cref="Activate"/>）ではなく<b>準備完了</b>に紐づけるのは、
        /// 許可は遷移の所有者が後から出すもので、その間もカメラは到着位置を映していてほしいため。
        /// </summary>
        public event System.Action Prepared;

        /// <summary>
        /// 活動と入力を許可する（§5.1 手順 8）。<b>遷移の所有者だけが呼ぶ。</b>
        /// 世代・対象・タイムアウト状態を確認したうえでの許可なので、ここは確定だけを行う。
        /// 直開き（遷移を伴わない起動）では所有者が居ないため、初期化担当が自分で呼ぶ。
        /// </summary>
        public void Activate()
        {
            ReadyCount++;
            IsAreaReady = true;
        }

        /// <summary>準備が整ったか（活動許可とは別）。</summary>
        public bool IsPrepared { get; private set; }

        /// <summary>準備完了を報告した回数（診断・テスト用）。</summary>
        public int PreparedCount { get; private set; }

        /// <summary>遷移の受理時に活動を閉じる（§6.2 手順 3）。</summary>
        public void CloseForTransition()
        {
            IsAreaReady = false;
        }

        /// <summary>
        /// 遷移が失敗し、<b>この Scene がまだ生きている</b>ときに元の活動を再開する（§6.3 の 2 行目）。
        ///
        /// 初期化をやり直すわけではないので <see cref="ReadyCount"/> は増やさず、別に数える。
        /// 中断済みの調査を自動再開しないのは呼び出し側の責任（§6.3）。
        /// </summary>
        public void ReopenAfterFailedTransition()
        {
            ReopenCount++;
            IsAreaReady = true;
        }

        /// <summary>失敗から活動を戻した回数（診断・テスト用）。</summary>
        public int ReopenCount { get; private set; }

        private void OnDestroy()
        {
            IsAreaReady = false;
        }
    }
}
