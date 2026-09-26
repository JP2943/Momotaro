using System.Collections;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 登録簿の Area 帰属（P5.5 仕様書 §4.3。棚卸し報告書 §2 が挙げた最大の危険箇所）。
    ///
    /// static Registry 5 件はすべて Area Scene の部品が <c>OnEnable</c> で自己登録する。
    /// Area が 2 つ同時に在ると、境界の向こうのレバー・扉が Interact 候補に挙がり、
    /// 犬丸が隣 Area の地点へ調査に行き、<b>敵が境界越しに索敵する</b>。
    ///
    /// <b>構造ゲート（保存時から非 Active）だけでは塞げない。</b> Prepared は Actor を復元するために
    /// 有効化が要り、その時点で登録が走るため。だから登録は止めず、利用者側で除外する。
    ///
    /// EditMode では検査できない（Scene handle を分けられない）ので PlayMode に置く。
    /// 本物の Area Scene を 2 枚重ねずに、実行時 Scene の<b>最小の擬似 Area</b>を隣に置いて見る。
    /// </summary>
    public sealed class P55AreaScopePlayTests
    {
        private readonly List<Object> _spawned = new List<Object>();
        private Scene _neighbourScene;
        private Scene _plainScene;
        private readonly object _owner = new object();

        [SetUp]
        public void SetUp()
        {
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            AreaScope.ResetDiagnostics();
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
        }

        [UnityTearDown]
        public IEnumerator TearDownRoutine()
        {
            PerceptionTargetRegistry.Clear();
            AreaInteractableRegistry.Clear();
            InvestigationPointRegistry.Clear();
            CurrentAreaProvider.ClearForTests();

            if (_neighbourScene.IsValid() && _neighbourScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_neighbourScene);
            }

            if (_plainScene.IsValid() && _plainScene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_plainScene);
            }

            _neighbourScene = default;
            _plainScene = default;

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
            AreaBundleDirectory.ClearForTests();
            AreaScope.ResetDiagnostics();
            yield return null;
        }

        // ---------------------------------------------------------------- 索敵

        /// <summary>
        /// <b>境界越しの索敵をさせない。</b> 隣 Area の対象は最寄り敵対対象にならない（§4.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator Perception_DoesNotSeeTargetsInTheNeighbouringArea()
        {
            yield return BuildTwoAreas();

            // 隣 Area の対象を<b>近く</b>に置く。距離で負けているのに選ばれないことを見たい。
            ScopedTarget near = NewTarget(_neighbourScene, new Vector3(1f, 0f, 0f));
            ScopedTarget far = NewTarget(CurrentScene(), new Vector3(30f, 0f, 0f));

            Assert.IsTrue(PerceptionTargetRegistry.TryGetNearestHostile(
                Vector3.zero, CombatFaction.Enemy, out IPerceptionTarget nearest));
            Assert.AreSame(far, nearest,
                "近くても隣 Area の対象は選ばない（境界越しの索敵をしない）。");
            Assert.AreNotSame(near, nearest);
            Assert.Greater(AreaScope.HiddenCount, 0, "絞り込みが実際に働いている。");
        }

        /// <summary>
        /// <b>現行の指定が無ければ絞らない。</b> 単一 Area 構成（P3.5／P4／P5）とテストを
        /// 従来どおり動かすための安全側の既定（§4.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator Perception_WithoutAnActiveArea_SeesEverything()
        {
            yield return BuildTwoAreas();
            Scene wasCurrent = CurrentScene();
            CurrentAreaProvider.ReleaseIfOwner(_owner);
            Assert.IsFalse(CurrentAreaProvider.HasScope, "前提：現行の指定が無い。");

            ScopedTarget near = NewTarget(_neighbourScene, new Vector3(1f, 0f, 0f));
            NewTarget(wasCurrent, new Vector3(30f, 0f, 0f));

            Assert.IsTrue(PerceptionTargetRegistry.TryGetNearestHostile(
                Vector3.zero, CombatFaction.Enemy, out IPerceptionTarget nearest));
            Assert.AreSame(near, nearest, "指定が無ければ従来どおり最寄りを返す。");
            Assert.AreEqual(0, AreaScope.HiddenCount, "何も隠していない。");
        }

        /// <summary>
        /// <b>Area ではない Scene の物は隠さない。</b> 常駐・起動 Scene・テストが作った Scene に
        /// 居る対象まで消すと、「主人公が誰にも見えない」という形で壊れる（§4.3 の安全側）。
        /// </summary>
        [UnityTest]
        public IEnumerator Perception_StillSeesTargetsOutsideAnyArea()
        {
            yield return BuildTwoAreas();

            // 束を載せていない Scene＝Area ではない。
            _plainScene = SceneManager.CreateScene("P55_PlainScene");
            ScopedTarget outsider = NewTarget(_plainScene, new Vector3(1f, 0f, 0f));
            NewTarget(CurrentScene(), new Vector3(30f, 0f, 0f));

            Assert.IsTrue(PerceptionTargetRegistry.TryGetNearestHostile(
                Vector3.zero, CombatFaction.Enemy, out IPerceptionTarget nearest));
            Assert.AreSame(outsider, nearest, "Area 外の対象は絞り込みの対象外。");
        }

        // ---------------------------------------------------------------- Interact・調査

        /// <summary>
        /// <b>境界の向こうのレバー・扉を Interact 候補にしない</b>／
        /// <b>隣 Area の地点へ犬丸を行かせない</b>（§4.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator InteractablesAndInvestigationPoints_AreLimitedToTheActiveArea()
        {
            yield return BuildTwoAreas();

            ScopedInteractable mine = NewInteractable(CurrentScene(), "lever_mine");
            ScopedInteractable theirs = NewInteractable(_neighbourScene, "lever_theirs");
            ScopedPoint pointMine = NewPoint(CurrentScene(), "point_mine");
            ScopedPoint pointTheirs = NewPoint(_neighbourScene, "point_theirs");

            var interactables = new List<IAreaInteractable>();
            AreaInteractableRegistry.CopyTo(interactables);
            CollectionAssert.Contains(interactables, mine);
            CollectionAssert.DoesNotContain(interactables, theirs,
                "境界の向こうの仕掛けは Interact 候補に挙げない。");

            var points = new List<IInvestigationPoint>();
            InvestigationPointRegistry.CopyTo(points);
            CollectionAssert.Contains(points, pointMine);
            CollectionAssert.DoesNotContain(points, pointTheirs,
                "隣 Area の調査地点へは行かせない。");
        }

        // ---------------------------------------------------------------- 撤去

        /// <summary>
        /// 撤去する Area に属する物だけを見分けられる（Projectile の一括解除の土台。§4.3）。
        /// </summary>
        [UnityTest]
        public IEnumerator BelongsTo_TellsApartTheTwoAreas()
        {
            yield return BuildTwoAreas();

            ScopedTarget mine = NewTarget(CurrentScene(), Vector3.zero);
            ScopedTarget theirs = NewTarget(_neighbourScene, Vector3.zero);

            Assert.IsTrue(AreaScope.BelongsTo(mine, CurrentScene().handle));
            Assert.IsFalse(AreaScope.BelongsTo(theirs, CurrentScene().handle));
            Assert.IsTrue(AreaScope.BelongsTo(theirs, _neighbourScene.handle));
            Assert.IsFalse(AreaScope.BelongsTo(null, CurrentScene().handle), "null は誰にも属さない。");
            Assert.IsFalse(AreaScope.BelongsTo(mine, 0), "handle 0 では当たらない。");
        }

        // ---------------------------------------------------------------- 組み立て

        /// <summary>
        /// 擬似 Area を 2 つ作り、片方を現行に指定する。
        /// <b>本物の Area Scene は読まない。</b> ここで見たいのは登録簿の絞り込みで、
        /// 2 Area 同時の初期化ではない（それは活動隔離の実装が入る工程の仕事）。
        /// </summary>
        private IEnumerator BuildTwoAreas()
        {
            AreaRuntimeBundle current = NewStubBundle("area_p55_current", SceneManager.GetActiveScene());

            _neighbourScene = SceneManager.CreateScene("P55_NeighbourArea");
            NewStubBundle("area_p55_neighbour", _neighbourScene);
            yield return null;

            Assert.AreEqual(2, AreaBundleDirectory.Count, "前提：Area の束が 2 件。");
            Assert.IsTrue(CurrentAreaProvider.TrySetCurrent(_owner, current), "前提：現行を指定できる。");
            AreaScope.ResetDiagnostics();
        }

        private Scene CurrentScene() => CurrentAreaProvider.Current.gameObject.scene;

        private AreaRuntimeBundle NewStubBundle(string areaId, Scene scene)
        {
            var definition = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(definition);
            SetPrivate(definition, "_id", new StableId(areaId));
            var entry = new AreaEntryDefinition();
            entry.EditorSet(new StableId(areaId + "_start"), CardinalDirection.North);
            definition.EditorSet(
                "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity",
                0,
                new List<AreaEntryDefinition> { entry },
                new StableId(areaId + "_start"));

            GameObject go = NewObject("StubAreaRoot_" + areaId, scene);
            AreaRoot root = go.AddComponent<AreaRoot>();
            root.EditorSet(definition, new List<AreaEntryPoint>());
            AreaContext context = go.AddComponent<AreaContext>();
            AreaRuntimeBundle bundle = go.AddComponent<AreaRuntimeBundle>();
            bundle.EditorSet(root, context);
            return bundle;
        }

        private GameObject NewObject(string name, Scene scene)
        {
            var go = new GameObject(name);
            if (scene.IsValid() && scene != SceneManager.GetActiveScene())
            {
                SceneManager.MoveGameObjectToScene(go, scene);
            }
            else
            {
                _spawned.Add(go);
            }

            return go;
        }

        private ScopedTarget NewTarget(Scene scene, Vector3 position)
        {
            GameObject go = NewObject("Target", scene);
            go.transform.position = position;
            return go.AddComponent<ScopedTarget>();
        }

        private ScopedInteractable NewInteractable(Scene scene, string id)
        {
            GameObject go = NewObject("Interactable_" + id, scene);
            ScopedInteractable item = go.AddComponent<ScopedInteractable>();
            item.Id = new StableId(id);
            AreaInteractableRegistry.Register(item);
            return item;
        }

        private ScopedPoint NewPoint(Scene scene, string id)
        {
            GameObject go = NewObject("Point_" + id, scene);
            ScopedPoint point = go.AddComponent<ScopedPoint>();
            point.Id = new StableId(id);
            InvestigationPointRegistry.Register(point);
            return point;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            for (System.Type t = target.GetType(); t != null; t = t.BaseType)
            {
                System.Reflection.FieldInfo f = t.GetField(
                    field,
                    System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.DeclaredOnly);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail("フィールドが見つかりません: " + field);
        }

        // ---------------------------------------------------------------- 検査用の最小部品

        /// <summary>索敵対象の最小実装。Scene 帰属を持たせるために MonoBehaviour にしている。</summary>
        private sealed class ScopedTarget : MonoBehaviour, IPerceptionTarget
        {
            public int ActorId => GetInstanceID();
            public CombatFaction Faction => CombatFaction.Player;
            public Vector3 Position => transform.position;
            public bool IsActive => true;

            private void OnEnable() => PerceptionTargetRegistry.Register(this);
            private void OnDisable() => PerceptionTargetRegistry.Unregister(this);
        }

        /// <summary>Interact 対象の最小実装。</summary>
        private sealed class ScopedInteractable : MonoBehaviour, IAreaInteractable
        {
            public StableId Id { get; set; }
            public StableId InteractableId => Id;
            public StableId AreaId => default;
            public int FloorId => 0;
            public Vector3 InteractionAnchor => transform.position;
            public float InteractionRadius => 0f;
            public bool IsAvailable => true;
            public string Prompt => "調べる";
            public AreaInteractionOutcome Interact() => AreaInteractionOutcome.Accepted();
        }

        /// <summary>調査地点の最小実装。</summary>
        private sealed class ScopedPoint : MonoBehaviour, IInvestigationPoint
        {
            public StableId Id { get; set; }
            public StableId PointId => Id;
            public StableId RequiredCompanion => default;
            public StableId DiscoveryId => default;
            public Vector3 Position => transform.position;
            public Vector3 ApproachPosition => transform.position;
            public Vector3 ApproachFacing => Vector3.forward;
            public bool IsAvailable => true;
            public InvestigationSettings Settings => default;
            public string Prompt => "調べる";
            public string MissingCompanionHint => "";
            public string CompletedText => "";
        }
    }
}
