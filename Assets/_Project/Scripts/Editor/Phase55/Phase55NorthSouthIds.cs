using Momotaro.Core.Identification;
using Momotaro.Editor.Phase5;
using UnityEngine;

namespace Momotaro.Editor.Phase55
{
    /// <summary>
    /// P5.5 の<b>南北配置</b>の出力先と ID（仕様書 §11 の P03。工程 P55-05c）。
    ///
    /// <b>配置ごとに別の AreaId を使う</b>（付録 B.5 の裁定）。東西配置と同じ理由で、
    /// 進行の引き継ぎは保証しない。Scene・Data は東西と別のフォルダへ出す。
    ///
    /// 南を S、北を N と呼ぶ。Builder の中では S が「エリア A」、N が「エリア B」に当たる——
    /// 生成器は 1 つで、違うのは<b>設定だけ</b>（付録 B.1）。
    /// </summary>
    public static class Phase55NorthSouthIds
    {
        /// <summary>Scene の出力先。</summary>
        public const string SceneFolder = "Assets/_Project/Scenes/Tests/Phase55NS";

        /// <summary>Data の出力先（NavMesh Asset もここ）。</summary>
        public const string DataFolder = "Assets/_Project/Data/Tests/Phase55NS";

        /// <summary>南のエリアの Scene（Builder の「A」）。</summary>
        public const string AreaSScenePath = SceneFolder + "/SCN_Phase55NS_AreaS.unity";

        /// <summary>北のエリアの Scene（Builder の「B」）。</summary>
        public const string AreaNScenePath = SceneFolder + "/SCN_Phase55NS_AreaN.unity";

        /// <summary>統合起動 Scene。</summary>
        public const string TrialScenePath = SceneFolder + "/SCN_Phase55NS_ConnectionTrial.unity";

        /// <summary>南のエリアの Data。</summary>
        public const string AreaSDataPath = DataFolder + "/SO_Area_P55_S.asset";

        /// <summary>北のエリアの Data。</summary>
        public const string AreaNDataPath = DataFolder + "/SO_Area_P55_N.asset";

        /// <summary>カタログの Data。</summary>
        public const string CatalogDataPath = DataFolder + "/SO_AreaCatalog_P55NS.asset";

        /// <summary>接続一覧の Data。</summary>
        public const string ConnectionDataPath = DataFolder + "/SO_AreaConnections_P55NS.asset";

        /// <summary>南のエリアの安定 ID。</summary>
        public static readonly StableId AreaS = new StableId("area_p55_s");

        /// <summary>北のエリアの安定 ID。</summary>
        public static readonly StableId AreaN = new StableId("area_p55_n");

        /// <summary>カタログの安定 ID。</summary>
        public static readonly StableId Catalog = new StableId("area_catalog_p55_ns");

        /// <summary>接続一覧の安定 ID。</summary>
        public static readonly StableId Connections = new StableId("area_connections_p55_ns");

        /// <summary>S の北の出入口（N へ）。</summary>
        public static readonly StableId ExitSNorth = new StableId("exit_p55_s_north");

        /// <summary>N の南の出入口（S へ）。</summary>
        public static readonly StableId ExitNSouth = new StableId("exit_p55_n_south");

        /// <summary>S 側の境界寄せカメラ領域（§7.1）。</summary>
        public static readonly StableId RegionSSeam = new StableId("region_p55_s_seam");

        /// <summary>N 側の境界寄せカメラ領域。</summary>
        public static readonly StableId RegionNSeam = new StableId("region_p55_n_seam");

        /// <summary>S → N（北へ）の接続。</summary>
        public static readonly StableId ConnectionSToN = new StableId("conn_p55_s_north_to_n");

        /// <summary>N → S（南へ）の接続。</summary>
        public static readonly StableId ConnectionNToS = new StableId("conn_p55_n_south_to_s");
    }

    /// <summary>
    /// P5.5 の南北配置の共通ワールド座標（§7.1／§11 の P03。工程 P55-05c）。
    ///
    /// <b>東西配置の「向きだけ違う版」ではない。</b> 接続軸が Z になると、
    /// カメラが接続軸以外（X）へ動かないことに加えて、
    /// <b>スライドの全区間で見える範囲が床に収まる</b>ことが別の条件になる。
    ///
    /// 見える半幅は <c>orthographicSize × 画面比</c> ＝ 5×16/9 ≒ 8.89 で、S の床は幅 24
    /// （x −12〜12）。カメラの X が ±3.11 を超えると、床の外——つまり
    /// <b>黒い帯</b>——が映る。東西配置では隣の Area が横に続くので x=9 でも覆えたが、
    /// 南北配置で隣が続くのは Z なので<b>横は自分の床だけで覆う</b>しかない。
    /// だから接続通路は<b>部屋の X 中央</b>に置く。
    /// 東の通路（x=9）へ北の接続口を開ける案は、この計算で落ちた。
    /// </summary>
    public static class Phase55NorthSouthLayout
    {
        /// <summary>S（南のエリア）の Authoring 原点。ここを世界の基準にする。</summary>
        public static readonly Vector3 AreaSOrigin = Vector3.zero;

