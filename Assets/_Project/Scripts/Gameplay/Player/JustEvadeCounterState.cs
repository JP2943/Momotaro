namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// ジャスト回避の反撃強化（P6C 仕様 §3・§5）。純粋クラスで、時間は呼び出し側が Gameplay 時計の deltaTime を渡す。
    ///
    /// 規則：
    /// <list type="bullet">
    /// <item>成功（<see cref="Grant"/>）で権利を 1 回分持ち、残時間を有効時間に置き直す。<b>蓄積しない</b>（何度成功しても 1 回分、残時間の更新だけ）。</item>
    /// <item>有効区間は「成功時刻から満了時刻<b>未満</b>」。残時間が 0 になった Tick 以降は使えない（同時は強化なし）。</item>
    /// <item><see cref="TryConsume"/> は<b>通常攻撃の段が実際に開始した時</b>だけ呼ぶ。消費は戻さない（空振り・被弾・キャンセルでも）。</item>
    /// <item>死亡・入場・休息等で <see cref="Clear"/>。保存はしない（短時間の戦闘状態。P6C 仕様 §8）。</item>
    /// </list>
    /// </summary>
    public sealed class JustEvadeCounterState
    {
        private readonly float _multiplier;
        private readonly float _seconds;
        private float _remaining;
        private bool _charged;

        public JustEvadeCounterState(float multiplier = 1.5f, float seconds = 2.0f)
        {
            _multiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 1f ? 1f : multiplier;
            _seconds = float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f ? 0f : seconds;
        }

        /// <summary>強化の権利を持っているか（未使用・未満了）。</summary>
        public bool IsCharged => _charged;

        /// <summary>残時間（権利が無ければ 0）。</summary>
        public float Remaining => _charged ? _remaining : 0f;

        /// <summary>倍率（設定値）。</summary>
        public float Multiplier => _multiplier;

        /// <summary>有効時間（設定値）。</summary>
        public float Seconds => _seconds;

        /// <summary>付与した回数（更新を含む。診断・テスト用）。</summary>
        public int GrantCount { get; private set; }

        /// <summary>消費した回数（診断・テスト用）。</summary>
        public int ConsumeCount { get; private set; }

        /// <summary>満了で失った回数（診断・テスト用）。</summary>
        public int ExpireCount { get; private set; }

        /// <summary>成功で権利を得る（既に持っていれば残時間を有効時間へ更新するだけ）。</summary>
        public void Grant()
        {
            if (_seconds <= 0f)
            {
                return;
            }

            _charged = true;
            _remaining = _seconds;
            GrantCount++;
        }

        /// <summary>Gameplay 時計で残時間を減らす。0 に達したら権利を失う。</summary>
        public void Tick(float deltaTime)
        {
            if (!_charged || deltaTime <= 0f)
            {
                return;
            }

            _remaining -= deltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _charged = false;
                ExpireCount++;
            }
        }

        /// <summary>段の開始時に呼ぶ。権利があれば消費して倍率を返す（無ければ 1 と false）。</summary>
        public bool TryConsume(out float multiplier)
        {
            if (!_charged)
            {
                multiplier = 1f;
                return false;
            }

            _charged = false;
            _remaining = 0f;
            ConsumeCount++;
            multiplier = _multiplier;
            return true;
        }

        /// <summary>権利を消す（死亡・入場・休息・Load）。回数の記録は残す。</summary>
        public void Clear()
        {
            _charged = false;
            _remaining = 0f;
        }
    }
}
