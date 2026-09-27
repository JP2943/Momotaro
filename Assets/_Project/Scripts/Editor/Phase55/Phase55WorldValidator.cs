using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
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
        /// A・B を<b>同時に</b>開いて配置と接続を検査する。
        ///
        /// <b>Additive で 2 枚載せる。</b> 1 枚ずつ Single で開くと、2 枚目を開いた時点で
        /// 1 枚目の部品が破棄され、比較に使えるのは値写しだけになる。実行時も 2 枚同時に
        /// 載る構成なので（§4.1）、検査も同じ載せ方にしておくほうが実態に近い。
        /// </summary>
        public static void Validate(List<string> errors, List<string> warnings)
        {
            if (!TryOpenBoth(errors, out Scene sceneA, out Scene sceneB))
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

                Compare(a, b, errors);
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
        public static bool TryOpenBoth(List<string> errors, out Scene sceneA, out Scene sceneB)
        {
            sceneA = default;
            sceneB = default;

            foreach (string path in new[] { Phase55WorldIds.AreaAScenePath, Phase55WorldIds.AreaBScenePath })
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(path) == null)
                {
                    errors.Add("Scene がありません: " + path + "（build-phase55-world を先に実行する）。");
                    return false;
                }
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            sceneA = EditorSceneManager.OpenScene(Phase55WorldIds.AreaAScenePath, OpenSceneMode.Additive);
            sceneB = EditorSceneManager.OpenScene(Phase55WorldIds.AreaBScenePath, OpenSceneMode.Additive);
            return true;
        }

        /// <summary>2 つの Area の<b>関係</b>を見る（1 枚ずつでは言えないもの）。</summary>
        public static void Compare(AreaFacts a, AreaFacts b, List<string> errors)
        {
            ValidateSeam(a, b, errors);
            ValidatePassageIsOpen(a, Phase5SeamSide.East, errors);
            ValidatePassageIsOpen(b, Phase5SeamSide.West, errors);
            ValidateTravelOrder(a, b, errors);
            ValidateCameraAxis(a, b, errors);
            ValidateSlideIsCovered(a, b, errors);
            ValidateConnections(a, b, errors);
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

            // 東西の外周壁。接続口の判定に使う（同じ理由で Renderer から採る）。
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                if (renderer.name.StartsWith("Wall_East") || renderer.name.StartsWith("Wall_West"))
                {
                    collected.SideWalls.Add(renderer.bounds);
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

            facts = collected;
            return true;
        }

        private static Bounds Encapsulated(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        // ---------------------------------------------------------------- 境界（§3.2）

        /// <summary>
        /// A の東端と B の西端が<b>同じ X で突き合わさっている</b>こと（§3.2）。
        ///
        /// 隙間があれば床の穴になり、重なればちらつく。床高も一致していること。
        /// </summary>
        private static void ValidateSeam(AreaFacts a, AreaFacts b, List<string> errors)
        {
            float aEast = a.FloorBounds.max.x;
            float bWest = b.FloorBounds.min.x;

            if (Mathf.Abs(aEast - bWest) > Tolerance)
            {
                errors.Add("A の東端（x=" + aEast + "）と B の西端（x=" + bWest
                    + "）が一致しません。隙間なら床の穴、重なりならちらつきになります（§3.2）。");
            }

            if (Mathf.Abs(a.FloorTop - FloorTopY) > Tolerance
                || Mathf.Abs(b.FloorTop - FloorTopY) > Tolerance)
            {
                errors.Add("床の上面 Y が一致しません（A=" + a.FloorTop + " B=" + b.FloorTop
                    + "。期待 " + FloorTopY + "）。段差のある接続は作らない（§3.2）。");
            }

            // Z の重なりが通路幅を満たすこと。片側が浅いと、通路の端が虚空へ出る。
            float overlapMin = Mathf.Max(a.FloorBounds.min.z, b.FloorBounds.min.z);
            float overlapMax = Mathf.Min(a.FloorBounds.max.z, b.FloorBounds.max.z);
            float passageMin = Phase55WorldLayout.SeamZ - Phase55WorldLayout.PassageWidth * 0.5f;
            float passageMax = Phase55WorldLayout.SeamZ + Phase55WorldLayout.PassageWidth * 0.5f;

            if (overlapMin > passageMin + Tolerance || overlapMax < passageMax - Tolerance)
            {
                errors.Add("接続通路（z " + passageMin + "〜" + passageMax
                    + "）が両 Area の床に収まっていません（重なり z " + overlapMin + "〜" + overlapMax + "）。");
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
            AreaFacts facts, Phase5SeamSide side, List<string> errors)
        {
            float seamX = side == Phase5SeamSide.East ? facts.FloorBounds.max.x : facts.FloorBounds.min.x;
            float passageMin = Phase55WorldLayout.SeamZ - Phase55WorldLayout.PassageWidth * 0.5f;
            float passageMax = Phase55WorldLayout.SeamZ + Phase55WorldLayout.PassageWidth * 0.5f;

            for (int i = 0; i < facts.SideWalls.Count; i++)
            {
                Bounds wall = facts.SideWalls[i];

                // 境界にある壁だけを見る（反対側の外周壁は関係ない）。
                if (Mathf.Abs(wall.center.x - seamX) > 1f)
                {
                    continue;
                }

                if (wall.max.z > passageMin + Tolerance && wall.min.z < passageMax - Tolerance)
                {
                    errors.Add(facts.ScenePath + ": 接続通路（z " + passageMin + "〜" + passageMax
                        + "）に外周壁が残っています（壁 z " + wall.min.z + "〜" + wall.max.z
                        + "）。境界の区間は通路として開いていること（§7.3）。");
                }
            }
        }

        // ---------------------------------------------------------------- 並び（§3.2）

        /// <summary>
        /// 出入口の受理位置 → 境界 → 到着位置が<b>進行方向へ並ぶ</b>こと（§3.2）。
        ///
        /// 並んでいないと、到着してから戻る動きや長距離の自動歩行が必要になる。
        /// </summary>
        private static void ValidateTravelOrder(AreaFacts a, AreaFacts b, List<string> errors)
        {
            float seamX = a.FloorBounds.max.x;

            if (TryGet(a.Exits, Phase55WorldIds.ExitAEast, out Vector3 exitEast, a, "出入口", errors)
                && TryGet(b.Entries, Phase5AreaIds.AreaBFromA, out Vector3 arrivalInB, b, "入口", errors))
            {
                if (!(exitEast.x < seamX - Tolerance && seamX < arrivalInB.x - Tolerance))
                {
                    errors.Add("東へ進む並びが崩れています（出入口 x=" + exitEast.x
                        + " → 境界 x=" + seamX + " → 到着 x=" + arrivalInB.x + "。§3.2）。");
                }
            }

            if (TryGet(b.Exits, Phase55WorldIds.ExitBWest, out Vector3 exitWest, b, "出入口", errors)
                && TryGet(a.Entries, Phase5AreaIds.AreaAFromB, out Vector3 arrivalInA, a, "入口", errors))
            {
                if (!(exitWest.x > seamX + Tolerance && seamX > arrivalInA.x + Tolerance))
                {
                    errors.Add("西へ戻る並びが崩れています（出入口 x=" + exitWest.x
                        + " → 境界 x=" + seamX + " → 到着 x=" + arrivalInA.x + "。§3.2）。");
                }
            }
        }

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

        // ---------------------------------------------------------------- カメラ（§7.1）

        /// <summary>
        /// 東西の接続では<b>Camera の移動が X だけ</b>であること（§7.1）。
        ///
        /// 両 Area の領域は見える範囲より深いので、clamp の結果は領域の中心へ寄る。
        /// 中心 Z の違う領域どうしを繋ぐと、スライドが接続軸以外（Z）へもずれる——
        /// 「接続軸以外にずれる設計は Validator で不合格」。
        /// </summary>
        private static void ValidateCameraAxis(AreaFacts a, AreaFacts b, List<string> errors)
        {
            if (!TryFocus(a, Phase5AreaIds.AreaAFromB, out Vector3 focusInA, errors)
                || !TryFocus(b, Phase5AreaIds.AreaBFromA, out Vector3 focusInB, errors))
            {
                return;
            }

            if (Mathf.Abs(focusInA.z - focusInB.z) > 0.05f)
            {
                errors.Add("東西の接続なのにカメラの Z が動きます（A 側 z=" + focusInA.z
                    + " B 側 z=" + focusInB.z + "）。接続軸以外にずれる設計は不合格（§7.1）。");
            }

            if (Mathf.Abs(focusInA.x - focusInB.x) < 0.5f)
            {
                errors.Add("カメラが X へ動きません（両側 x≈" + focusInA.x
                    + "）。スライドする意味が無い配置です（§7.1）。");
            }
        }

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

            // 領域選択も clamp も<b>実行時と同じ純粋関数</b>を使う（別の期待値を作らない）。
            if (!CameraRegionSelector.TrySelect(facts.Regions, arrival, out CameraRegionDefinition region))
            {
                region = facts.DefaultRegion;
            }

            focus = CameraBoundsMath.ClampFocus(arrival, region, HalfFootprint());
            return true;
        }

        private static Vector2 HalfFootprint() => CameraBoundsMath.HalfFootprint(
            Phase5Layout.CameraOrthographicSize, TrialAspect, Phase5Layout.CameraPitchDegrees);

        // ---------------------------------------------------------------- 覆い（§7.3）

        /// <summary>
        /// スライドの<b>全区間</b>でカメラが見る範囲が両 Area の床で覆われること（§7.3）。
        ///
        /// 端の 2 枚だけ見ても足りない。途中のフレームで境界の手前が切れると、
        /// そこに黒い帯が出る。始点から終点までを刻んで、見える X 範囲が
        /// A∪B の床に収まっているかを確かめる。
        /// </summary>
        private static void ValidateSlideIsCovered(AreaFacts a, AreaFacts b, List<string> errors)
        {
            if (!TryFocus(a, Phase5AreaIds.AreaAFromB, out Vector3 from, errors)
                || !TryFocus(b, Phase5AreaIds.AreaBFromA, out Vector3 to, errors))
            {
                return;
            }

            Vector2 half = HalfFootprint();
            float westLimit = Mathf.Min(a.FloorBounds.min.x, b.FloorBounds.min.x);
            float eastLimit = Mathf.Max(a.FloorBounds.max.x, b.FloorBounds.max.x);
            float southLimit = Mathf.Max(a.FloorBounds.min.z, b.FloorBounds.min.z);
            float northLimit = Mathf.Min(a.FloorBounds.max.z, b.FloorBounds.max.z);

            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 at = Vector3.Lerp(from, to, i / (float)steps);
                if (at.x - half.x < westLimit - Tolerance || at.x + half.x > eastLimit + Tolerance)
                {
                    errors.Add("スライド途中（" + i + "/" + steps + "・x=" + at.x
                        + "）でカメラが床の外を映します（床 x " + westLimit + "〜" + eastLimit
                        + "／見える半幅 " + half.x + "）。黒い帯になります（§7.3）。");
                    return;
                }

                if (at.z - half.y < southLimit - Tolerance || at.z + half.y > northLimit + Tolerance)
                {
                    errors.Add("スライド途中（" + i + "/" + steps + "・z=" + at.z
                        + "）でカメラが床の外を映します（両 Area が重なる z " + southLimit
                        + "〜" + northLimit + "／見える半奥行 " + half.y + "）。");
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- 接続 Data（§3.1）

        /// <summary>
        /// 接続レコードが<b>Scene の出入口と噛み合っている</b>こと（§3.1）。
        ///
        /// Data 側の整合（逆方向の対・向きの反転）は <c>AreaConnectionData.Validate</c> が見る。
        /// ここが見るのは「その ExitId が本当に Scene に居るか」——Data だけ直して
        /// Scene を直し忘れると、実行時に押しても何も起きない。
        /// </summary>
        private static void ValidateConnections(AreaFacts a, AreaFacts b, List<string> errors)
        {
            var data = AssetDatabase.LoadAssetAtPath<AreaConnectionData>(
                Phase55WorldIds.ConnectionDataPath);
            if (data == null)
            {
                errors.Add("接続一覧がありません: " + Phase55WorldIds.ConnectionDataPath);
                return;
            }

            if (data.Connections.Count != 2)
            {
                errors.Add("東西の往復は 2 レコードで表します（いまは " + data.Connections.Count + " 件。§3.1）。");
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

                AreaFacts owner = c.FromAreaId.Equals(Phase55WorldIds.AreaA) ? a
                    : c.FromAreaId.Equals(Phase55WorldIds.AreaB) ? b : null;
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

                AreaFacts destination = c.ToAreaId.Equals(Phase55WorldIds.AreaA) ? a
                    : c.ToAreaId.Equals(Phase55WorldIds.AreaB) ? b : null;
                if (destination != null && !destination.Entries.ContainsKey(c.EntryId.Value))
                {
                    errors.Add("接続 " + c.ConnectionId.Value + " の入口 '" + c.EntryId.Value
                        + "' が " + destination.ScenePath + " にありません。");
                }
            }
        }
    }
}
