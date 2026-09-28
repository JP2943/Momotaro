using Momotaro.Core.Identification;
using Momotaro.Editor.Phase5;
using UnityEngine;

namespace Momotaro.Editor.Phase55
{
    /// <summary>
    /// P5.5 の実ワールド配置の出力先と ID（仕様書 §3.1／§3.2／付録 B.5）。
    ///
    /// <b>P5 と P5.5 は独立した試遊構成として別の AreaId を使う</b>（付録 B.5 の裁定）。
    /// 入口・仕掛け・調査・発見の ID は再利用するが、<b>構成間の進行引き継ぎは保証しない</b>——
    /// <c>GameSessionState</c> は AreaId をキーにエリア進行（仕掛け・調査・Encounter の記録）を
    /// 持つので、子の ID が同じでも P5 と P5.5 の進行は別物になる。
    /// 同一 Session に両構成を混在させない。
    ///
    /// <b>これは試遊構成の併存に対する措置で、地形や配置を変えるたびに AreaId を
    /// 変える規則ではない。</b>
    ///
    /// Scene・Area Data・カタログは <c>Phase55</c> フォルダへ分けて作り、
    /// P5 の受入用 Scene をそのまま残す（§3.2）。
    /// </summary>
    public static class Phase55WorldIds
    {
        /// <summary>Scene の出力先。</summary>
        public const string SceneFolder = "Assets/_Project/Scenes/Tests/Phase55";

        /// <summary>Data の出力先（NavMesh Asset もここ）。</summary>
        public const string DataFolder = "Assets/_Project/Data/Tests/Phase55";

        /// <summary>エリア A の Scene。</summary>
        public const string AreaAScenePath = SceneFolder + "/SCN_Phase55_AreaA.unity";

        /// <summary>エリア B の Scene。</summary>
        public const string AreaBScenePath = SceneFolder + "/SCN_Phase55_AreaB.unity";

        /// <summary>統合起動 Scene。</summary>
        public const string TrialScenePath = SceneFolder + "/SCN_Phase55_ConnectionTrial.unity";

        /// <summary>エリア A の Data。</summary>
        public const string AreaADataPath = DataFolder + "/SO_Area_P55_A.asset";

        /// <summary>エリア B の Data。</summary>
        public const string AreaBDataPath = DataFolder + "/SO_Area_P55_B.asset";

        /// <summary>カタログの Data。</summary>
        public const string CatalogDataPath = DataFolder + "/SO_AreaCatalog_P55.asset";

        /// <summary>接続一覧の Data。</summary>
        public const string ConnectionDataPath = DataFolder + "/SO_AreaConnections_P55.asset";

        /// <summary>
        /// この配置のエリア A の安定 ID。
        ///
        /// <b>P5 とは別の ID を使う</b>（付録 B.5 の裁定）。Data Asset の安定 ID は
        /// プロジェクト全体で一意という不変条件があり、同じ AreaId の
        /// <c>AreaDefinition</c> を 2 つ置くとそこで落ちる。
        /// 入口・仕掛け・調査・発見の ID は再利用するが、
        /// <b>構成間の進行引き継ぎは保証しない</b>（詳細は <c>Phase5BuildTargets.AreaAId</c>）。
        /// </summary>
        public static readonly StableId AreaA = new StableId("area_p55_a");

        /// <summary>この配置のエリア B の安定 ID。</summary>
        public static readonly StableId AreaB = new StableId("area_p55_b");

        /// <summary>カタログの安定 ID。</summary>
        public static readonly StableId Catalog = new StableId("area_catalog_p55");

        /// <summary>接続一覧の安定 ID。</summary>
        public static readonly StableId Connections = new StableId("area_connections_p55");

        /// <summary>A の東の出入口（B へ）。</summary>
        public static readonly StableId ExitAEast = new StableId("exit_p55_a_east");

        /// <summary>B の西の出入口（A へ）。</summary>
        public static readonly StableId ExitBWest = new StableId("exit_p55_b_west");

        /// <summary>A 側の境界寄せカメラ領域（§7.1）。</summary>
        public static readonly StableId RegionASeam = new StableId("region_p55_a_seam");

        /// <summary>B 側の境界寄せカメラ領域。</summary>
        public static readonly StableId RegionBSeam = new StableId("region_p55_b_seam");

        /// <summary>A → B（東へ）の接続。</summary>
        public static readonly StableId ConnectionAToB = new StableId("conn_p55_a_east_to_b");

        /// <summary>B → A（西へ）の接続。</summary>
        public static readonly StableId ConnectionBToA = new StableId("conn_p55_b_west_to_a");
    }

