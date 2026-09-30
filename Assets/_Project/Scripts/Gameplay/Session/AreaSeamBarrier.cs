using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 接続口をふさぐ<b>見えない境界</b>（P5.5 §7.3。工程 P55-14b。試遊報告③）。
    ///
    /// <b>なぜ要るのか。</b> 外周壁は接続口の区間を空けて 2 本に分けてある——
    /// スライド中は両 Area が描かれるので、境界に<b>見える</b>壁が立っていると画面を覆う。
    /// ところがそれで<b>当たり判定まで無くした</b>ので、出口判定（§6.1 の「範囲内で出口方向へ
    /// 0.15 秒連続入力」）が成立しないまま通り抜けると、主人公が Area の外へ出られた。
    /// カメラは追従範囲に収まっているので、<b>主人公が画面から消えたまま進む</b>。
    ///
    /// <b>見えないので画面を覆わない。</b> 壁を消した理由は Renderer の話で、Collider には当てはまらない。
    ///
    /// <b>遷移は境界を跨がない。</b> 受理されると主人公は行き先の<b>入口へ配置される</b>
    /// （<c>AreaInitializer.PlaceArrivals</c>）ので、物理的に境界を越える必要がそもそも無い。
    /// だから常設でよく、遷移中に開ける必要も無い。
    ///
    /// <b>ただしスライドの表示経路検査からは外す</b>（§7.2。GPT 受入 3）。
    /// あの検査は出発側の当たりを戻して <c>SphereCast</c> で壁を見るので、
    /// この境界を普通の壁として扱うと<b>必ず塞がっている</b>ことになり、
    /// 接続そのものが「表示経路を安全に作れません」で失敗する。
    /// 外すのは<b>いま使っている接続の境界だけ</b>で、通常の壁・閉じた門・
    /// 別の接続の境界は障害物のまま扱う——だから接続 ID を持たせてある。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaSeamBarrier : MonoBehaviour
    {
        [Tooltip("この境界がふさいでいる接続の ID。表示経路検査から外す相手をこれで選ぶ。")]
        [SerializeField] private string _connectionId = string.Empty;

        [Tooltip("逆向きの接続 ID（同じ口を両側から使うため）。")]
        [SerializeField] private string _reverseConnectionId = string.Empty;

        [Tooltip("通行を止める Collider（見た目は持たない）。")]
        [SerializeField] private Collider _blocker;

        /// <summary>ふさいでいる接続の ID。</summary>
        public StableId ConnectionId =>
            string.IsNullOrEmpty(_connectionId) ? default : new StableId(_connectionId);

        /// <summary>逆向きの接続 ID。</summary>
        public StableId ReverseConnectionId =>
            string.IsNullOrEmpty(_reverseConnectionId) ? default : new StableId(_reverseConnectionId);

        /// <summary>通行を止める Collider。</summary>
        public Collider Blocker => _blocker;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _blocker != null && ConnectionId.IsValid;

        /// <summary>
        /// <b>見た目を持っていないか</b>（Validator・テスト用）。
        ///
        /// Renderer を持たせると、壁を消した理由（スライド中に画面を覆う）へ逆戻りする。
        /// </summary>
        public bool IsInvisible => GetComponentInChildren<Renderer>(true) == null;

        /// <summary>その接続の境界か（往復どちらの ID でも一致させる）。</summary>
        public bool Covers(StableId connectionId)
        {
            if (!connectionId.IsValid)
            {
                return false;
            }

            return (ConnectionId.IsValid && ConnectionId.Value == connectionId.Value)
                   || (ReverseConnectionId.IsValid && ReverseConnectionId.Value == connectionId.Value);
        }

        /// <summary>Builder・テストからの設定。</summary>
        public void Configure(StableId connectionId, StableId reverseConnectionId, Collider blocker)
        {
            _connectionId = connectionId.Value ?? string.Empty;
            _reverseConnectionId = reverseConnectionId.Value ?? string.Empty;
            _blocker = blocker;
        }
    }
}
