using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 敵回避能力の無敵・Cooldown 状態（Phase3 P3-10。§9「短い無敵、CD 3〜5秒、連続不可」）。純粋・決定的（deltaTime 注入）で
    /// MonoBehaviour 非依存。<see cref="TryStart"/> で回避を開始し短い無敵（I-frame）を得る。無敵が切れると Cooldown に入り、
    /// Cooldown 明けまで再回避できない（＝連続不可）。危険刺激の検知や退避移動は上位（<see cref="IEnemyDangerSense"/>／Controller）が担う。
    /// </summary>
    public sealed class EnemyEvadeAbility
    {
        /// <summary>回避の無敵時間（秒）の既定。§9「短い無敵」。</summary>
        public const float DefaultInvulnerableSeconds = 0.30f;

        private readonly float _invulnSeconds;
        private readonly float _cooldown;
        private bool _evading;
        private float _invulnRemaining;
        private float _cooldownRemaining;

        /// <param name="cooldownSeconds">再回避までの Cooldown（秒。Archetype 由来。3〜5）。</param>
        /// <param name="invulnerableSeconds">無敵時間（秒。既定 <see cref="DefaultInvulnerableSeconds"/>）。</param>
        public EnemyEvadeAbility(float cooldownSeconds = 4f, float invulnerableSeconds = DefaultInvulnerableSeconds)
        {
            _cooldown = Mathf.Max(0f, cooldownSeconds);
            _invulnSeconds = Mathf.Max(0.01f, invulnerableSeconds);
        }

        /// <summary>回避中（無敵）か。この間の命中は無効化する。</summary>
        public bool IsInvulnerable => _evading && _invulnRemaining > 0f;

        /// <summary>回避モーション中か（無敵時間中。退避移動の期間に用いる）。</summary>
        public bool IsEvading => _evading;

        /// <summary>今から回避できるか（非回避・Cooldown 明け＝連続不可を満たす）。</summary>
        public bool IsReady => !_evading && _cooldownRemaining <= 0f;

        /// <summary>Cooldown の残り秒（Debug/テスト用）。</summary>
        public float CooldownRemaining => _cooldownRemaining;

        /// <summary>無敵の残り秒（Debug/テスト用）。</summary>
        public float InvulnerableRemaining => _invulnRemaining;

        /// <summary>回避を開始する。開始できたら true（回避中・Cooldown 中は false＝連続不可）。</summary>
        public bool TryStart()
        {
            if (!IsReady)
            {
                return false;
            }

            _evading = true;
            _invulnRemaining = _invulnSeconds;
            return true;
        }

        /// <summary>時間を進める。回避中は無敵を減らし、切れたら回避終了→Cooldown へ。非回避時は Cooldown を減らす。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            if (_evading)
            {
                _invulnRemaining = Mathf.Max(0f, _invulnRemaining - deltaTime);
                if (_invulnRemaining <= 0f)
                {
                    _evading = false;
                    _cooldownRemaining = _cooldown; // 無敵終了で Cooldown 開始（連続不可）。
                }

                return;
            }

            if (_cooldownRemaining > 0f)
            {
                _cooldownRemaining = Mathf.Max(0f, _cooldownRemaining - deltaTime);
            }
        }

        /// <summary>回避・Cooldown を初期化する（撃破・無効化・検証の再試行用）。</summary>
        public void Reset()
        {
            _evading = false;
            _invulnRemaining = 0f;
            _cooldownRemaining = 0f;
        }
    }
}
