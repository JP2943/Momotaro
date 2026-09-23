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
    }
}