    /// <summary>
    /// P5.5 の共通ワールド座標（仕様書 §3.2／§7.1）。
    ///
    /// <b>A の東に B を置く固定座標である。</b> Scene を読み込むたびに root を動かす方式は採らない。
    /// 数値は P5 の寸法から導いてあり、直接書いた定数はほとんど無い——
    /// A の幅・B の幅を変えたときに境界だけ古い値に取り残されないようにするため。
    /// </summary>
    public static class Phase55WorldLayout
    {
        /// <summary>A の Authoring 原点。ここを世界の基準にする。</summary>
        public static readonly Vector3 AreaAOrigin = Vector3.zero;

        /// <summary>
        /// 接続境界の世界 X。<b>A の東端</b>である。
        ///
        /// A の床は原点中心なので東端は幅の半分。ここで B の西端と突き合わせる。
        /// </summary>
        public const float SeamX = Phase5Layout.AreaAWidth * 0.5f;

        /// <summary>
        /// B の Authoring 原点。<b>B の西端が境界に一致する</b>ように決める。
        ///
        /// B の床も原点中心なので、西端＝原点 − 幅の半分。これを境界へ合わせる。
        /// 目分量の定数を置くと、B の幅を変えたときに隙間や重なりができる。
        /// </summary>
        public static readonly Vector3 AreaBOrigin =
            new Vector3(SeamX + Phase5Layout.AreaBWidth * 0.5f, 0f, 0f);

        /// <summary>
        /// 接続通路の中心 Z。<b>0 を接続軸にする</b>。
        ///
        /// §7.1 は「東西なら Camera の移動は X だけ」と定める。P5 の z=6 のままだと
        /// A の東通路（奥行 18・clamp 範囲 ±2.90）と B の全体（奥行 22・±4.90）で
        /// 寄せ先が違い、Z にもずれる。
        ///
        /// <b>ただし z=0 に置くだけでは足りない。</b> 領域が見える奥行より深いと
        /// clamp は範囲内で<b>追従先の Z をそのまま残す</b>ので、通路の端（z=±1 など）から
        /// 入れば Camera も z=±1 に居る。以前ここに書いていた「領域が画面より広いので
        /// 中心へ寄る」は誤りだった（GPT レビュー R14 の指摘 2）。
        /// 軸に乗せるのは下の<b>境界寄せ領域</b>の仕事である。
        /// </summary>
        public const float SeamZ = 0f;

        /// <summary>接続通路の幅（Z 方向）。正常ルートの通路幅と同じ（§3.2）。</summary>
        public const float PassageWidth = Phase5Layout.CorridorWidth;

        /// <summary>
        /// A の門の Z。<b>仕切りの開口（z &lt; −4）と接続通路（z = 0）の間</b>。
        ///
        /// 門より手前で境界へ抜けられると「開通しないと進めない」という §7.3 の意味が消える。
        /// 開口の北端 −4 より北、通路の南端（−2）より南に置く。
        /// </summary>
        public const float AreaAGateZ = -3.2f;

        /// <summary>
        /// 境界寄せカメラ領域の奥行（§7.1）。<b>見える奥行より狭いこと</b>が条件。
        ///
        /// 見える奥行の半分は <c>orthographicSize / sin(俯角)</c> ＝ 5/sin55° ≒ 6.10 で、
        /// 画面比に依らない。奥行 8（半分 4 &lt; 6.10）なので <c>ClampFocus</c> は
        /// Z を<b>領域の中央＝接続軸</b>へ固定する。
        /// <b>この関係は定数で信じず検査で見る</b>——俯角や投影サイズを変えたら壊れる。
        /// </summary>
        public const float SeamCameraRegionDepth = 8f;

        /// <summary>
        /// A 側の境界寄せ領域の中心（Area ローカル）。東の通路の、境界寄りの区間を覆う。
        /// </summary>
        public static readonly Vector3 SeamCameraRegionACenter =
            new Vector3(Phase5Layout.AreaAEastRegionCenter.x, 0f, SeamZ);

        /// <summary>
        /// 同・大きさ（X, Z）。<b>両軸とも見える広がりより狭い</b>。
        /// 幅 6 は見える横幅（≒17.8）より狭いので X も通路の中央へ固定され、
        /// 奥行 8 は見える奥行（≒12.2）より狭いので Z も接続軸へ固定される——
        /// 境界での画面が 1 点に止まるので、スライドは純粋な X の移動になる。
        /// </summary>
        public static readonly Vector2 SeamCameraRegionASize =
            new Vector2(Phase5Layout.AreaAEastRegionSize.x, SeamCameraRegionDepth);