        /// <summary>
        /// 接続境界の世界 Z。<b>S の北端</b>である。
        /// S の床は原点中心なので北端は奥行の半分。ここで N の南端と突き合わせる。
        /// </summary>
        public const float SeamZ = Phase5Layout.AreaADepth * 0.5f;

        /// <summary>
        /// 接続通路の中心 X。<b>部屋の X 中央</b>に置く（上の説明のとおり）。
        /// ここを外すと、スライドの途中でカメラが床の外を映す。
        /// </summary>
        public const float SeamX = 0f;

        /// <summary>接続通路の幅（X 方向）。正常ルートの通路幅と同じ（§3.2）。</summary>
        public const float PassageWidth = Phase5Layout.CorridorWidth;

        /// <summary>
        /// N の Authoring 原点。<b>N の南端が境界に、N の通路が S の通路に一致する</b>ように決める。
        ///
        /// N の床も原点中心なので、南端＝原点 − 奥行の半分。X は通路をそろえる。
        /// </summary>
        public static readonly Vector3 AreaNOrigin =
            new Vector3(SeamX, 0f, SeamZ + Phase5Layout.AreaBDepth * 0.5f);

        /// <summary>境界寄せカメラ領域の奥行・幅の上限を決める見える広がり（§7.1 の説明は東西と同じ）。</summary>
        public const float SeamCameraRegionDepth = 8f;

        /// <summary>
        /// S 側の境界寄せ領域の中心（Area ローカル）。境界の手前 4m に画面を止める。
        /// 領域は Z に 8m（z 1〜9）で、北端が境界に接する。
        /// </summary>
        public static readonly Vector3 SeamCameraRegionSCenter =
            new Vector3(SeamX, 0f, SeamZ - SeamCameraRegionDepth * 0.5f);

        /// <summary>
        /// 同・大きさ（X, Z）。<b>両軸とも見える広がりより狭い</b>
        /// （幅 6 &lt; 17.8、奥行 8 &lt; 12.2）ので、カメラは 1 点に止まる。
        /// </summary>
        public static readonly Vector2 SeamCameraRegionSSize =
            new Vector2(6f, SeamCameraRegionDepth);

        /// <summary>N 側の境界寄せ領域の中心（Area ローカル）。南端から 10m を覆う。</summary>
        public static readonly Vector3 SeamCameraRegionNCenter =
            new Vector3(0f, 0f, -Phase5Layout.AreaBDepth * 0.5f + 5f);

        /// <summary>同・大きさ（X, Z）。奥行 10 &lt; 12.2、幅 8 &lt; 17.8。</summary>
        public static readonly Vector2 SeamCameraRegionNSize = new Vector2(8f, 10f);

        /// <summary>
        /// S の門の位置（レバーで開通する）。<b>部屋を端から端まで塞ぐ</b>。
        ///
        /// 北へ抜ける道は東の通路だけではないので、東西配置のように通路 1 本を塞いでも
        /// 回り込める。南北配置では<b>部屋の全幅</b>を塞ぐ板を、
        /// 開始点（z=−6）より北・水場（z 3〜8）より南へ置く。
        ///
        /// <b>調査地点（z=1）から 0.7m 離す。</b> Interact の遮蔽判定は
        /// <b>半径 0.25m の球</b>を飛ばすので（<c>PhysicsObstacleProbe</c>）、
        /// 錨のすぐ向こうに壁があると<b>錨の手前から調べても遮蔽と判定される</b>。
        /// 最初は z=1.5（板の南面が z=1.2）に置いていて、余裕が 0.2m しか無く、
        /// <b>2 回まぐれで通ったあとに落ちた</b>（記録 027）。
        /// </summary>
        public static readonly Vector3 AreaSGatePosition = new Vector3(0f, 0f, 2f);

        /// <summary>同・大きさ。床の幅 24 を少し超えて塞ぐ。</summary>
        public static readonly Vector3 AreaSGateSize =
            new Vector3(Phase5Layout.AreaAWidth + 0.2f, Phase5Layout.WallHeight, 0.6f);

        /// <summary>S → N の開放出入口（北端の内側 0.6m）。</summary>
        public static readonly Vector3 AreaSExitToN =
            new Vector3(SeamX, 0f, Phase5Layout.AreaADepth * 0.5f - 0.6f);

        /// <summary>S の「N から戻る」入口（北端の内側 2.5m）。</summary>
        public static readonly Vector3 AreaSEntryFromN =
            new Vector3(SeamX, 0f, Phase5Layout.AreaADepth * 0.5f - 2.5f);

