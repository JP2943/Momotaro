using System.Collections.Generic;
using System.Linq;
using Momotaro.Editor.Phase35;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-06：試遊 Scene 生成 Editor ツール（<see cref="Phase35CombatTrialBuilder"/>）の決定性・安全性・配線を検証する。テスト用の一時パスへ生成し、
    /// 構成（Player1／Main Camera1／Controller1／初期敵0／Prefab 割当／Floor 上面 Y=0／負スケール無し／Missing Script 無し）に加え、
    /// P3.5-05A/05B のフィードバック・VFX プレゼンタが揃い相互参照が配線されていること（Coordinator の各サブ効果、CameraShake の対象＝子カメラ、
    /// 主人公斬撃/敵斬撃/警告の素材割当）を確認する。再生成で増殖しないこと、不正な出力先で保存せず失敗することも確認する。テスト後は空 Scene へ戻す。
    /// </summary>
    public sealed class Phase35CombatTrialBuilderTests
    {
        private const string TempPath = "Assets/_Project/Scenes/Tests/__P35TmpCombatTrial__.unity";

        private SceneSetup[] _originalSetup;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty || string.IsNullOrEmpty(s.path))
                {
                    Assert.Ignore(
                        "Phase3.5 Scene生成テストは現在のSceneを置換するため、未保存または変更中のSceneがある場合は実行できません。"
                        + "Sceneを保存してから再実行してください。（対象: " + (string.IsNullOrEmpty(s.path) ? "無題Scene" : s.path) + "）");
                }
            }

            _originalSetup = EditorSceneManager.GetSceneManagerSetup();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (_originalSetup == null)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TempPath) != null)
            {
                AssetDatabase.DeleteAsset(TempPath);
            }

            RestoreOriginalSetup();
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TempPath) != null)
            {
                AssetDatabase.DeleteAsset(TempPath);
            }
        }

        private void RestoreOriginalSetup()
        {
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
            Phase35CombatTrialBuilder.BuildResult r = Phase35CombatTrialBuilder.Build(TempPath);
            Assert.IsTrue(r.Success, "生成成功: " + r.Message);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(TempPath), "Scene 資産が保存される。");
            Scene scene = SceneManager.GetActiveScene();
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
            Assert.AreEqual(0f, floor.position.y + floor.localScale.y * 0.5f, 1e-4f, "Floor 上面が Y=0。");

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
        public void Build_WiresFeedbackSystems()
        {
            Scene scene = BuildAndGet();

            Assert.AreEqual(1, InScene<CombatFeedbackDispatcher>(scene).Count, "Dispatcher は 1 つ。");
            Assert.AreEqual(1, InScene<HitStopController>(scene).Count, "HitStop は 1 つ。");
            Assert.AreEqual(1, InScene<HitFlashPresenter>(scene).Count, "HitFlash は 1 つ。");
            Assert.AreEqual(1, InScene<CameraShakePresenter>(scene).Count, "CameraShake は 1 つ。");
            Assert.AreEqual(1, InScene<CombatSePlayer>(scene).Count, "SE は 1 つ。");
            Assert.AreEqual(1, InScene<EnemyDefeatFadePresenter>(scene).Count, "撃破Fade は 1 つ。");

            List<CombatFeedbackPresenter> coords = InScene<CombatFeedbackPresenter>(scene);
            Assert.AreEqual(1, coords.Count, "Coordinator は 1 つ。");
            CombatFeedbackPresenter coord = coords[0];
            Assert.IsNotNull(coord.HitStop, "Coordinator に HitStop が配線されている。");
            Assert.IsNotNull(coord.Flash, "Coordinator に Flash が配線されている。");
            Assert.IsNotNull(coord.CameraShake, "Coordinator に CameraShake が配線されている。");
            Assert.IsNotNull(coord.Se, "Coordinator に SE が配線されている。");
        }

        [Test]
        public void Build_CameraShakeTargetsChildCamera()
        {
            Scene scene = BuildAndGet();
            CameraShakePresenter shake = InScene<CameraShakePresenter>(scene)[0];
            Assert.IsNotNull(shake.Target, "CameraShake の対象が割り当てられている。");
            Assert.IsNotNull(shake.Target.GetComponent<Camera>(), "CameraShake の対象は子 Main Camera（follow に上書きされない localPosition）。");
            Assert.IsTrue(shake.Target.CompareTag("MainCamera"), "対象は Main Camera。");
        }

        [Test]
        public void Build_WiresVfxPresenters_WithRealFrames()
        {
            Scene scene = BuildAndGet();

            List<PlayerSlashVfxPresenter> players = InScene<PlayerSlashVfxPresenter>(scene);
            Assert.AreEqual(1, players.Count, "主人公斬撃 VFX は 1 つ。");
            PlayerSlashVfxPresenter pv = players[0];
            Assert.IsNotNull(pv.Stage1Frames, "1 段目素材セットがある。");
            Assert.Greater(pv.Stage1Frames.down.Length, 0, "1 段目 Down に実素材が割り当てられている。");
            Assert.Greater(pv.SpecialFrames.up.Length, 0, "必殺技 Up に実素材が割り当てられている。");

            List<EnemySlashVfxPresenter> enemies = InScene<EnemySlashVfxPresenter>(scene);
            Assert.AreEqual(1, enemies.Count, "敵斬撃 VFX は 1 つ。");
            EnemySlashVfxPresenter.EnemySlashEntry[] entries = enemies[0].Entries;
            Assert.IsNotNull(entries, "敵斬撃エントリがある。");
            EnemySlashVfxPresenter.EnemySlashEntry small = entries.FirstOrDefault(e => e.key == "Small");
            EnemySlashVfxPresenter.EnemySlashEntry medium = entries.FirstOrDefault(e => e.key == "Medium");
            Assert.IsNotNull(small, "Small 鍵のエントリがある。");
            Assert.IsNotNull(medium, "Medium 鍵のエントリがある。");
            Assert.Greater(small.normal.down.Length, 0, "Small 通常 Down に実素材。");
            Assert.Greater(medium.normal.down.Length, 0, "Medium 通常 Down に実素材。");
            // 現状すべての敵が既定鍵 "Small" で解決されるため、強敵の強・ガード不能斬撃が出るよう Small に全分類を登録する。
            Assert.Greater(small.heavy.down.Length, 0, "Small に強素材も割当（強敵の強斬撃対応）。");
            Assert.Greater(small.unblockable.down.Length, 0, "Small にガード不能素材も割当（強敵のガード不能斬撃対応）。");

            List<EnemyUnblockableWarningPresenter> warns = InScene<EnemyUnblockableWarningPresenter>(scene);
            Assert.AreEqual(1, warns.Count, "ガード不能警告は 1 つ。");
            Assert.Greater(warns[0].WarningFrames.Length, 0, "警告フレームに実素材が割り当てられている。");
        }

        [Test]
        public void Rebuild_SamePath_DoesNotDuplicate()
        {
            Scene first = BuildAndGet();
            int coords1 = InScene<CombatFeedbackPresenter>(first).Count;
            int roots1 = first.GetRootGameObjects().Length;

            Scene second = BuildAndGet();
            Assert.AreEqual(coords1, InScene<CombatFeedbackPresenter>(second).Count, "Coordinator は増殖しない。");
            Assert.AreEqual(roots1, second.GetRootGameObjects().Length, "ルート数は一定（増殖しない）。");
            Assert.AreEqual(0, InScene<EnemyActor>(second).Count, "再生成でも初期敵 0 体。");
        }

        [Test]
        public void Build_InvalidOutputPath_FailsWithoutSaving()
        {
            Phase35CombatTrialBuilder.BuildResult r = Phase35CombatTrialBuilder.Build("/tmp/outside_project.unity");
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
