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
        }

        /// <summary>
        /// 初期化の完了を確定する（§5.1 手順 8）。Bind・復元・配置・カメラ・HUD の確認が
        /// すべて終わってから 1 回だけ呼ぶ。二重呼び出しは数えるが状態は変えない。
        /// </summary>
        public void ConfirmReady()
        {
            ReadyCount++;
            IsAreaReady = true;
        }

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
