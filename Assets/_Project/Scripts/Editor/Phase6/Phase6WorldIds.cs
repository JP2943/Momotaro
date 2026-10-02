using Momotaro.Core.Identification;
using Momotaro.Editor.Phase5;
using UnityEngine;

namespace Momotaro.Editor.Phase6
{
    /// <summary>
    /// P6A 専用の検証 campaign の ID とパス（P6 仕様 §11）。<b>本編や既存 P5.5 とは分ける</b>——
    /// 既存試遊の報酬や数値は書き換えない（仕様 §2）。
    /// </summary>
    public static class Phase6WorldIds
    {
        public const string SceneFolder = "Assets/_Project/Scenes/Tests/Phase6A";
        public const string DataFolder = "Assets/_Project/Data/Tests/Phase6A";

        public const string AreaAScenePath = SceneFolder + "/SCN_Phase6A_AreaA.unity";
        public const string AreaBScenePath = SceneFolder + "/SCN_Phase6A_AreaB.unity";
        public const string AreaCScenePath = SceneFolder + "/SCN_Phase6A_AreaC.unity";
        public const string TitleScenePath = SceneFolder + "/SCN_Phase6A_Title.unity";

        public const string AreaADataPath = DataFolder + "/SO_Area_P6A_A.asset";
        public const string AreaBDataPath = DataFolder + "/SO_Area_P6A_B.asset";
        public const string AreaCDataPath = DataFolder + "/SO_Area_P6A_C.asset";
        public const string CatalogDataPath = DataFolder + "/SO_AreaCatalog_P6A.asset";
        public const string ConnectionDataPath = DataFolder + "/SO_AreaConnections_P6A.asset";
        public const string GrowthVitalityPath = DataFolder + "/SO_Growth_P6A_Vitality.asset";
        public const string EncounterBNorthPath = DataFolder + "/SO_Encounter_P6A_B_North.asset";
        public const string EncounterBSouthPath = DataFolder + "/SO_Encounter_P6A_B_South.asset";
        public const string EncounterCBossPath = DataFolder + "/SO_Encounter_P6A_C_Boss.asset";
        public const string EnemyElitePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        public static string RewardPath(string name) => DataFolder + "/SO_Reward_P6A_" + name + ".asset";

        // ---- campaign ----
        public static readonly StableId Campaign = new StableId("campaign_p6a");
        public static readonly StableId Connections = new StableId("area_connections_p6a");

        // ---- Area ----
        public static readonly StableId AreaA = new StableId("area_p6_a");
        public static readonly StableId AreaB = new StableId("area_p6_b");
        public static readonly StableId AreaC = new StableId("area_p6_c");

        // ---- 入口（A・B の既定入口は P5 と同じ ID を使う：Builder の生成手順が同じ） ----
        public static readonly StableId EntryAShrine = new StableId("entry_p6_a_shrine");
        public static readonly StableId EntryBFromC = new StableId("entry_p6_b_from_c");
        public static readonly StableId EntryCFromB = new StableId("entry_p6_c_from_b");
        public static readonly StableId EntryCShrine = new StableId("entry_p6_c_shrine");

        // ---- 出入口と接続 ----
        public static readonly StableId ExitAEast = new StableId("exit_p6_a_east");
        public static readonly StableId ExitBWest = new StableId("exit_p6_b_west");
        public static readonly StableId ExitBEast = new StableId("exit_p6_b_east");
        public static readonly StableId ExitCWest = new StableId("exit_p6_c_west");
        public static readonly StableId ConnectionAToB = new StableId("conn_p6_a_to_b");
        public static readonly StableId ConnectionBToA = new StableId("conn_p6_b_to_a");
        public static readonly StableId ConnectionBToC = new StableId("conn_p6_b_to_c");
        public static readonly StableId ConnectionCToB = new StableId("conn_p6_c_to_b");

        // ---- お地蔵様 ----
        public static readonly StableId ShrineA = new StableId("shrine_p6_a");
        public static readonly StableId ShrineC = new StableId("shrine_p6_c");

        // ---- 遭遇戦・普通敵・配置物・仕掛け・調査 ----
        public static readonly StableId EncounterBNorth = new StableId("encounter_p6_b_north");
        public static readonly StableId EncounterBSouth = new StableId("encounter_p6_b_south");
        public static readonly StableId EncounterCBoss = new StableId("encounter_p6_c_boss");
        public static readonly StableId FieldA1 = new StableId("field_p6_a_01");
        public static readonly StableId FieldA2 = new StableId("field_p6_a_02");
        public static readonly StableId PickupTonic = new StableId("pickup_p6_b_tonic");
        public static readonly StableId FindScroll = new StableId("find_p6_b_scroll");
        public static readonly StableId FlagBCache = new StableId("flag_p6_b_cache");
        public static readonly StableId PointCOpen = new StableId("point_p6_c_01");
        public static readonly StableId DiscoveryCOpen = new StableId("discovery_p6_c_01");
        public static readonly StableId RegionCDefault = new StableId("region_p6_c_default");

        // ---- 所持品・成長 ----
        public static readonly StableId ItemTonic = new StableId("item_p6_tonic");
        public const int TonicMaxStack = 3;
        public static readonly StableId GrowthVitality = new StableId("growth_p6_vitality");

