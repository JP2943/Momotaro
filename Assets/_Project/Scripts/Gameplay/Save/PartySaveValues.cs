using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Save
{
    /// <summary>
    /// 主人公の保存値（P6A-02。<c>P6_SaveInventory.md</c> §2）。<b>中断用の投影</b>を掛けたあとの値。
    /// 一時動作（Hurt 硬直・Break・攻撃）は持たない——再開は入口で中立になるため。
    /// </summary>
    public readonly struct PlayerSaveValues
    {
        public PlayerSaveValues(int hp, float stamina, float staminaRegenDelay, float invincibleRemaining)
        {
            Hp = hp;
            Stamina = stamina;
            StaminaRegenDelay = staminaRegenDelay;
            InvincibleRemaining = invincibleRemaining;
        }

        /// <summary>現在 HP（1 以上。HP 0 の通常保存は作らない）。</summary>
        public int Hp { get; }

        /// <summary>現在スタミナ。</summary>
        public float Stamina { get; }

        /// <summary>スタミナ回復待ちの残り秒。</summary>
        public float StaminaRegenDelay { get; }

        /// <summary>被弾後無敵の残り秒。</summary>
        public float InvincibleRemaining { get; }
    }

    /// <summary>犬丸（仲間）の保存値（P6A-02）。ひるみ・攻撃 Plan・構え中・回避中は持たない。</summary>
    public readonly struct CompanionSaveValues
    {
        public CompanionSaveValues(StableId companionId, int hp, bool isDown, float recoveryRemaining,
            float invincibleRemaining, float attackCooldown, float guardCooldown, float evadeCooldown,
            float guardianCooldown)
        {
            CompanionId = companionId;
            Hp = hp;
            IsDown = isDown;
            RecoveryRemaining = recoveryRemaining;
            InvincibleRemaining = invincibleRemaining;
            AttackCooldown = attackCooldown;
            GuardCooldown = guardCooldown;
            EvadeCooldown = evadeCooldown;
            GuardianCooldown = guardianCooldown;
        }

        public StableId CompanionId { get; }
        public int Hp { get; }
        public bool IsDown { get; }
        public float RecoveryRemaining { get; }
        public float InvincibleRemaining { get; }
        public float AttackCooldown { get; }
        public float GuardCooldown { get; }
        public float EvadeCooldown { get; }
        public float GuardianCooldown { get; }
    }

    /// <summary>主人公と仲間の保存値の組（活動 Area から 1 回だけ採取する）。</summary>
    public readonly struct PartySaveValues
    {
        public PartySaveValues(PlayerSaveValues player, bool hasCompanion, CompanionSaveValues companion)
        {
            Player = player;
            HasCompanion = hasCompanion;
            Companion = companion;
        }

        public PlayerSaveValues Player { get; }

        /// <summary>仲間の値を含むか（仲間の居ない構成では false）。</summary>
        public bool HasCompanion { get; }

        public CompanionSaveValues Companion { get; }
    }

    /// <summary>
    /// 活動中の Area の Actor から保存値を<b>非破壊で</b>採る窓口（P6A-02。仕様 §8）。
    /// 遷移用の <c>AreaActorTransferPort.Capture</c>（行動を止めてから採る）とは別物で、攻撃中断・回復・付与・入力消費を起こさない。
    /// </summary>
    public interface ISaveActorSource
    {
        /// <summary>採取できる状態か（Actor がそろい、主人公が生存している）。</summary>
        bool CanExportForSave { get; }

        /// <summary>現在の値を中断用に投影して採る。</summary>
        PartySaveValues ExportForSave();
    }
}
