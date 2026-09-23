using Momotaro.Core.Identification;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// P5 の安定 ID と出力先（P5-02。仕様書 v1.1 §3.1／§13.1）。
    ///
    /// Scene 名・表示名・Unity InstanceID を永続的な識別子にしない。
    /// StableId は既存の小文字 snake_case・64 文字以内の規約を継承する（§3.1）。
    /// </summary>
    public static class Phase5AreaIds
    {
        /// <summary>Scene の出力先（§13.1）。</summary>
        public const string SceneFolder = "Assets/_Project/Scenes/Tests/Phase5";

        /// <summary>Data の出力先（§13.1）。GameDataAsset 派生のメイン Asset として作る（§13.4）。</summary>
        public const string DataFolder = "Assets/_Project/Data/Tests/Phase5";

        /// <summary>統合起動 Scene。初期化後 A へ移動する（§3.1）。</summary>
        public const string TrialScenePath = SceneFolder + "/SCN_Phase5_ExplorationTrial.unity";

        /// <summary>エリア A の Scene。</summary>
        public const string AreaAScenePath = SceneFolder + "/SCN_Phase5_AreaA.unity";

        /// <summary>エリア B の Scene。</summary>
        public const string AreaBScenePath = SceneFolder + "/SCN_Phase5_AreaB.unity";

        /// <summary>エリア A の定義 Asset。</summary>
        public const string AreaADataPath = DataFolder + "/SO_Area_P5_A.asset";

        /// <summary>エリア B の定義 Asset。</summary>
        public const string AreaBDataPath = DataFolder + "/SO_Area_P5_B.asset";

        /// <summary>カタログ Asset。</summary>
        public const string CatalogDataPath = DataFolder + "/SO_AreaCatalog_P5.asset";

        /// <summary>エリア A の安定 ID。</summary>
        public static readonly StableId AreaA = new StableId("area_p5_a");

        /// <summary>エリア B の安定 ID。</summary>
        public static readonly StableId AreaB = new StableId("area_p5_b");

        /// <summary>A の開始点／死亡再開点（§3.1。P5 では固定）。</summary>
        public static readonly StableId AreaAStart = new StableId("area_p5_a_start");

        /// <summary>A の B から戻る入口。</summary>
        public static readonly StableId AreaAFromB = new StableId("area_p5_a_from_b");

        /// <summary>B の A から来る入口。B 直開きの既定入口でもある（§3.1）。</summary>
        public static readonly StableId AreaBFromA = new StableId("area_p5_b_from_a");

        /// <summary>カタログ Asset の安定 ID。</summary>
        public static readonly StableId Catalog = new StableId("area_catalog_p5");

        // ---- P5-04：仕掛け（§7）----

        /// <summary>A の門を開ける Flag（レバーと門が共有する正本。§7.3）。</summary>
        public static readonly StableId FlagAGate = new StableId("flag_p5_a_gate");

        /// <summary>A の調査地点（通行できる側）。</summary>
        public static readonly StableId PointAOpen = new StableId("point_p5_a_01");

        /// <summary>A の調査地点（衝立の向こう側。壁越しは拒否される。§7.2）。</summary>
        public static readonly StableId PointABlocked = new StableId("point_p5_a_blocked");

        /// <summary>A の調査で見つかるもの。</summary>
        public static readonly StableId DiscoveryAOpen = new StableId("discovery_p5_a_01");

        /// <summary>A の壁越し地点で見つかるもの。</summary>
        public static readonly StableId DiscoveryABlocked = new StableId("discovery_p5_a_blocked");

        /// <summary>B から A へ戻る扉（Interact 1 回で要求する。§6.1）。</summary>
        public static readonly StableId DoorBToA = new StableId("door_p5_b_to_a");

        // ---- P5-06：カメラ領域（§11）----

        /// <summary>A の既定領域（どの部屋にも入らないときに使う。§11）。</summary>
        public static readonly StableId RegionADefault = new StableId("region_p5_a_default");

        /// <summary>A の西の大部屋。</summary>
        public static readonly StableId RegionAWest = new StableId("region_p5_a_west");

        /// <summary>A の東の通路。横が入りきらないので中央固定になる（§11）。</summary>
        public static readonly StableId RegionAEast = new StableId("region_p5_a_east");

        /// <summary>B の既定領域。</summary>
        public static readonly StableId RegionBDefault = new StableId("region_p5_b_default");

        /// <summary>調査の設定 Asset（P4 の試遊と同じものを使う）。</summary>
        public const string InvestigationSettingsPath = "Assets/_Project/Data/Exploration/SO_Investigation_Trial.asset";
    }
}
