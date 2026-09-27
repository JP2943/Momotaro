using UnityEngine;

namespace Momotaro.Editor.Phase5
{
    /// <summary>外周壁のどの面に接続口を開けるか。</summary>
    public enum Phase5SeamSide
    {
        /// <summary>開けない。</summary>
        None = 0,

        /// <summary>東（+X）側。</summary>
        East = 1,

        /// <summary>西（−X）側。</summary>
        West = 2,
    }

    /// <summary>
    /// 外周壁に開ける接続口（P5.5 仕様書 §3.2／§7.3）。
    ///
    /// P5 は Scene を丸ごと読み替える遷移だったので、外周壁は閉じたままでよかった。
    /// P5.5 は<b>両 Area の実マップを描画したままカメラを送る</b>ので、
    /// 境界の区間は「通路として開いている」見た目でなければならない（§7.3）。
    /// 塞いだまま Collider だけ通す形は使わない——黒い帯や画面を覆う壁になる。
    /// </summary>
    public readonly struct Phase5SeamOpening
    {
        /// <summary>開けない。</summary>
        public static readonly Phase5SeamOpening None = default;

        /// <summary>作る。</summary>
        public Phase5SeamOpening(Phase5SeamSide side, float centerZ, float width)
        {
            Side = side;
            CenterZ = centerZ;
            Width = width;
        }

        /// <summary>どの面か。</summary>
        public Phase5SeamSide Side { get; }

        /// <summary>接続口の中心 Z（Area のローカル座標）。</summary>
        public float CenterZ { get; }

        /// <summary>接続口の幅（Z 方向）。</summary>
        public float Width { get; }

        /// <summary>開ける指定になっているか。</summary>
        public bool IsSet => Side != Phase5SeamSide.None && Width > 0f;
    }

    /// <summary>
    /// 探索 Scene を<b>どこへ・どの配置で</b>生成するか（P5.5 仕様書 §3.2）。
    ///
    /// <b>生成器は 1 つ、設定は 2 つ。</b> P5 の受入用 Scene と P5.5 の実ワールド配置は
    /// 別のフォルダへ別の Asset として作る（§3.2「P5 の受入用 Scene を保存し、
    /// P5.5 の配置へ黙って作り替えない」）。地形・仕掛け・システムの組み立ては同じものなので、
    /// 二つの Builder へ写すと必ず片方だけ直された状態になる。
    ///
    /// <b>local→world 変換はここ 1 か所に集める</b>（§3.2）。Area の中身はすべて
    /// <c>AreaRoot</c> の下にローカル座標で組み、最後に <see cref="AreaAOrigin"/>／
    /// <see cref="AreaBOrigin"/> で根をずらしてから NavMesh を焼く。
    /// 個々の定数へオフセットを足す形にすると、Camera 領域や Spawn だけ足し忘れる。
    /// </summary>
    public sealed class Phase5BuildTargets
    {
        /// <summary>結果表示・NavMesh Asset 名に使う短い呼び名（"P5" など）。</summary>
        public string Label { get; set; } = "P5";

        /// <summary>Scene の出力フォルダ。</summary>
        public string SceneFolder { get; set; }

        /// <summary>Data の出力フォルダ。NavMesh の Asset もここへ置く。</summary>
        public string DataFolder { get; set; }

        /// <summary>エリア A の Scene。</summary>
        public string AreaAScenePath { get; set; }

        /// <summary>エリア B の Scene。</summary>
        public string AreaBScenePath { get; set; }

        /// <summary>統合起動 Scene。</summary>
        public string TrialScenePath { get; set; }

        /// <summary>エリア A の Data。</summary>
        public string AreaADataPath { get; set; }

        /// <summary>エリア B の Data。</summary>
        public string AreaBDataPath { get; set; }

        /// <summary>カタログの Data。</summary>
        public string CatalogDataPath { get; set; }

        /// <summary>
        /// エリア A の安定 ID。
        ///
        /// <b>配置ごとに別の ID を使う。</b> 仕様書 §3.2 は「AreaId／EntryId は既存を再利用し、
        /// 接続のために進行 ID を振り直さない」と書いているが、この repo では
        /// <b>Data Asset の安定 ID はプロジェクト全体で一意</b>という不変条件が別にあり
        /// （<c>ProjectDataValidator</c>／<c>ProjectAssetIntegrityTests</c>）、
        /// 同じ AreaId の <c>AreaDefinition</c> を 2 つ置くとそこで落ちる。
        ///
        /// ID を分けたのは、一意性の穴を開けるより副作用が小さいと判断したため。
        /// <c>AreaInstanceHandle</c>・<c>CurrentAreaProvider</c>・カタログ引きはどれも
        /// 「AreaId が Area を一意に指す」前提で書かれており、同じ ID の別レイアウトを
        /// 許すとこれらが<b>型では防げない曖昧さ</b>を持つ。
        /// <b>入口 ID・仕掛け・調査・発見の ID は再利用する</b>（進行に効くのはこちら）。
        /// 判断の是非は検査で見ていただきたい。
        /// </summary>
        public Core.Identification.StableId AreaAId { get; set; } = Phase5AreaIds.AreaA;

