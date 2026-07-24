using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 戦闘の物理レイヤー方針（Phase2 P2-09。仕様書 §3.4 / §10）。敵は "Enemy" レイヤーへ置き、主人公（既定 "Default"）と敵の
    /// 物理衝突を無効化して「敵はすり抜け可能」を実現する。壁・障害物は Default に置いたまま Player↔Default の衝突を維持し、
    /// 「壁はすり抜け不可」を保つ。攻撃判定は OverlapBox（衝突マトリクス非依存）で敵を検出するため本設定の影響を受けない。
    /// </summary>
    public static class CombatLayers
    {
        /// <summary>敵レイヤー名。</summary>
        public const string EnemyLayerName = "Enemy";

        /// <summary>主人公が属するレイヤー名（既定）。壁・障害物も同レイヤーで衝突を維持する。</summary>
        public const string PlayerLayerName = "Default";

        /// <summary>"Enemy" レイヤーの番号（未定義なら -1）。</summary>
        public static int EnemyLayer => LayerMask.NameToLayer(EnemyLayerName);

        /// <summary>主人公レイヤーの番号（既定 Default=0）。</summary>
        public static int PlayerLayer => LayerMask.NameToLayer(PlayerLayerName);

        /// <summary>
        /// 敵オブジェクトを Enemy レイヤーへ置き、Player↔Enemy の物理衝突を無効化する（敵すり抜け）。"Enemy" レイヤーが未定義なら
        /// 何もしない。壁（Default）↔Player の衝突は維持される。攻撃の OverlapBox 検出には影響しない。
        /// </summary>
        public static void ConfigureEnemy(GameObject enemy)
        {
            if (enemy == null)
            {
                return;
            }

            int e = EnemyLayer;
            if (e < 0)
            {
                return;
            }

            enemy.layer = e;

            int p = PlayerLayer;
            if (p >= 0)
            {
                Physics.IgnoreLayerCollision(e, p, true);
            }
        }
    }
}
