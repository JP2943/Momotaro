using Momotaro.Core.Identification;
using Momotaro.Editor.Phase5;
using UnityEngine;

namespace Momotaro.Editor.Phase6
{
    /// <summary>
    /// 検証ワールドの設定（P6B 04。仕様 §10「P6A の Builder と 3 エリア検証構成を設定で再利用」）。
    /// 地形・配置・Area の ID は共通で、<b>フォルダ・Scene 名・Data 名・campaign ID・報酬・成長・試遊値</b>が分かれる。
    /// P6A の生成物（Scene・Data・報酬）は P6B の生成で変わらない。
    /// </summary>
    public sealed class Phase6Profile
    {
        private Phase6Profile(string tag, string sceneFolder, string dataFolder, string campaignId, string connectionsId,
            string title, bool isP6B)
        {
            Tag = tag;
            SceneFolder = sceneFolder;
            DataFolder = dataFolder;
            CampaignId = new StableId(campaignId);
            ConnectionsId = new StableId(connectionsId);
            Title = title;
            IsP6B = isP6B;
        }

        /// <summary>P6A（既存。値は従来と同じ）。</summary>
        public static readonly Phase6Profile P6A = new Phase6Profile("P6A", "Assets/_Project/Scenes/Tests/Phase6A",
            "Assets/_Project/Data/Tests/Phase6A", "campaign_p6a", "area_connections_p6a", "P6A 進行・保存試遊", false);

        /// <summary>P6B（成長・きびだんご試遊。専用 campaign・Data・保存スロット）。</summary>
        public static readonly Phase6Profile P6B = new Phase6Profile("P6B", "Assets/_Project/Scenes/Tests/Phase6B",
            "Assets/_Project/Data/Tests/Phase6B", "campaign_p6b", "area_connections_p6b", "P6B 成長・回復試遊", true);

        /// <summary>名前の札（"P6A"／"P6B"）。Scene・Data のファイル名に入る。</summary>
        public string Tag { get; }

        public string SceneFolder { get; }

        public string DataFolder { get; }

        public StableId CampaignId { get; }

        public StableId ConnectionsId { get; }

        /// <summary>タイトルの見出し。</summary>
        public string Title { get; }

        /// <summary>P6B の拡張（9 ノード・きびだんご使用・払い戻し・専用スロット）を持つか。</summary>
        public bool IsP6B { get; }

        /// <summary>Scene 名の札（"Phase6A"／"Phase6B"）。</summary>
        public string SceneTag => "Phase6" + Tag.Substring(2);
    }

    /// <summary>
    /// P6 の検証 campaign の ID とパス（P6 仕様 §11）。<b>本編や既存 P5.5 とは分ける</b>——
    /// 既存試遊の報酬や数値は書き換えない（仕様 §2）。パスは <see cref="Profile"/>（P6A／P6B）で分かれる。
    /// </summary>
    public static class Phase6WorldIds
    {
        /// <summary>
        /// 生成・検査の対象（既定 P6A）。<b>Builder／Validator が try/finally で一時的にだけ切り替える</b>——
        /// 切り替えたまま戻さないと、以後の P6A の操作が P6B の Data を見に行く。
        /// </summary>
        public static Phase6Profile Profile { get; set; } = Phase6Profile.P6A;

        public static string SceneFolder => Profile.SceneFolder;
        public static string DataFolder => Profile.DataFolder;

        public static string AreaAScenePath => SceneFolder + "/SCN_" + Profile.SceneTag + "_AreaA.unity";
        public static string AreaBScenePath => SceneFolder + "/SCN_" + Profile.SceneTag + "_AreaB.unity";
        public static string AreaCScenePath => SceneFolder + "/SCN_" + Profile.SceneTag + "_AreaC.unity";
        public static string TitleScenePath => SceneFolder + "/SCN_" + Profile.SceneTag + "_Title.unity";

        public static string AreaADataPath => DataFolder + "/SO_Area_" + Profile.Tag + "_A.asset";
        public static string AreaBDataPath => DataFolder + "/SO_Area_" + Profile.Tag + "_B.asset";
        public static string AreaCDataPath => DataFolder + "/SO_Area_" + Profile.Tag + "_C.asset";
        public static string CatalogDataPath => DataFolder + "/SO_AreaCatalog_" + Profile.Tag + ".asset";
        public static string ConnectionDataPath => DataFolder + "/SO_AreaConnections_" + Profile.Tag + ".asset";
        public static string GrowthVitalityPath => DataFolder + "/SO_Growth_P6A_Vitality.asset";
        public static string EncounterBNorthPath => DataFolder + "/SO_Encounter_" + Profile.Tag + "_B_North.asset";
        public static string EncounterBSouthPath => DataFolder + "/SO_Encounter_" + Profile.Tag + "_B_South.asset";
        public static string EncounterCBossPath => DataFolder + "/SO_Encounter_" + Profile.Tag + "_C_Boss.asset";
        public const string EnemyElitePrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        public static string RewardPath(string name) => DataFolder + "/SO_Reward_" + Profile.Tag + "_" + name + ".asset";

