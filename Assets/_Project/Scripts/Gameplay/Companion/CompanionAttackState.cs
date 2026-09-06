namespace Momotaro.Gameplay.Companion
{
    /// <summary>仲間の攻撃進行（P4-03）。予兆・判定・後隙の 3 段に分ける（敵・主人公と同じ語彙）。</summary>
    public enum CompanionAttackPhase
    {
        /// <summary>攻撃していない。</summary>
        Idle = 0,

        /// <summary>予兆（振りかぶり）。判定は出ていない。</summary>
        Startup = 1,

        /// <summary>判定中。この間だけ Hitbox を出す。</summary>
        Active = 2,

        /// <summary>後隙。判定は消えている。</summary>
        Recovery = 3,
    }

    /// <summary>
    /// 仲間の攻撃タイミング（P4-03）。純粋 C# で、時間は <see cref="Tick"/> に外部注入する（EditMode で決定的に検証できる）。
    /// 判定の実行（OverlapBox）も命中の生成も行わず、「いま何段目か」「判定を出してよいか」だけを答える。
    ///
    /// 段は「その段の残り時間を引く」のではなく<b>攻撃開始からの累積経過</b>から求める。そのため 1 Tick が段の境界を
    /// またいでも余りを失わず、フレーム落ち時に段が 1 つずれることがない（攻撃全体より長い 1 Tick はそのまま完了する）。
    /// </summary>
    public sealed class CompanionAttackState
    {
        private float _startup;
        private float _active;
        private float _recovery;

        /// <summary>現在の段。</summary>
        public CompanionAttackPhase Phase { get; private set; } = CompanionAttackPhase.Idle;

        /// <summary>攻撃開始からの経過秒。</summary>
        public float Elapsed { get; private set; }

        /// <summary>攻撃中か（予兆・判定・後隙のいずれか）。</summary>
        public bool IsAttacking => Phase != CompanionAttackPhase.Idle;

        /// <summary>いま判定を出してよいか（Active 中のみ）。</summary>
        public bool IsHitboxActive => Phase == CompanionAttackPhase.Active;

        /// <summary>直近の <see cref="Tick"/> で攻撃が完了したか（1 Tick だけ true。クールダウン開始の合図）。</summary>
        public bool Finished { get; private set; }

        /// <summary>攻撃全体の長さ（秒）。</summary>
        public float TotalSeconds => _startup + _active + _recovery;

        /// <summary>
        /// 攻撃を開始する。既に攻撃中なら開始しない（多重発動しない）。負の秒数は 0 に丸める。
        /// 開始直後の段は経過 0 から求めるため、予兆 0 の攻撃はその場で判定段に入る。
        /// </summary>
        public bool Begin(float startupSeconds, float activeSeconds, float recoverySeconds)
        {
            if (IsAttacking)
            {
                return false;
            }

            _startup = startupSeconds < 0f ? 0f : startupSeconds;
            _active = activeSeconds < 0f ? 0f : activeSeconds;
            _recovery = recoverySeconds < 0f ? 0f : recoverySeconds;
            Elapsed = 0f;
            Finished = false;
            Phase = ResolvePhase(0f);
            return IsAttacking;
        }

        /// <summary>1 Tick 進める。負の deltaTime は 0 として扱う。</summary>
        public void Tick(float deltaTime)
        {
            Finished = false;
            if (!IsAttacking)
            {
                return;
            }

            Elapsed += deltaTime < 0f ? 0f : deltaTime;
            CompanionAttackPhase next = ResolvePhase(Elapsed);
            if (next == CompanionAttackPhase.Idle)
            {
                Phase = CompanionAttackPhase.Idle;
                Finished = true;
                return;
            }

            Phase = next;
        }

        /// <summary>攻撃を中断する（ひるみ・ダウン・退場・対象消失・無効化）。判定は即座に消える。</summary>
        public void Cancel()
        {
            Phase = CompanionAttackPhase.Idle;
            Elapsed = 0f;
            Finished = false;
        }

        /// <summary>経過秒から段を求める（境界は「その段の終わりと同時に次段へ」）。</summary>
        private CompanionAttackPhase ResolvePhase(float elapsed)
        {
            if (TotalSeconds <= 0f)
            {
                return CompanionAttackPhase.Idle; // 長さゼロの攻撃は成立しない。
            }

            if (elapsed < _startup)
            {
                return CompanionAttackPhase.Startup;
            }

            if (elapsed < _startup + _active)
            {
                return CompanionAttackPhase.Active;
            }

            if (elapsed < TotalSeconds)
            {
                return CompanionAttackPhase.Recovery;
            }

            return CompanionAttackPhase.Idle;
        }
    }
}
