namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>
    /// 敵攻撃の段階機（Phase3 P3-04。§6.3）。不変 <see cref="EnemyAttackSnapshot"/> を用いて Prepare→Active→Recovery を
    /// 時間で進める純粋状態機。Prepare 中の追尾停止時刻・Active の Hitbox 窓を提供し、中断は <see cref="Cancel"/> で即時解除する。
    /// Gameplay 時間は deltaTime 注入で進め、Animator Event に依存しない。攻撃途中に原本 Data が変わっても Snapshot は不変。
    /// </summary>
    public sealed class EnemyAttackMachine
    {
        /// <summary>攻撃段階。</summary>
        public enum Phase
        {
            None = 0,
            Prepare = 1,
            Active = 2,
            Recovery = 3,
        }

        private EnemyAttackSnapshot _snapshot;
        private bool _hasSnapshot;

        /// <summary>1 Tick で発生した段階遷移。</summary>
        public readonly struct TickResult
        {
            public bool EnteredActive { get; }
            public bool EnteredRecovery { get; }
            public bool Finished { get; }

            public TickResult(bool enteredActive, bool enteredRecovery, bool finished)
            {
                EnteredActive = enteredActive;
                EnteredRecovery = enteredRecovery;
                Finished = finished;
            }
        }

        /// <summary>現在段階。</summary>
        public Phase Current { get; private set; } = Phase.None;

        /// <summary>攻撃中か（None 以外）。</summary>
        public bool IsAttacking => Current != Phase.None;

        /// <summary>開始からの経過秒。</summary>
        public float Elapsed { get; private set; }

        /// <summary>実行中の不変 Snapshot。</summary>
        public EnemyAttackSnapshot Snapshot => _snapshot;

        /// <summary>Hitbox／Projectile 生成の有効窓（Active 中）か。</summary>
        public bool IsHitboxActive => Current == Phase.Active;

        /// <summary>Prepare 中で追尾（方向更新）が有効か。追尾停止時刻を過ぎると固定する。</summary>
        public bool IsTrackingActive => Current == Phase.Prepare && _hasSnapshot && Elapsed < _snapshot.TrackingStopSeconds;

        /// <summary>攻撃を開始する（開始時に Snapshot を確定）。</summary>
        public void Begin(in EnemyAttackSnapshot snapshot)
        {
            _snapshot = snapshot;
            _hasSnapshot = true;
            Current = Phase.Prepare;
            Elapsed = 0f;
        }

        /// <summary>時間を進める。段階遷移を返す（呼び出し側が状態・予兆・Hitbox・Cooldown に反映）。</summary>
        public TickResult Tick(float deltaTime)
        {
            if (Current == Phase.None || !_hasSnapshot)
            {
                return new TickResult(false, false, false);
            }

            Elapsed += deltaTime;
            float toActive = _snapshot.PrepareSeconds;
            float toRecovery = _snapshot.PrepareSeconds + _snapshot.ActiveSeconds;
            float toEnd = toRecovery + _snapshot.RecoverySeconds;

            bool enteredActive = false;
            bool enteredRecovery = false;
            bool finished = false;

            if (Current == Phase.Prepare && Elapsed >= toActive)
            {
                Current = Phase.Active;
                enteredActive = true;
            }

            if (Current == Phase.Active && Elapsed >= toRecovery)
            {
                Current = Phase.Recovery;
                enteredRecovery = true;
            }

            if (Current == Phase.Recovery && Elapsed >= toEnd)
            {
                Current = Phase.None;
                finished = true;
            }

            return new TickResult(enteredActive, enteredRecovery, finished);
        }

        /// <summary>中断（Stagger／Stunned／Down／Disable）。段階を即時 None にする（Cleanup 経路）。</summary>
        public void Cancel()
        {
            Current = Phase.None;
            Elapsed = 0f;
        }
    }
}
