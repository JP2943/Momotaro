using UnityEngine;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// P5 の仮エリアの寸法と座標（P5-02。仕様書 v1.1 §3.2）。
    ///
    /// A 約 24×18、B 約 28×22 を「配置開始時の目安」として置く。<b>地形や戦闘の最終バランス値ではない</b>。
    /// 実寸はカメラ画角・移動速度・Collider 径に合わせて調整してよい（§3.2）。
    ///
    /// 通路幅は最大通行 Actor の直径＋0.4 以上（<see cref="CorridorWidth"/>）。犬丸のためだけに主人公が押し戻されない。
    /// </summary>
    public static class Phase5Layout
    {
        /// <summary>床の厚み（上面 Y=0 にする）。</summary>
        public const float FloorThickness = 0.5f;

        /// <summary>壁の高さ。低い壁＋開放天井で中が見える（§1.3）。</summary>
        public const float WallHeight = 2.0f;

        /// <summary>壁の厚み。</summary>
        public const float WallThickness = 0.5f;

        /// <summary>正常ルートの通路幅（最大通行 Actor の直径＋0.4 以上。§3.2）。</summary>
        public const float CorridorWidth = 4.0f;

        // ---- エリア A ----

        /// <summary>A の床の広がり（X）。</summary>
        public const float AreaAWidth = 24f;

        /// <summary>A の床の広がり（Z）。</summary>
        public const float AreaADepth = 18f;

        /// <summary>A の開始点／死亡再開点。</summary>
        public static readonly Vector3 AreaAStart = new Vector3(-8f, 0f, -6f);

        /// <summary>A の B から戻る入口。</summary>
        public static readonly Vector3 AreaAFromB = new Vector3(9.5f, 0f, 6f);

        /// <summary>A の開始点の代替配置候補（到着位置が塞がっているとき。§6.3）。</summary>
        public static readonly Vector3[] AreaAStartAlternates =
        {
            new Vector3(-6f, 0f, -6f),
            new Vector3(-8f, 0f, -4f),
        };

        /// <summary>A の戻り入口の代替配置候補。</summary>
        public static readonly Vector3[] AreaAFromBAlternates =
        {
            new Vector3(9.5f, 0f, 4f),
            new Vector3(7.5f, 0f, 6f),
        };

        /// <summary>A → B の開放出入口（東端）。</summary>
        public static readonly Vector3 AreaAExitToB = new Vector3(11.4f, 0f, 6f);

        /// <summary>L 字通路を作る仕切り壁の X。西の大部屋と東の通路を分ける。</summary>
        public const float AreaADividerX = 6f;

        /// <summary>仕切り壁の開口（南側）の Z 範囲の北端。ここより北は壁。</summary>
        public const float AreaADividerGapNorthZ = -4f;

        /// <summary>通路を塞ぐ門の位置（レバーで開通する。P5-04 で機能を足す）。</summary>
        public static readonly Vector3 AreaAGate = new Vector3(9f, 0f, 0f);

        /// <summary>レバーの位置。</summary>
        public static readonly Vector3 AreaALever = new Vector3(2f, 0f, -7f);

        /// <summary>正常な調査地点。</summary>
        public static readonly Vector3 AreaAInvestigation = new Vector3(-3f, 0f, 1f);

        /// <summary>壁越しで拒否される調査地点（衝立の向こう側）。</summary>
        public static readonly Vector3 AreaAInvestigationBlocked = new Vector3(-9.5f, 0f, -1f);

        /// <summary>壁越し調査を成立させるための衝立の位置。</summary>
        public static readonly Vector3 AreaABlockingScreen = new Vector3(-7.5f, 0f, -1f);

        /// <summary>水場（通行不可）の中心。</summary>
        public static readonly Vector3 AreaAWater = new Vector3(-8f, 0f, 5.5f);

        /// <summary>水場の広がり（X, Z）。</summary>
        public static readonly Vector2 AreaAWaterSize = new Vector2(7f, 5f);

        // ---- エリア B ----

        /// <summary>B の床の広がり（X）。</summary>
        public const float AreaBWidth = 28f;

        /// <summary>B の床の広がり（Z）。</summary>
        public const float AreaBDepth = 22f;

        /// <summary>B の A から来る入口。</summary>
        public static readonly Vector3 AreaBFromA = new Vector3(-11.5f, 0f, 6f);

        /// <summary>B の入口の代替配置候補。</summary>
        public static readonly Vector3[] AreaBFromAAlternates =
        {
            new Vector3(-11.5f, 0f, 4f),
            new Vector3(-9.5f, 0f, 6f),
        };

        /// <summary>A へ戻る Interact 扉の位置（西端）。</summary>
        public static readonly Vector3 AreaBDoorToA = new Vector3(-13.4f, 0f, 6f);

        /// <summary>B の正常な調査地点。</summary>
        public static readonly Vector3 AreaBInvestigation = new Vector3(-2f, 0f, 8f);

        /// <summary>遭遇 Trigger の中心。アリーナ境界より十分内側へ置く（§8.2）。</summary>
        public static readonly Vector3 AreaBEncounterTrigger = new Vector3(2f, 0f, 0f);

        /// <summary>アリーナ境界の中心。</summary>
        public static readonly Vector3 AreaBArenaCenter = new Vector3(4f, 0f, 0f);

        /// <summary>アリーナ境界の広がり（X, Z）。</summary>
        public static readonly Vector2 AreaBArenaSize = new Vector2(16f, 16f);

        /// <summary>敵の出現点。EnemyIds の順に対応させる（§8.1）。敵数以上を用意する。</summary>
        public static readonly Vector3[] AreaBSpawnPoints =
        {
            new Vector3(8f, 0f, 3f),
            new Vector3(9f, 0f, -3f),
            new Vector3(6f, 0f, 0f),
        };

        /// <summary>入口と重ならない安全な戦闘復帰点（§3.2）。</summary>
        public static readonly Vector3 AreaBCombatReturn = new Vector3(0f, 0f, -8f);
    }
}
