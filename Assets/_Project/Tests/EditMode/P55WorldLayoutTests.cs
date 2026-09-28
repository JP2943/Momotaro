using System.Collections.Generic;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Editor.Phase55;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P5.5 の実ワールド配置（仕様書 §3.2／§7.1／§7.3。工程 P55-03c。§11 の V01／V02）。
    ///
    /// <b>生成物そのものを検査する。</b> Builder の定数を読み合わせる検査は、
    /// Builder と定数が同時に間違っていると通ってしまう。ここでは生成された Scene を開き、
    /// 見えている Renderer と Transform だけから境界・通路・並び・カメラ軸を確かめる。
    ///
    /// <b>壊して落ちることも見る</b>（V02）。配置をずらす・通路を塞ぐ・通路を領域の中心から
    /// 外す、の 3 つは検査が無ければ<b>見た目でしか気付けない</b>種類の壊れ方である。
    /// </summary>
    public sealed class P55WorldLayoutTests
    {
        [TearDown]
        public void TearDown()
        {
            // 壊したまま次へ進まない。保存はしないので、開き直せば出荷状態へ戻る。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ---------------------------------------------------------------- V01

        /// <summary>
        /// V01：生成された P5.5 の世界が接続検査を通る。
        ///
        /// 遭遇戦を置かない A の警告 1 件だけが出る（構成どおり）。警告を 0 件に固定しないのは、
        /// 「この Area に遭遇戦を置かない」は選択であって欠陥ではないため。
        /// </summary>
        [Test]
        public void TheGeneratedWorld_PassesTheConnectionValidator()
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Phase55WorldValidator.Validate(errors, warnings);

            Assert.IsEmpty(errors,
                "P5.5 の実ワールド配置が検査を通らない：\n- " + string.Join("\n- ", errors));
        }

        /// <summary>
        /// V01：<b>P5 の受入用生成物を汚さない</b>（§3.2「P5 の受入用 Scene を保存し、
        /// P5.5 の配置へ黙って作り替えない」）。
        ///
        /// 同じ AreaId を使い回すので、Area Data とカタログを共有すると
        /// <b>どちらの配置で動いているのかが実行時まで分からなくなる</b>。
        /// 別 Asset・別 ID・別 Scene パスであることをここで固定する。
        /// </summary>
        [Test]
        public void TheP5AndP55Catalogs_StayIndependent()
        {
            var p5 = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase5AreaIds.CatalogDataPath);
            var p55 = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(Phase55WorldIds.CatalogDataPath);
            Assert.IsNotNull(p5, "P5 のカタログがある。");
            Assert.IsNotNull(p55, "P5.5 のカタログがある。");
            Assert.AreNotSame(p5, p55, "同じ Asset を使い回していない。");
            Assert.AreNotEqual(p5.Id.Value, p55.Id.Value, "安定 ID も別（§3.2）。");

            AssertScenePaths(p5, "Assets/_Project/Scenes/Tests/Phase5/");
            AssertScenePaths(p55, "Assets/_Project/Scenes/Tests/Phase55/");

            // <b>南北配置も独立した生成物である</b>（工程 P55-05c）。配置を足したときに
            // カタログを共有すると、実行時にどちらの配置で動いているのか分からなくなる。
            var ns = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(
                Phase55NorthSouthIds.CatalogDataPath);
            Assert.IsNotNull(ns, "南北配置のカタログがある。");
            Assert.AreNotSame(p55, ns, "東西と南北で同じ Asset を使い回していない。");
            Assert.AreNotEqual(p55.Id.Value, ns.Id.Value, "安定 ID も別（付録 B.5）。");
            AssertScenePaths(ns, "Assets/_Project/Scenes/Tests/Phase55NS/");

            // Area Data も別 Asset。共有すると、後から片方の Scene パスを書き換えたときに
            // もう片方が黙って別の配置を指す。
            var areaAP5 = AssetDatabase.LoadAssetAtPath<AreaDefinition>(Phase5AreaIds.AreaADataPath);
            var areaAP55 = AssetDatabase.LoadAssetAtPath<AreaDefinition>(Phase55WorldIds.AreaADataPath);
            Assert.IsNotNull(areaAP5);
            Assert.IsNotNull(areaAP55);
            Assert.AreNotSame(areaAP5, areaAP55, "A の Data は配置ごとに別 Asset。");

            // <b>AreaId は配置ごとに別</b>（付録 B.5 の裁定）。Data Asset の安定 ID は
            // プロジェクト全体で一意という不変条件があり（ProjectDataValidator／
            // ProjectAssetIntegrityTests）、同じ AreaId の AreaDefinition を 2 つ置くと落ちる。
            Assert.AreNotEqual(areaAP5.Id.Value, areaAP55.Id.Value,
                "AreaId は配置ごとに別（安定 ID の一意性を守るため。裁定は付録 B.5）。");

            // <b>入口 ID は再利用する。</b> ただし<b>進行の引き継ぎは保証しない</b>——
            // GameSessionState は AreaId をキーにエリア進行を持つので、
            // 子の ID が同じでも P5 と P5.5 の進行は別物になる（付録 B.5）。
            Assert.IsTrue(HasEntry(areaAP55, Phase5AreaIds.AreaAStart),
                "P5.5 の A も開始入口 '" + Phase5AreaIds.AreaAStart.Value + "' を持つ。");
            Assert.IsTrue(HasEntry(areaAP55, Phase5AreaIds.AreaAFromB),
                "P5.5 の A も戻り入口 '" + Phase5AreaIds.AreaAFromB.Value + "' を持つ。");
        }

        private static bool HasEntry(AreaDefinition area, Momotaro.Core.Identification.StableId entryId)
        {
            for (int i = 0; i < area.Entries.Count; i++)
            {
                if (area.Entries[i].EntryId.Equals(entryId))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertScenePaths(AreaCatalogData catalog, string expectedFolder)
        {
            Assert.Greater(catalog.Areas.Count, 0, catalog.name + " にエリアがある。");
            for (int i = 0; i < catalog.Areas.Count; i++)
            {
                AreaDefinition area = catalog.Areas[i];
                Assert.IsNotNull(area, catalog.name + " のエリアが空でない。");
                Assert.IsTrue(area.ScenePath.StartsWith(expectedFolder),
                    catalog.name + " の '" + area.Id.Value + "' が " + expectedFolder
                    + " を指していない（実際=" + area.ScenePath + "）。");
            }
        }

        // ---------------------------------------------------------------- V02

        /// <summary>
        /// V02：<b>接続ズレ</b>を検出する（§3.2）。
        ///
        /// B を東へ 2m ずらすと床の間に隙間ができる。実行時には「境界の先が虚空」になるが、
        /// Scene を 1 枚ずつ見る検査では<b>どちらも正しく見える</b>。
        /// </summary>
        [TestCase("EastWest")]
        [TestCase("NorthSouth")]
        public void MovingAnAreaOffTheSeam_FailsValidation(string arrangement)
        {
            Phase55Arrangement r = Arrangement(arrangement);
            AssertBreakingIsDetected(r, "が一致しません", (a, b) =>
            {
                b.transform.position += r.Point(2f, 0f);
            });
        }

        /// <summary>
        /// V02：<b>通路を塞ぐ</b>と検出する（§7.3）。
        ///
        /// スライド中は両 Area が描かれているので、境界に壁が残っていると
        /// 画面を覆う壁として見える。Collider を無効にしても見た目は残るので、
        /// 「通れるから良い」では合格にしない。
        /// </summary>
        [TestCase("EastWest")]
        [TestCase("NorthSouth")]
        public void WallingUpThePassage_FailsValidation(string arrangement)
        {
            Phase55Arrangement r = Arrangement(arrangement);
            AssertBreakingIsDetected(r, "接続通路", (a, b) =>
            {
                Material mat = Phase5Placeholder.EnsureMaterial("M_P5_Wall", Phase5Placeholder.WallColor);

                // 名前は外周壁として採られるものにする（検査は "Wall_東西南北" だけを見る）。
                var wall = new GameObject(r.Axis == Phase5SeamAxis.X
                    ? "Wall_East_Intruder" : "Wall_North_Intruder");
                SceneManager.MoveGameObjectToScene(wall, a.gameObject.scene);
                wall.transform.SetParent(a.transform, false);

                // 境界は生成物から採る（定数を読み合わせない）。
                Bounds floor = FloorBoundsOf(a);
                Vector3 at = r.Point(r.ForwardEdge(floor), r.PassageCenter);
                wall.transform.position = new Vector3(at.x, Phase5Layout.WallHeight * 0.5f, at.z);
                Vector3 size = r.Point(Phase5Layout.WallThickness, r.PassageWidth);
                wall.transform.localScale = new Vector3(
                    Mathf.Max(size.x, 0.01f), Phase5Layout.WallHeight, Mathf.Max(size.z, 0.01f));

                var renderer = wall.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                wall.AddComponent<MeshFilter>().sharedMesh = CubeMesh();
            });
        }

        /// <summary>
        /// V02：<b>接続軸以外へずれる配置</b>を検出する（§7.1）。
        ///
        /// 東西の接続なのに Camera が Z へも動くと、地続きの見た目が崩れる。
        /// 入口を境界寄せ領域の外へ出すと、到着時のカメラが接続軸から外れる。
        /// <b>通路の端から入る場合</b>も同じ壊れ方で、そちらは Validator が
        /// 進入位置を刻んで見る（付録 B.3.1／B.3.2）。
        /// </summary>
        [TestCase("EastWest")]
        [TestCase("NorthSouth")]
        public void MovingThePassageOffTheRegionCentre_FailsValidation(string arrangement)
        {
            Phase55Arrangement r = Arrangement(arrangement);
            AssertBreakingIsDetected(r, "接続軸", (a, b) =>
            {
                foreach (AreaEntryPointMover mover in AreaEntryPointMover.For(b))
                {
                    // <b>直交軸へずらす</b>（接続軸へずらしても「遠いだけ」で軸は外れない）。
                    mover.Shift(r.Point(0f, 6f));
                }
            });
        }

        /// <summary>この名前の配置（テスト名を ASCII に保つための引き当て）。</summary>
        private static Phase55Arrangement Arrangement(string key) =>
            key == "NorthSouth" ? Phase55Arrangements.NorthSouth() : Phase55Arrangements.EastWest();

        /// <summary>その Area の床の Bounds（見えている Renderer から採る）。</summary>
        private static Bounds FloorBoundsOf(AreaRootHandle handle)
        {
            bool found = false;
            var bounds = new Bounds();
            foreach (Renderer renderer in handle.transform.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.name != "Floor")
                {
                    continue;
                }

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            Assert.IsTrue(found, "床（Floor）がある。");
            return bounds;
        }

        // ---------------------------------------------------------------- 補助

        /// <summary>
        /// 壊したときに検査が落ちることを見る。<b>壊す前に通ることも確かめる</b>——
        /// もともと落ちていたなら、その注入は何も検出していない。
        /// </summary>
        private static void AssertBreakingIsDetected(
            Phase55Arrangement arrangement, string expectedFragment,
            System.Action<AreaRootHandle, AreaRootHandle> breakIt)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Assert.IsTrue(
                Phase55WorldValidator.TryOpenBoth(arrangement, errors, out Scene sceneA, out Scene sceneB),
                "前提：両 Scene を開ける。");

            Assert.IsTrue(Phase55WorldValidator.TryCollect(sceneA, errors, warnings, out var before0));
            Assert.IsTrue(Phase55WorldValidator.TryCollect(sceneB, errors, warnings, out var before1));
            Phase55WorldValidator.Compare(arrangement, before0, before1, errors);
            Assert.IsEmpty(errors, "前提：壊す前は通る：\n- " + string.Join("\n- ", errors));

            breakIt(AreaRootHandle.In(sceneA), AreaRootHandle.In(sceneB));

            var after = new List<string>();
            var afterWarnings = new List<string>();
            Assert.IsTrue(Phase55WorldValidator.TryCollect(sceneA, after, afterWarnings, out var broken0));
            Assert.IsTrue(Phase55WorldValidator.TryCollect(sceneB, after, afterWarnings, out var broken1));
            Phase55WorldValidator.Compare(arrangement, broken0, broken1, after);

            bool found = false;
            for (int i = 0; i < after.Count; i++)
            {
                if (after[i].Contains(expectedFragment))
                {
                    found = true;
                    break;
                }
            }

            Assert.IsTrue(found,
                "'" + expectedFragment + "' を含むエラーが出ていない：\n- " + string.Join("\n- ", after));
        }

        private static Mesh CubeMesh()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = probe.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(probe);
            return mesh;
        }

        /// <summary>その Scene の <c>AreaRoot</c>（壊す操作の宛先）。</summary>
        private sealed class AreaRootHandle
        {
            private readonly GameObject _root;

            private AreaRootHandle(GameObject root)
            {
                _root = root;
            }

            /// <summary>この Area の根。</summary>
            public Transform transform => _root.transform;

            /// <summary>この Area の根の GameObject。</summary>
            public GameObject gameObject => _root;

            /// <summary>その Scene の根を引く。</summary>
            public static AreaRootHandle In(Scene scene)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (root.GetComponent<Momotaro.Gameplay.Session.AreaRoot>() != null)
                    {
                        return new AreaRootHandle(root);
                    }
                }

                Assert.Fail("AreaRoot が見つかりません: " + scene.path);
                return null;
            }
        }

        /// <summary>入口をまとめて動かす（注入用）。</summary>
        private sealed class AreaEntryPointMover
        {
            private readonly Transform _target;

            private AreaEntryPointMover(Transform target)
            {
                _target = target;
            }

            /// <summary>その Area の入口すべて。</summary>
            public static IEnumerable<AreaEntryPointMover> For(AreaRootHandle root)
            {
                var movers = new List<AreaEntryPointMover>();
                foreach (Momotaro.Gameplay.Session.AreaEntryPoint entry in
                    root.gameObject.GetComponentsInChildren<Momotaro.Gameplay.Session.AreaEntryPoint>(true))
                {
                    movers.Add(new AreaEntryPointMover(entry.transform));
                }

                return movers;
            }

            /// <summary>ずらす。</summary>
            public void Shift(Vector3 delta)
            {
                _target.position += delta;
            }
        }
    }
}