        /// <summary>同・代替配置候補（§6.3）。水場（x −11.5〜−4.5）を避けて東寄りへ逃がす。</summary>
        public static readonly Vector3[] AreaSEntryFromNAlternates =
        {
            new Vector3(SeamX + 2f, 0f, Phase5Layout.AreaADepth * 0.5f - 2.5f),
            new Vector3(SeamX, 0f, Phase5Layout.AreaADepth * 0.5f - 4.5f),
        };

        /// <summary>
        /// N の「S から来る」入口（南端の内側 1.5m。Area ローカル）。
        ///
        /// <b>戦闘区画（z −8〜8）の南 1.5m に置く</b>。東西配置では入口が区画の
        /// 横 7.5m 外だったが、南北配置では入口が区画と同じ X に来るので、
        /// 奥行きで離すしかない。区画の線の上へ置くと、封鎖した瞬間に壁へめり込む。
        /// </summary>
        public static readonly Vector3 AreaNEntryFromS =
            new Vector3(0f, 0f, -Phase5Layout.AreaBDepth * 0.5f + 1.5f);

        /// <summary>同・代替配置候補。奥行きは変えず、横へ逃がす（北へ寄せると区画に入る）。</summary>
        public static readonly Vector3[] AreaNEntryFromSAlternates =
        {
            new Vector3(-2f, 0f, -Phase5Layout.AreaBDepth * 0.5f + 1.5f),
            new Vector3(2f, 0f, -Phase5Layout.AreaBDepth * 0.5f + 1.5f),
        };

        /// <summary>N → S の出入口と Interact 扉（南端の内側 0.6m）。</summary>
        public static readonly Vector3 AreaNDoorToS =
            new Vector3(0f, 0f, -Phase5Layout.AreaBDepth * 0.5f + 0.6f);

        /// <summary>この配置の設定（Builder へ渡す正本）。</summary>
        public static Phase5BuildTargets Targets() => new Phase5BuildTargets
        {
            Label = "P55NS",
            SceneFolder = Phase55NorthSouthIds.SceneFolder,
            DataFolder = Phase55NorthSouthIds.DataFolder,
            AreaAScenePath = Phase55NorthSouthIds.AreaSScenePath,
            AreaBScenePath = Phase55NorthSouthIds.AreaNScenePath,
            TrialScenePath = Phase55NorthSouthIds.TrialScenePath,
            AreaADataPath = Phase55NorthSouthIds.AreaSDataPath,
            AreaBDataPath = Phase55NorthSouthIds.AreaNDataPath,
            CatalogDataPath = Phase55NorthSouthIds.CatalogDataPath,
            CatalogId = Phase55NorthSouthIds.Catalog,
            CatalogDisplayName = "P5.5 エリアカタログ（南北配置）",
            ConnectionDataPath = Phase55NorthSouthIds.ConnectionDataPath,
            TrialHeadline = "P5.5 エリア接続試遊（南北・起動）",
            AreaAId = Phase55NorthSouthIds.AreaS,
            AreaBId = Phase55NorthSouthIds.AreaN,
            AreaAOrigin = AreaSOrigin,
            AreaBOrigin = AreaNOrigin,
            AreaASeam = new Phase5SeamOpening(Phase5SeamSide.North, SeamX, PassageWidth),
            AreaBSeam = new Phase5SeamOpening(Phase5SeamSide.South, SeamX, PassageWidth),
            ExitAToB = Phase55NorthSouthIds.ExitSNorth,
            ExitBToA = Phase55NorthSouthIds.ExitNSouth,
            AreaAGatePosition = AreaSGatePosition,
            AreaAGateSize = AreaSGateSize,
            SeamCameraRegionAId = Phase55NorthSouthIds.RegionSSeam,
            SeamCameraRegionBId = Phase55NorthSouthIds.RegionNSeam,
            SeamCameraRegionACenter = SeamCameraRegionSCenter,
            SeamCameraRegionASize = SeamCameraRegionSSize,
            SeamCameraRegionBCenter = SeamCameraRegionNCenter,
            SeamCameraRegionBSize = SeamCameraRegionNSize,
            AreaAExitPosition = AreaSExitToN,
            AreaAExitDirection = Vector3.forward,
            AreaAEntryFromB = AreaSEntryFromN,
            AreaAEntryFromBAlternates = AreaSEntryFromNAlternates,
            AreaBEntryFromA = AreaNEntryFromS,
            AreaBEntryFromAAlternates = AreaNEntryFromSAlternates,
            AreaBDoorToA = AreaNDoorToS,
            AreaBExitDirection = Vector3.back,
            AreaAStartFacing = Core.World.CardinalDirection.North,
            AreaAFromBFacing = Core.World.CardinalDirection.South,
            AreaBFromAFacing = Core.World.CardinalDirection.North,
        };
    }
}
