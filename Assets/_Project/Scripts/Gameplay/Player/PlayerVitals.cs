using System;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Vitals;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の Runtime Vitals（HP・スタミナ）（Phase1 P1-10）。PlayerData の最大値から生成し、
    /// ScriptableObject 原本は変更しない。値は本オブジェクト（Runtime State）でのみ保持する。
    /// </summary>
    public sealed class PlayerVitals
    {
        /// <summary>HP。</summary>
        public Vital Health { get; }

        /// <summary>スタミナ。</summary>
        public Vital Stamina { get; }

        public PlayerVitals(int maxHp, int maxStamina)
        {
            Health = new Vital(maxHp);
            Stamina = new Vital(maxStamina);
        }

        /// <summary>
        /// PlayerData の最大値から Runtime Vitals を生成する。data は読み取りのみで変更しない。
        /// </summary>
        public static PlayerVitals FromData(PlayerData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            return new PlayerVitals(data.MaxHp, data.MaxStamina);
        }
    }
}
