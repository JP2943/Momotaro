using System.Collections.Generic;
using Momotaro.Editor.Phase35;
using Momotaro.Editor.Phase4;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using Momotaro.Presentation.Companion;
using Momotaro.Presentation.Hud;
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
            Assert.AreEqual(1, All<TrialStageController>(scene).Count, "試遊段階が 1 つある。");
            Assert.AreEqual(1, All<InvestigationCoordinator>(scene).Count, "探索の調停役が 1 つある。");
            Assert.AreEqual(Phase4CompanionTrialBuilder.InvestigationPointCount,
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

        /// <summary>
        /// 明示開始の配線（P4-08R。v1.0 §13.1）：WaveRunner の自動開始が切れ、試遊段階が Wave と探索の調停役へ、
        /// 活動 Context が試遊段階へ繋がっている。3.5 の既定（自動開始）はこの Scene のインスタンスでだけ切る。
        /// </summary>
        [Test]
        public void Build_MakesCombatStartExplicit_AndStartsInFreeExploration()
        {
            Scene scene = BuildTrial();

            WaveRunner waves = All<WaveRunner>(scene)[0];
            var so = new SerializedObject(waves);
            Assert.IsFalse(so.FindProperty("_autoStart").boolValue, "P4 試遊 Scene の Wave は自動開始しない。");

            TrialStageController stage = All<TrialStageController>(scene)[0];
            Assert.AreSame(waves, stage.Waves, "試遊段階が Wave を起動する。");
            Assert.AreSame(All<InvestigationCoordinator>(scene)[0], stage.Investigation, "試遊段階が探索を解放してから起動する。");
            Assert.IsFalse(stage.EncounterRequested, "起動直後は自由探索（戦闘は要求されていない）。");

            CompanionActivityContext context = All<CompanionActivityContext>(scene)[0];
            Assert.AreSame(stage, context.Stage, "活動 Context が試遊段階を読む（開始前の Preparing を自由探索として供給する）。");
        }

        /// <summary>地点の構成：正常・壁・未加入が揃い、PointId が重複しない（§13.1）。</summary>
        [Test]
        public void Build_PlacesNormalWalledAndUnrecruitedPoints()
        {
            Scene scene = BuildTrial();
            List<CompanionInvestigationPoint> points = All<CompanionInvestigationPoint>(scene);
            var ids = new HashSet<string>();
            foreach (CompanionInvestigationPoint point in points)
            {
                Assert.IsTrue(point.PointId.IsValid && ids.Add(point.PointId.Value), point.name + "：PointId が有効で重複しない。");
                Assert.IsNotNull(point.SettingsData, point.name + "：設定 Data が入っている。");
            }

            Assert.IsTrue(points.Exists(pt => pt.RequiredCompanion.Equals(Phase4InvestigationLayerBuilder.UnrecruitedCompanionId)),
                "未加入条件を確認できる地点がある。");
            Assert.IsTrue(points.Exists(pt => pt.name.Contains("Walled")), "壁で到達できない検証用地点がある。");
        }

        [Test]
        public void Rebuild_SamePath_DoesNotDuplicate()
        {
            Scene first = BuildTrial();
            int rootsBefore = first.rootCount;

            Scene second = BuildTrial();

            Assert.AreEqual(rootsBefore, second.rootCount, "再生成でルートが増えない。");
            Assert.AreEqual(1, All<CompanionActor>(second).Count, "犬丸も増えない。");
            Assert.AreEqual(1, All<TrialStageController>(second).Count, "試遊段階も増えない。");
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
        public void MissingTrialStage_IsDetected()
        {
            Scene scene = BuildTrial();
            Assert.AreEqual(0, Errors(scene).Count, "前提：生成直後はエラー 0。");

            Object.DestroyImmediate(All<TrialStageController>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("TrialStageController")),
                "試遊段階の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>自動開始が有効なままなら不合格（Play した瞬間に敵が湧き、自由探索の段階が無くなる）。</summary>
        [Test]
        public void AutoStartLeftEnabled_IsDetected()
        {
            Scene scene = BuildTrial();
            var so = new SerializedObject(All<WaveRunner>(scene)[0]);
            so.FindProperty("_autoStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("自動開始")),
                "自動開始の残りを検出する:\n- " + string.Join("\n- ", errors));
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

        // ================================================================
        // P4-07B：入力・表示の復元と検出
        // ================================================================

        /// <summary>入力仲介・マーカー・短文 UI・明示開始入力が復元され、同じ調停役と地点を指す（R01。v1.0 §13.2「入力、UI」）。</summary>
        [Test]
        public void Build_WiresInteractInput_Markers_Hud_AndCombatStartInput()
        {
            Scene scene = BuildTrial();
            InvestigationCoordinator coordinator = All<InvestigationCoordinator>(scene)[0];
            TrialStageController stage = All<TrialStageController>(scene)[0];

            List<InvestigationInteractInput> inputs = All<InvestigationInteractInput>(scene);
            Assert.AreEqual(1, inputs.Count, "入力仲介は 1 つ。");
            Assert.AreSame(coordinator, inputs[0].Coordinator, "入力仲介はこの Scene の調停役へ渡す。");

            List<CompanionInvestigationPoint> points = All<CompanionInvestigationPoint>(scene);
            List<InvestigationPointMarker> markers = All<InvestigationPointMarker>(scene);
            Assert.AreEqual(points.Count, markers.Count, "地点ごとにマーカーが 1 つ。");
            foreach (InvestigationPointMarker marker in markers)
            {
                Assert.IsNotNull(marker.Point, marker.name + "：地点が配線される。");
                Assert.IsNotNull(marker.Ring, marker.name + "：輪がある。");
                Assert.IsNotNull(marker.Ring.sprite, marker.name + "：輪の Sprite が割り当たる（Gizmos に依存しない）。");
                Assert.IsNotNull(marker.Label, marker.name + "：文字がある。");
                Assert.IsNotNull(marker.Label.font, marker.name + "：文字のフォントが割り当たる。");
            }

            List<InvestigationPromptHud> huds = All<InvestigationPromptHud>(scene);
            Assert.AreEqual(1, huds.Count, "短文 UI は 1 つ。");
            Assert.AreSame(coordinator, huds[0].Coordinator);
            Assert.AreSame(stage, huds[0].Stage, "開始行のために試遊段階を知っている。");
            Assert.AreEqual(markers.Count, huds[0].Markers.Count, "候補の案内を全マーカーへ配れる。");

            List<TrialCombatStartInput> starts = All<TrialCombatStartInput>(scene);
            Assert.AreEqual(1, starts.Count, "明示開始の入力は 1 つ。");
            Assert.AreSame(stage, starts[0].Stage);

            // 犬丸の Prefab 実体に表示代理が載っている。
            CompanionActor actor = All<CompanionActor>(scene)[0];
            var proxy = actor.GetComponent<CompanionInvestigationProxyPresenter>();
            Assert.IsNotNull(proxy, "表示代理が付く。");
            Assert.IsNotNull(proxy.ProxyBody.sprite, "代理の本体は仮素材を使う。");
            Assert.IsNotNull(proxy.ProxyArrow.sprite);
            Assert.IsNotNull(proxy.Label);
            Assert.AreSame(actor.GetComponent<CompanionPlaceholderPresenter>(), proxy.Normal, "抑制する通常表示は同じ Body のもの。");
        }

        /// <summary>入力仲介が無ければ不合格（Interact を押しても何も起きない Scene を合格させない。R02）。</summary>
        [Test]
        public void MissingInteractInput_IsDetected()
        {
            Scene scene = BuildTrial();
            Object.DestroyImmediate(All<InvestigationInteractInput>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("InvestigationInteractInput")),
                "入力仲介の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>マーカーの無い地点・文字の無いマーカー・UI の欠落を検出する（R02。§11「必須の仮素材・テキスト参照は厳格検査」）。</summary>
        [Test]
        public void MissingMarkerOrHud_IsDetected()
        {
            Scene scene = BuildTrial();
            List<InvestigationPointMarker> markers = All<InvestigationPointMarker>(scene);
            InvestigationPointMarker first = markers[0];
            string pointName = first.gameObject.name;
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(markers[1].Label.gameObject);
            Object.DestroyImmediate(All<InvestigationPromptHud>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains(pointName) && e.Contains("InvestigationPointMarker")),
                "マーカーの無い地点を検出する:\n- " + string.Join("\n- ", errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("TextMesh")), "文字の欠落を検出する。");
            Assert.IsTrue(errors.Exists(e => e.Contains("InvestigationPromptHud")), "短文 UI の欠落を検出する。");
        }

        /// <summary>明示開始の入力が無ければ不合格（自由探索から戦闘へ進めない。R02）。</summary>
        [Test]
        public void MissingCombatStartInput_IsDetected()
        {
            Scene scene = BuildTrial();
            Object.DestroyImmediate(All<TrialCombatStartInput>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("TrialCombatStartInput")),
                "明示開始の入力の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>表示代理を外した犬丸は不合格（Down 中の調査が見えず、通常表示の抑制もされない。R02）。</summary>
        [Test]
        public void MissingProxyPresenter_IsDetected()
        {
            Scene scene = BuildTrial();
            CompanionActor actor = All<CompanionActor>(scene)[0];
            Object.DestroyImmediate(actor.GetComponent<CompanionInvestigationProxyPresenter>());

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionInvestigationProxyPresenter")),
                "表示代理の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }
    }
}
