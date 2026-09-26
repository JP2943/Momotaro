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
    /// P5.5 の参照集合と現行指定（仕様書 §4.3。棚卸し報告書 §3／§5）。
    ///
    /// <b>ここで押さえるのは「常駐が Scene 側を当て推量で掴まない」こと。</b>
    /// P5 は Single 読込だったので <c>FindFirstObjectByType</c> が「いま遊んでいるエリアのもの」と
    /// 一致していた。2 Area 同時読込ではこの等式が崩れ、先読み中の隣エリアを掴んでしまう。
    ///
    /// <b>EditMode では <c>OnEnable</c> が走らない。</b> 登録の自動化そのものは PlayMode
    /// （<c>P55AreaBundlePlayTests</c>）で実 Scene を読んで確かめる。ここでは索引と指定の規則を見る。
    /// </summary>
    public sealed class P55AreaBundleTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();

            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        // ---------------------------------------------------------------- 索引

        [Test]
        public void Register_IsIdempotentAndUnregisterRemovesOnlyThatBundle()
        {
            AreaRuntimeBundle a = NewBundle("area_x");
            AreaRuntimeBundle b = NewBundle("area_y");

            AreaBundleDirectory.Register(a);
            AreaBundleDirectory.Register(a);
            AreaBundleDirectory.Register(b);
            Assert.AreEqual(2, AreaBundleDirectory.Count, "二重登録はしない。");

            AreaBundleDirectory.Unregister(a);
            Assert.AreEqual(1, AreaBundleDirectory.Count);
            Assert.AreSame(b, AreaBundleDirectory.All[0], "他の束は残る。");

            AreaBundleDirectory.Register(null);
            AreaBundleDirectory.Unregister(null);
            Assert.AreEqual(1, AreaBundleDirectory.Count, "null は無視する。");
        }

        [Test]
        public void TryGetSingle_OnlySucceedsWhenExactlyOneAreaIsLoaded()
        {
            Assert.IsFalse(AreaBundleDirectory.TryGetSingle(out _), "0 件では引けない。");

            AreaRuntimeBundle a = NewBundle("area_x");
            AreaBundleDirectory.Register(a);
            Assert.IsTrue(AreaBundleDirectory.TryGetSingle(out AreaRuntimeBundle single));
            Assert.AreSame(a, single);

            AreaBundleDirectory.Register(NewBundle("area_y"));

            // <b>2 件では当て推量しない。</b> ここで片方を返すと、先読み中の隣エリアを
            // 「いま遊んでいるエリア」として扱う事故が静かに通る。
            Assert.IsFalse(AreaBundleDirectory.TryGetSingle(out _),
                "2 Area 載っているときは互換経路を使わせない。");
        }

        [Test]
        public void TryGetByInstance_DistinguishesReloadsOfTheSameArea()
        {
            AreaRuntimeBundle first = NewBundle("area_x");
            AreaRuntimeBundle second = NewBundle("area_x");
            var areaX = new StableId("area_x");
            first.BindInstance(new AreaInstanceHandle(areaX, 1));
            second.BindInstance(new AreaInstanceHandle(areaX, 2));
            AreaBundleDirectory.Register(first);
            AreaBundleDirectory.Register(second);

            Assert.IsTrue(AreaBundleDirectory.TryGetByInstance(
                new AreaInstanceHandle(areaX, 2), out AreaRuntimeBundle found));
            Assert.AreSame(second, found, "同じ AreaId でも世代で区別する。");

            Assert.IsFalse(AreaBundleDirectory.TryGetByInstance(
                new AreaInstanceHandle(areaX, 3), out _), "居ない世代は引けない。");
            Assert.IsFalse(AreaBundleDirectory.TryGetByInstance(AreaInstanceHandle.None, out _),
                "無効なハンドルでは引けない。");
        }

        [Test]
        public void TryGetByScene_NeedsARealSceneHandle()
        {
            AreaRuntimeBundle a = NewBundle("area_x");
            AreaBundleDirectory.Register(a);

            Assert.IsTrue(AreaBundleDirectory.TryGetByScene(a.SceneHandle, out AreaRuntimeBundle found));
            Assert.AreSame(a, found);

            Assert.IsFalse(AreaBundleDirectory.TryGetByScene(0, out _), "0 は無効な handle。");
            Assert.IsFalse(AreaBundleDirectory.TryGetByScene(a.SceneHandle + 12345, out _),
                "知らない Scene では引けない。");
        }

        // ---------------------------------------------------------------- 現行の指定

        [Test]
        public void CurrentArea_IsOwnerMatchedAndCannotBeStolen()
        {
            AreaRuntimeBundle a = NewBundle("area_x");
            AreaRuntimeBundle b = NewBundle("area_y");
            var resident = new object();
            var intruder = new object();

            Assert.IsFalse(CurrentAreaProvider.HasScope, "初期状態は未指定。");
            Assert.IsFalse(CurrentAreaProvider.TrySetCurrent(null, a), "所有者を添えない指定は通らない。");

            Assert.IsTrue(CurrentAreaProvider.TrySetCurrent(resident, a));
            Assert.AreSame(a, CurrentAreaProvider.Current);
            Assert.AreEqual(a.SceneHandle, CurrentAreaProvider.ActiveSceneHandle);

            Assert.IsFalse(CurrentAreaProvider.TrySetCurrent(intruder, b), "別の所有者は奪えない。");
            Assert.AreSame(a, CurrentAreaProvider.Current);

            Assert.IsTrue(CurrentAreaProvider.TrySetCurrent(resident, b), "同じ所有者は差し替えられる。");
            Assert.AreSame(b, CurrentAreaProvider.Current);

            CurrentAreaProvider.ReleaseIfOwner(intruder);
            Assert.AreSame(b, CurrentAreaProvider.Current, "他人の解除は効かない（§5.2 所有者一致）。");

            CurrentAreaProvider.ReleaseIfOwner(resident);
            Assert.IsFalse(CurrentAreaProvider.HasScope);
        }

        [Test]
        public void UnregisteringTheCurrentBundle_DropsTheDesignation()
        {
            AreaRuntimeBundle a = NewBundle("area_x");
            var resident = new object();
            AreaBundleDirectory.Register(a);
            Assert.IsTrue(CurrentAreaProvider.TrySetCurrent(resident, a));

            AreaBundleDirectory.Unregister(a);

            // 破棄済みの束を「現行」のまま残すと、常駐が死んだ参照越しに Scene を触る。
            // 未指定へ落として互換経路に任せるほうが被害が小さい。
            Assert.IsFalse(CurrentAreaProvider.HasScope, "消えた束は現行から外す。");
            Assert.AreEqual(AreaInstanceHandle.None, CurrentAreaProvider.ActiveInstance);
        }

        [Test]
        public void UnregisteringAnotherBundle_DoesNotDropTheDesignation()
        {
            AreaRuntimeBundle a = NewBundle("area_x");
            AreaRuntimeBundle b = NewBundle("area_y");
            var resident = new object();
            AreaBundleDirectory.Register(a);
            AreaBundleDirectory.Register(b);
            Assert.IsTrue(CurrentAreaProvider.TrySetCurrent(resident, a));

            AreaBundleDirectory.Unregister(b);

            Assert.AreSame(a, CurrentAreaProvider.Current, "隣の Area の撤去で現行を失わない。");
        }

        // ---------------------------------------------------------------- 配線と引き当て

        [Test]
        public void IsWired_NeedsTheRootDefinitionAndContext()
        {
            var go = new GameObject("AreaRoot_test");
            _spawned.Add(go);
            AreaRoot root = go.AddComponent<AreaRoot>();
            AreaContext context = go.AddComponent<AreaContext>();
            AreaRuntimeBundle bundle = go.AddComponent<AreaRuntimeBundle>();

            bundle.EditorSet(root, context);
            Assert.IsFalse(bundle.IsWired, "AreaDefinition が無ければ未配線。");

            root.EditorSet(NewDefinition("area_x"), new List<AreaEntryPoint>());
            Assert.IsTrue(bundle.IsWired);
            Assert.AreEqual(new StableId("area_x"), bundle.AreaId);

            bundle.EditorSet(root, null);
            Assert.IsFalse(bundle.IsWired, "AreaContext が無ければ未配線。");
        }

        [Test]
        public void TryResolve_FindsPartsInOtherRootsOfTheSameScene()
        {
            AreaRuntimeBundle bundle = NewBundle("area_x");

            // 束自身の下ではなく、<b>同じ Scene の別の根</b>に置く。
            // 自分の子だけを見る実装だと AreaSystems の下の部品を引けない。
            var other = new GameObject("OtherRoot");
            _spawned.Add(other);
            var child = new GameObject("Child");
            child.transform.SetParent(other.transform, false);
            AreaEntryPoint entry = child.AddComponent<AreaEntryPoint>();

            Assert.IsTrue(bundle.TryResolve(out AreaEntryPoint found), "同じ Scene の別の根からでも引ける。");
            Assert.AreSame(entry, found);
        }

        // <b>非 Active を含めて探すことは EditMode では検査できない。</b>
        // EditMode では <c>GetComponentInChildren&lt;T&gt;()</c>（includeInactive を渡さない形）でも
        // 非 Active の子が返ってくるため、includeInactive を外した欠陷を入れても通ってしまう
        // （欠陷注入 B で判明）。これは PlayMode（P55AreaBundlePlayTests）で見る。

        // Scene をまたいで引かないことは PlayMode（P55AreaBundlePlayTests）で見る。
        // EditMode では追加 Scene を開けない（未保存の無题 Scene が開いていると例外）し、
        // プレビュー Scene は <c>FindFirstObjectByType</c> からも見えないので、
        // 「全 Scene 検索へ戻した」欠陷を入れても検知できない（欠陷注入 A で判明した）。

        // ---------------------------------------------------------------- 補助

        private AreaRuntimeBundle NewBundle(string areaId)
        {
            var go = new GameObject("AreaRoot_" + areaId);
            _spawned.Add(go);
            AreaRoot root = go.AddComponent<AreaRoot>();
            root.EditorSet(NewDefinition(areaId), new List<AreaEntryPoint>());
            AreaContext context = go.AddComponent<AreaContext>();
            AreaRuntimeBundle bundle = go.AddComponent<AreaRuntimeBundle>();
            bundle.EditorSet(root, context);
            return bundle;
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

        /// <summary>private フィールドへ値を入れる（基底クラスまで辿る。既存テストと同じ作法）。</summary>
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
