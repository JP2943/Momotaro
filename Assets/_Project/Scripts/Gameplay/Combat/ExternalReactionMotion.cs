using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 通常ヒットバック／ガードバックの「移動プロファイル」を距離・時間で決定的に表す純粋モデル（Phase3.5 P3.5-08A。仕様書 §7.4）。
    /// 物理・Rigidbody・Transform を持たず、指定方向（XZ 平面へ平坦化）に「距離 ÷ 時間」の一定速度を <see cref="HitbackSeconds"/> 相当
    /// だけ供給する。実際の移動適用（Rigidbody 速度への反映・壁停止・Y 不変）は Motor（<see cref="IReactionMotor"/> 実装）が担う。
    ///
    /// モデル自体は Y を持たない（方向の Y 成分を捨てる）ため、Motor 側で Y を保持すれば Y 座標は不変になる。時間駆動は外部
    /// （Motor の FixedUpdate）から <see cref="Tick"/> で与え、テストが決定的に検証できる。壁による短縮は物理側で起き、モデルは
    /// 「与えたい速度」のみを保持する（距離はあくまで空走時の理論値）。
    /// </summary>
    public sealed class ExternalReactionMotion
    {
        private Vector3 _velocity; // XZ のみ（Y=0）。
        private float _remaining;

        /// <summary>供給中か（残時間 &gt; 0）。</summary>
        public bool IsActive => _remaining > 0f;

        /// <summary>現在供給している XZ 速度（World、Y=0）。非供給時はゼロ。</summary>
        public Vector3 CurrentVelocity => _remaining > 0f ? _velocity : Vector3.zero;

        /// <summary>残時間（秒。テスト・診断用）。</summary>
        public float Remaining => _remaining;

        /// <summary>
        /// 押し出しを開始する。<paramref name="direction"/> は XZ へ平坦化・正規化する。距離・時間のいずれかが 0 以下、または
        /// 方向が実質ゼロなら供給しない（<see cref="Clear"/> 相当）。二重呼び出しは上書き（最新の押し出しを優先）。
        /// </summary>
        public void Begin(Vector3 direction, float distance, float seconds)
        {
            Vector3 dir = new Vector3(direction.x, 0f, direction.z);
            float mag = dir.magnitude;
            if (mag < 1e-6f || distance <= 0f || seconds <= 0f)
            {
                Clear();
                return;
            }

            dir /= mag;
            _velocity = dir * (distance / seconds);
            _remaining = seconds;
        }

        /// <summary>時間を進める（Motor の FixedUpdate から与える）。残時間が尽きたら速度を落とす。</summary>
        public void Tick(float deltaTime)
        {
            if (_remaining <= 0f)
            {
                return;
            }

            _remaining -= deltaTime < 0f ? 0f : deltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _velocity = Vector3.zero;
            }
        }

        /// <summary>供給を打ち切る（Disable・Defeated・Intermission・Retry・Scene 離脱で残留変位を残さない）。</summary>
        public void Clear()
        {
            _remaining = 0f;
            _velocity = Vector3.zero;
        }
    }
}
