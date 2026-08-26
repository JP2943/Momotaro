namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// ジャストガード成立時に、近接攻撃者へ短時間の強制ひるみを付与するための契約（Phase3.5 P3.5-08A。仕様書 §7.5）。
    /// 実装（<see cref="Momotaro.Gameplay.Enemy.EnemyActor"/>）は、指定秒数のひるみ状態へ入り、進行中の攻撃（Hitbox／Active／
    /// Recovery／Telegraph／移動／次攻撃要求）を中断して Attack Slot を解放する。Down／Stunned／Defeated は上書きしない。
    ///
    /// HP ダメージや Flinch 蓄積の水増しでは代用しない（既存の JG 体幹反射は別途維持する）。飛び道具の JG では射手本人を
    /// ひるませないため、呼び出し側が近接命中のみ本契約を用いる。将来の Boss は無効化または専用の短縮値を実装で扱う。
    /// </summary>
    public interface IForcedFlinchReceiver
    {
        /// <summary>
        /// <paramref name="seconds"/> 秒の強制ひるみを付与する。0 以下は無処理。既に Down／Stunned／Defeated 等のより高優先な
        /// 状態にある場合は上書きしない（実装が優先度で判断する）。
        /// </summary>
        void ForceFlinch(float seconds);
    }
}
