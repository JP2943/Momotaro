using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.World;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 活動ゲートと先読み要求（P5.5 仕様書 §4.2）。
    ///
    /// <b>「読み込んでから無効化する」では間に合わない</b>のが §4.2 の出発点で、
    /// だから保存状態そのものを閉じておく。ここで固めるのは
    /// 「閉じているとは何か」「開ける／閉めるで何が変わるか」「要求が漏れたときどうなるか」。
    ///
    /// EditMode では <c>Awake</c> が走らないので、読み込み時の自動開放は
    /// PlayMode（<c>P55ActivityGatePlayTests</c>）で見る。
    /// </summary>
    public sealed class P55ActivityGateTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            AreaStagingRequest.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AreaStagingRequest.ResetForTests();

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        // ---------------------------------------------------------------- 先読み要求

        [Test]
        public void StagingRequest_IsSingleAndNeedsAValidArea()
        {
            Assert.IsFalse(AreaStagingRequest.IsRequested);
            Assert.IsFalse(AreaStagingRequest.TryRequest(default), "無効な ID では要求できない。");

            var areaX = new StableId("area_x");
            Assert.IsTrue(AreaStagingRequest.TryRequest(areaX));
            Assert.AreEqual(areaX, AreaStagingRequest.AreaId);

            // 重ねて要求させない。直列化は在留台帳が持つので、ここで 2 件目を受けると
            // 「どちらの Area を待たせているのか」が分からなくなる。
            Assert.IsFalse(AreaStagingRequest.TryRequest(new StableId("area_y")),
                "重ねて要求はできない。");
            Assert.AreEqual(areaX, AreaStagingRequest.AreaId, "宛先は書き換わらない。");
            Assert.AreEqual(1, AreaStagingRequest.RequestedCount);
        }

        [Test]
        public void StagingRequest_IsConsumedOnlyByItsOwnArea()
        {
            var areaX = new StableId("area_x");
            Assert.IsTrue(AreaStagingRequest.TryRequest(areaX));

            // <b>宛先違いは素通り。</b> ここで消費してしまうと、要求した Area が
            // あとから読まれたときに開いた状態で起動する。
            Assert.IsFalse(AreaStagingRequest.TryConsumeFor(new StableId("area_y")));
            Assert.IsTrue(AreaStagingRequest.IsRequested, "宛先違いでは要求は残る。");
            Assert.AreEqual(1, AreaStagingRequest.MismatchCount);

            Assert.IsTrue(AreaStagingRequest.TryConsumeFor(areaX));
            Assert.IsFalse(AreaStagingRequest.IsRequested, "消費したら残さない。");
            Assert.AreEqual(1, AreaStagingRequest.ConsumedCount);

            Assert.IsFalse(AreaStagingRequest.TryConsumeFor(areaX), "二度は消費できない。");
        }

        [Test]
        public void StagingRequest_CanBeWithdrawn()
        {
            Assert.IsTrue(AreaStagingRequest.TryRequest(new StableId("area_x")));
            AreaStagingRequest.Clear();

            // 読込の開始失敗・タイムアウトで取り下げる経路。取り下げられないと、
            // 次に直開きした Scene が閉じたまま起動する（＝何も動かないゲームに見える）。
            Assert.IsFalse(AreaStagingRequest.IsRequested);
            Assert.IsFalse(AreaStagingRequest.TryConsumeFor(new StableId("area_x")));
        }

        // ---------------------------------------------------------------- 配線

        [Test]
        public void IsWired_RefusesAGateThatClosesNothing()
        {
            Gate gate = NewGate();

            gate.Component.EditorSet(gate.Root, new List<GameObject>());
            Assert.IsFalse(gate.Component.IsWired,
                "閉じる対象が 1 つも無いゲートは配線漏れ（閉じているつもりで何も閉じていない）。");

            gate.Component.EditorSet(null, new List<GameObject> { gate.Systems });
            Assert.IsFalse(gate.Component.IsWired, "宛先照合に使う AreaRoot が要る。");

            gate.Component.EditorSet(gate.Root, new List<GameObject> { gate.Systems, null });
            Assert.IsFalse(gate.Component.IsWired, "壊れた参照が混ざっていれば未配線。");

            gate.Component.EditorSet(gate.Root, new List<GameObject> { gate.Systems });
            Assert.IsTrue(gate.Component.IsWired);
        }

        // ---------------------------------------------------------------- 保存状態

        [Test]
        public void IsClosedAsSaved_NamesWhateverWasLeftOpen()
        {
            Gate gate = NewGate();
            gate.Component.EditorCloseForShipping();
            Assert.IsTrue(gate.Component.IsClosedAsSaved(out string _), "閉じた直後は閉じている。");

            gate.Systems.SetActive(true);
            Assert.IsFalse(gate.Component.IsClosedAsSaved(out string rootReason));
            StringAssert.Contains(gate.Systems.name, rootReason, "どの根が開いていたかを言う。");
            gate.Systems.SetActive(false);

            gate.Collider.enabled = true;
            Assert.IsFalse(gate.Component.IsClosedAsSaved(out string colliderReason));
            StringAssert.Contains("Collider", colliderReason);
            gate.Collider.enabled = false;

            gate.Behaviour.enabled = true;
            Assert.IsFalse(gate.Component.IsClosedAsSaved(out string behaviourReason));
            StringAssert.Contains(gate.Behaviour.GetType().Name, behaviourReason);
        }

        // ---------------------------------------------------------------- 開ける・閉める

        [Test]
        public void Open_TurnsOnAllThreeKinds_AndCloseTurnsThemBack()
        {
            Gate gate = NewGate();
            gate.Component.EditorCloseForShipping();
            Assert.IsFalse(gate.Component.IsOpen);

            gate.Component.Open();

            Assert.IsTrue(gate.Component.IsOpen);
            Assert.IsTrue(gate.Systems.activeSelf, "Gameplay の根が動き出す。");
            Assert.IsTrue(gate.Collider.enabled, "地形・仕掛けの物理が戻る。");
            Assert.IsTrue(gate.Behaviour.enabled, "登録する部品が動き出す。");
            Assert.AreEqual(1, gate.Component.OpenCount);

            gate.Component.Close();

            Assert.IsFalse(gate.Component.IsOpen);
            Assert.IsFalse(gate.Systems.activeSelf);
            Assert.IsFalse(gate.Collider.enabled);
            Assert.IsFalse(gate.Behaviour.enabled);
            Assert.AreEqual(1, gate.Component.CloseCount);
        }

        [Test]
        public void Open_IsIdempotentAndSurvivesBrokenReferences()
        {
            Gate gate = NewGate();

            // 壊れた参照が混ざっていても、残りは開ける。
            // ここで例外を投げると「1 つ壊れているだけで Area が丸ごと起動しない」ことになる。
            gate.Component.EditorSet(
                gate.Root,
                new List<GameObject> { gate.Systems, null },
                new List<Collider> { gate.Collider, null },
                new List<Behaviour> { gate.Behaviour, null });

            gate.Component.Open();
            gate.Component.Open();

            Assert.IsTrue(gate.Systems.activeSelf);
            Assert.IsTrue(gate.Collider.enabled);
            Assert.IsTrue(gate.Behaviour.enabled);

            // 2 回目は<b>無操作</b>（重複呼び出しで状態を押し直さない。R12）。
            Assert.AreEqual(1, gate.Component.OpenCount, "実際に開けたのは 1 回。");
            Assert.AreEqual(1, gate.Component.RedundantOpenCount, "2 回目は無操作として数える。");
        }

        // ---------------------------------------------------------------- 表示系（先読みのときだけ）

        /// <summary>
        /// 表示系（Camera／AudioListener／Light）は<b>有効のまま出荷する</b>（P5.5 §4.2）。
        ///
        /// 保存時に切っておくと、Editor で Scene を開いたときに Game ビューが真っ黒になる。
        /// これらには登録や購読の副作用が無いので、先読みのときに <c>Awake</c> で切れば間に合う。
        /// </summary>
        [Test]
        public void StagedOnlyVisuals_ShipEnabled_AndAreNotPartOfTheSavedClosedState()
        {
            Gate gate = NewGate();
            gate.Component.EditorCloseForShipping();

            Assert.IsFalse(gate.Systems.activeSelf, "Gameplay は閉じて出荷する。");
            Assert.IsTrue(gate.Visual.enabled, "表示系は有効のまま出荷する。");
            Assert.IsTrue(gate.Component.IsClosedAsSaved(out string _),
                "表示系が有効でも「閉じている」と見なす。");
        }

        /// <summary>閉めれば表示系も止まり、開けば戻る（先読みのやり直しに必要）。</summary>
        [Test]
        public void Close_AlsoStopsTheVisuals_AndOpenBringsThemBack()
        {
            Gate gate = NewGate();
            gate.Component.EditorCloseForShipping();

            gate.Component.Close();
            Assert.IsFalse(gate.Visual.enabled, "閉めれば Camera も描かない。");

            gate.Component.Open();
            Assert.IsTrue(gate.Visual.enabled);
            Assert.IsTrue(gate.Systems.activeSelf);
        }

        // ---------------------------------------------------------------- 再開と仕掛けの状態

        /// <summary>
        /// <b>開通済みの門が再開で再び塞がらない</b>（GPT レビュー R11 の指摘 2）。
        ///
        /// 門は開通したときに自分の Collider と Obstacle を無効にする。
        /// ゲートが再開で一律に有効化すると、<b>見た目は開いているのに通れない</b>になる。
        /// 遷移失敗から出発側を再開する経路で起きる。
        /// </summary>
        [Test]
        public void ReopeningTheGate_DoesNotCloseADoorThatWasAlreadyOpened()
        {
            Gate gate = NewGate();
            var doorGo = new GameObject("Door");
            _spawned.Add(doorGo);
            doorGo.transform.SetParent(gate.Root.transform, false);
            var visualGo = new GameObject("ClosedVisual");
            visualGo.transform.SetParent(doorGo.transform, false);
            Collider blocker = doorGo.AddComponent<BoxCollider>();
            var obstacle = doorGo.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            AreaFlagDoor door = doorGo.AddComponent<AreaFlagDoor>();
            door.Bind(new StableId("flag_x"), blocker, visualGo, obstacle);

            // 門の Collider／Obstacle もゲートの停止対象（実 Scene と同じ構成）。
            gate.Component.EditorSet(
                gate.Root,
                new List<GameObject> { gate.Systems },
                new List<Collider> { gate.Collider, blocker },
                new List<Behaviour> { gate.Behaviour, obstacle, door });
            gate.Component.EditorCloseForShipping();
            gate.Component.Open();

            // 1. 門を開通させる。
            Assert.IsTrue(door.TryApplyOpened(out string _));
            Assert.IsTrue(door.IsOpened);
            Assert.IsFalse(blocker.enabled, "前提：通行が開いている。");
            Assert.IsFalse(obstacle.enabled, "前提：くり抜きも外れている。");
            Assert.IsFalse(visualGo.activeSelf, "前提：見た目も開いている。");
            Assert.AreEqual(1, door.AppliedCount);

            // 2. 活動ゲートを閉めて、3. 開け直す。
            gate.Component.Close();
            gate.Component.Open();

            // 4. 門の状態は保たれている。
            Assert.IsFalse(blocker.enabled, "再開で門が再び塞がらない。");
            Assert.IsFalse(obstacle.enabled, "くり抜きも戻らない。");
            Assert.IsFalse(visualGo.activeSelf, "見た目も開通のまま。");
            Assert.AreEqual(1, door.AppliedCount, "開通回数は増やさない（§7.3）。");

            // その他の停止対象はちゃんと戻っている。
            Assert.IsTrue(gate.Collider.enabled, "門以外の物理は戻る。");
            Assert.IsTrue(gate.Systems.activeSelf, "Gameplay も戻る。");

            // 重複した Open() でも壊れない。
            gate.Component.Open();
            Assert.IsFalse(blocker.enabled);
            Assert.AreEqual(1, door.AppliedCount);
        }

        /// <summary>
        /// 初回起動は「保存時のすべて停止」と別扱いで、一律に有効化する（§4.2）。
        /// ここを復元にしてしまうと、何も覚えていないので Area が永久に止まる。
        /// </summary>
        [Test]
        public void TheFirstOpen_EnablesEverything_BecauseThereIsNothingToRestore()
        {
            Gate gate = NewGate();
            gate.Component.EditorCloseForShipping();

            Assert.IsFalse(gate.Component.HasRestoreState, "出荷状態では覚えていない。");

            gate.Component.Open();

            Assert.IsTrue(gate.Systems.activeSelf);
            Assert.IsTrue(gate.Collider.enabled);
            Assert.IsTrue(gate.Behaviour.enabled);

            gate.Component.Close();
            Assert.IsTrue(gate.Component.HasRestoreState, "閉めれば覚える。");
        }

        /// <summary>
        /// 門の開通状態は<b>改めて適用し直せる</b>（§4.2の再同期の土台）。
        /// 以前は <c>IsOpened</c> なら即座に true を返していたため、何も直らなかった。
        /// </summary>
        [Test]
        public void ReapplyingAnOpenedDoor_FixesThePhysicsWithoutCountingAgain()
        {
            var doorGo = new GameObject("Door");
            _spawned.Add(doorGo);
            var visualGo = new GameObject("ClosedVisual");
            visualGo.transform.SetParent(doorGo.transform, false);
            Collider blocker = doorGo.AddComponent<BoxCollider>();
            var obstacle = doorGo.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            AreaFlagDoor door = doorGo.AddComponent<AreaFlagDoor>();
            door.Bind(new StableId("flag_x"), blocker, visualGo, obstacle);

            Assert.IsTrue(door.TryApplyOpened(out string _));
            Assert.AreEqual(1, door.AppliedCount);

            // 他の事情で物理が有効に戻ってしまった状況を作る。
            blocker.enabled = true;
            obstacle.enabled = true;
            visualGo.SetActive(true);

            Assert.IsTrue(door.TryApplyOpened(out string _), "開通済みでも真として返る。");
            Assert.IsFalse(blocker.enabled, "再適用で通行が直る。");
            Assert.IsFalse(obstacle.enabled, "くり抜きも直る。");
            Assert.IsFalse(visualGo.activeSelf, "見た目も直る。");
            Assert.AreEqual(1, door.AppliedCount, "開通回数は増やさない。");
            Assert.AreEqual(1, door.ReappliedCount, "押し直した回数は別に数える。");

            // 開通していない門には何もしない。
            var closedGo = new GameObject("ClosedDoor");
            _spawned.Add(closedGo);
            Collider closedBlocker = closedGo.AddComponent<BoxCollider>();
            AreaFlagDoor closed = closedGo.AddComponent<AreaFlagDoor>();
            closed.Bind(new StableId("flag_y"), closedBlocker, null);
            Assert.IsFalse(closed.TryReapplyOpened(out string _), "開通していない門を勝手に開けない。");
            Assert.IsTrue(closedBlocker.enabled);
        }

        /// <summary>
        /// <b>初回起動のあとに門を開通しても、重複 Open で塞がらない</b>（R12 の経路 1）。
        ///
        /// この時点では復元記録がまだ無いので、打ち切りがなければ
        /// <c>Apply(true)</c> が門の Collider／Obstacle を再有効化してしまう。
        /// </summary>
        [Test]
        public void ADoorOpenedAfterTheFirstStart_SurvivesARedundantOpen()
        {
            DoorFixture door = NewGateWithDoor();
            door.Gate.Component.EditorCloseForShipping();
            door.Gate.Component.Open();
            Assert.IsFalse(door.Gate.Component.HasRestoreState, "前提：復元記録はまだ無い。");

            Assert.IsTrue(door.Door.TryApplyOpened(out string _));
            AssertDoorStaysOpen(door, "前提");

            door.Gate.Component.Open();

            AssertDoorStaysOpen(door, "重複 Open のあと");
            Assert.AreEqual(1, door.Gate.Component.RedundantOpenCount);
        }

        /// <summary>
        /// <b>再開のあとに門を開通しても、重複 Open で塞がらない</b>（R12 の経路 2）。
        ///
        /// 復元記録には「門が閉じていた状態」が入っているので、
        /// 打ち切りがなければそれを復元して<b>開通を巻き戻す</b>。
        /// </summary>
        [Test]
        public void ADoorOpenedAfterAReopen_SurvivesARedundantOpen()
        {
            DoorFixture door = NewGateWithDoor();
            door.Gate.Component.EditorCloseForShipping();
            door.Gate.Component.Open();

            // 門は閉じたまま閉めて、その状態を復元記録に入れる。
            Assert.IsTrue(door.Blocker.enabled, "前提：門は閉じている。");
            door.Gate.Component.Close();
            door.Gate.Component.Open();
            Assert.IsTrue(door.Gate.Component.HasRestoreState, "前提：復元記録に「閉じていた」が入っている。");
            Assert.IsTrue(door.Blocker.enabled, "前提：復元でも閉じたまま。");

            // そのあとに開通する。
            Assert.IsTrue(door.Door.TryApplyOpened(out string _));
            AssertDoorStaysOpen(door, "前提");

            door.Gate.Component.Open();

            AssertDoorStaysOpen(door, "重複 Open のあと");
            Assert.AreEqual(1, door.Gate.Component.RedundantOpenCount);
        }

        // ---------------------------------------------------------------- 門付きの組み立て

        private struct DoorFixture
        {
            public Gate Gate;
            public AreaFlagDoor Door;
            public Collider Blocker;
            public UnityEngine.AI.NavMeshObstacle Obstacle;
            public GameObject ClosedVisual;
        }

        /// <summary>実 Scene と同じ構成（門の Collider／Obstacle もゲートの停止対象）を作る。</summary>
        private DoorFixture NewGateWithDoor()
        {
            Gate gate = NewGate();
            var doorGo = new GameObject("Door");
            _spawned.Add(doorGo);
            doorGo.transform.SetParent(gate.Root.transform, false);
            var visualGo = new GameObject("ClosedVisual");
            visualGo.transform.SetParent(doorGo.transform, false);
            Collider blocker = doorGo.AddComponent<BoxCollider>();
            var obstacle = doorGo.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            AreaFlagDoor door = doorGo.AddComponent<AreaFlagDoor>();
            door.Bind(new StableId("flag_x"), blocker, visualGo, obstacle);

            gate.Component.EditorSet(
                gate.Root,
                new List<GameObject> { gate.Systems },
                new List<Collider> { gate.Collider, blocker },
                new List<Behaviour> { gate.Behaviour, obstacle, door });

            return new DoorFixture
            {
                Gate = gate,
                Door = door,
                Blocker = blocker,
                Obstacle = obstacle,
                ClosedVisual = visualGo,
            };
        }

        private static void AssertDoorStaysOpen(DoorFixture door, string label)
        {
            Assert.IsFalse(door.Blocker.enabled, label + "：通行が開いている。");
            Assert.IsFalse(door.Obstacle.enabled, label + "：くり抜きが外れている。");
            Assert.IsFalse(door.ClosedVisual.activeSelf, label + "：見た目も開通している。");
            Assert.AreEqual(1, door.Door.AppliedCount, label + "：開通回数は 1。");
        }

        // ---------------------------------------------------------------- 補助

        private struct Gate
        {
            public AreaActivityGate Component;
            public AreaRoot Root;
            public GameObject Systems;
            public Collider Collider;
            public MonoBehaviour Behaviour;
            public Behaviour Visual;
        }

        private Gate NewGate()
        {
            var rootGo = new GameObject("AreaRoot_test");
            _spawned.Add(rootGo);
            AreaRoot root = rootGo.AddComponent<AreaRoot>();
            root.EditorSet(NewDefinition("area_x"), new List<AreaEntryPoint>());

            var systems = new GameObject("AreaSystems");
            systems.transform.SetParent(rootGo.transform, false);
            systems.AddComponent<AreaContext>();

            var terrain = new GameObject("Terrain");
            terrain.transform.SetParent(rootGo.transform, false);
            Collider collider = terrain.AddComponent<BoxCollider>();

            var fixture = new GameObject("Fixture");
            fixture.transform.SetParent(rootGo.transform, false);
            MonoBehaviour behaviour = fixture.AddComponent<AreaEntryPoint>();

            // 表示系は AreaRoot の外（実 Scene と同じ配置）。
            var cameraGo = new GameObject("Main Camera");
            _spawned.Add(cameraGo);
            Behaviour visual = cameraGo.AddComponent<Camera>();

            AreaActivityGate gate = rootGo.AddComponent<AreaActivityGate>();
            gate.EditorSet(
                root,
                new List<GameObject> { systems },
                new List<Collider> { collider },
                new List<Behaviour> { behaviour },
                new List<Behaviour> { visual });

            return new Gate
            {
                Component = gate,
                Root = root,
                Systems = systems,
                Collider = collider,
                Behaviour = behaviour,
                Visual = visual,
            };
        }

        private AreaDefinition NewDefinition(string areaId)
        {
            var asset = ScriptableObject.CreateInstance<AreaDefinition>();
            _spawned.Add(asset);
            SetPrivate(asset, "_id", new StableId(areaId));
            var entry = new AreaEntryDefinition();
            entry.EditorSet(new StableId(areaId + "_start"), CardinalDirection.North);
            asset.EditorSet(
                "Assets/_Project/Scenes/Tests/Phase5/SCN_Phase5_AreaA.unity",
                0,
                new List<AreaEntryDefinition> { entry },
                new StableId(areaId + "_start"));
            return asset;
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
    }
}
