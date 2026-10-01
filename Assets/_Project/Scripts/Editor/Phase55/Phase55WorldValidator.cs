using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using Momotaro.Presentation.Cameras;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Editor.Phase55
{
    /// <summary>
    /// P5.5 の実ワールド配置を検査する（仕様書 §3.2／§7.1／§7.3／§10。工程 P55-03c）。
    ///
    /// <b>ここだけが「2 つの Scene の関係」を見られる。</b> P5 の Scene 検査は Scene 1 枚ずつで、
    /// 「A の東端と B の西端が噛み合っているか」「境界の区間が通路として開いているか」
    /// 「スライドが接続軸以外へずれないか」はどれも 1 枚では言えない。
    ///
    /// <b>見えるものだけで判定する。</b> 期待値は Builder の定数ではなく、
    /// 生成された Scene の Collider と Transform から採る——定数を読み合わせる検査は、
    /// Builder と定数が同時に間違っていると通ってしまう。
    /// </summary>
    public static class Phase55WorldValidator
    {
        /// <summary>試遊基準の画面比（§7.3「試遊基準は 16:9」）。</summary>
        private const float TrialAspect = 16f / 9f;

        /// <summary>床の上面 Y。両 Area で一致していること（§3.2）。</summary>
        private const float FloorTopY = 0f;

        /// <summary>座標の一致に許す誤差（m）。</summary>
        private const float Tolerance = 0.01f;

        /// <summary>遮蔽判定が飛ばす球の半径（<c>PhysicsObstacleProbe</c> の既定）。</summary>
        private const float InteractionProbeRadius = 0.25f;

        /// <summary>Interact の錨と門のあいだに要る隙間（球の直径ぶん）。</summary>
        private const float RequiredInteractionClearance = InteractionProbeRadius * 2f;

        /// <summary>
        /// 片側の Area から採った事実。
        ///
        /// <b>値だけを持つ。</b> Scene は 1 枚ずつ Single で開くので、B を開いた時点で
        /// A の GameObject は破棄されている。部品の参照を持ち回ると、比較のときには
        /// 全部「破棄済み」になっている（実際に踏んだ：領域が決まらないと報告された）。
        /// </summary>
        public sealed class AreaFacts
        {
            public string ScenePath;
            public Bounds FloorBounds;
            public float FloorTop;
            public CameraRegionDefinition DefaultRegion;
            public readonly List<CameraRegionDefinition> Regions = new List<CameraRegionDefinition>();
            public readonly Dictionary<string, Vector3> Entries = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Vector3> Exits = new Dictionary<string, Vector3>();
            public readonly List<Bounds> SideWalls = new List<Bounds>();

            /// <summary>レバーで開く門の見た目の箱（遮蔽を作る本体）。</summary>
            public readonly List<Bounds> FlagDoors = new List<Bounds>();

            /// <summary>Interact の錨（調査地点・レバー）。</summary>
            public readonly Dictionary<string, Vector3> InteractionAnchors =
                new Dictionary<string, Vector3>();
            public string CatalogPath;
            public string ConnectionPath;
        }

        [MenuItem("Momotaro/Phase 5.5/Validate Connected World")]
        private static void ValidateInteractive()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Validate(errors, warnings);

            string body = errors.Count == 0
                ? "検査を通りました（警告 " + warnings.Count + " 件）。"
                : "エラー " + errors.Count + " 件。\n" + string.Join("\n", errors);
            EditorUtility.DisplayDialog("P5.5 実ワールド配置の検査", body, "OK");
        }

        /// <summary>
        /// <b>全配置</b>（東西・南北）を検査する。
        ///
        /// <b>配置を足したら検査も付いてくる形にしてある。</b> 一覧をなめるので、
        /// <c>Phase55Arrangements</c> へ足した配置は黙って検査から漏れない——
        /// 「作ったが見ていない」を構造で防ぐ。
        /// </summary>
        public static void Validate(List<string> errors, List<string> warnings)
        {
            foreach (Phase55Arrangement arrangement in Phase55Arrangements.All())
            {
                int before = errors.Count;
                Validate(arrangement, errors, warnings);
                for (int i = before; i < errors.Count; i++)
                {
                    errors[i] = "【" + arrangement.Label + "】" + errors[i];
                }
            }
        }

        /// <summary>
        /// A・B を<b>同時に</b>開いて配置と接続を検査する。
        ///
        /// <b>Additive で 2 枚載せる。</b> 1 枚ずつ Single で開くと、2 枚目を開いた時点で
        /// 1 枚目の部品が破棄され、比較に使えるのは値写しだけになる。実行時も 2 枚同時に
        /// 載る構成なので（§4.1）、検査も同じ載せ方にしておくほうが実態に近い。
        /// </summary>
        public static void Validate(
            Phase55Arrangement arrangement, List<string> errors, List<string> warnings)
        {
            if (!TryOpenBoth(arrangement, errors, out Scene sceneA, out Scene sceneB))
            {
                return;
            }

            try
            {
                if (!TryCollect(sceneA, errors, warnings, out AreaFacts a)
                    || !TryCollect(sceneB, errors, warnings, out AreaFacts b))
                {
                    return;
                }

                Compare(arrangement, a, b, errors);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        /// <summary>
        /// A・B を空の Scene の上へ Additive で載せる。
        /// <b>検査とテストが同じ載せ方を使う</b>ために公開してある（別の手順で開くと別の結果になる）。
        /// </summary>
        public static bool TryOpenBoth(
            Phase55Arrangement arrangement, List<string> errors, out Scene sceneA, out Scene sceneB)
        {
            sceneA = default;
            sceneB = default;

            foreach (string path in new[] { arrangement.AreaAScenePath, arrangement.AreaBScenePath })
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(path) == null)
                {
                    errors.Add("Scene がありません: " + path + "（build-phase55-world を先に実行する）。");
                    return false;
                }
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            sceneA = EditorSceneManager.OpenScene(arrangement.AreaAScenePath, OpenSceneMode.Additive);
            sceneB = EditorSceneManager.OpenScene(arrangement.AreaBScenePath, OpenSceneMode.Additive);
            return true;
        }

        /// <summary>2 つの Area の<b>関係</b>を見る（1 枚ずつでは言えないもの）。</summary>
        public static void Compare(
            Phase55Arrangement arrangement, AreaFacts a, AreaFacts b, List<string> errors)
        {
            ValidateSeam(arrangement, a, b, errors);
            ValidatePassageIsOpen(arrangement, a, forward: true, errors);
            ValidatePassageIsOpen(arrangement, b, forward: false, errors);
            ValidateTravelOrder(arrangement, a, b, errors);
            ValidateCameraAxis(arrangement, a, b, errors);
            ValidateSlideIsCovered(arrangement, a, b, errors);
            ValidateConnections(arrangement, a, b, errors);
            ValidateBoundData(arrangement, a, errors);
            ValidateBoundData(arrangement, b, errors);
            ValidateInteractionClearance(a, errors);
            ValidateInteractionClearance(b, errors);
        }

        /// <summary>
        /// Area Scene が<b>この配置の</b>カタログと接続一覧を指していること（§3.1／§3.2）。
        ///
        /// ここを取り違えても Scene は成立してしまう——実行時に初めて
        /// 「この Area はカタログに無い」「出入口の接続が引けない」という形で壊れる。
        /// 実際に踏んだ：生成器がカタログのパスを P5 に固定していたため、
        /// P5.5 の Area Scene が P5 のカタログを指していた。
        /// </summary>
        private static void ValidateBoundData(
            Phase55Arrangement arrangement, AreaFacts facts, List<string> errors)
        {
            if (facts.CatalogPath != arrangement.CatalogDataPath)
            {
                errors.Add(facts.ScenePath + ": 初期化担当がこの配置のカタログを指していません（実際="
                    + (facts.CatalogPath ?? "未設定") + " 期待=" + arrangement.CatalogDataPath + "）。");
            }

            if (facts.ConnectionPath != arrangement.ConnectionDataPath)
            {
                errors.Add(facts.ScenePath + ": 初期化担当がこの配置の接続一覧を指していません（実際="
                    + (facts.ConnectionPath ?? "未設定") + " 期待=" + arrangement.ConnectionDataPath
                    + "）。出入口から接続を引けません（§3.1）。");
            }
        }

        // ---------------------------------------------------------------- 事実の採取

        /// <summary>
        /// Scene を開いて必要な事実だけを採る。
        /// <b>P5 の Scene 検査も通す</b>——配置が正しくても Area として壊れていれば不合格。
        /// </summary>
        public static bool TryCollect(
            Scene scene, List<string> errors, List<string> warnings, out AreaFacts facts)
        {
            facts = null;
            string scenePath = scene.path;
            Phase5ExplorationValidator.Validate(scene, errors, warnings);

            var collected = new AreaFacts { ScenePath = scenePath };

            List<AreaRoot> roots = Phase5ExplorationValidator.Components<AreaRoot>(scene);
            if (roots.Count != 1)
            {
                errors.Add(scenePath + ": AreaRoot が 1 つではありません。");
                return false;
            }

            AreaRoot root = roots[0];

            // <b>床と壁は Renderer から採る</b>。
            //
            // Collider は活動ゲートが出荷時に無効化しているので、<c>Collider.bounds</c> は
            // 中心だけの空の Bounds を返す（実際に踏んだ：東端が x=0 と報告された）。
            // §7.3 が見張っているのは「見える範囲が覆われているか」なので、
            // 見た目の境界で判定するほうが問いにも合っている。
            bool floorFound = false;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.name != "Floor")
                {
                    continue;
                }

                collected.FloorBounds = floorFound
                    ? Encapsulated(collected.FloorBounds, renderer.bounds)
                    : renderer.bounds;
                floorFound = true;
            }

            if (!floorFound)
            {
                errors.Add(scenePath + ": 床（Floor）が見つかりません。");
                return false;
            }

            collected.FloorTop = collected.FloorBounds.max.y;

            // <b>背景面</b>（§7.3 の「背景の補完」。工程 P55-11a）。
            //
            // 隣 Area がまだ読み込まれていない間、境界の向こうは未描画の虚空になる。
            // 保持（§6.2 手順 11）は到着まわりしか覆えないので、各 Area が自分の地形より
            // 広い背景面を 1 枚持つ。<b>Builder 再生成で消えたことに気付けるように</b>ここで見る。
            Renderer backdrop = null;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null && renderer.name == "Backdrop")
                {
                    backdrop = renderer;
                    break;
                }
            }

            if (backdrop == null)
            {
                errors.Add(scenePath + ": 背景面（Backdrop）が見つかりません（§7.3 の背景の補完）。");
                return false;
            }

            if (backdrop.GetComponent<Collider>() != null)
            {
                errors.Add(scenePath + ": 背景面が Collider を持っています"
                    + "（移動可能範囲・NavMesh・AI に関与させない。§7.3）。");
            }

            if (backdrop.bounds.max.y > collected.FloorBounds.min.y)
            {
                errors.Add(scenePath + ": 背景面が床より奥にありません（背景の上面 "
                    + backdrop.bounds.max.y + " ／床の下面 " + collected.FloorBounds.min.y
                    + "）。地形が読み込まれたら自然に隠れる構成にする（§7.3）。");
            }

            // <b>遠景の濃淡</b>（工程 P55-15d。裁定 2 の作業項目 4）。
            //
            // 背景面は<b>暗くはなかった</b>（実測で実地形の 0.77／0.90）。足りなかったのは
            // <b>散らばり</b>で、標準偏差 2.4e-05 ——完全に均一な一枚板だった。
            // そこへ 2 色の板を並べて濃淡を作っている。
            //
            // ここで見るのは 2 つ。<b>消えたことに気付けること</b>と、
            // <b>床の下に収まっていること</b>——上へ出ると、隣 Area が読み込まれたときに
            // <b>隣の床を突き抜けて</b>見える。
            int patchCount = 0;
            float patchTop = float.MinValue;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.name != "BackdropPatch")
                {
                    continue;
                }

                patchCount++;
                patchTop = Mathf.Max(patchTop, renderer.bounds.max.y);
                if (renderer.GetComponent<Collider>() != null)
                {
                    errors.Add(scenePath + ": 遠景の濃淡（BackdropPatch）が Collider を持っています"
                        + "（背景面と同じく移動・NavMesh・AI に関与させない。§7.3）。");
                }
            }

            if (patchCount == 0)
            {
                errors.Add(scenePath + ": 遠景の濃淡（BackdropPatch）が見つかりません"
                    + "（§7.3 の背景の補完。均一な一枚板は「描画不良に見えない表現」の"
                    + "受入を満たさない。付録 C.43）。");
            }
            else if (patchTop > collected.FloorBounds.min.y)
            {
                errors.Add(scenePath + ": 遠景の濃淡が床より上へ出ています（濃淡の上面 "
                    + patchTop + " ／床の下面 " + collected.FloorBounds.min.y
                    + "）。隣 Area の床を突き抜けて見える（付録 C.43）。");
            }

            float marginX = backdrop.bounds.extents.x - collected.FloorBounds.extents.x;
            float marginZ = backdrop.bounds.extents.z - collected.FloorBounds.extents.z;
            float required = Phase5Layout.BackdropMargin - 0.5f;
            if (marginX < required || marginZ < required)
            {
                errors.Add(scenePath + ": 背景面が地形の外側へ十分はみ出していません（x+"
                    + marginX + " z+" + marginZ + " ／必要 " + required
                    + "）。カメラが端へ寄ったときに虚空が出る（§7.3）。");
            }

            // 外周壁。接続口の判定に使う（同じ理由で Renderer から採る）。
            //
            // <b>4 面すべてを採る。</b> 以前は東西の 2 面だけ採っていたので、
            // 南北の接続口を塞いだままでも検査が通ってしまった（配置を足して初めて分かる穴）。
            // 仕切り（Wall_Divider）と衝立（Wall_Screen）は外周ではないので入らない。
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                if (IsOuterWall(renderer.name))
                {
                    collected.SideWalls.Add(renderer.bounds);
                }
            }

            // 門の見た目の箱と Interact の錨。<b>遮蔽判定は球を飛ばす</b>ので、
            // 錨のすぐ向こうに門があると錨の手前からでも遮蔽になる（下の検査）。
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer != null && renderer.GetComponentInParent<AreaFlagDoor>() != null)
                {
                    collected.FlagDoors.Add(renderer.bounds);
                }
            }

            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is IAreaInteractable interactable && interactable.InteractableId.IsValid)
                {
                    collected.InteractionAnchors[interactable.InteractableId.Value] =
                        interactable.InteractionAnchor;
                }
            }

            foreach (AreaEntryPoint entry in root.EntryPoints)
            {
                if (entry != null && entry.EntryId.IsValid)
                {
                    collected.Entries[entry.EntryId.Value] = entry.ArrivalPosition;
                }
            }

            foreach (AreaExitGate gate in root.ExitGates)
            {
                if (gate != null && gate.ExitId.IsValid)
                {
                    collected.Exits[gate.ExitId.Value] = gate.transform.position;
                }
            }

            // <b>接続口をふさぐ見えない境界</b>（工程 P55-14b。§7.3。試遊報告③）。
            //
            // 外周壁は開口ぶん空いているので、当たりが無いと
            // <b>出口判定が成立しないまま Area の外へ出られる</b>。
            // 見た目を持たせてはいけない——壁を消した理由（スライド中に画面を覆う）へ逆戻りする。
            List<Momotaro.Gameplay.Session.AreaSeamBarrier> barriers =
                Phase5ExplorationValidator.Components<Momotaro.Gameplay.Session.AreaSeamBarrier>(scene);
            if (barriers.Count == 0)
            {
                errors.Add("接続口の見えない境界（AreaSeamBarrier）がありません（§7.3。工程 P55-14b）。"
                           + "Scene を作り直してください。");
            }

            for (int i = 0; i < barriers.Count; i++)
            {
                Momotaro.Gameplay.Session.AreaSeamBarrier barrier = barriers[i];
                if (!barrier.IsWired)
                {
                    errors.Add("接続口の境界が未配線です（Collider か接続 ID が無い）: " + barrier.name);
                    continue;
                }

                if (!barrier.IsInvisible)
                {
                    errors.Add("接続口の境界が見た目を持っています（" + barrier.name
                               + "）。スライド中に画面を覆うので Renderer は持たせません。");
                }

                if (barrier.Blocker.isTrigger)
                {
                    errors.Add("接続口の境界が Trigger になっています（" + barrier.name
                               + "）。通行を止められません。");
                }
            }

            List<AreaCameraRegionSet> sets =
                Phase5ExplorationValidator.Components<AreaCameraRegionSet>(scene);
            if (sets.Count != 1)
            {
                errors.Add(scenePath + ": カメラの領域集合が 1 つではありません。");
                return false;
            }

            // 領域は<b>値で</b>取り出す（Scene を閉じたあとに参照は使えない）。
            AreaCameraRegionSet set = sets[0];
            if (set.DefaultRegion == null)
            {
                errors.Add(scenePath + ": 既定のカメラ領域が未配線です。");
                return false;
            }

            collected.DefaultRegion = set.DefaultRegion.Definition;
            for (int i = 0; i < set.Regions.Count; i++)
            {
                if (set.Regions[i] != null)
                {
                    collected.Regions.Add(set.Regions[i].Definition);
                }
            }

            // 初期化担当が渡す Data。<b>配置ごとに正しいものを指しているか</b>を見る。
            List<Momotaro.Infrastructure.World.AreaInitializer> initializers =
                Phase5ExplorationValidator.Components<Momotaro.Infrastructure.World.AreaInitializer>(scene);
            if (initializers.Count != 1)
            {
                errors.Add(scenePath + ": エリア初期化担当が 1 つではありません。");
                return false;
            }

            collected.CatalogPath = initializers[0].Catalog != null
                ? AssetDatabase.GetAssetPath(initializers[0].Catalog) : null;
            collected.ConnectionPath = initializers[0].Connections != null
                ? AssetDatabase.GetAssetPath(initializers[0].Connections) : null;

            facts = collected;
            return true;
        }

        private static Bounds Encapsulated(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        /// <summary>外周壁の名前か（仕切り・衝立は外周ではない）。</summary>
        private static bool IsOuterWall(string name) =>
            name.StartsWith("Wall_North") || name.StartsWith("Wall_South")
            || name.StartsWith("Wall_East") || name.StartsWith("Wall_West");

        // ---------------------------------------------------------------- 境界（§3.2）

        /// <summary>
        /// A の進行方向の端と B の反対側の端が<b>同じ座標で突き合わさっている</b>こと（§3.2）。
        ///
        /// 隙間があれば床の穴になり、重なればちらつく。床高も一致していること。
        /// <b>軸は配置が持つ</b>——東西なら X、南北なら Z を見る。
        /// </summary>
        private static void ValidateSeam(
            Phase55Arrangement r, AreaFacts a, AreaFacts b, List<string> errors)
        {
            float aEdge = r.ForwardEdge(a.FloorBounds);
            float bEdge = r.BackwardEdge(b.FloorBounds);

            if (Mathf.Abs(aEdge - bEdge) > Tolerance)
            {
                errors.Add("A の端（" + r.AlongName + "=" + aEdge + "）と B の端（"
                    + r.AlongName + "=" + bEdge
                    + "）が一致しません。隙間なら床の穴、重なりならちらつきになります（§3.2）。");
            }

            if (Mathf.Abs(a.FloorTop - FloorTopY) > Tolerance
                || Mathf.Abs(b.FloorTop - FloorTopY) > Tolerance)
            {
                errors.Add("床の上面 Y が一致しません（A=" + a.FloorTop + " B=" + b.FloorTop
                    + "。期待 " + FloorTopY + "）。段差のある接続は作らない（§3.2）。");
            }

            // 直交軸の重なりが通路幅を満たすこと。片側が浅いと、通路の端が虚空へ出る。
            float overlapMin = Mathf.Max(r.AcrossMin(a.FloorBounds), r.AcrossMin(b.FloorBounds));
            float overlapMax = Mathf.Min(r.AcrossMax(a.FloorBounds), r.AcrossMax(b.FloorBounds));
            float passageMin = r.PassageCenter - r.PassageWidth * 0.5f;
            float passageMax = r.PassageCenter + r.PassageWidth * 0.5f;

            if (overlapMin > passageMin + Tolerance || overlapMax < passageMax - Tolerance)
            {
                errors.Add("接続通路（" + r.AcrossName + " " + passageMin + "〜" + passageMax
                    + "）が両 Area の床に収まっていません（重なり " + r.AcrossName + " "
                    + overlapMin + "〜" + overlapMax + "）。");
            }
        }

        // ---------------------------------------------------------------- 通路（§7.3）

        /// <summary>
        /// 境界側の外周壁が<b>通路の区間だけ開いている</b>こと（§7.3）。
        ///
        /// スライド中は両 Area が描かれているので、境界に壁が立っていると
        /// <b>画面を覆う壁</b>として見える。Collider だけ通す形は使わない。
        /// </summary>
        private static void ValidatePassageIsOpen(
            Phase55Arrangement r, AreaFacts facts, bool forward, List<string> errors)
        {
            float seam = forward ? r.ForwardEdge(facts.FloorBounds) : r.BackwardEdge(facts.FloorBounds);
            float passageMin = r.PassageCenter - r.PassageWidth * 0.5f;
            float passageMax = r.PassageCenter + r.PassageWidth * 0.5f;

            for (int i = 0; i < facts.SideWalls.Count; i++)
            {
                Bounds wall = facts.SideWalls[i];

                // 境界にある壁だけを見る（反対側・直交する外周壁は関係ない）。
                if (Mathf.Abs(r.Along(wall.center) - seam) > 1f)
                {
                    continue;
                }

                if (r.AcrossMax(wall) > passageMin + Tolerance
                    && r.AcrossMin(wall) < passageMax - Tolerance)
                {
                    errors.Add(facts.ScenePath + ": 接続通路（" + r.AcrossName + " " + passageMin
                        + "〜" + passageMax + "）に外周壁が残っています（壁 " + r.AcrossName + " "
                        + r.AcrossMin(wall) + "〜" + r.AcrossMax(wall)
                        + "）。境界の区間は通路として開いていること（§7.3）。");
                }
            }
        }

        // ---------------------------------------------------------------- 並び（§3.2）

        /// <summary>
        /// 出入口の受理位置 → 境界 → 到着位置が<b>進行方向へ並ぶ</b>こと（§3.2）。
        ///
        /// 並んでいないと、到着してから戻る動きや長距離の自動歩行が必要になる。
        /// <b>「進行方向」は配置が持つ符号で決める</b>——東なら +X、北なら +Z。
        /// </summary>
        private static void ValidateTravelOrder(
            Phase55Arrangement r, AreaFacts a, AreaFacts b, List<string> errors)
        {
            float seam = r.ForwardEdge(a.FloorBounds);
            float sign = r.Forward;

            if (TryGet(a.Exits, r.ExitFromA, out Vector3 exitOut, a, "出入口", errors)
                && TryGet(b.Entries, r.EntryInB, out Vector3 arrivalInB, b, "入口", errors))
            {
                AssertOrder(r, sign, r.Along(exitOut), seam, r.Along(arrivalInB),
                    "進む", errors);
            }

            if (TryGet(b.Exits, r.ExitFromB, out Vector3 exitBack, b, "出入口", errors)
                && TryGet(a.Entries, r.EntryInA, out Vector3 arrivalInA, a, "入口", errors))
            {
                AssertOrder(r, -sign, r.Along(exitBack), seam, r.Along(arrivalInA),
                    "戻る", errors);
            }
        }

        /// <summary>出入口 → 境界 → 到着が、その向き（<paramref name="sign"/>）へ並んでいること。</summary>
        private static void AssertOrder(
            Phase55Arrangement r, float sign, float exit, float seam, float arrival,
            string label, List<string> errors)
        {
            bool ordered = (seam - exit) * sign > Tolerance && (arrival - seam) * sign > Tolerance;
            if (!ordered)
            {
                errors.Add(label + "並びが崩れています（出入口 " + r.AlongName + "=" + exit
                    + " → 境界 " + r.AlongName + "=" + seam
                    + " → 到着 " + r.AlongName + "=" + arrival
                    + "。進行の符号 " + sign + "。§3.2）。");
            }
        }

        // ---------------------------------------------------------------- カメラ（§7.1）

        /// <summary>
        /// 接続では<b>Camera の移動が接続の帯に収まる</b>こと（§7.1 改定。工程 P55-14d）。
        ///
        /// <b>入口の中心だけを比べては足りない</b>（GPT レビュー R14 の指摘 2）。
        /// 出入口には幅があるので、通路の端から入ることができる。
        /// 同一 Area 内は連続追従になったので、通路の端から入ると
        /// <b>Camera の基準位置も接続軸から外れる</b>——それは正常である。
        ///
        /// 見るのは 3 つ：
        /// <list type="number">
        /// <item>通路の幅いっぱいに刻んだ進入位置で、Camera が<b>帯の内側</b>に居ること。</item>
        /// <item><b>到着時の Camera は接続軸に乗っている</b>こと（誤差許容だけ。ここは緩めない）。
        /// 入口が通路の中心からずれた配置は、ここで落ちる。</item>
        /// <item>両側の Camera が<b>接続方向へ動く</b>こと。</item>
        /// </list>
        ///
        /// <b>帯の定義は <see cref="AreaConnectionRules"/> が正本</b>で、
        /// 実行時（<c>AreaSlideTransitionRunner</c>）も同じ関数を使う。
        /// </summary>
        private static void ValidateCameraAxis(
            Phase55Arrangement r, AreaFacts a, AreaFacts b, List<string> errors)
        {
            float seam = r.ForwardEdge(a.FloorBounds);
            float axis = r.PassageCenter;
            float half = r.PassageWidth * 0.5f;
            float sign = r.Forward;

            // 通路の中心と両端（少し内側）から、境界へ向かって近づく道のり。
            float[] acrosses = { axis, axis + half - 0.5f, axis - half + 0.5f };
            float[] aAlongs = { seam - 0.5f * sign, seam - 1.5f * sign, seam - 2.5f * sign };
            float[] bAlongs = { seam + 0.5f * sign, seam + 1.5f * sign, seam + 2.5f * sign };

            AssertApproachStaysInsideBand(r, a, "A", acrosses, aAlongs, axis, errors);
            AssertApproachStaysInsideBand(r, b, "B", acrosses, bAlongs, axis, errors);

            if (!TryFocus(a, r.EntryInA, out Vector3 focusInA, errors)
                || !TryFocus(b, r.EntryInB, out Vector3 focusInB, errors))
            {
                return;
            }

            // <b>到着時は接続軸に乗っている</b>——ここは誤差許容だけで、帯へは緩めない。
            // 帯が守るのは「通路のどこから入ってもよい」であって、
            // 「入口がどこにあってもよい」ではない（GPT 裁定 (a) の注意）。
            if (Mathf.Abs(r.Across(focusInA) - axis) > AreaConnectionRules.AxisEpsilon
                || Mathf.Abs(r.Across(focusInB) - axis) > AreaConnectionRules.AxisEpsilon)
            {
                errors.Add("到着時のカメラが接続軸に乗っていません（A 側 " + r.AcrossName + "="
                    + r.Across(focusInA) + " B 側 " + r.AcrossName + "=" + r.Across(focusInB)
                    + " 軸 " + r.AcrossName + "=" + axis + "。§7.1）。");
            }

            if (!AreaConnectionRules.MovesAlongConnection(
                    r.Along(focusInA) - r.Along(focusInB)))
            {
                errors.Add("カメラが接続軸へ動きません（両側 " + r.AlongName + "≒"
                    + r.Along(focusInA) + "）。スライドする意味が無い配置です（§7.1）。");
            }
        }

        /// <summary>
        /// 境界へ近づく道のりのどこからでも、カメラが<b>接続の帯の内側</b>に居ること
        /// （§7.1 改定。工程 P55-14d）。
        ///
        /// <b>帯からはみ出す配置は、通路の外まで追従が効いている</b>ことを意味する——
        /// 通路の脇に立ってカメラだけ通路の外を映している状態で、スライドが始まってしまう。
        /// </summary>
        private static void AssertApproachStaysInsideBand(
            Phase55Arrangement r, AreaFacts facts, string label,
            float[] acrosses, float[] alongs, float axis, List<string> errors)
        {
            for (int i = 0; i < alongs.Length; i++)
            {
                for (int j = 0; j < acrosses.Length; j++)
                {
                    Vector3 at = r.Point(alongs[i], acrosses[j]);
                    Vector3 focus = FocusAt(facts, at);
                    float offset = r.Across(focus) - axis;
                    if (!AreaConnectionRules.IsWithinSlideBand(offset, r.PassageWidth))
                    {
                        errors.Add(label + " 側の進入位置 " + at + " でカメラの " + r.AcrossName
                            + " が接続の帯から外れます（カメラ " + r.AcrossName + "=" + r.Across(focus)
                            + " 軸 " + axis + " ずれ " + offset + "／"
                            + AreaConnectionRules.DescribeSlideBand(r.PassageWidth)
                            + "）。§7.1。");
                        return;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- 覆い（§7.3）

        /// <summary>
        /// スライドの<b>全区間</b>でカメラが見る範囲が両 Area の床で覆われること（§7.3）。
        ///
        /// 端の 2 枚だけ見ても足りない。途中のフレームで境界の手前が切れると、
        /// そこに黒い帯が出る。始点から終点までを刻んで、見える範囲が
        /// A∪B の床に収まっているかを確かめる。
        ///
        /// <b>接続軸と直交軸で条件が違う。</b> 接続軸は 2 つの床がつながっているので
        /// 両方の外側の端まで使えるが、<b>直交軸は重なっているぶんしか使えない</b>——
        /// 片方しか無い座標を映すと、そこが黒い帯になる。
        /// 南北配置で通路を部屋の端へ寄せられなかったのはこの条件のため。
        /// </summary>
        private static void ValidateSlideIsCovered(
            Phase55Arrangement r, AreaFacts a, AreaFacts b, List<string> errors)
        {
            if (!TryFocus(a, r.EntryInA, out Vector3 from, errors)
                || !TryFocus(b, r.EntryInB, out Vector3 to, errors))
            {
                return;
            }

            Vector2 half = HalfFootprint();
            float alongHalf = r.AlongHalf(half);
            float acrossHalf = r.AcrossHalf(half);

            float alongLow = Mathf.Min(r.AlongMin(a.FloorBounds), r.AlongMin(b.FloorBounds));
            float alongHigh = Mathf.Max(r.AlongMax(a.FloorBounds), r.AlongMax(b.FloorBounds));
            float acrossLow = Mathf.Max(r.AcrossMin(a.FloorBounds), r.AcrossMin(b.FloorBounds));
            float acrossHigh = Mathf.Min(r.AcrossMax(a.FloorBounds), r.AcrossMax(b.FloorBounds));

            // <b>始点は接続軸の上とは限らない</b>（§7.1 改定。工程 P55-14d）。
            // 同一 Area 内が連続追従になったので、通路の端から入れば
            // スライドは<b>帯の端から</b>始まる。覆いは<b>いちばん外から出発する経路</b>で見る。
            float band = AreaConnectionRules.SlideAcrossHalfWidth(r.PassageWidth);
            float[] startOffsets = { 0f, band, -band };

            const int steps = 16;
            foreach (float startOffset in startOffsets)
            {
                Vector3 start = from + Offset(r, startOffset);
                for (int i = 0; i <= steps; i++)
                {
                    Vector3 at = Vector3.Lerp(start, to, i / (float)steps);
                    float along = r.Along(at);
                    float across = r.Across(at);

                    if (along - alongHalf < alongLow - Tolerance
                        || along + alongHalf > alongHigh + Tolerance)
                    {
                        errors.Add("スライド途中（軸外の出発 " + startOffset + "・" + i + "/" + steps
                            + "・" + r.AlongName + "=" + along
                            + "）でカメラが床の外を映します（床 " + r.AlongName + " " + alongLow + "〜"
                            + alongHigh + "／見える半分 " + alongHalf + "）。黒い帯になります（§7.3）。");
                        return;
                    }

                    if (across - acrossHalf < acrossLow - Tolerance
                        || across + acrossHalf > acrossHigh + Tolerance)
                    {
                        errors.Add("スライド途中（軸外の出発 " + startOffset + "・" + i + "/" + steps
                            + "・" + r.AcrossName + "=" + across
                            + "）でカメラが床の外を映します（両 Area が重なる " + r.AcrossName + " "
                            + acrossLow + "〜" + acrossHigh + "／見える半分 " + acrossHalf
                            + "）。帯の端から出発しても覆えている必要があります（§7.3）。");
                        return;
                    }
                }
            }
        }

        /// <summary>接続軸と直交する向きのずらし。</summary>
        private static Vector3 Offset(Phase55Arrangement r, float across) =>
            r.Axis == Phase5SeamAxis.X ? new Vector3(0f, 0f, across) : new Vector3(across, 0f, 0f);

        /// <summary>その入口へ到着したときのカメラ基準位置（領域選択と clamp は実行時と同じ純粋関数）。</summary>
        private static bool TryFocus(
            AreaFacts facts, StableId entryId, out Vector3 focus, List<string> errors)
        {
            focus = default;
            if (!facts.Entries.TryGetValue(entryId.Value, out Vector3 arrival))
            {
                errors.Add(facts.ScenePath + ": 入口 '" + entryId.Value + "' がありません。");
                return false;
            }

            focus = FocusAt(facts, arrival);
            return true;
        }

        /// <summary>
        /// その位置を追従したときのカメラ基準位置。
        /// <b>領域選択も clamp も実行時と同じ純粋関数</b>を使う（別の期待値を作らない）。
        /// </summary>
        private static Vector3 FocusAt(AreaFacts facts, Vector3 worldPosition)
        {
            if (!CameraRegionSelector.TrySelect(
                    facts.Regions, worldPosition, out CameraRegionDefinition region))
            {
                region = facts.DefaultRegion;
            }

            return CameraBoundsMath.ClampFocus(worldPosition, region, HalfFootprint());
        }

        private static Vector2 HalfFootprint() => CameraBoundsMath.HalfFootprint(
            Phase5Layout.CameraOrthographicSize, TrialAspect, Phase5Layout.CameraPitchDegrees);

        private static bool TryGet(
            Dictionary<string, Vector3> map, StableId id, out Vector3 position,
            AreaFacts facts, string label, List<string> errors)
        {
            if (map.TryGetValue(id.Value, out position))
            {
                return true;
            }

            errors.Add(facts.ScenePath + ": " + label + " '" + id.Value + "' がありません。");
            position = default;
            return false;
        }

        // ---------------------------------------------------------------- 接続 Data（§3.1）

        /// <summary>
        /// 接続レコードが<b>Scene の出入口と噛み合っている</b>こと（§3.1）。
        ///
        /// Data 側の整合（逆方向の対・向きの反転）は <c>AreaConnectionData.Validate</c> が見る。
        /// ここが見るのは「その ExitId が本当に Scene に居るか」——Data だけ直して
        /// Scene を直し忘れると、実行時に押しても何も起きない。
        /// </summary>
        private static void ValidateConnections(
            Phase55Arrangement r, AreaFacts a, AreaFacts b, List<string> errors)
        {
            var data = AssetDatabase.LoadAssetAtPath<AreaConnectionData>(r.ConnectionDataPath);
            if (data == null)
            {
                errors.Add("接続一覧がありません: " + r.ConnectionDataPath);
                return;
            }

            if (data.Connections.Count != 2)
            {
                errors.Add("往復は 2 レコードで表します（いまは " + data.Connections.Count + " 件。§3.1）。");
            }

            foreach (AreaConnectionDefinition c in data.Connections)
            {
                if (c == null)
                {
                    continue;
                }

                if (c.Style != AreaTransitionStyle.Slide)
                {
                    errors.Add("接続 " + c.ConnectionId.Value + " が Slide ではありません（§3.1）。");
                }

                // <b>向きが「実際に置かれた床の並び」と合っていること。</b>
                //
                // 期待値を配置の設定から採ってはいけない——生成器も検査も同じ値を読むので、
                // <b>設定を書き換えると両方まとめてずれて通ってしまう</b>（注入 46 で実際に通った）。
                // Data だけ東西のまま残しても Scene は成立してしまい、
                // 実行時に「軸不一致」で初めて断られる（P03 はそこで落ちた）。
                // ここは Scene から採った床の中心どうしの向きを期待値にする。
                bool isForward = c.ConnectionId.Equals(r.ConnectionAToB);
                bool isBackward = c.ConnectionId.Equals(r.ConnectionBToA);
                if (!isForward && !isBackward)
                {
                    errors.Add("接続 " + c.ConnectionId.Value + " はこの配置の接続ではありません。");
                }
                else
                {
                    AreaConnectionDirection expected = isForward
                        ? DirectionFromFloors(a, b)
                        : DirectionFromFloors(b, a);
                    if (c.Direction != expected)
                    {
                        errors.Add("接続 " + c.ConnectionId.Value + " の向きが実際の並びと違います（実際="
                            + c.Direction + " 床の並び=" + expected + "。§3.1）。");
                    }
                }

                AreaFacts owner = c.FromAreaId.Equals(r.AreaAId) ? a
                    : c.FromAreaId.Equals(r.AreaBId) ? b : null;
                if (owner == null)
                {
                    errors.Add("接続 " + c.ConnectionId.Value + " の出発エリア '"
                        + c.FromAreaId.Value + "' がこの配置にありません。");
                    continue;
                }

                if (!owner.Exits.ContainsKey(c.ExitId.Value))
                {
                    errors.Add("接続 " + c.ConnectionId.Value + " の出入口 '" + c.ExitId.Value
                        + "' が " + owner.ScenePath + " にありません（Data だけ直して Scene を直し忘れている）。");
                }

                AreaFacts destination = c.ToAreaId.Equals(r.AreaAId) ? a
                    : c.ToAreaId.Equals(r.AreaBId) ? b : null;
                if (destination != null && !destination.Entries.ContainsKey(c.EntryId.Value))
                {
                    errors.Add("接続 " + c.ConnectionId.Value + " の入口 '" + c.EntryId.Value
                        + "' が " + destination.ScenePath + " にありません。");
                }
            }
        }

        /// <summary>
        /// 床の中心どうしの向き（<b>生成物から採る期待値</b>）。
        ///
        /// 斜めに置かれていたら、そもそも 4 方向では表せない——大きいほうの軸で決めて、
        /// もう一方が同じくらい大きければ <c>None</c> を返し、必ず不一致として挙がるようにする。
        /// </summary>
        private static AreaConnectionDirection DirectionFromFloors(AreaFacts from, AreaFacts to)
        {
            Vector3 delta = to.FloorBounds.center - from.FloorBounds.center;
            float ax = Mathf.Abs(delta.x);
            float az = Mathf.Abs(delta.z);

            if (Mathf.Approximately(ax, az) || Mathf.Max(ax, az) < Tolerance)
            {
                return AreaConnectionDirection.None;
            }

            if (ax > az)
            {
                return delta.x > 0f ? AreaConnectionDirection.East : AreaConnectionDirection.West;
            }

            return delta.z > 0f ? AreaConnectionDirection.North : AreaConnectionDirection.South;
        }
        /// <summary>
        /// Interact の錨が<b>門から十分離れている</b>こと。
        ///
        /// 遮蔽判定は <c>PhysicsObstacleProbe</c> が<b>半径 0.25m の球</b>を飛ばす。
        /// 錨のすぐ向こうに壁があると、<b>錨の手前から調べても</b>球が壁を掠めて
        /// 「遮蔽あり」になり、調査できない。
        ///
        /// <b>門だけを見る。</b> 外周壁や衝立は「壁越しでは調べられない」を作るために
        /// わざと近くへ置いてあり（<c>point_p5_a_blocked</c>）、それは仕様である。
        /// 門は配置ごとに位置が変わるので、配置を足したときに詰まりやすい——
        /// 南北配置では 0.2m しか空いておらず、<b>2 回まぐれで通ったあとに落ちた</b>（記録 027）。
        /// </summary>
        private static void ValidateInteractionClearance(AreaFacts facts, List<string> errors)
        {
            if (facts.FlagDoors.Count == 0 || facts.InteractionAnchors.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, Vector3> anchor in facts.InteractionAnchors)
            {
                for (int i = 0; i < facts.FlagDoors.Count; i++)
                {
                    Bounds door = facts.FlagDoors[i];
                    Vector3 flatAnchor = new Vector3(anchor.Value.x, door.center.y, anchor.Value.z);
                    float gap = Vector3.Distance(door.ClosestPoint(flatAnchor), flatAnchor);
                    if (gap < RequiredInteractionClearance)
                    {
                        errors.Add(facts.ScenePath + ": Interact の錨 '" + anchor.Key
                            + "' が門に近すぎます（隙間 " + gap + "m／必要 "
                            + RequiredInteractionClearance + "m）。遮蔽判定は半径 "
                            + InteractionProbeRadius + "m の球なので、"
                            + "錨の向こう側の壁でも手前からの調査が塞がれます。");
                        break;
                    }
                }
            }
        }
    }
}
