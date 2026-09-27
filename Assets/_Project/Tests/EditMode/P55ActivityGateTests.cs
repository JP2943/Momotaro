using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Core.World;
using Momotaro.Data.World;
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
                new List<MonoBehaviour> { gate.Behaviour, null });

            gate.Component.Open();
            gate.Component.Open();

            Assert.IsTrue(gate.Systems.activeSelf);
            Assert.IsTrue(gate.Collider.enabled);
            Assert.IsTrue(gate.Behaviour.enabled);
            Assert.AreEqual(2, gate.Component.OpenCount, "呼んだ回数はそのまま数える。");
        }

        // ---------------------------------------------------------------- 補助

        private struct Gate
        {
            public AreaActivityGate Component;
            public AreaRoot Root;
            public GameObject Systems;
            public Collider Collider;
            public MonoBehaviour Behaviour;
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

            AreaActivityGate gate = rootGo.AddComponent<AreaActivityGate>();
            gate.EditorSet(
                root,
                new List<GameObject> { systems },
                new List<Collider> { collider },
                new List<MonoBehaviour> { behaviour });

            return new Gate
            {
                Component = gate,
                Root = root,
                Systems = systems,
                Collider = collider,
                Behaviour = behaviour,
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