        /// <summary>
        /// B 側の境界寄せ領域の中心（Area ローカル）。西端から 10m を覆う。
        /// <b>B の全体を覆わない</b>——覆うと戦闘区画（z −8〜8）でも Z が固定され、
        /// 追従の見え方が変わってしまう。
        /// </summary>
        public static readonly Vector3 SeamCameraRegionBCenter =
            new Vector3(-Phase5Layout.AreaBWidth * 0.5f + 5f, 0f, SeamZ);

        /// <summary>同・大きさ（X, Z）。</summary>
        public static readonly Vector2 SeamCameraRegionBSize =
            new Vector2(10f, SeamCameraRegionDepth);

        /// <summary>接続境界へ向かう向き（A から見て東）。</summary>
        public static readonly Vector3 EastwardStep = Vector3.right;

        /// <summary>
        /// P5 のレイアウト定数を、この配置の接続通路（<see cref="SeamZ"/>）へ平行移動する。
        ///
        /// <b>Builder ではなく設定側で動かす。</b> 以前は Builder が同じ計算をしていたが、
        /// 平行移動する軸が Z に固定されていて<b>東西の接続しか表せなかった</b>。
        /// 座標は配置の持ち物である（付録 B.1）。
        /// </summary>
        private static Vector3 Shift(Vector3 p) =>
            new Vector3(p.x, p.y, p.z - Phase5Layout.SeamDefaultZ + SeamZ);

        private static Vector3[] Shift(Vector3[] ps)
        {
            var shifted = new Vector3[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                shifted[i] = Shift(ps[i]);
            }

            return shifted;
        }

        /// <summary>この配置の設定（Builder へ渡す正本）。</summary>
        public static Phase5BuildTargets Targets() => new Phase5BuildTargets
        {
            Label = "P55",
            SceneFolder = Phase55WorldIds.SceneFolder,
            DataFolder = Phase55WorldIds.DataFolder,
            AreaAScenePath = Phase55WorldIds.AreaAScenePath,
            AreaBScenePath = Phase55WorldIds.AreaBScenePath,
            TrialScenePath = Phase55WorldIds.TrialScenePath,
            AreaADataPath = Phase55WorldIds.AreaADataPath,
            AreaBDataPath = Phase55WorldIds.AreaBDataPath,
            CatalogDataPath = Phase55WorldIds.CatalogDataPath,
            CatalogId = Phase55WorldIds.Catalog,
            CatalogDisplayName = "P5.5 エリアカタログ（実ワールド配置）",
            ConnectionDataPath = Phase55WorldIds.ConnectionDataPath,
            TrialHeadline = "P5.5 エリア接続試遊（起動）",
            AreaAId = Phase55WorldIds.AreaA,
            AreaBId = Phase55WorldIds.AreaB,
            AreaAOrigin = AreaAOrigin,
            AreaBOrigin = AreaBOrigin,
            AreaASeam = new Phase5SeamOpening(Phase5SeamSide.East, SeamZ, PassageWidth),
            AreaBSeam = new Phase5SeamOpening(Phase5SeamSide.West, SeamZ, PassageWidth),
            ExitAToB = Phase55WorldIds.ExitAEast,
            ExitBToA = Phase55WorldIds.ExitBWest,
            AreaAGatePosition = new Vector3(
                Phase5Layout.AreaAGate.x, Phase5Layout.AreaAGate.y, AreaAGateZ),
            SeamCameraRegionAId = Phase55WorldIds.RegionASeam,
            SeamCameraRegionBId = Phase55WorldIds.RegionBSeam,
            SeamCameraRegionACenter = SeamCameraRegionACenter,
            SeamCameraRegionASize = SeamCameraRegionASize,
            SeamCameraRegionBCenter = SeamCameraRegionBCenter,
            SeamCameraRegionBSize = SeamCameraRegionBSize,
            AreaAExitPosition = Shift(Phase5Layout.AreaAExitToB),
            AreaAExitDirection = Vector3.right,
            AreaAEntryFromB = Shift(Phase5Layout.AreaAFromB),
            AreaAEntryFromBAlternates = Shift(Phase5Layout.AreaAFromBAlternates),
            AreaBEntryFromA = Shift(Phase5Layout.AreaBFromA),
            AreaBEntryFromAAlternates = Shift(Phase5Layout.AreaBFromAAlternates),
            AreaBDoorToA = Shift(Phase5Layout.AreaBDoorToA),
            AreaBExitDirection = Vector3.left,
            AreaAStartFacing = Core.World.CardinalDirection.North,
            AreaAFromBFacing = Core.World.CardinalDirection.West,
            AreaBFromAFacing = Core.World.CardinalDirection.East,
        };
    }
}
