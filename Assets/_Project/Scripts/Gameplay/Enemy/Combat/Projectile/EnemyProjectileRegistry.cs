using System.Collections.Generic;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// 生存中の敵 Projectile の狭いレジストリ（Phase3.5 P3.5-02）。<see cref="Momotaro.Gameplay.Enemy.Perception.PerceptionTargetRegistry"/>
    /// と同じ最小パターンで、飛翔中の <see cref="EnemyProjectile"/> を自己登録・自己解除し、プレイヤー死亡時に一括 Cleanup できるようにする。
    ///
    /// 「万能 Manager」ではなく Projectile ライフサイクルの列挙だけを担う。毎フレームの全 Scene 検索（FindObjectsByType）を避けるための
    /// 器であり、AI・命中・表示の判断は持たない。二重登録・二重解除・空集合の Cleanup は安全。テストは <see cref="Clear"/> で初期化できる。
    /// </summary>
    public static class EnemyProjectileRegistry
    {
        private static readonly List<EnemyProjectile> _live = new List<EnemyProjectile>();

        /// <summary>生存登録数（テスト／Debug 用）。</summary>
        public static int LiveCount => _live.Count;

        /// <summary>飛翔開始した Projectile を登録する（null・重複は無視）。</summary>
        public static void Register(EnemyProjectile projectile)
        {
            if (projectile != null && !_live.Contains(projectile))
            {
                _live.Add(projectile);
            }
        }

        /// <summary>登録を解除する（未登録でも安全）。</summary>
        public static void Unregister(EnemyProjectile projectile) => _live.Remove(projectile);

        /// <summary>全登録を消去する（テスト用。実 Projectile の破棄は行わない）。</summary>
        public static void Clear() => _live.Clear();

        /// <summary>
        /// 生存中の全 Projectile を消滅させる（プレイヤー死亡時の一括掃除。§4.1）。各 <see cref="EnemyProjectile.Cleanup"/> は
        /// 登録解除を伴うため、走査中の集合変更に耐えるようスナップショットを反復する。二重呼び出し・空集合でも安全（冪等）。
        /// </summary>
        public static void DespawnAll()
        {
            if (_live.Count == 0)
            {
                return;
            }

            EnemyProjectile[] snapshot = _live.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                EnemyProjectile p = snapshot[i];
                if (p != null) // UnityEngine.Object の破棄済み（fake-null）も == 演算子で安全に除外する。
                {
                    p.Cleanup();
                }
            }
        }
    }
}
