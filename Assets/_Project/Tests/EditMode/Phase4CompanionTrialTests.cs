using System.Collections.Generic;
using Momotaro.Editor.Phase35;
using Momotaro.Editor.Phase4;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-08R：仲間試遊 Scene の Builder と Validator を検証する。
    ///
    /// この Scene は Phase3.5 の試遊 Scene の<b>上に</b>仲間の層を載せて作る。したがって固定すべきことは 3 つ。
    /// <list type="number">
    /// <item><description>載せたものが揃っている（犬丸・活動 Context・調査地点・指示の入力）。</description></item>
    /// <item><description><b>載せたことで 3.5 の受入条件を壊していない</b>——これが合成で作る形のいちばんの risk。</description></item>
    /// <item><description>Validator が欠落を実際に検出する（壊してみせて確かめる）。</description></item>
    /// </list>
    ///
    /// Scene を置換するため、<b>未保存の変更があるときだけ</b>自ら Skip する（F01 で揃えた作法）。
    /// </summary>
    public sealed class Phase4CompanionTrialTests
    {
        private const string TempPath = "Assets/_Project/Scenes/Tests/__P4TmpTrial__.unity";

        private SceneSetup[] _originalSetup;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.isDirty)
                {
                    Assert.Ignore(
                        "Phase4 試遊 Scene 生成テストは現在の Scene を置換するため、未保存の変更がある場合は実行できません。"
                        + "（対象: " + (string.IsNullOrEmpty(s.path) ? "無題Scene" : s.path) + "）");
                }
            }

            _originalSetup = EditorSceneManager.GetSceneManagerSetup();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
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

            CompanionActivityProvider.Current = null;
            InvestigationPointRegistry.Clear();
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

        // ---- 補助 ----

        private static Scene BuildTrial()
        {
            Phase4CompanionTrialBuilder.BuildResult r = Phase4CompanionTrialBuilder.Build(TempPath);
            Assert.IsTrue(r.Success, "生成成功: " + r.Message);
            Scene scene = SceneManager.GetActiveScene();
            Assert.AreEqual(TempPath, scene.path, "生成 Scene が開いている。");
            return scene;
        }

        private static List<string> Errors(Scene scene)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Phase4CompanionTrialValidator.Validate(scene, errors, warnings);
            return errors;
        }

        private static List<T> All<T>(Scene scene) where T : Component
        {
            var list = new List<T>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                list.AddRange(root.GetComponentsInChildren<T>(true));
            }

            return list;
        }

        // ================================================================
        // 1. 載せたものが揃っている
        // ================================================================

        [Test]
        public void Build_AddsTheCompanionLayerOnTopOfTheTrialScene()
        {
            Scene scene = BuildTrial();

            Assert.AreEqual(1, All<CompanionActor>(scene).Count, "犬丸が 1 体いる。");
            Assert.AreEqual(1, All<CompanionActivityContext>(scene).Count, "活動 Context が 1 つある。");
            Assert.AreEqual(1, All<CompanionOrderInput>(scene).Count, "指示の入力が 1 つある。");
            Assert.AreEqual(Phase4CompanionTrialBuilder.InvestigationPointPositions.Length,
                All<CompanionInvestigationPoint>(scene).Count, "調査地点が決められた数だけある。");

            // 3.5 側の主要システムも残っている（合成の土台を壊していない）。
            Assert.AreEqual(1, All<WaveRunner>(scene).Count, "Wave 進行は 3.5 のものがそのまま残る。");
            Assert.AreEqual(1, All<CombatSessionController>(scene).Count, "戦闘 Session も 1 つ。");
        }

        /// <summary>活動 Context がこの Scene の Session に繋がっている（別 Scene のものを掴んでいない）。</summary>
        [Test]
        public void Build_WiresActivityContextToTheTrialSession()
        {
            Scene scene = BuildTrial();

            CompanionActivityContext context = All<CompanionActivityContext>(scene)[0];
            Assert.IsTrue(context.IsWired, "供給元として成立している。");
            Assert.AreSame(All<CombatSessionController>(scene)[0], context.Session,
                "配線先はこの Scene の戦闘 Session。");
        }

        /// <summary>指示の入力が犬丸に繋がっている（キーを押して何も起きない、を防ぐ）。</summary>
        [Test]
        public void Build_WiresOrderInputToTheCompanion()
        {
            Scene scene = BuildTrial();

            CompanionOrderInput input = All<CompanionOrderInput>(scene)[0];
            Assert.AreEqual(1, input.CompanionCount, "相手が繋がっている。");
            Assert.IsTrue(input.AnyFollowing(), "初期状態は「ついて来い」。");
        }

        /// <summary>調査地点が主人公の紐の内側にある（外だと探索は有効なのに一度も動かない）。</summary>
        [Test]
        public void Build_PlacesInvestigationPointsWithinLeash()
        {
            Scene scene = BuildTrial();

            Transform player = All<PlayerStateController>(scene)[0].transform;
            CompanionActor actor = All<CompanionActor>(scene)[0];
            float leash = actor.Data.InvestigateLeashDistance;

            foreach (CompanionInvestigationPoint point in All<CompanionInvestigationPoint>(scene))
            {
                float distance = FormationSlot.HorizontalDistance(player.position, point.transform.position);
                Assert.LessOrEqual(distance, leash,
                    point.name + " は主人公から紐（" + leash + "m）の内側にある。");
            }
        }

        [Test]
        public void Rebuild_SamePath_DoesNotDuplicate()
        {
            Scene first = BuildTrial();
            int rootsBefore = first.rootCount;

            Scene second = BuildTrial();

            Assert.AreEqual(rootsBefore, second.rootCount, "再生成でルートが増えない。");
            Assert.AreEqual(1, All<CompanionActor>(second).Count, "犬丸も増えない。");
            Assert.AreEqual(1, All<CompanionOrderInput>(second).Count, "指示の入力も増えない。");
        }

        [Test]
        public void Build_InvalidOutputPath_FailsWithoutSaving()
        {
            Phase4CompanionTrialBuilder.BuildResult r = Phase4CompanionTrialBuilder.Build("Temp/OutsideAssets.unity");

            Assert.IsFalse(r.Success, "Assets 配下でなければ生成しない。");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SceneAsset>("Temp/OutsideAssets.unity"));
        }

        // ================================================================
        // 2. 3.5 の受入条件を壊していない
        // ================================================================

        /// <summary>
        /// <b>仲間を載せても Phase3.5 の統合受入をそのまま通る。</b>合成で作る形でいちばん怖いのは、
        /// 足したものが土台の不変条件（重複 Session・デバッグ HUD の混入・初期敵 0 など）を破ることなので、
        /// 3.5 の Validator を単体で当てて確かめる。
        /// </summary>
        [Test]
        public void TrialScene_StillSatisfiesPhase35Acceptance()
        {
            Scene scene = BuildTrial();

            var errors = new List<string>();
            var warnings = new List<string>();
            Phase35CombatTrialValidator.Validate(scene, errors, warnings);

            Assert.AreEqual(0, errors.Count,
                "仲間の層を足しても 3.5 の統合受入を満たす:\n- " + string.Join("\n- ", errors));
        }

        [Test]
        public void FreshlyBuiltTrial_HasNoErrors()
        {
            Scene scene = BuildTrial();
            List<string> errors = Errors(scene);

            Assert.AreEqual(0, errors.Count,
                "生成直後の試遊 Scene は 3.5 と仲間の両方の不変条件を満たす:\n- " + string.Join("\n- ", errors));
        }

        // ================================================================
        // 3. 欠落を実際に検出する
        // ================================================================

        [Test]
        public void MissingOrderInput_IsDetected()
        {
            Scene scene = BuildTrial();
            Assert.AreEqual(0, Errors(scene).Count, "前提：生成直後はエラー 0。");

            Object.DestroyImmediate(All<CompanionOrderInput>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionOrderInput")),
                "指示の入力の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>
        /// 置いてあっても相手が 0 体なら不合格。キーを押しても何も起きず、
        /// 「指示が壊れている」のか「繋ぎ忘れ」なのかが実機では区別できないため。
        /// </summary>
        [Test]
        public void OrderInputWithoutCompanions_IsDetected()
        {
            Scene scene = BuildTrial();
            All<CompanionOrderInput>(scene)[0].ClearCompanions();

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("相手が 1 体も繋がっていません")),
                "繋ぎ忘れを検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>活動 Context を外したら検出する（試遊 Scene でも仲間の不変条件は同じ）。</summary>
        [Test]
        public void MissingActivityContext_IsDetected()
        {
            Scene scene = BuildTrial();
            Object.DestroyImmediate(All<CompanionActivityContext>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionActivityContext")),
                "活動 Context の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }
    }
}
