using System.Collections.Generic;
using Momotaro.Editor.Phase3;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-12：Scene 生成 Editor ツール（<see cref="Phase3EnemyTestFieldBuilder"/>）の決定性・安全性を検証する。テスト用の一時パスへ生成し、
    /// 開いた生成 Scene（Build は Single で開いたまま残す）の構成（Player1／Camera1／Controller1／初期敵0／Prefab 割当／Floor 上面 Y=0／
    /// Player ルート Y=0／負スケール無し／Missing Script 無し）を確認する。再生成で増殖しないこと、不正な出力先で保存せず失敗することも確認する。
    /// テスト後は空 Scene へ戻して一時 Scene を削除する。
    /// </summary>
    public sealed class Phase3EnemyTestFieldBuilderTests
    {
        private const string TempPath = "Assets/_Project/Scenes/Tests/__P3TmpEnemyTest__.unity";

        private SceneSetup[] _originalSetup;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // テストは Build/TearDown で NewSceneMode.Single により現在の Scene を置換するため、実行前に Editor の Scene 構成を退避する。
            _originalSetup = EditorSceneManager.GetSceneManagerSetup();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            // 一時 Scene を削除し、元の Scene 構成へ復元する（ユーザーが開いていた Scene を壊さない）。
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TempPath) != null)
            {
                AssetDatabase.DeleteAsset(TempPath);
            }

            RestoreOriginalSetup();
        }

        [TearDown]
        public void TearDown()
        {
            // 各テスト後：開いている生成 Scene を空 Scene に置換してから一時アセットを削除する（元 Scene の復元は OneTimeTearDown）。
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TempPath) != null)
            {
                AssetDatabase.DeleteAsset(TempPath);
            }
        }

        private void RestoreOriginalSetup()
        {
            // 保存済み Scene 構成のみ復元できる。未保存・無題 Scene を含む場合は復元不可のため、空 Scene のまま残す（安全側）。
            bool restorable = _originalSetup != null && _originalSetup.Length > 0;
            if (restorable)
            {
                for (int i = 0; i < _originalSetup.Length; i++)
                {
                    if (string.IsNullOrEmpty(_originalSetup[i].path))
                    {
                        restorable = false;
                        break;
                    }
                }
            }

            if (restorable)
            {
                EditorSceneManager.RestoreSceneManagerSetup(_originalSetup);
            }
            else
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static List<T> InScene<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                list.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return list;
        }

        private static Scene BuildAndGet()
        {
            Phase3EnemyTestFieldBuilder.BuildResult r = Phase3EnemyTestFieldBuilder.Build(TempPath);
            Assert.IsTrue(r.Success, "生成成功: " + r.Message);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(TempPath), "Scene 資産が保存される。");
            Scene scene = SceneManager.GetActiveScene(); // Build は生成 Scene を開いたまま Active にする。
            Assert.AreEqual(TempPath, scene.path, "生成 Scene が開いている。");
            return scene;
        }

        [Test]
        public void Build_ProducesDeterministicStructure()
        {
            Scene scene = BuildAndGet();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                        "Missing Script: " + t.name);
                }
            }

            Assert.AreEqual(1, InScene<PlayerStateController>(scene).Count, "Player は 1 体。");
            Assert.AreEqual(1, InScene<EnemyTestFieldController>(scene).Count, "Controller は 1 つ。");
            Assert.AreEqual(0, InScene<EnemyActor>(scene).Count, "初期状態の有効な敵は 0 体。");

            int cams = 0;
            foreach (Camera c in InScene<Camera>(scene))
            {
                if (c.CompareTag("MainCamera")) cams++;
            }
            Assert.AreEqual(1, cams, "Main Camera は 1 台。");

            EnemyTestFieldController ctrl = InScene<EnemyTestFieldController>(scene)[0];
            Assert.IsNotNull(ctrl.MeleePrefab, "近接 Prefab 割当。");
            Assert.IsNotNull(ctrl.RangedPrefab, "遠距離 Prefab 割当。");
            Assert.IsNotNull(ctrl.ElitePrefab, "強敵 Prefab 割当。");

            Transform floor = FindByName(scene, "Floor");
            Assert.IsNotNull(floor, "Floor が存在。");
            float top = floor.position.y + floor.localScale.y * 0.5f;
            Assert.AreEqual(0f, top, 1e-4f, "Floor 上面が Y=0。");

            Transform player = FindByName(scene, "Player");
            Assert.IsNotNull(player, "Player が存在。");
            Assert.AreEqual(0f, player.position.y, 1e-4f, "Player ルート Y=0。");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    Vector3 s = t.localScale;
                    Assert.IsTrue(s.x >= 0f && s.y >= 0f && s.z >= 0f, "負スケール: " + t.name);
                }
            }
        }

        [Test]
        public void Rebuild_SamePath_DoesNotDuplicate()
        {
            Scene first = BuildAndGet();
            int controllers1 = InScene<EnemyTestFieldController>(first).Count;
            int roots1 = first.GetRootGameObjects().Length;

            Scene second = BuildAndGet(); // 同一パスへ再生成（Single で置換）。
            Assert.AreEqual(controllers1, InScene<EnemyTestFieldController>(second).Count, "Controller は増殖しない。");
            Assert.AreEqual(roots1, second.GetRootGameObjects().Length, "ルート数は一定（増殖しない）。");
            Assert.AreEqual(0, InScene<EnemyActor>(second).Count, "再生成でも初期敵 0 体。");
        }

        [Test]
        public void Build_InvalidOutputPath_FailsWithoutSaving()
        {
            Phase3EnemyTestFieldBuilder.BuildResult r = Phase3EnemyTestFieldBuilder.Build("/tmp/outside_project.unity");
            Assert.IsFalse(r.Success, "Assets 外の出力先は失敗する。");
        }

        private static Transform FindByName(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name)
                    {
                        return t;
                    }
                }
            }

            return null;
        }
    }
}
