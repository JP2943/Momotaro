namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 攻撃の先行入力（Buffer）を一定時間だけ保持する純粋クラス（Phase2 P2-02。仕様書 3.1.1 / 本書 §4.2）。
    /// 攻撃可能でない瞬間の押下を <see cref="Buffer"/> で預かり、<see cref="Tick"/> で時間経過させ、
    /// 期限内に <see cref="TryConsume"/> されれば実行、切れれば破棄する。Input System・時間 API に依存せず、
    /// deltaTime を外部から受け取るためテスト可能。
    ///
    /// 保持時間（Window）は試作値 0.30 秒を既定とし、呼び出し側（Inspector 値）から外部化する
    /// （本書 §0.2「試作値はコードへ直書きしない」）。段別の詳細時間・キャンセル窓は P2-03B で扱う。
    /// </summary>
    public sealed class AttackInputBuffer
    {
        private float _remaining;

        /// <summary>先行入力を保持する時間（秒）。</summary>
        public float Window { get; }

        /// <summary>有効な先行入力を保持しているか。</summary>
        public bool HasBuffered => _remaining > 0f;

        /// <summary>残り保持時間（秒。診断・テスト用）。</summary>
        public float Remaining => _remaining;

        /// <summary>保持時間（秒）を指定して生成する。負値は 0 に丸める。</summary>
        public AttackInputBuffer(float window)
        {
            Window = window < 0f ? 0f : window;
        }

        /// <summary>押下を預かり、保持タイマーを満タンにする。</summary>
        public void Buffer()
        {
            _remaining = Window;
        }

        /// <summary>時間を進める。0 未満にはならない。</summary>
        public void Tick(float deltaTime)
        {
            if (_remaining <= 0f)
            {
                return;
            }

            _remaining -= deltaTime;
            if (_remaining < 0f)
            {
                _remaining = 0f;
            }
        }

        /// <summary>
        /// 先行入力を消費する。保持中なら消費して true、保持していなければ false。
        /// 消費すると保持は解除される（1 押下 = 1 消費）。
        /// </summary>
        public bool TryConsume()
        {
            if (_remaining <= 0f)
            {
                return false;
            }

            _remaining = 0f;
            return true;
        }

        /// <summary>保持を破棄する（Disable・モード遮断時など）。</summary>
        public void Clear()
        {
            _remaining = 0f;
        }
    }
}