        // ---- 敵 ----
        public static readonly StableId EnemyElite = new StableId("enemy_elite_prototype");
    }

    /// <summary>
    /// P6A の検証用の数値（P6 仕様 §11 の「仮の報酬例」。完成版経済ではない）。
    /// </summary>
    public static class Phase6TrialValues
    {
        public const int ArrivalA = 0;
        public const int ArrivalB = 10;
        public const int ArrivalC = 10;
        public const int Kill = 1;
        public const int EncounterClear = 30;
        public const int Discovery = 15;
        public const int GrowthCost = 20;
        public const int GrowthMaxHp = 10;
        public const int KibidangoCapacity = 3;
    }

    /// <summary>
    /// P6A の 3 エリアの配置（東西に A–B–C）。Area ローカル座標。
    /// A・B の地形は P5 の Builder（P5.5 の東西配置と同じ寸法）、C は同じ部品で組む。
    /// </summary>
    public static class Phase6WorldLayout
    {
        public const float SeamZ = 0f;
        public const float PassageWidth = Phase5Layout.CorridorWidth;
        public const float AreaCWidth = Phase5Layout.AreaAWidth;
        public const float AreaCDepth = Phase5Layout.AreaADepth;

        public static readonly Vector3 AreaAOrigin = Vector3.zero;
        public static readonly Vector3 AreaBOrigin =
            new Vector3(Phase5Layout.AreaAWidth * 0.5f + Phase5Layout.AreaBWidth * 0.5f, 0f, 0f);
        public static readonly Vector3 AreaCOrigin =
            AreaBOrigin + new Vector3(Phase5Layout.AreaBWidth * 0.5f + AreaCWidth * 0.5f, 0f, 0f);

        // ---- A ----
        public static readonly Vector3 ShrineA = new Vector3(-5f, 0f, -7.6f);
        public static readonly Vector3 EntryAShrine = new Vector3(-5f, 0f, -6f);
        public static readonly Vector3[] EntryAShrineAlternates = { new Vector3(-3.8f, 0f, -6f), new Vector3(-6.2f, 0f, -6f) };
        public static readonly Vector3 FieldA1 = new Vector3(1f, 0f, 6f);
        public static readonly Vector3 FieldA2 = new Vector3(3.5f, 0f, 7.5f);

        // ---- B ----
        public static readonly Vector3 ExitBToC = new Vector3(13.4f, 0f, SeamZ);
        public static readonly Vector3 EntryBFromC = new Vector3(11.5f, 0f, SeamZ);
        public static readonly Vector3[] EntryBFromCAlternates = { new Vector3(11.5f, 0f, -1.5f), new Vector3(9.5f, 0f, SeamZ) };
        public static readonly Vector3 BNorthArenaCenter = new Vector3(3f, 0f, 6f);
        public static readonly Vector3 BSouthArenaCenter = new Vector3(3f, 0f, -6f);
        public static readonly Vector2 BArenaSize = new Vector2(10f, 8f);
        public static readonly Vector3 BNorthTrigger = new Vector3(1f, 0f, 6f);
        public static readonly Vector3 BSouthTrigger = new Vector3(1f, 0f, -6f);
        public static readonly Vector3[] BNorthSpawns = { new Vector3(5.5f, 0f, 8.2f), new Vector3(6.5f, 0f, 4.2f) };
        public static readonly Vector3[] BSouthSpawns = { new Vector3(5.5f, 0f, -8.2f), new Vector3(6.5f, 0f, -4.2f) };
        public static readonly Vector3 PickupTonic = new Vector3(-8f, 0f, -8f);
        public static readonly Vector3 CacheWall = new Vector3(10f, 0f, 9f);
        public static readonly Vector3 CacheWallSize = new Vector3(0.5f, Phase5Layout.WallHeight, 4f);
        public static readonly Vector3 CacheGate = new Vector3(12f, 0f, 7f);
        public static readonly Vector3 CacheGateSize = new Vector3(4f, Phase5Layout.WallHeight, 0.6f);
        public static readonly Vector3 FindScroll = new Vector3(12f, 0f, 9.4f);

        // ---- C ----
        public static readonly Vector3 EntryCFromB = new Vector3(-9.5f, 0f, SeamZ);
        public static readonly Vector3[] EntryCFromBAlternates = { new Vector3(-9.5f, 0f, -1.5f), new Vector3(-7.5f, 0f, SeamZ) };
        public static readonly Vector3 ExitCToB = new Vector3(-11.4f, 0f, SeamZ);
        public static readonly Vector3 ShrineC = new Vector3(-6f, 0f, -7.6f);
        public static readonly Vector3 EntryCShrine = new Vector3(-6f, 0f, -6f);
        public static readonly Vector3[] EntryCShrineAlternates = { new Vector3(-4.8f, 0f, -6f), new Vector3(-7.2f, 0f, -6f) };
        public static readonly Vector3 CBossArenaCenter = new Vector3(5f, 0f, 0f);
        public static readonly Vector2 CBossArenaSize = new Vector2(12f, 12f);
        public static readonly Vector3 CBossTrigger = new Vector3(4f, 0f, 0f);
        public static readonly Vector3[] CBossSpawns = { new Vector3(9f, 0f, 3f) };
        public static readonly Vector3 PointC = new Vector3(-6f, 0f, 6f);
    }
}
