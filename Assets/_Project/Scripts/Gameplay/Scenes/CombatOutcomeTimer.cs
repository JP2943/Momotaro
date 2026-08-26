namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 勝敗結果の表示・Retry 受付の時間境界を持つ純粋モデル（Phase3.5 P3.5-08。仕様書 §4.3 / §9.1 / §9.2）。
    /// Victory／Defeat に入った瞬間から Game Time を積み、Retry 受付（既定 0.50s の誤入力防止後）と結果パネル表示
    /// （既定 0.75s 後）の可否を決定的に返す。MonoBehaviour 非依存にし、境界（直前／一致／直後）を EditMode で検証できる。
    ///
    /// 時間管理・GameMode ロック・Scene 再読込・入力読取は <see cref="CombatOutcomeController"/> が担い、本クラスは可否計算のみ。
    /// </summary>
    public sealed class CombatOutcomeTimer
    {
        private readonly float _retryArmDelay;   // 誤入力防止（§4.3 0.50s）。
        private readonly float _panelDelay;       // 結果パネル表示（§9.1 0.75s）。

        private bool _active;
        private float _elapsed;

        /// <summary>受付遅延（Retry 有効まで）とパネル表示遅延を指定して生成する（既定 0.50s / 0.75s）。</summary>
        public CombatOutcomeTimer(float retryArmDelay = 0.50f, float panelDelay = 0.75f)
        {
            _retryArmDelay = retryArmDelay < 0f ? 0f : retryArmDelay;
            _panelDelay = panelDelay < 0f ? 0f : panelDelay;
        }

        /// <summary>結果状態（Victory／Defeat）で計時中か。</summary>
        public bool Active => _active;

        /// <summary>経過秒（結果状態に入ってから）。</summary>
        public float Elapsed => _elapsed;

        /// <summary>Retry 入力を受け付けてよいか（誤入力防止時間を過ぎたか）。非結果状態では常に false。</summary>
        public bool RetryArmed => _active && _elapsed >= _retryArmDelay;

        /// <summary>結果パネルを表示してよいか。非結果状態では常に false。</summary>
        public bool ResultVisible => _active && _elapsed >= _panelDelay;

        /// <summary>結果状態に入る（計時開始・リセット）。二重 Enter は先頭からやり直す。</summary>
        public void Enter()
        {
            _active = true;
            _elapsed = 0f;
        }

        /// <summary>計時を止めてリセットする（結果状態を抜ける・再読込・Disable）。二重呼び出し安全。</summary>
        public void Reset()
        {
            _active = false;
            _elapsed = 0f;
        }

        /// <summary>時間を進める（Game Time。結果状態のみ積算。0 以下の dt は無視）。</summary>
        public void Tick(float deltaTime)
        {
            if (_active && deltaTime > 0f)
            {
                _elapsed += deltaTime;
            }
        }
    }
}
