using Momotaro.Data.Combat;

namespace Momotaro.Gameplay.Enemy.Screen
{
    /// <summary>
    /// 攻撃分類ごとの画面外開始可否（Phase3 §8.2）。純粋関数。画面内なら全分類が開始可。画面外では、
    /// 強／ガード不能は開始不可、近接（通常・突進）は画面内に入るまで開始不可、遠距離（投射）は画面端警告を表示できる場合だけ開始可。
    /// 開始済みの攻撃は画面外へ出ても継続する（本判定は「開始」時のみ評価し、継続には用いない）。
    /// </summary>
    public static class OffscreenAttackPolicy
    {
        /// <summary>
        /// この攻撃を今 開始してよいか。<paramref name="isOnScreen"/> は攻撃者（敵）が画面内か。
        /// <paramref name="requiresOffscreenWarning"/> と <paramref name="offscreenWarningAvailable"/> は遠距離の画面端警告条件（P3-08）。
        /// </summary>
        public static bool CanStart(
            EnemyAttackClass attackClass,
            bool requiresOffscreenWarning,
            bool isOnScreen,
            bool offscreenWarningAvailable)
        {
            if (isOnScreen)
            {
                return true; // 画面内は全分類可。
            }

            switch (attackClass)
            {
                case EnemyAttackClass.Heavy:
                case EnemyAttackClass.Unblockable:
                    return false; // 画面外の強／ガード不能は禁止（§8.2）。

                case EnemyAttackClass.Projectile:
                    // 遠距離は画面端警告を出せる場合だけ開始可。警告不要指定なら画面外でも可（データ裁量）。
                    return requiresOffscreenWarning ? offscreenWarningAvailable : true;

                default:
                    // 近接（通常・突進）は画面内に入ってから Prepare を開始する。
                    return false;
            }
        }
    }
}
