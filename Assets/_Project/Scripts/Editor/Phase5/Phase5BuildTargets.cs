using UnityEngine;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// 接続が走る軸（P5.5 §7.1）。
    ///
    /// <b>「接続軸」とはカメラが動く軸である。</b> 東西の接続なら X、南北の接続なら Z。
    /// もう一方（直交軸）は境界寄せ領域で接続軸へ固定し、スライドを純粋な 1 軸の移動にする。
    /// </summary>
    public enum Phase5SeamAxis
    {
        /// <summary>X（東西）。</summary>
        X = 0,

        /// <summary>Z（南北）。</summary>
        Z = 1,
    }

    /// <summary>外周壁のどの面に接続口を開けるか。</summary>
    public enum Phase5SeamSide
    {
        /// <summary>開けない。</summary>
        None = 0,

        /// <summary>東（+X）側。</summary>
        East = 1,

        /// <summary>西（−X）側。</summary>
        West = 2,

        /// <summary>北（+Z）側。</summary>
        North = 3,

        /// <summary>南（−Z）側。</summary>
        South = 4,
    }

    /// <summary>接続口の面についての小さな問い合わせ。</summary>
    public static class Phase5SeamSides
    {
        /// <summary>
        /// その面の接続が走る軸。
        ///
        /// <b>面の法線の軸が接続軸である。</b> 東西の壁を抜けると X へ進み、
        /// 南北の壁を抜けると Z へ進む。壁が伸びる向きは<b>直交軸</b>で、
        /// 接続口の中心・幅はそちらで測る（<see cref="Phase5SeamOpening.Center"/>）。
        /// </summary>
        public static Phase5SeamAxis AxisOf(Phase5SeamSide side) =>
            side == Phase5SeamSide.North || side == Phase5SeamSide.South
                ? Phase5SeamAxis.Z
                : Phase5SeamAxis.X;

        /// <summary>接続軸の正方向へ 1（東 +X／西 −X／北 +Z／南 −Z）。</summary>
        public static Vector3 OutwardOf(Phase5SeamSide side)
        {
            switch (side)
            {
                case Phase5SeamSide.East: return Vector3.right;
                case Phase5SeamSide.West: return Vector3.left;
                case Phase5SeamSide.North: return Vector3.forward;
                case Phase5SeamSide.South: return Vector3.back;
                default: return Vector3.zero;
            }
        }
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
        public Phase5SeamOpening(Phase5SeamSide side, float center, float width)
        {
            Side = side;
            Center = center;
            Width = width;
        }

        /// <summary>どの面か。</summary>
        public Phase5SeamSide Side { get; }

        /// <summary>
        /// 接続口の中心（Area のローカル座標。<b>壁が伸びる軸で測る</b>）。
        ///
        /// 東西の壁は Z へ伸びるので Z、南北の壁は X へ伸びるので X。
        /// <b>接続軸ではなく直交軸の座標である</b>——ここを取り違えると、
        /// 開口が壁の外へ出て「壁が 1 本もない面」になる。
        /// </summary>
        public float Center { get; }

        /// <summary>接続口の幅（<see cref="Center"/> と同じ軸）。</summary>
        public float Width { get; }

        /// <summary>この接続が走る軸。</summary>
        public Phase5SeamAxis Axis => Phase5SeamSides.AxisOf(Side);

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
        ///
        /// <b>裁定（付録 B.5）</b>：P5 と P5.5 は独立した試遊構成として別の AreaId を使う。
        /// 入口・仕掛け・調査・発見の ID は再利用するが、<b>構成間の進行引き継ぎは保証しない</b>。
        /// 同一 Session に両構成を混在させない。
        ///
        /// <b>「子の ID を再利用すれば進行に影響しない」は誤りだった。</b>
        /// <c>GameSessionState</c> は AreaId をキーにエリア進行を持ち、その配下に
        /// 仕掛け・調査・Encounter の記録がある。子の ID が同じでも、
        /// P5 と P5.5 の進行は別物になる（GPT レビュー R14 の裁定 1）。
        ///
        /// これは試遊構成の併存に対する措置で、<b>地形や配置を変えるたびに
        /// AreaId を変える規則ではない</b>。
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
        /// <summary>
        /// 接続口をふさぐ見えない境界に持たせる接続 ID（工程 P55-14b）。
        /// 順方向と逆方向の両方を持たせるのは、往復が<b>同じ口</b>を使うためである。
        /// 接続口が無い配置（P5 の探索試遊）では空でよい。
        /// </summary>
        public Core.Identification.StableId SeamConnectionForwardId { get; set; }

        /// <summary>同じ口の逆方向の接続 ID（工程 P55-14b）。</summary>
        public Core.Identification.StableId SeamConnectionReverseId { get; set; }

        public Phase5SeamOpening AreaASeam { get; set; } = Phase5SeamOpening.None;

        /// <summary>エリア B の外周壁に開ける接続口。</summary>
        public Phase5SeamOpening AreaBSeam { get; set; } = Phase5SeamOpening.None;

        /// <summary>A → B の出入口に与える ID（空なら割り当てない。§3.1）。</summary>
        public Core.Identification.StableId ExitAToB { get; set; }

        /// <summary>B → A の出入口に与える ID（空なら割り当てない。§3.1）。</summary>
        public Core.Identification.StableId ExitBToA { get; set; }

        /// <summary>
        /// 接続が走る軸（<see cref="AreaASeam"/> の面から決まる）。接続口が無い構成では X。
        ///
        /// <b>軸は面から導き、別の設定項目にしない。</b> 面と軸を二重に書くと、
        /// 片方だけ直した設定が「東の壁に開けて南北へスライドする」形で成立してしまう。
        /// </summary>
        public Phase5SeamAxis SeamAxis => AreaASeam.IsSet ? AreaASeam.Axis : Phase5SeamAxis.X;

        // ---- 境界まわりの座標（Area ローカル。§3.2）----
        //
        // <b>Builder は境界まわりの座標を計算しない。</b> 以前は P5 のレイアウト定数を
        // 「接続通路の Z へ平行移動する」形で Builder が作っていたが、これは東西の接続しか
        // 表せない（平行移動する軸が Z に固定されている）。配置ごとの設定が座標を持つ形にすれば、
        // 東西でも南北でも同じ Builder が通る——付録 B.1 の「生成器は 1 つ、設定は複数」。
        //
        // 既定値は P5 の受入用 Scene の値そのままで、<c>Phase5()</c> が入れる。

        /// <summary>A → B の開放出入口の位置。</summary>
        public Vector3 AreaAExitPosition { get; set; } = Phase5Layout.AreaAExitToB;

        /// <summary>A → B の出口方向（この向きへ入力し続けると出る。XZ の単位ベクトル）。</summary>
        public Vector3 AreaAExitDirection { get; set; } = Vector3.right;

        /// <summary>A の「B から戻る」入口の位置。</summary>
        public Vector3 AreaAEntryFromB { get; set; } = Phase5Layout.AreaAFromB;

        /// <summary>同・代替配置候補（到着位置が塞がっているとき。§6.3）。</summary>
        public Vector3[] AreaAEntryFromBAlternates { get; set; } = Phase5Layout.AreaAFromBAlternates;

        /// <summary>B の「A から来る」入口の位置。</summary>
        public Vector3 AreaBEntryFromA { get; set; } = Phase5Layout.AreaBFromA;

        /// <summary>同・代替配置候補。</summary>
        public Vector3[] AreaBEntryFromAAlternates { get; set; } = Phase5Layout.AreaBFromAAlternates;

        /// <summary>B → A の出入口と Interact 扉の位置（同じ場所に併存させる）。</summary>
        public Vector3 AreaBDoorToA { get; set; } = Phase5Layout.AreaBDoorToA;

        /// <summary>B → A の出口方向。</summary>
        public Vector3 AreaBExitDirection { get; set; } = Vector3.left;

        // ---- 入口の向き（Data 側。§3.1）----
        //
        // <b>向きは Data に焼く値で、Scene の座標からは導かない。</b> 到着時に主人公が
        // どちらを向くかは配置の意図であって、入口の座標では決まらない。

        /// <summary>A の開始／再開入口の向き。</summary>
        public Core.World.CardinalDirection AreaAStartFacing { get; set; }
            = Core.World.CardinalDirection.North;

        /// <summary>A の「B から戻る」入口の向き（B から来たのだから B の反対を向く）。</summary>
        public Core.World.CardinalDirection AreaAFromBFacing { get; set; }
            = Core.World.CardinalDirection.West;

        /// <summary>B の「A から来る」入口の向き（A から来たのだから A の反対を向く）。</summary>
        public Core.World.CardinalDirection AreaBFromAFacing { get; set; }
            = Core.World.CardinalDirection.East;

        /// <summary>
        /// A の門（レバーで開通する）の位置。既定は P5 の配置。
        ///
        /// <b>「開通しないと境界へ届かない」ようにする。</b> 門を回り込めると
        /// 「開通しないと進めない」という §7.3 の意味が消える。どこを塞げば回り込めないかは
        /// 地形と接続口の面で変わるので、位置も大きさも配置が持つ。
        /// </summary>
        public Vector3 AreaAGatePosition { get; set; } = Phase5Layout.AreaAGate;

        /// <summary>
        /// A の門の大きさ。既定は東の通路（仕切り x=6 と外壁 x=12 の間の 6m）を
        /// 少し余らせて塞ぐ 6.2m。
        /// </summary>
        public Vector3 AreaAGateSize { get; set; }
            = new Vector3(6.2f, Phase5Layout.WallHeight, 0.6f);

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
