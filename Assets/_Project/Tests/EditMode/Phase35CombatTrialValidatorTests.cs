using System.Collections.Generic;
using Momotaro.Editor.Phase35;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-10（配布・統合受入）：試遊 Scene 統合受入 Validator（<see cref="Phase35CombatTrialValidator"/>）を検証する。生成直後の Scene が
    /// 無エラーで通ること（正常系）、主要システムが欠落した空 Scene や、重複 Session／デバッグ HUD 混入がエラーとして検出されること
    /// （P3.5-10 ②：重複 HUD/Session の除去を回帰固定）を確認する。ビルダーテストと同じく一時パスへ生成し、後始末で元 Scene を保護する。
    /// </summary>
    public sealed class Phase35CombatTrialValidatorTests
    {
        private const string TempPath = "Assets/_Project/Scenes/Tests/__P35TmpValidator__.unity";

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
                        "Phase3.5 Validator テストは現在の Scene を置換するため、未保存または変更中の Scene がある場合は実行できません。"
                        + "Scene を保存してから再実行してください。（対象: " + (string.IsNullOrEmpty(s.path) ? "無題Scene" : s.path) + "）");
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

        private static Scene BuildTrialScene()
        {
            Phase35CombatTrialBuilder.BuildResult r = Phase35CombatTrialBuilder.Build(TempPath);
            Assert.IsTrue(r.Success, "生成成功: " + r.Message);
            Scene scene = SceneManager.GetActiveScene();
            Assert.AreEqual(TempPath, scene.path, "生成 Scene が開いている。");
            return scene;
        }

        private static (List<string> errors, List<string> warnings) Run(Scene scene)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Phase35CombatTrialValidator.Validate(scene, errors, warnings);
            return (errors, warnings);
        }

        [Test]
        public void FreshlyBuiltScene_HasNoErrors()
        {
            Scene scene = BuildTrialScene();
            (List<string> errors, List<string> _) = Run(scene);
            Assert.AreEqual(0, errors.Count, "生成直後の試遊 Scene は統合受入を満たす:\n- " + string.Join("\n- ", errors));
        }

        [Test]
        public void EmptyScene_ReportsMissingSystems()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            (List<string> errors, List<string> _) = Run(scene);
            Assert.Greater(errors.Count, 0, "主要システムが無い空 Scene はエラーになる。");
            Assert.IsTrue(errors.Exists(e => e.Contains("CombatSessionController")), "Session の欠落を報告する。");
        }

        [Test]
        public void DuplicateSession_IsDetected()
        {
            Scene scene = BuildTrialScene();
            int before = Run(scene).errors.Count;
            Assert.AreEqual(0, before, "前提：生成直後はエラー 0。");

            // 重複 Session を混入させる（P3.5-10 ②：重複を検出できることの回帰）。
            var dup = new GameObject("DuplicateSession");
            dup.AddComponent<CombatSessionController>();

            (List<string> errors, List<string> _) = Run(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CombatSessionController") && e.Contains("重複")),
                "重複 Session を検出する:\n- " + string.Join("\n- ", errors));
        }

        [Test]
        public void DebugHudPresent_IsForbidden()
        {
            Scene scene = BuildTrialScene();

            // デバッグ HUD の混入（試遊 Scene には含めない）。
            var dbg = new GameObject("StrayDebugHud");
            dbg.AddComponent<CombatDebugHud>();

            (List<string> errors, List<string> _) = Run(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CombatDebugHud")),
                "デバッグ HUD の混入を検出する:\n- " + string.Join("\n- ", errors));
        }
    }
}
