using System.Collections.Generic;
using System.IO;
using Momotaro.Core.Identification;
using Momotaro.Data;
using Momotaro.Data.Events;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using Momotaro.Editor.Validation;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Input;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P5-09：生成物の静的検査（仕様書 Momotaro_P5_Detailed_Spec_v1.1.md §13、§15.4 の V01〜V05）。
    ///
    /// <b>ここが守るのは「テストの外に残る穴」</b>。受入テストは必要な参照を自分で注入してしまうので、
    /// 配線し忘れた Scene・登録し忘れた Build Settings・壊れた ID だけがテストの網から漏れる。
    /// だから出荷物そのものを開いて検査する。
    ///
    /// Scene を置き換えるので、未保存の変更があるときは実行しない（`CLAUDE.md` の「未保存 Scene 保護」）。
    /// 止めるのは<b>未保存のときだけ</b>：無題でも未編集なら失われるものは無い。
    /// </summary>
    public sealed class P5ValidatorTests
    {
        /// <summary>V05 で作る不正 Fixture の置き場（生成先の中に置くことに意味がある）。</summary>
        private const string BrokenFixturePath = Phase5AreaIds.DataFolder + "/__P5BrokenFixture__.asset";

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
                        "P5 Validator テストは現在の Scene を置換するため、未保存の Scene がある場合は実行できません。"
                        + "（対象: " + (string.IsNullOrEmpty(s.path) ? "無題Scene" : s.path) + "）");
                }
            }

            _originalSetup = EditorSceneManager.GetSceneManagerSetup();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            DeleteBrokenFixture();
            RestoreOriginalSetup();
            RestoreGameplayActionMap();
        }

        /// <summary>
        /// project-wide の Action Asset で Gameplay マップを有効へ戻す。
        ///
        /// この一式は Scene を作り直して開き直すので、Editor の入力まわりを大きく揺らす。
        /// Action Map の有効・無効は<b>プロジェクトの Asset に残り、PlayMode を抜けても消えない</b>ため、
        /// ここで戻しておかないと、後続の PlayMode で実キーのテストが一斉に無反応になりうる
        /// （P5-08・P5-09 で 2 度踏んだ症状）。
        /// </summary>
        private static void RestoreGameplayActionMap()
        {
            UnityEngine.InputSystem.InputActionAsset asset = UnityEngine.InputSystem.InputSystem.actions;
            if (asset == null)
            {
                return;
            }

            foreach (UnityEngine.InputSystem.InputActionMap map in asset.actionMaps)
            {
                if (map.name == "Gameplay")
                {
                    map.Enable();
                }
                else if (map.enabled)
                {
                    map.Disable();
                }
            }
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            DeleteBrokenFixture();
        }

        // ---------------------------------------------------------------- V01

        /// <summary>
        /// P5-V01：<b>出荷した生成物</b>が Scene・Data・Asset のすべての検査を通る（§13.2 の 3 行）。
        ///
        /// 3 つを 1 本で見るのは、どれか 1 つだけ緑でも試遊が立ち上がらないから。
        /// 「Scene は正しいが Build Settings に無い」は、遊べないという意味で欠落と同じ。
        /// </summary>
        [Test]
        public void GeneratedAreas_PassSceneDataAndAssetValidators()
        {
            // ---- Asset／Build（Scene を開かずに分かること）----
            var assetErrors = new List<string>();
            var assetWarnings = new List<string>();
            Phase5AssetValidator.Validate(assetErrors, assetWarnings);
            Assert.AreEqual(0, assetErrors.Count,
                "出荷物の Asset／Build 検査:\n- " + string.Join("\n- ", assetErrors));

            foreach (string path in Phase5AssetValidator.RequiredScenePaths)
            {
                Assert.IsTrue(Phase5AssetValidator.IsRegisteredAndEnabled(path),
                    "Build Settings に有効な登録がある: " + path);
            }

            // ---- Data（既存の全件検査に P5 の生成物が乗っている）----
            DataValidationReport report = ProjectDataValidator.RunAll();
            Assert.IsFalse(report.HasErrors,
                "Data 検証:\n- " + string.Join("\n- ", report.Errors));

            // ---- Scene（A と B を実際に開く）----
            foreach (string scenePath in Phase5ExplorationValidator.AreaScenePaths)
            {
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                (List<string> errors, List<string> _) = RunScene(scene);
                Assert.AreEqual(0, errors.Count,
                    scenePath + " の Scene 検査:\n- " + string.Join("\n- ", errors));
            }
        }

        // ---------------------------------------------------------------- V02

        /// <summary>
        /// P5-V02：壊し方ごとに<b>その壊れ方が分かる</b>指摘が出る（§15.4）。
        ///
        /// 「何か落ちた」では直せない。重複・欠落・未配線・Floor・空構成・入力二重を
        /// それぞれ別の文言で出すことまでを検査する。
        /// </summary>
        [Test]
        public void BrokenFixtures_FailWithSpecificDiagnostics()
        {
            Scene scene = EditorSceneManager.OpenScene(Phase5AreaIds.AreaBScenePath, OpenSceneMode.Single);
            Assert.AreEqual(0, RunScene(scene).errors.Count, "前提：出荷 Scene はエラー 0。");

            // ---- 入力二重（同じ押下を 2 回消費する。§7.1）----
            var extraInput = new GameObject("__DuplicateInteractInput__");
            extraInput.AddComponent<AreaInteractInput>();
            AssertHasError(scene, "AreaInteractInput", "Interact の入力仲介の重複");
            Object.DestroyImmediate(extraInput);

            // ---- P4 の旧仲介の混入（§13.3 の 4 行目）----
            var legacyInput = new GameObject("__LegacyInvestigationInput__");
            legacyInput.AddComponent<InvestigationInteractInput>();
            AssertHasError(scene, "InvestigationInteractInput", "P4 の旧入力仲介の混入");
            Object.DestroyImmediate(legacyInput);

            // ---- 重複 ID（調査地点の PointId）----
            List<CompanionInvestigationPoint> points =
                Phase5ExplorationValidator.Components<CompanionInvestigationPoint>(scene);
            Assert.Greater(points.Count, 0, "前提：調査地点がある。");
            GameObject duplicated = Object.Instantiate(points[0].gameObject);
            duplicated.name = "__DuplicatePoint__";
            AssertHasError(scene, "PointId が重複", "調査地点 ID の重複");
            Object.DestroyImmediate(duplicated);

            // ---- 入口欠落（Data にはあるのに Scene の実体が無い）----
            List<AreaRoot> roots = Phase5ExplorationValidator.Components<AreaRoot>(scene);
            Assert.AreEqual(1, roots.Count, "前提：エリアの根は 1 つ。");
            AreaEntryPoint entry = roots[0].EntryPoints[0];
            StableId removedEntry = entry.EntryId;
            Object.DestroyImmediate(entry.gameObject);
            AssertHasError(scene, removedEntry.Value, "入口の欠落");

            // 壊したまま次へ進まない：Scene を開き直して出荷状態へ戻す。
            scene = EditorSceneManager.OpenScene(Phase5AreaIds.AreaBScenePath, OpenSceneMode.Single);
            Assert.AreEqual(0, RunScene(scene).errors.Count, "前提：開き直してエラー 0 に戻る。");

            // ---- 未配線 Context（置いてあるが繋がっていない）----
            List<CompanionActivityContext> contexts =
                Phase5ExplorationValidator.Components<CompanionActivityContext>(scene);
            Assert.AreEqual(1, contexts.Count, "前提：活動 Context は 1 つ。");
            var so = new SerializedObject(contexts[0]);
            so.FindProperty("_areaEncounter").objectReferenceValue = null;
            so.FindProperty("_session").objectReferenceValue = null;
            so.FindProperty("_noEncounterInThisArea").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssertHasError(scene, "仲間の活動 Context が配線されていません", "未配線 Context");

            scene = EditorSceneManager.OpenScene(Phase5AreaIds.AreaBScenePath, OpenSceneMode.Single);

            // ---- Floor 不整合（P5 は Floor 0 のみ）----
            var brokenArea = ScriptableObject.CreateInstance<AreaDefinition>();
            brokenArea.EditorSet(Phase5AreaIds.AreaBScenePath, 1, new List<AreaEntryDefinition>(), default);
            var rootSo = new SerializedObject(Phase5ExplorationValidator.Components<AreaRoot>(scene)[0]);
            rootSo.FindProperty("_definition").objectReferenceValue = brokenArea;
            rootSo.ApplyModifiedPropertiesWithoutUndo();
            AssertHasError(scene, "Floor 0 のみ対応", "Floor 不整合");
            Object.DestroyImmediate(brokenArea);

            // ---- 空 Encounter と Boss（Data 側。出荷 Data は壊さない）----
            var emptyEncounter = ScriptableObject.CreateInstance<EncounterData>();
            var encounterErrors = new List<string>();
            Phase5AssetValidator.ValidateEncounter(emptyEncounter, encounterErrors);
            Assert.IsTrue(encounterErrors.Exists(e => e.Contains("敵構成が空")),
                "空の敵構成を通さない:\n- " + string.Join("\n- ", encounterErrors));
            Object.DestroyImmediate(emptyEncounter);
        }

        // ---------------------------------------------------------------- V03

        /// <summary>
        /// P5-V03：2 回生成しても重複せず、<b>共有の原本を書き換えない</b>（§13.1）。
        ///
        /// 再生成で共有 Prefab や本番 AttackData が変わると、P5 のためにゲーム全体が動く。
        /// 中身の同一性はファイルのバイト列で見る：「参照は同じだが値が変わった」を通さないため。
        /// </summary>
        [Test]
        public void Rebuild_IsRepeatableAndDoesNotModifySharedAssets()
        {
            string[] shared =
            {
                "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab",
                "Assets/_Project/Prefabs/Companions/PF_Companion_Inumaru.prefab",
                Phase5AreaIds.EnemyMeleePrefabPath,
                Phase5AreaIds.EnemyRangedPrefabPath,
                Phase5AreaIds.InvestigationSettingsPath,
            };

            var before = new Dictionary<string, string>();
            foreach (string path in shared)
            {
                Assert.IsTrue(File.Exists(path), "前提：共有の原本がある: " + path);
                before[path] = Hash(path);
            }

            Phase5ExplorationBuilder.BuildResult first = Phase5ExplorationBuilder.BuildAll();
            Assert.IsTrue(first.Success, "1 回目の生成: " + first.Message);

            Dictionary<string, int> counts = CountAreaComponents();

            Phase5ExplorationBuilder.BuildResult second = Phase5ExplorationBuilder.BuildAll();
            Assert.IsTrue(second.Success, "2 回目の生成: " + second.Message);

            Dictionary<string, int> after = CountAreaComponents();
            foreach (KeyValuePair<string, int> pair in counts)
            {
                Assert.AreEqual(pair.Value, after[pair.Key],
                    "2 回生成しても " + pair.Key + " の数が増えない（重複しない）。");
            }

            foreach (string path in shared)
            {
                Assert.AreEqual(before[path], Hash(path),
                    "再生成で共有の原本を書き換えない: " + path);
            }

            // Build Settings も増えっぱなしにしない（既存項目を維持して追記・更新する。§13.1）。
            foreach (string path in Phase5AssetValidator.RequiredScenePaths)
            {
                Assert.AreEqual(1, CountBuildSettingsEntries(path),
                    "Build Settings の登録が重複しない: " + path);
                Assert.IsTrue(Phase5AssetValidator.IsRegisteredAndEnabled(path));
            }
        }

        // ---------------------------------------------------------------- V04

        /// <summary>
        /// P5-V04：未保存の Scene があるときは<b>生成しない</b>。勝手に保存も破棄もしない（§13.1）。
        ///
        /// ここは「断れること」だけでなく「断ったあとも編集が残っていること」まで見る。
        /// 断ってから保存していたら、断った意味が無い。
        /// </summary>
        [Test]
        public void DirtyScene_BlocksDestructiveGeneration()
        {
            Scene scratch = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var edit = new GameObject("__UnsavedEdit__");
            EditorSceneManager.MarkSceneDirty(scratch);
            Assert.IsTrue(scratch.isDirty, "前提：未保存の変更がある。");

            string areaBefore = Hash(Phase5AreaIds.AreaAScenePath);

            Phase5ExplorationBuilder.BuildResult result = Phase5ExplorationBuilder.BuildAll();

            Assert.IsFalse(result.Success, "未保存の変更があれば生成しない。");
            StringAssert.Contains("未保存", result.Message);

            Scene current = SceneManager.GetActiveScene();
            Assert.IsTrue(current.isDirty, "断ったあとも未保存のまま（勝手に保存しない）。");
            Assert.IsNotNull(GameObject.Find("__UnsavedEdit__"), "編集内容を破棄しない。");
            Assert.AreEqual(areaBefore, Hash(Phase5AreaIds.AreaAScenePath), "出荷 Scene も書き換えない。");

            Object.DestroyImmediate(edit);
        }

        // ---------------------------------------------------------------- V05

        /// <summary>
        /// P5-V05：Phase5 の生成先に置いた Data が<b>既存の全件検査に実際に乗る</b>（§13.4、裁定 3）。
        ///
        /// 「収集対象のはず」で終わらせない。生成先へ不正な Data を 1 つ置き、
        /// 収集されること・<see cref="ProjectDataValidator.RunAll"/> が落ちることを確かめ、
        /// 片付けたあとに正規の Data が全件合格へ戻ることまで見る。
        /// </summary>
        [Test]
        public void ProjectDataValidation_IncludesPhase5GeneratedFolder()
        {
            Assert.IsFalse(ProjectDataValidator.RunAll().HasErrors, "前提：出荷 Data は全件合格。");

            try
            {
                var broken = ScriptableObject.CreateInstance<AreaDefinition>();
                // Id を入れない（StableId が不正）＋ Scene パスも入れない＝既存検査が落とすべき形。
                AssetDatabase.CreateAsset(broken, BrokenFixturePath);
                AssetDatabase.SaveAssets();

                List<GameDataAsset> collected = ProjectDataValidator.CollectAllDataAssets();
                Assert.IsTrue(collected.Exists(a => AssetDatabase.GetAssetPath(a) == BrokenFixturePath),
                    "生成先の Data が収集対象に入る（§13.4）。収集数=" + collected.Count);

                DataValidationReport report = ProjectDataValidator.RunAll();
                Assert.IsTrue(report.HasErrors,
                    "不正な Data を全件検査が落とす（Tests フォルダを検査対象外にしない）。");
            }
            finally
            {
                DeleteBrokenFixture();
            }

            Assert.IsFalse(ProjectDataValidator.RunAll().HasErrors,
                "片付けたら正規 Data は全件合格へ戻る。");
        }

        // ---------------------------------------------------------------- ヘルパ

        private static (List<string> errors, List<string> warnings) RunScene(Scene scene)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Phase5ExplorationValidator.Validate(scene, errors, warnings);
            return (errors, warnings);
        }

        private static void AssertHasError(Scene scene, string keyword, string label)
        {
            (List<string> errors, List<string> _) = RunScene(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains(keyword)),
                label + " を検出する（手掛かり: " + keyword + "）:\n- " + string.Join("\n- ", errors));
        }

        private static Dictionary<string, int> CountAreaComponents()
        {
            var counts = new Dictionary<string, int>();
            foreach (string scenePath in Phase5ExplorationValidator.AreaScenePaths)
            {
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                counts[scenePath + ":AreaRoot"] = Phase5ExplorationValidator.Count<AreaRoot>(scene);
                counts[scenePath + ":Interact"] = Phase5ExplorationValidator.Count<AreaInteractInput>(scene);
                counts[scenePath + ":Point"] =
                    Phase5ExplorationValidator.Count<CompanionInvestigationPoint>(scene);
                counts[scenePath + ":Respawn"] = Phase5ExplorationValidator.Count<CampaignRespawnRunner>(scene);
                counts[scenePath + ":Encounter"] = Phase5ExplorationValidator.Count<AreaEncounterRunner>(scene);
                counts[scenePath + ":Roots"] = scene.GetRootGameObjects().Length;
            }

            return counts;
        }

        private static int CountBuildSettingsEntries(string scenePath)
        {
            int n = 0;
            foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
            {
                if (s != null && s.path == scenePath)
                {
                    n++;
                }
            }

            return n;
        }

        private static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return System.Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
            }
        }

        private static void DeleteBrokenFixture()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(BrokenFixturePath) != null)
            {
                AssetDatabase.DeleteAsset(BrokenFixturePath);
                AssetDatabase.SaveAssets();
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
    }
}
