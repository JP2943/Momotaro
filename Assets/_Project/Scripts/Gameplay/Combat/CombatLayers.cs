using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 戦闘の物理レイヤー方針（Phase2 P2-09。仕様書 §3.4 / §10）。主人公は "Player"、敵は "Enemy"、壁・地形は "Default" に置き、
    /// Player↔Enemy の物理衝突のみ無効化する（敵はすり抜け可能）。Player↔Default（壁）と Enemy↔Default（壁）は衝突を維持し、
    /// 主人公も敵も壁では停止する。攻撃判定は OverlapBox（衝突マトリクス非依存）で敵を検出するため本設定の影響を受けない。
    /// レイヤーはルートだけでなく、実際に Collider を持つ子階層にも適用する。
    /// </summary>
    public static class CombatLayers
    {
        /// <summary>主人公レイヤー名。</summary>
        public const string PlayerLayerName = "Player";

        /// <summary>敵レイヤー名。</summary>
        public const string EnemyLayerName = "Enemy";

        /// <summary>壁・地形が属するレイヤー名。</summary>
        public const string WallLayerName = "Default";

        /// <summary>"Player" レイヤーの番号（未定義なら -1）。</summary>
        public static int PlayerLayer => LayerMask.NameToLayer(PlayerLayerName);

        /// <summary>"Enemy" レイヤーの番号（未定義なら -1）。</summary>
        public static int EnemyLayer => LayerMask.NameToLayer(EnemyLayerName);

        /// <summary>壁・地形レイヤーの番号（既定 Default=0）。</summary>
        public static int WallLayer => LayerMask.NameToLayer(WallLayerName);

        /// <summary>
        /// Player↔Enemy の物理衝突のみ無効化する（敵すり抜け）。Player↔Default・Enemy↔Default は既定（衝突）のまま維持する。
        /// 両レイヤーが定義されているときだけ適用する。
        /// </summary>
        public static void EnsureCollisionPolicy()
        {
            int p = PlayerLayer;
            int e = EnemyLayer;
            if (p >= 0 && e >= 0)
            {
                Physics.IgnoreLayerCollision(p, e, true);
            }
        }

        /// <summary>主人公のルートと配下 Collider を Player レイヤーへ置き、衝突方針を適用する。</summary>
        public static void ConfigurePlayer(GameObject playerRoot)
        {
            SetLayerOnColliders(playerRoot, PlayerLayer);
            EnsureCollisionPolicy();
        }

        /// <summary>敵のルートと配下 Collider を Enemy レイヤーへ置き、衝突方針を適用する。</summary>
        public static void ConfigureEnemy(GameObject enemyRoot)
        {
            SetLayerOnColliders(enemyRoot, EnemyLayer);
            EnsureCollisionPolicy();
        }

        /// <summary>
        /// ルートと、配下で Collider を持つ全 GameObject を指定レイヤーへ設定する（物理 Collider を持つ子階層にも適用）。
        /// Collider を持たない子（Visual/Sprite 等）は変更しない（カメラ Culling へ影響させない）。layer が未定義なら何もしない。
        /// </summary>
        public static void SetLayerOnColliders(GameObject root, int layer)
        {
            if (root == null || layer < 0)
            {
                return;
            }

            root.layer = layer;
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].gameObject.layer = layer;
            }
        }
    }
}