        /// <summary>エリア B の安定 ID（<see cref="AreaAId"/> と同じ理由で配置ごとに分ける）。</summary>
        public Core.Identification.StableId AreaBId { get; set; } = Phase5AreaIds.AreaB;

        /// <summary>カタログの安定 ID。</summary>
        public Core.Identification.StableId CatalogId { get; set; }

        /// <summary>カタログの表示名。</summary>
        public string CatalogDisplayName { get; set; }

        /// <summary>
        /// この配置の接続一覧の<b>パス</b>（P5.5 §3.1）。空なら接続を持たない構成。
        ///
        /// Area Scene の初期化担当がこの Asset を遷移サービスへ渡す。渡さないと、出入口は
        /// 従来どおり行き先を直接指すだけになり「東へスライドする」情報がどこにも無い。
        ///
        /// <b>Asset の参照ではなくパスで持つ。</b> 生成の途中には
        /// <c>AssetDatabase.Refresh</c>／<c>SaveAssets</c> が何度も挟まり、
        /// 事前に掴んだ <c>ScriptableObject</c> の参照は<b>そこで無効になることがある</b>
        /// （実際に踏んだ：接続を渡したのに Scene には未設定で保存された）。
        /// カタログも同じ理由でパスで持っている。
        /// </summary>
        public string ConnectionDataPath { get; set; }

        /// <summary>エリア A の Authoring 原点（世界座標）。</summary>
        public Vector3 AreaAOrigin { get; set; } = Vector3.zero;

        /// <summary>エリア B の Authoring 原点（世界座標）。</summary>
        public Vector3 AreaBOrigin { get; set; } = Vector3.zero;

        /// <summary>エリア A の外周壁に開ける接続口。</summary>
        public Phase5SeamOpening AreaASeam { get; set; } = Phase5SeamOpening.None;

        /// <summary>エリア B の外周壁に開ける接続口。</summary>
        public Phase5SeamOpening AreaBSeam { get; set; } = Phase5SeamOpening.None;

        /// <summary>A → B の出入口に与える ID（空なら割り当てない。§3.1）。</summary>
        public Core.Identification.StableId ExitAToB { get; set; }

        /// <summary>B → A の出入口に与える ID（空なら割り当てない。§3.1）。</summary>
        public Core.Identification.StableId ExitBToA { get; set; }

        /// <summary>
        /// 接続通路の中心 Z（P5.5 §7.1）。既定は <see cref="Phase5Layout.SeamDefaultZ"/>。
        ///
        /// <b>両 Area の Camera 領域が同じ Z へ clamp する位置を選ぶ。</b>
        /// §7.1 は「東西なら Camera の移動は X だけ」と定める。A の東通路（奥行 18）と
        /// B の全体（奥行 22）は見える範囲より深いので、clamp の結果は<b>領域の中心へ寄る</b>——
        /// z=6 のような偏った位置に通路を置くと、A 側は 2.9、B 側は 4.9 へ寄って
        /// <b>接続軸以外（Z）にもずれる</b>。どちらの領域も中心 z=0 なので、通路も z=0 に置く。
        ///
        /// 出入口・入口・その代替配置はこの値に合わせて Z ごと平行移動する。
        /// 個別に座標を渡す形にすると、代替配置や目印だけ古い Z に取り残される。
        /// </summary>
        public float SeamZ { get; set; } = Phase5Layout.SeamDefaultZ;

        /// <summary>
        /// A の門（レバーで開通する）の Z。既定は P5 の配置。
        ///
        /// <b>通路の入口と仕切りの開口の「間」に置く。</b> 門より手前で境界へ抜けられると
        /// 「開通しないと進めない」という §7.3 の意味が消える。
        /// </summary>
        public float AreaAGateZ { get; set; }

        /// <summary>統合起動 Scene に置く見出し。</summary>
        public string TrialHeadline { get; set; } = "P5 探索試遊（起動）";

        /// <summary>P5 の受入用 Scene（従来配置。原点も接続口も無し）。</summary>
        public static Phase5BuildTargets Phase5() => new Phase5BuildTargets
        {
            Label = "P5",
            SceneFolder = Phase5AreaIds.SceneFolder,
            DataFolder = Phase5AreaIds.DataFolder,
            AreaAScenePath = Phase5AreaIds.AreaAScenePath,
            AreaBScenePath = Phase5AreaIds.AreaBScenePath,
            TrialScenePath = Phase5AreaIds.TrialScenePath,
            AreaADataPath = Phase5AreaIds.AreaADataPath,
            AreaBDataPath = Phase5AreaIds.AreaBDataPath,
            CatalogDataPath = Phase5AreaIds.CatalogDataPath,
            CatalogId = Phase5AreaIds.Catalog,
            CatalogDisplayName = "P5 エリアカタログ",
            TrialHeadline = "P5 探索試遊（起動）",
        };

        /// <summary>NavMesh Asset の名前（Area ごと。P5 と P5.5 で衝突させない）。</summary>
        public string NavMeshAssetName(string areaSuffix) => "NavMesh_" + Label + "_" + areaSuffix;
    }
}
