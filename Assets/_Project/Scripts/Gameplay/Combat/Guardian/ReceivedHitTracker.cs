using System.Collections.Generic;

namespace Momotaro.Gameplay.Combat.Guardian
{
    /// <summary>
    /// 受け手側の多重ヒット排除（P4-01）。攻撃側の <see cref="MultiHitTracker"/> は「1 回の攻撃発動」が所有し
    /// （<see cref="HitId"/> × 対象）で弾くが、肩代わりによる転送は攻撃側を経由しないためそこでは弾けない。
    /// そこで守護者（仲間）の被弾入口が本トラッカーを所有し、<see cref="HitId"/> 単位で最初の 1 回だけ受理する。
    ///
    /// これにより、判定からの直接命中と主人公からの転送が<b>どちらの順で届いても</b>、2 回目は HP・状態・リアクション・
    /// 結果通知のいずれも発生しない。<see cref="HitId"/> は「攻撃発動 × 段」なので、別段・別発動は別命中として受理される。
    ///
    /// 記録は容量上限つきの FIFO で保持し、上限を超えた分は古い順に忘れる。これにより Encounter やリセットの呼び忘れが
    /// あっても本編進行で無制限に蓄積しない（同一フレーム内の重複排除には十分な容量を既定とする）。
    /// 明示的な初期化は <see cref="Clear"/>（Encounter 開始・Down 後の完全退場・Disable・Scene 離脱）で行う。
    /// </summary>
    public sealed class ReceivedHitTracker
    {
        /// <summary>既定の保持件数。同一フレームに集中する多重命中を弾くには十分な値。</summary>
        public const int DefaultCapacity = 64;

        private readonly HashSet<HitId> _accepted;
        private readonly Queue<HitId> _order;
        private readonly int _capacity;

        /// <summary>既定容量で生成する。</summary>
        public ReceivedHitTracker() : this(DefaultCapacity)
        {
        }

        /// <summary>容量を指定して生成する（1 未満は 1 に丸める）。</summary>
        public ReceivedHitTracker(int capacity)
        {
            _capacity = capacity < 1 ? 1 : capacity;
            _accepted = new HashSet<HitId>();
            _order = new Queue<HitId>(_capacity);
        }

        /// <summary>保持件数（診断・テスト用）。</summary>
        public int Count => _accepted.Count;

        /// <summary>保持できる最大件数。</summary>
        public int Capacity => _capacity;

        /// <summary>
        /// この命中を受理してよいかを判定し、初回なら記録して true を返す。既に受理済みなら false（＝無視すべき重複）。
        /// 受理した結果、保持件数が上限を超えた場合は最も古い記録を 1 件忘れる。
        /// </summary>
        public bool TryAccept(HitId hitId)
        {
            if (!_accepted.Add(hitId))
            {
                return false;
            }

            _order.Enqueue(hitId);
            if (_order.Count > _capacity)
            {
                HitId oldest = _order.Dequeue();
                _accepted.Remove(oldest);
            }

            return true;
        }

        /// <summary>この命中が受理済みかを返す（副作用なし）。</summary>
        public bool HasAccepted(HitId hitId) => _accepted.Contains(hitId);

        /// <summary>記録をすべて破棄する（Encounter 開始・退場・Disable・Scene 離脱）。二重呼び出し安全。</summary>
        public void Clear()
        {
            _accepted.Clear();
            _order.Clear();
        }
    }
}
