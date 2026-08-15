using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// 敵攻撃パイプラインが Projectile 攻撃の Active で 1 発生成するための契約（Phase3 P3-08。§9.2）。
    /// <see cref="EnemyAttackController"/> は具象 Launcher を GetComponent で解決し、Active 突入時に本メソッドを 1 回呼ぶ。
    /// </summary>
    public interface IEnemyProjectileLauncher
    {
        /// <summary>
        /// 1 発発射する。<paramref name="origin"/> は発射者の位置、<paramref name="direction"/> は狙い方向（XZ）。生成できたら true。
        /// </summary>
        bool TryLaunch(in EnemyAttackSnapshot snapshot, Vector3 origin, Vector3 direction,
            ICombatActor owner, float attackPower, HitId hitId);
    }
}
