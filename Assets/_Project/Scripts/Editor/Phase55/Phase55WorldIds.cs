using Momotaro.Core.Identification;
using Momotaro.Editor.Phase5;
using UnityEngine;

namespace Momotaro.Editor.Phase55
{
    /// <summary>
    /// P5.5 の実ワールド配置の出力先と ID（仕様書 §3.1／§3.2）。
    ///
    /// <b>AreaId・EntryId は P5 のものを再利用する</b>（§3.2「接続のために進行 ID を振り直さない」）。
    /// 別なのは<b>置き場所と配置</b>だけ——Scene・Area Data・カタログを
    /// <c>Phase55</c> フォルダへ分けて作り、P5 の受入用 Scene をそのまま残す。
    /// 同じ AreaId の P5／P5.5 カタログは別の試遊構成として選ぶもので、
    /// 同じ Session へ混在させない（§3.2）。
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
        /// <b>P5 とは別の ID を使う。</b> 仕様書 §3.2 は AreaId の再利用を求めているが、
        /// この repo には「Data Asset の安定 ID はプロジェクト全体で一意」という不変条件が
        /// 別にあり、同じ AreaId の <c>AreaDefinition</c> を 2 つ置くとそこで落ちる。
        /// 理由と判断は <c>Phase5BuildTargets.AreaAId</c> に書いてある。
        /// <b>入口・仕掛け・調査・発見の ID は P5 のものを再利用する</b>（進行に効くのはこちら）。
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
        /// 接続通路の中心 Z。<b>0（両 Area の Camera 領域の中心）</b>。
        ///
        /// §7.1 は「東西なら Camera の移動は X だけ」と定める。A の東通路（奥行 18）と
        /// B の全体（奥行 22）はどちらも見える範囲より深いので、clamp は<b>領域の中心へ寄る</b>。
        /// P5 の z=6 のままだと A 側は約 2.9、B 側は約 4.9 へ寄って<b>Z にもずれる</b>。
        /// 両方の領域中心が z=0 なので、通路も z=0 に置く。
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

        /// <summary>接続境界へ向かう向き（A から見て東）。</summary>
        public static readonly Vector3 EastwardStep = Vector3.right;

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
            SeamZ = SeamZ,
            AreaAGateZ = AreaAGateZ,
        };
    }
}
