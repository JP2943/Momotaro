using Momotaro.Gameplay.Companion;

namespace Momotaro.Gameplay.Transfer
{
    /// <summary>ひるみ（§4.5）。蓄積と各残り時間を持ち越す。耐性・持続秒は Data から構築済みのものを使う。</summary>
    public readonly struct FlinchTransferSnapshot
    {
        public float Accumulation { get; }
        public float HoldRemaining { get; }
        public float FlinchRemaining { get; }
        public float ImmunityRemaining { get; }

        public FlinchTransferSnapshot(float accumulation, float holdRemaining, float flinchRemaining, float immunityRemaining)
        {
            Accumulation = accumulation;
            HoldRemaining = holdRemaining;
            FlinchRemaining = flinchRemaining;
            ImmunityRemaining = immunityRemaining;
        }
    }

    /// <summary>
    /// 仲間の生存値（§4.5）。HP・Down・復帰残り・被弾後無敵残り・ひるみ。
    /// <b>Revive／Reset による時間や HP の再初期化をしない</b>ため、Import は値をそのまま置く。
    /// </summary>
    public readonly struct CompanionVitalsTransferSnapshot
    {
        public int Hp { get; }
        public bool IsDown { get; }
        public float RecoveryRemaining { get; }
        public float PostHitInvincibleRemaining { get; }
        public FlinchTransferSnapshot Flinch { get; }

        public CompanionVitalsTransferSnapshot(
            int hp, bool isDown, float recoveryRemaining, float postHitInvincibleRemaining, FlinchTransferSnapshot flinch)
        {
            Hp = hp;
            IsDown = isDown;
            RecoveryRemaining = recoveryRemaining;
            PostHitInvincibleRemaining = postHitInvincibleRemaining;
            Flinch = flinch;
        }
    }

    /// <summary>構え能力（§4.5）。<b>Release 後</b>に採取した CD 残り。構え中フラグ・保持経過は持ち越さない。</summary>
    public readonly struct GuardAbilityTransferSnapshot
    {
        public float CooldownRemaining { get; }

        public GuardAbilityTransferSnapshot(float cooldownRemaining)
        {
            CooldownRemaining = cooldownRemaining;
        }
    }

    /// <summary>
    /// 回避能力（§4.5）。<b>中断後</b>に採取した CD 残り。
    /// 回避動作と<b>回避由来の無敵は持ち越さない</b>（回避終了後に無敵だけ復活させないため）。
    /// </summary>
    public readonly struct EvadeAbilityTransferSnapshot
    {
        public float CooldownRemaining { get; }

        public EvadeAbilityTransferSnapshot(float cooldownRemaining)
        {
            CooldownRemaining = cooldownRemaining;
        }
    }

    /// <summary>防御の合成 Snapshot。同じ CD を Controller と能力の両方へ複製しない（§4.5）。</summary>
    public readonly struct CompanionDefenseTransferSnapshot
    {
        public GuardAbilityTransferSnapshot Guard { get; }
        public EvadeAbilityTransferSnapshot Evade { get; }

        public CompanionDefenseTransferSnapshot(GuardAbilityTransferSnapshot guard, EvadeAbilityTransferSnapshot evade)
        {
            Guard = guard;
            Evade = evade;
        }
    }

    /// <summary>
    /// 仲間の通常攻撃（§4.5）。<b>中断で生じた CD を含む</b>ので、
    /// <c>CancelAttack()</c> の完了後に採取する（§4.4 の実在の落とし穴）。
    /// 攻撃 Plan・Startup／Active／Recovery 進捗・標的・Hitbox は持ち越さない。
    /// </summary>
    public readonly struct CompanionCombatTransferSnapshot
    {
        public float CooldownRemaining { get; }

        public CompanionCombatTransferSnapshot(float cooldownRemaining)
        {
            CooldownRemaining = cooldownRemaining;
        }
    }

    /// <summary>守護（§4.5）。CD 残りのみ。転送中フラグ・実行券・HitId 再入情報は持ち越さない。</summary>
    public readonly struct CompanionGuardianTransferSnapshot
    {
        public float CooldownRemaining { get; }

        public CompanionGuardianTransferSnapshot(float cooldownRemaining)
        {
            CooldownRemaining = cooldownRemaining;
        }
    }

    /// <summary>
    /// 仲間の配置状態（§4.6）。値の Import だけでは復元完了にしないため、
    /// 状態そのものを別に持ち越して <c>CompanionStateArbiter</c> の復元 API で反映する。
    /// </summary>
    public readonly struct CompanionActorTransferSnapshot
    {
        public CompanionState State { get; }

        public CompanionActorTransferSnapshot(CompanionState state)
        {
            State = state;
        }
    }
}
