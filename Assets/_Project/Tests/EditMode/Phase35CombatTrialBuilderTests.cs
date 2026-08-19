using System.Collections.Generic;
using System.Linq;
using Momotaro.Editor.Phase35;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.SceneFlow;
using Momotaro.Presentation.Combat;
using Momotaro.Presentation.Diagnostics;
using Momotaro.Presentation.Hud;
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
            Assert.AreEqual(1, InScene<CombatSessionController>(scene).Count, "Session は 1 つ。");
            Assert.AreEqual(1, InScene<WaveRunner>(scene).Count, "WaveRunner は 1 つ。");
            Assert.AreEqual(1, InScene<CombatOutcomeController>(scene).Count, "勝敗統合は 1 つ（P3.5-08）。");
            Assert.AreEqual(1, InScene<CombatSceneReloader>(scene).Count, "Scene 再読込は 1 つ（P3.5-08）。");
            Assert.AreEqual(1, InScene<CombatRetryInput>(scene).Count, "Retry 入力は 1 つ（P3.5-08）。");
            Assert.AreEqual(1, InScene<CombatPlayHud>(scene).Count, "試遊 HUD は 1 つ。");
            Assert.AreEqual(0, InScene<EnemyTestFieldController>(scene).Count, "試遊 Scene に手動編成ツールは置かない（Wave 駆動へ置換）。");
            Assert.AreEqual(0, InScene<EnemyActor>(scene).Count, "初期状態の有効な敵は 0 体。");

            int cams = 0;
            foreach (Camera c in InScene<Camera>(scene))
            {
                if (c.CompareTag("MainCamera")) cams++;
            }
            Assert.AreEqual(1, cams, "Main Camera は 1 台。");

            WaveRunner runner = InScene<WaveRunner>(scene)[0];
            Assert.IsNotNull(runner.MeleePrefab, "近接 Prefab 割当。");
            Assert.IsNotNull(runner.RangedPrefab, "遠距離 Prefab 割当。");
            Assert.IsNotNull(runner.ElitePrefab, "強敵 Prefab 割当。");
            Assert.AreEqual(4, runner.WaveCount, "Wave は 4 構成（§8.2）。");
            Assert.AreEqual(4, runner.SpawnPointCount, "固定 Spawn Point は 4 つ。");

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
            // 完成 VFX 前提：方向ごとの期待枚数を満たす（Combo1=3, Combo3=4, Special=5）。
            Assert.AreEqual(3, pv.Stage1Frames.down.Length, "1 段目 Down は 3 コマ。");
            Assert.AreEqual(4, pv.Stage3Frames.down.Length, "3 段目 Down は 4 コマ。");
            Assert.AreEqual(5, pv.SpecialFrames.up.Length, "必殺技 Up は 5 コマ。");

            List<EnemySlashVfxPresenter> enemies = InScene<EnemySlashVfxPresenter>(scene);
            Assert.AreEqual(1, enemies.Count, "敵斬撃 VFX は 1 つ。");
            EnemySlashVfxPresenter.EnemySlashEntry[] entries = enemies[0].Entries;
            Assert.IsNotNull(entries, "敵斬撃エントリがある。");
            EnemySlashVfxPresenter.EnemySlashEntry small = entries.FirstOrDefault(e => e.key == "Small");
            EnemySlashVfxPresenter.EnemySlashEntry medium = entries.FirstOrDefault(e => e.key == "Medium");
            Assert.IsNotNull(small, "Small 鍵のエントリがある。");
            Assert.IsNotNull(medium, "Medium 鍵のエントリがある。");
            Assert.AreEqual(3, small.normal.down.Length, "Small 通常 Down は 3 コマ。");
            Assert.AreEqual(3, medium.normal.down.Length, "Medium 通常 Down は 3 コマ。");
            // 侍骸骨（Medium）は強・ガード不能も持つ（鍵は archetype 駆動＝強敵→"Medium"。P3.5-06）。
            Assert.AreEqual(4, medium.heavy.down.Length, "Medium 強 Down は 4 コマ（強敵の強斬撃）。");
            Assert.AreEqual(4, medium.unblockable.down.Length, "Medium ガード不能 Down は 4 コマ（強敵のガード不能斬撃）。");

            List<EnemyUnblockableWarningPresenter> warns = InScene<EnemyUnblockableWarningPresenter>(scene);
            Assert.AreEqual(1, warns.Count, "ガード不能警告は 1 つ。");
            Assert.AreEqual(4, warns[0].WarningFrames.Length, "警告フレームは 4 コマ（無方向フラット）。");
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
