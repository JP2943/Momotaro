using System.Collections.Generic;
using System.Reflection;
using Momotaro.Editor.Phase4;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-FIX F01：仲間検証 Scene の Builder と Validator を検証する。
    ///
    /// 固定するのは 2 つ。<b>生成が決定的であること</b>（同じ入力から同じ構成、再生成で増えない）と、
    /// <b>Validator が移行期の穴を実際に検出すること</b>（活動 Context の欠落・未配線、調停役の欠落、
    /// 追従対象の未設定）。後者は「壊してみせて、検出されること」を確かめる形で置く。
    /// 生成直後の Scene が無エラーで通るだけでは、Validator が何も見ていなくても緑になる。
    ///
    /// この Scene 生成テストは現在の Scene を置換する。<b>未保存の変更があるときだけ</b>自ら Skip し、
    /// 保存済み・無題で未編集の Scene しか開いていない場合は実行する
    /// （無題の空 Scene で常に Skip していたころは、この種のテストが一度も走らないまま緑に見えていた）。
    /// </summary>
    public sealed class Phase4CompanionFieldTests
    {
        private const string TempPath = "Assets/_Project/Scenes/Tests/__P4TmpField__.unity";

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
                        "Phase4 Scene 生成テストは現在の Scene を置換するため、未保存の変更がある場合は実行できません。"
                        + "Scene を保存してから再実行してください。（対象: "
                        + (string.IsNullOrEmpty(s.path) ? "無題Scene" : s.path) + "）");
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

            // Context の OnEnable は Editor では走らないが、別のテストが差した供給元を持ち越さない。
            CompanionActivityProvider.Current = null;
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
                        restorable = false; // 無題 Scene は復元できない（空の新規 Scene で置き換える）。
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

        private static Scene BuildField()
        {
            Phase4CompanionFieldBuilder.BuildResult r = Phase4CompanionFieldBuilder.Build(TempPath);
            Assert.IsTrue(r.Success, "生成成功: " + r.Message);
            Scene scene = SceneManager.GetActiveScene();
            Assert.AreEqual(TempPath, scene.path, "生成 Scene が開いている。");
            return scene;
        }

        private static List<string> Errors(Scene scene)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            Phase4CompanionFieldValidator.Validate(scene, errors, warnings);
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

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        // ================================================================
        // Builder
        // ================================================================

        [Test]
        public void Build_ProducesDeterministicStructure()
        {
            Scene scene = BuildField();

            var rootNames = new List<string>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                rootNames.Add(root.name);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "Environment", "Player", "CameraRig", "Directional Light", "SceneMode",
                    "SpawnCenter", "InvestigationPoints", "Phase4Systems", "Inumaru",
                },
                rootNames,
                "ルートの構成は固定（生成し直せば必ず同じ形へ戻る）:\n- " + string.Join("\n- ", rootNames));

            Assert.AreEqual(1, All<PlayerStateController>(scene).Count, "主人公は 1 体。");
            Assert.AreEqual(1, All<CompanionActor>(scene).Count, "仲間は 1 体。");
            Assert.AreEqual(1, All<CompanionActivityContext>(scene).Count, "活動 Context は 1 つ。");
            Assert.AreEqual(1, All<CombatSessionController>(scene).Count, "戦闘 Session は 1 つ。");

            GameObject player = GameObject.Find("Player");
            Assert.IsNotNull(player);
            Assert.AreEqual(Phase4CompanionFieldBuilder.PlayerPosition, player.transform.position,
                "主人公の初期位置は固定。");
        }

        [Test]
        public void Build_WiresActivityContextToTheSessionInThisScene()
        {
            Scene scene = BuildField();

            CompanionActivityContext context = All<CompanionActivityContext>(scene)[0];

            Assert.IsTrue(context.IsWired, "活動 Context は供給元として成立している。");
            Assert.IsNotNull(context.Session, "戦闘 Session が配線されている。");
            Assert.AreSame(All<CombatSessionController>(scene)[0], context.Session,
                "配線先はこの Scene の Session（別 Scene のものを掴んでいない）。");
        }

        [Test]
        public void Build_WiresCompanionFollowAndGuardianToThePlayer()
        {
            Scene scene = BuildField();

            Transform player = All<PlayerStateController>(scene)[0].transform;
            CompanionActor actor = All<CompanionActor>(scene)[0];

            var follow = actor.GetComponent<CompanionFollowController>();
            Assert.IsNotNull(follow, "追従が付いている。");
            Assert.AreEqual(player.root, follow.Leader.root, "追従対象は主人公。");

            var guardian = actor.GetComponent<CompanionGuardianController>();
            Assert.IsNotNull(guardian, "守護が付いている。");
            Assert.AreEqual(player.root, guardian.ProtectedTarget.root,
                "守護対象も Scene に焼いてある（Runtime の解決に頼らない）。");
        }

        [Test]
        public void Rebuild_SamePath_DoesNotDuplicate()
        {
            Scene first = BuildField();
            int rootsBefore = first.rootCount;
            int companionsBefore = All<CompanionActor>(first).Count;

            Scene second = BuildField(); // 同じパスへ再生成。

            Assert.AreEqual(rootsBefore, second.rootCount, "再生成でルートが増えない。");
            Assert.AreEqual(companionsBefore, All<CompanionActor>(second).Count, "仲間も増えない。");
            Assert.AreEqual(1, All<CompanionActivityContext>(second).Count, "活動 Context も増えない。");
        }

        [Test]
        public void Build_InvalidOutputPath_FailsWithoutSaving()
        {
            Phase4CompanionFieldBuilder.BuildResult r = Phase4CompanionFieldBuilder.Build("Temp/OutsideAssets.unity");

            Assert.IsFalse(r.Success, "Assets 配下でなければ生成しない。");
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SceneAsset>("Temp/OutsideAssets.unity"),
                "壊れた Scene を残さない。");
        }

        // ================================================================
        // Validator（正常系）
        // ================================================================

        [Test]
        public void FreshlyBuiltField_HasNoErrors()
        {
            Scene scene = BuildField();
            List<string> errors = Errors(scene);

            Assert.AreEqual(0, errors.Count,
                "生成直後の検証 Scene は不変条件を満たす:\n- " + string.Join("\n- ", errors));
        }

        [Test]
        public void EmptyScene_ReportsMissingActivityContext()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            List<string> errors = Errors(scene);

            Assert.Greater(errors.Count, 0, "何も無い Scene はエラーになる。");
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionActivityContext")),
                "活動 Context の欠落を報告する:\n- " + string.Join("\n- ", errors));
        }

        // ================================================================
        // Validator（移行期の穴を実際に検出するか）
        // ================================================================

        /// <summary>
        /// 穴 1：活動 Context を取り除くと検出される。取り除いた Scene では
        /// 仲間が移行期フォールバック（常に自由行動）で動き、会話・Pause の区別ができなくなる。
        /// </summary>
        [Test]
        public void MissingActivityContext_IsDetected()
        {
            Scene scene = BuildField();
            Assert.AreEqual(0, Errors(scene).Count, "前提：生成直後はエラー 0。");

            Object.DestroyImmediate(All<CompanionActivityContext>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionActivityContext")),
                "活動 Context を外したら検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>
        /// 穴 1 の裏側：置いてあっても未配線なら常に停止を返す。置き忘れと同じくらい分かりにくいので、
        /// 「置いてある」だけで通してはいけない。
        /// </summary>
        [Test]
        public void UnwiredActivityContext_IsDetected()
        {
            Scene scene = BuildField();
            CompanionActivityContext context = All<CompanionActivityContext>(scene)[0];

            SetPrivateField(context, "_session", null); // 配線を外す（宣言も無いまま）。
            Assert.IsFalse(context.IsWired, "前提：未配線になった。");

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("未配線")),
                "未配線の活動 Context を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>生成された仲間には両方の調停役が載っている（穴 2 が塞がっていることの正面からの確認）。</summary>
        [Test]
        public void Build_CompanionHasBothArbiters()
        {
            Scene scene = BuildField();
            CompanionActor actor = All<CompanionActor>(scene)[0];

            Assert.IsNotNull(actor.GetComponent<CompanionStateArbiter>(),
                "状態の書き手が 1 つに集約されている（無いと許可表も所有権も効かない）。");
            Assert.IsNotNull(actor.GetComponent<CompanionMovementArbiter>(),
                "移動と向きの書き手が 1 つに集約されている（無いと追従と戦闘が Motor を書き合う）。");
        }

        /// <summary>
        /// 穴 2：状態の調停役が欠けた仲間を検出する。
        ///
        /// <b>調停役だけを外すことはできない。</b><c>CompanionActor</c> が <c>RequireComponent</c> で要求しているため、
        /// Unity が破棄を拒む。これは今この Prefab から作る限り欠けないことの裏付けでもあるが、
        /// <b>属性を付ける前に保存された Scene は別</b>で、Unity は既存の serialize 済みオブジェクトへ
        /// 後から必須コンポーネントを足さない。その形（Actor ごと欠けた仲間）を再現して検査する。
        /// </summary>
        [Test]
        public void CompanionWithoutStateArbiter_IsDetected()
        {
            Scene scene = BuildField();
            CompanionActor actor = All<CompanionActor>(scene)[0];
            GameObject go = actor.gameObject;

            Object.DestroyImmediate(actor);                                 // 依存元を先に外す。
            Object.DestroyImmediate(go.GetComponent<CompanionStateArbiter>());

            Assert.IsNull(go.GetComponent<CompanionStateArbiter>(), "前提：調停役が外れた。");

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionStateArbiter")),
                "本体が無くても仲間として走査し、状態の調停役の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>穴 2：移動の調停役が欠けた仲間を検出する（外すと追従と戦闘が Motor を書き合う）。</summary>
        [Test]
        public void CompanionWithoutMovementArbiter_IsDetected()
        {
            Scene scene = BuildField();
            CompanionActor actor = All<CompanionActor>(scene)[0];
            GameObject go = actor.gameObject;

            Object.DestroyImmediate(go.GetComponent<CompanionMotor>());     // 依存元を先に外す。
            Object.DestroyImmediate(go.GetComponent<CompanionMovementArbiter>());

            Assert.IsNull(go.GetComponent<CompanionMovementArbiter>(), "前提：調停役が外れた。");

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionMovementArbiter")),
                "移動の調停役の欠落を、Motor の欠落に紛れさせず検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>追従対象が空の仲間は、Scene 上では誰にも付いていかない（Find* を使わないので自動解決もされない）。</summary>
        [Test]
        public void CompanionWithoutLeader_IsDetected()
        {
            Scene scene = BuildField();
            CompanionActor actor = All<CompanionActor>(scene)[0];

            SetPrivateField(actor.GetComponent<CompanionFollowController>(), "_leader", null);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("追従対象が未設定")),
                "追従対象の未設定を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>仲間が 1 体もいない Scene は、仲間の検証 Scene として成立しない。</summary>
        [Test]
        public void FieldWithoutCompanion_IsDetected()
        {
            Scene scene = BuildField();
            Object.DestroyImmediate(All<CompanionActor>(scene)[0].gameObject);

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionActor")),
                "仲間が居ないことを検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>
        /// 探索（P4-07A）が Scene として成立している：調査地点が置かれ、仲間に探索の駆動が載り、
        /// 地点が主人公の紐の内側にある。どれか 1 つでも欠けると「探索は有効なのに一度も動かない」になる。
        /// </summary>
        [Test]
        public void Build_PlacesInvestigationPointsWithinLeash()
        {
            Scene scene = BuildField();

            List<CompanionInvestigationPoint> points = All<CompanionInvestigationPoint>(scene);
            Assert.AreEqual(Phase4CompanionFieldBuilder.InvestigationPointPositions.Length, points.Count,
                "調査地点を決められた数だけ置く。");

            CompanionActor actor = All<CompanionActor>(scene)[0];
            Assert.IsNotNull(actor.GetComponent<CompanionInvestigationController>(),
                "仲間に探索の駆動が載っている（Data で探索できることになっている以上、必須）。");

            Transform player = All<PlayerStateController>(scene)[0].transform;
            float leash = actor.Data.InvestigateLeashDistance;
            foreach (CompanionInvestigationPoint point in points)
            {
                float distance = FormationSlot.HorizontalDistance(player.position, point.transform.position);
                Assert.LessOrEqual(distance, leash,
                    point.name + " は主人公から紐（" + leash + "m）の内側にある。外だと一度も調べに行かない。");
            }
        }

        /// <summary>調査地点が 1 つも無い Scene は、探索を試せないので検証 Scene として不合格。</summary>
        [Test]
        public void FieldWithoutInvestigationPoints_IsDetected()
        {
            Scene scene = BuildField();
            Assert.AreEqual(0, Errors(scene).Count, "前提：生成直後はエラー 0。");

            foreach (CompanionInvestigationPoint point in All<CompanionInvestigationPoint>(scene))
            {
                Object.DestroyImmediate(point.gameObject);
            }

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionInvestigationPoint")),
                "調査地点の欠落を検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>探索できる仲間から探索の駆動を外したら検出する（Data と実装の食い違い）。</summary>
        [Test]
        public void CompanionWithoutInvestigationController_IsDetected()
        {
            Scene scene = BuildField();
            CompanionActor actor = All<CompanionActor>(scene)[0];
            Assert.IsTrue(actor.Data.CanInvestigate, "前提：Data では探索できる。");

            Object.DestroyImmediate(actor.GetComponent<CompanionInvestigationController>());

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("CompanionInvestigationController")),
                "Data と実装の食い違いを検出する:\n- " + string.Join("\n- ", errors));
        }

        /// <summary>初期状態で敵が置かれていたら検出する（編成は Context Menu から出す約束）。</summary>
        [Test]
        public void InitialEnemies_AreForbidden()
        {
            Scene scene = BuildField();
            Assert.AreEqual(0, Errors(scene).Count, "前提：生成直後はエラー 0。");

            var stray = new GameObject("StrayEnemy");
            stray.AddComponent<Momotaro.Gameplay.Enemy.EnemyActor>();

            List<string> errors = Errors(scene);
            Assert.IsTrue(errors.Exists(e => e.Contains("初期状態の敵")),
                "初期配置の敵を検出する:\n- " + string.Join("\n- ", errors));
        }
    }
}