        /// <summary>P6B の成長ノードの Data パス（ID の接頭辞 growth_ を除いた名前で）。</summary>
        public static string GrowthNodePath(string name) => DataFolder + "/SO_Growth_P6B_" + name + ".asset";

        /// <summary>報酬 ID（P6A は従来の reward_p6a_*）。</summary>
        public static string RewardId(string name) => "reward_" + Profile.Tag.ToLowerInvariant() + "_" + name;

        // ---- campaign ----
        public static StableId Campaign => Profile.CampaignId;
        public static StableId Connections => Profile.ConnectionsId;

        /// <summary>
        /// クエスト段階の接続 fixture（受入 P6A 08）。P7 のクエスト状態を保存・死亡・休息へ通す境界を確かめるだけの ID で、
        /// クエストの中身・ランナーは無い。
        /// </summary>
        public static readonly StableId QuestFixture = new StableId("quest_p6a_fixture");

        // ---- Area ----
        // Area と遭遇戦は Data（GameDataAsset）なので、StableId はプロジェクト全体で一意でなければならない
        // （出荷前の Data 検証が重複を拒否する）。P6B は別 ID（area_p6b_*／encounter_p6b_*）にする。
        // 入口・出入口・お地蔵様・配置物は Area の中の ID なので共通のまま。
        public static StableId AreaA => new StableId(Profile.IsP6B ? "area_p6b_a" : "area_p6_a");
        public static StableId AreaB => new StableId(Profile.IsP6B ? "area_p6b_b" : "area_p6_b");
        public static StableId AreaC => new StableId(Profile.IsP6B ? "area_p6b_c" : "area_p6_c");

        // ---- 入口（A・B の既定入口は P5 と同じ ID を使う：Builder の生成手順が同じ） ----
        public static readonly StableId EntryAShrine = new StableId("entry_p6_a_shrine");
        public static readonly StableId EntryBFromC = new StableId("entry_p6_b_from_c");
        public static readonly StableId EntryCFromB = new StableId("entry_p6_c_from_b");
        public static readonly StableId EntryCShrine = new StableId("entry_p6_c_shrine");

        /// <summary>C の 2 つ目のお地蔵様の入口（同じ Area の中の旅立ちの fixture。レビュー 720161d 指摘 1）。</summary>
        public static readonly StableId EntryCShrine2 = new StableId("entry_p6_c_shrine_2");

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

        /// <summary>C の 2 つ目のお地蔵様（同じ Area の中の旅立ちの fixture）。</summary>
        public static readonly StableId ShrineC2 = new StableId("shrine_p6_c_2");

        // ---- 遭遇戦・普通敵・配置物・仕掛け・調査 ----
        public static StableId EncounterBNorth => new StableId(Profile.IsP6B ? "encounter_p6b_b_north" : "encounter_p6_b_north");
        public static StableId EncounterBSouth => new StableId(Profile.IsP6B ? "encounter_p6b_b_south" : "encounter_p6_b_south");
        public static StableId EncounterCBoss => new StableId(Profile.IsP6B ? "encounter_p6b_c_boss" : "encounter_p6_c_boss");
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

        // ---- テスト専用（死亡を何度も試すための措置。オーナー指示 2026-10-02。本編・他の試遊には効かない）----
        /// <summary>敵の攻撃力の倍率。</summary>
        public const float TestEnemyAttackScale = 2f;

        /// <summary>主人公の基礎最大 HP の倍率。</summary>
        public const float TestPlayerMaxHpScale = 0.5f;
    }

    /// <summary>
    /// P6B の試遊値と浅い 9 ノード（P6B 仕様 §3。機能試遊用の仮値で、章別収入・本番経済ではない）。
    /// 数値の正本は生成される Data（SkillNodeData・AreaCatalogData）。ここは Builder が書き込む元の表。
    /// </summary>
    public static class Phase6BTrialValues
    {
        /// <summary>A 初到達の一度きり報酬（New Game 直後に 300 で全取得 270 が可能。仕様 §10）。</summary>
        public const int ArrivalA = 300;

        public const int KibidangoCapacity = 3;

