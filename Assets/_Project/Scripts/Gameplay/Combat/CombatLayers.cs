using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 戦闘の物理レイヤー方針（Phase2 P2-09。仕様書 §3.4 / §10）。主人公は "Player"、敵は "Enemy"、仲間は "Ally"、
    /// 壁・地形は "Default" に置く。無効化するのは Player↔Enemy と、仲間まわりの Ally↔Player／Ally↔Enemy／Ally↔Ally。
    /// 壁との衝突（Player／Enemy／Ally ↔ Default）はいずれも維持し、全員が壁で停止する。
    ///
    /// 仲間をすり抜けにするのは、追従する仲間が主人公を押して操作感を壊さないため、敵の押し合いを増やさないため、
    /// そして 3 体（犬・猿・雉）が団子にならないため（P4-02）。壁で止まることは維持するので、経路失敗を検出して
    /// ワープで復帰する仕組み（<c>CompanionFollowModel</c>）が意味を持つ。
    /// 攻撃判定は OverlapBox（衝突マトリクス非依存）で対象を検出するため本設定の影響を受けない。
    /// レイヤーはルートだけでなく、実際に Collider を持つ子階層にも適用する。
    /// </summary>
    public static class CombatLayers
    {
        /// <summary>主人公レイヤー名。</summary>
        public const string PlayerLayerName = "Player";

        /// <summary>敵レイヤー名。</summary>
        public const string EnemyLayerName = "Enemy";

        /// <summary>仲間（犬・猿・雉）レイヤー名。</summary>
        public const string AllyLayerName = "Ally";

        /// <summary>壁・地形が属するレイヤー名。</summary>
        public const string WallLayerName = "Default";

        /// <summary>"Player" レイヤーの番号（未定義なら -1）。</summary>
        public static int PlayerLayer => LayerMask.NameToLayer(PlayerLayerName);

        /// <summary>"Enemy" レイヤーの番号（未定義なら -1）。</summary>
        public static int EnemyLayer => LayerMask.NameToLayer(EnemyLayerName);

        /// <summary>"Ally" レイヤーの番号（未定義なら -1）。</summary>
        public static int AllyLayer => LayerMask.NameToLayer(AllyLayerName);

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

            // 仲間（P4-02）：主人公・敵・仲間同士とはすり抜け、壁（Default）とだけ衝突する。
            int a = AllyLayer;
            if (a < 0)
            {
                return; // "Ally" 未定義の環境では何もしない（既存挙動を変えない）。
            }

            Physics.IgnoreLayerCollision(a, a, true);
            if (p >= 0)
            {
                Physics.IgnoreLayerCollision(a, p, true);
            }

            if (e >= 0)
            {
                Physics.IgnoreLayerCollision(a, e, true);
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

        /// <summary>仲間のルートと配下 Collider を Ally レイヤーへ置き、衝突方針を適用する（P4-02）。</summary>
        public static void ConfigureAlly(GameObject allyRoot)
        {
            SetLayerOnColliders(allyRoot, AllyLayer);
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
