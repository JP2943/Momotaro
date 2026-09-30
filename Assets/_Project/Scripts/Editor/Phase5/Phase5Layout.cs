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

        /// <summary>
        /// <b>背景面</b>が地形の外側へはみ出す幅（P5.5 §7.3。工程 P55-11a）。
        ///
        /// 隣 Area がまだ読み込まれていない間、境界の向こうは<b>未描画の虚空</b>になる。
        /// §7.3 は「必要な箇所には背景の補完も設ける」と定めるので、
        /// 各 Area が自分の地形より広い背景面を 1 枚持つ。
        ///
        /// <b>数の根拠。</b> 正射影カメラの見える範囲は
        /// 半幅 ＝ <see cref="CameraOrthographicSize"/> × 画面比（16:9 で約 8.9）、
        /// 半奥行 ＝ サイズ ÷ sin(俯角)（約 6.1）。カメラは Area の端まで寄れるので、
        /// 端から<b>半幅ぶん</b>はみ出していれば画面は覆える。14 はその上に余裕を足した値。
        /// </summary>
        public const float BackdropMargin = 14f;

        /// <summary>背景面の厚み（板として置くだけ。Collider は持たない）。</summary>
        public const float BackdropThickness = 0.5f;

        /// <summary>
        /// 背景面の上面を床の<b>下面</b>からどれだけ下げるか。
        ///
        /// 地形より<b>奥</b>に置く（§7.3 の「実地形より奥に描画し、地形が読み込まれたら
        /// 自然に隠れる」）。同じ高さに置くと Z ファイティングで<b>ちらつく</b>。
        /// </summary>
        public const float BackdropDropBelowFloor = 0.15f;

        /// <summary>壁の厚み。</summary>
        public const float WallThickness = 0.5f;

        /// <summary>正常ルートの通路幅（最大通行 Actor の直径＋0.4 以上。§3.2）。</summary>
        public const float CorridorWidth = 4.0f;

        /// <summary>
        /// P5 の接続通路の中心 Z。P5 は Scene を読み替える遷移なので、
        /// 出入口の Z は見た目の都合だけで決められた（§3.2 の座標統一は P5.5 の要求）。
        /// P5.5 はここを <see cref="Phase5BuildTargets.SeamZ"/> で上書きする。
        /// </summary>
        public const float SeamDefaultZ = 6f;

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

        // ---- カメラ（P5-06。仕様書 §11）----
        //
        // <b>暫定値。</b> §11 は「最終カメラサイズは P10b で決める」としている。
        // ここでは「部屋より見える範囲の方が狭い軸は追従し、広い軸は中央固定になる」ことを
        // 実際に起こす大きさを選んである（両方の枝が実 Scene で通る）。

        /// <summary>カメラの俯角（度）。真上が 90。固定（§11。P5 でズーム演出は足さない）。</summary>
        public const float CameraPitchDegrees = 55f;

        /// <summary>Orthographic の縦半分。暫定値（§11。最終値は P10b）。</summary>
        public const float CameraOrthographicSize = 5f;

        /// <summary>基準位置から見たカメラの高さ。</summary>
        public const float CameraHeight = 14f;

        /// <summary>
        /// Rig から見たカメラの局所位置。俯角と高さから決める。
        /// 目分量の固定オフセットにすると、俯角を変えたときに注視点がずれる。
        /// </summary>
        public static Vector3 CameraLocalOffset =>
            new Vector3(0f, CameraHeight, -CameraHeight / Mathf.Tan(CameraPitchDegrees * Mathf.Deg2Rad));

        /// <summary>
        /// カメラ領域を求めるときの<b>画面比の上限</b>（工程 P55-14d。裁定：エリア内は連続追従）。
        ///
        /// 受入の対象は 16:9 だが、<b>広い画面でもクランプが効かない</b>ようにここは広く採る。
        /// 21:9 の半幅は 5 × 2.33 ＝ 約 11.7 で、背景の余白（<see cref="BackdropMargin"/> ＝ 14）が
        /// それを覆う。クランプが効かない領域を余分に広く採っても害は無い——
        /// はみ出した先は背景が覆うだけで、追従は主人公の位置そのままになる。
        /// </summary>
        public const float CameraRegionAspect = 21f / 9f;

        /// <summary>
        /// 床で見える範囲の半分（<see cref="CameraOrthographicSize"/>・
        /// <see cref="CameraRegionAspect"/>・<see cref="CameraPitchDegrees"/> から求める）。
        ///
        /// <b>実行時と同じ純粋関数を使う</b>（別の期待値を作らない）。
        /// </summary>
        public static Vector2 CameraHalfFootprint =>
            Momotaro.Presentation.Cameras.CameraBoundsMath.HalfFootprint(
                CameraOrthographicSize, CameraRegionAspect, CameraPitchDegrees);

        /// <summary>
        /// <b>連続追従のための領域の大きさ</b>（工程 P55-14d。裁定：同一 Area 内は連続追従）。
        ///
        /// <b>エリアの大きさに、見える範囲の半分を四方へ足す。</b>
        /// <c>CameraBoundsMath.ClampFocus</c> は<b>画面がはみ出さないように</b>基準位置を寄せるので、
        /// 領域をエリアと同じにすると<b>端でカメラが止まる</b>（＝追従が切れる）。
        /// 四方へ半画面ぶん広げると <c>low = min, high = max</c> になり、
        /// **主人公がエリア内のどこに居ても基準位置はその位置そのもの**になる。
        ///
        /// <b>これが選べるのは背景の補完（付録 C.33）が入ったからである。</b>
        /// それまではカメラがエリアの外を映すと虚空が見えたので、
        /// 部屋ごとに小さな領域でクランプする必要があった。
        /// </summary>
        public static Vector2 FollowRegionSize(float width, float depth)
        {
            Vector2 half = CameraHalfFootprint;
            return new Vector2(width + (half.x * 2f), depth + (half.y * 2f));
        }

        /// <summary>A の追従領域（エリア全体＋見える範囲の半分）。**A 内は 1 領域だけ**。</summary>
        public static Vector2 AreaAFollowRegionSize => FollowRegionSize(AreaAWidth, AreaADepth);

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

        /// <summary>
        /// B の正常な調査地点。<b>アリーナ境界の外</b>に置く（境界は x -4〜12）。
        /// 外に置くと「犬丸が調査している最中に主人公だけが Trigger へ入る」が作れる。
        /// §8.3 の「調査未確定と戦闘開始 → 調査を中断して戦闘へ」を実機で通すための配置。
        /// </summary>
        public static readonly Vector3 AreaBInvestigation = new Vector3(-8f, 0f, 8f);

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

        /// <summary>
        /// 入口と重ならない安全な戦闘復帰点（§3.2、§8.4 手順 7）。
        /// <b>アリーナ境界の内側</b>に置く。境界の上に置くと、封鎖した瞬間に壁へめり込む。
        /// </summary>
        public static readonly Vector3 AreaBCombatReturn = new Vector3(0f, 0f, -5f);

        /// <summary>戦闘開始 Trigger の広がり（XZ）。境界より十分内側に収まる大きさ（§8.2 末尾）。</summary>
        public static readonly Vector2 AreaBEncounterTriggerSize = new Vector2(3f, 3f);

        /// <summary>B の追従領域（エリア全体＋見える範囲の半分）。**B 内も 1 領域だけ**。</summary>
        public static Vector2 AreaBFollowRegionSize => FollowRegionSize(AreaBWidth, AreaBDepth);
    }
}