        /// <summary>基礎回復量＝成長なしの基礎最大 HP（SO_Player_Momotaro 100）の半分の切り上げ。固定値（記録 001）。</summary>
        public const int KibidangoBaseHeal = 50;

        public const float KibidangoUseSeconds = 2f;
        public const float KibidangoCommitSeconds = 1.5f;
        public const float KibidangoMoveSpeedMultiplier = 0.2f;

        public const int RefundRightsInitial = 3;
        public const int RefundRightsPerChapter = 3;
        public const int RefundRightsMax = 6;

        /// <summary>章クリアの権利追加の接続 fixture（本番の章ではない。P6B 08）。</summary>
        public static readonly StableId ChapterFixture = new StableId("chapter_p6b_fixture");

        /// <summary>2 つ目の章 fixture（上限で増加 0 の章を確かめる）。</summary>
        public static readonly StableId ChapterFixture2 = new StableId("chapter_p6b_fixture_2");

        public const string SaveSlot = "p6b_slot0";

        /// <summary>1 ノードの定義（ID・名前・効果・前提・費用・列・段）。</summary>
        public readonly struct Node
        {
            public Node(string id, string name, string description, int cost, int column, int tier, string prerequisite,
                int maxHp = 0, float attack = 0f, int stamina = 0, float poise = 0f, int heal = 0, int capacity = 0)
            {
                Id = id;
                Name = name;
                Description = description;
                Cost = cost;
                Column = column;
                Tier = tier;
                Prerequisite = prerequisite;
                MaxHp = maxHp;
                Attack = attack;
                Stamina = stamina;
                Poise = poise;
                Heal = heal;
                Capacity = capacity;
            }

            public string Id { get; }
            public string Name { get; }
            public string Description { get; }
            public int Cost { get; }
            public int Column { get; }
            public int Tier { get; }
            public string Prerequisite { get; }
            public int MaxHp { get; }
            public float Attack { get; }
            public int Stamina { get; }
            public float Poise { get; }
            public int Heal { get; }
            public int Capacity { get; }

            /// <summary>Data のファイル名（growth_ を除く）。</summary>
            public string FileName => Id.Substring("growth_".Length);
        }

        /// <summary>三方向（体力・攻撃・スタミナ）× 3 段。各方向の最初は前提なし、排他なし、各 1 回。</summary>
        public static readonly Node[] Nodes =
        {
            new Node("growth_vit_01", "体力 一", "最大 HP +10", 20, 0, 0, null, maxHp: 10),
            new Node("growth_vit_02", "体力 二", "最大 HP +10", 40, 0, 1, "growth_vit_01", maxHp: 10),
            new Node("growth_vit_recovery_01", "滋養", "きびだんご回復量 +5", 30, 0, 2, "growth_vit_02", heal: 5),
            new Node("growth_atk_01", "剛剣 一", "刀の HP ダメージ倍率 +0.10", 20, 1, 0, null, attack: 0.10f),
            new Node("growth_atk_02", "剛剣 二", "刀の HP ダメージ倍率 +0.10", 40, 1, 1, "growth_atk_01", attack: 0.10f),
            new Node("growth_atk_stock_01", "腰袋", "きびだんご最大数 +1", 30, 1, 2, "growth_atk_02", capacity: 1),
            new Node("growth_sta_01", "持久 一", "最大スタミナ +10", 20, 2, 0, null, stamina: 10),
            new Node("growth_sta_posture_01", "崩し", "通常攻撃の体幹削り倍率 +0.10", 40, 2, 1, "growth_sta_01", poise: 0.10f),
            new Node("growth_sta_recovery_01", "養生", "きびだんご回復量 +5", 30, 2, 2, "growth_sta_posture_01", heal: 5),
        };
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
        public static readonly Vector3 ShrineC2 = new Vector3(-4f, 0f, 2.6f);
        public static readonly Vector3 EntryCShrine2 = new Vector3(-4f, 0f, 4.2f);
        public static readonly Vector3[] EntryCShrine2Alternates = { new Vector3(-2.8f, 0f, 4.2f), new Vector3(-5.2f, 0f, 4.2f) };
        public static readonly Vector3 CBossArenaCenter = new Vector3(5f, 0f, 0f);
        public static readonly Vector2 CBossArenaSize = new Vector2(12f, 12f);
        public static readonly Vector3 CBossTrigger = new Vector3(4f, 0f, 0f);
        public static readonly Vector3[] CBossSpawns = { new Vector3(9f, 0f, 3f) };
        public static readonly Vector3 PointC = new Vector3(-6f, 0f, 6f);
    }
}
