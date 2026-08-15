using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 敵ガード能力の時間・Cooldown 状態（Phase3 P3-10。§9「最大2秒、CD3秒」）。純粋・決定的（deltaTime 注入）で MonoBehaviour
    /// 非依存。<see cref="TryStart"/> で構え、<see cref="Tick"/> で経過を進め、最大保持時間で自動解除して Cooldown に入る。
    /// Cooldown 中は再構えできない。実際の被ダメージ軽減は <see cref="EnemyGuardMath"/>、方向・Special 貫通は命中時に判定する。
    /// </summary>
    public sealed class EnemyGuardAbility
    {
        /// <summary>ガードの最大保持時間（秒）。§9。</summary>
        public const float MaxHoldSeconds = 2f;

        private readonly float _maxHold;
        private readonly float _cooldown;
        private bool _guarding;
        private float _held;       // 現在の構え経過秒。
        private float _cooldownRemaining;

        /// <param name="cooldownSeconds">再構えまでの Cooldown（秒。Archetype 由来。既定 3）。</param>
        /// <param name="maxHoldSeconds">最大保持時間（秒。既定 <see cref="MaxHoldSeconds"/>）。</param>
        public EnemyGuardAbility(float cooldownSeconds = 3f, float maxHoldSeconds = MaxHoldSeconds)
        {
            _cooldown = Mathf.Max(0f, cooldownSeconds);
            _maxHold = Mathf.Max(0.01f, maxHoldSeconds);
        }

        /// <summary>構え中か（この間の前方命中を軽減する）。</summary>
        public bool IsGuarding => _guarding;

        /// <summary>今から構えられるか（非構え・Cooldown 明け）。</summary>
        public bool IsReady => !_guarding && _cooldownRemaining <= 0f;

        /// <summary>Cooldown の残り秒（Debug/テスト用）。</summary>
        public float CooldownRemaining => _cooldownRemaining;

        /// <summary>現在の構え経過秒（Debug/テスト用）。</summary>
        public float HeldSeconds => _held;

        /// <summary>構えを開始する。開始できたら true（Cooldown 中・構え中は false）。</summary>
        public bool TryStart()
        {
            if (!IsReady)
            {
                return false;
            }

            _guarding = true;
            _held = 0f;
            return true;
        }

        /// <summary>構えを終了し、Cooldown に入る（自発解除／危険消失）。冪等。</summary>
        public void Release()
        {
            if (!_guarding)
            {
                return;
            }

            _guarding = false;
            _held = 0f;
            _cooldownRemaining = _cooldown;
        }

        /// <summary>時間を進める。構え中は保持時間を積み、最大到達で自動解除して Cooldown へ。非構え時は Cooldown を減らす。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (_guarding)
            {
                _held += deltaTime;
                if (_held >= _maxHold)
                {
                    Release(); // 最大保持で自動解除 → Cooldown。
                }

                return;
            }

            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);
            }
        }

        /// <summary>構え・Cooldown を初期化する（撃破・無効化・検証の再試行用）。</summary>
        public void Reset()
        {
            _guarding = false;
            _held = 0f;
            _cooldownRemaining = 0f;
        }
    }
}
