using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;
using Momotaro.Presentation.Cameras;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 単一常駐 CameraRig の契約（P5.5 §4.3／付録 A）。
    ///
    /// ここで固めるのは 3 つ。
    /// <list type="number">
    /// <item><description>領域集合の索引が <b>Scene handle で引ける</b>こと（付録 A.1）。</description></item>
    /// <item><description>常駐 Camera の提供点が<b>所有者一致</b>で守られること（§5.2）。</description></item>
    /// <item><description><b>到着点の計算が実カメラを動かさない</b>こと（付録 A.3／§7.1）。</description></item>
    /// </list>
    ///
    /// 生成の一度限りと Commit での結び直しは実 Scene が要るので PlayMode で見る。
    /// </summary>
    public sealed class P55ResidentCameraTests
    {
        private readonly List<Object> _spawned = new List<Object>();
        private readonly object _owner = new object();

        [SetUp]
        public void SetUp()
        {
            AreaCameraRegionSetRegistry.ClearForTests();
            AreaCameraOwnerProvider.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            AreaBundleDirectory.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            AreaCameraRegionSetRegistry.ClearForTests();
            AreaCameraOwnerProvider.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            AreaBundleDirectory.ClearForTests();
            AreaCameraRigHost.ResetDiagnosticsForTests();

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
        public void RegionSetRegistry_IsKeyedBySceneHandle()
        {
            AreaCameraRegionSet set = NewRegionSet();
            AreaCameraRegionSetRegistry.Register(set);
            AreaCameraRegionSetRegistry.Register(set);
            Assert.AreEqual(1, AreaCameraRegionSetRegistry.Count, "二重登録はしない。");

            Assert.IsTrue(AreaCameraRegionSetRegistry.TryGetByScene(set.SceneHandle, out AreaCameraRegionSet found));
            Assert.AreSame(set, found);
            Assert.IsFalse(AreaCameraRegionSetRegistry.TryGetByScene(0, out _), "0 は無効な handle。");
            Assert.IsFalse(AreaCameraRegionSetRegistry.TryGetByScene(set.SceneHandle + 9999, out _),
                "知らない Scene では引けない。");

            AreaCameraRegionSetRegistry.Unregister(set);
            Assert.AreEqual(0, AreaCameraRegionSetRegistry.Count);
        }

        [Test]
        public void RegionSetRegistry_DoesNotGuessWhenTwoAreasAreLoaded()
        {
            AreaCameraRegionSetRegistry.Register(NewRegionSet());
            Assert.IsTrue(AreaCameraRegionSetRegistry.TryGetSingle(out _), "1 件なら引ける（単一 Area 構成）。");

            AreaCameraRegionSetRegistry.Register(NewRegionSet());

            // 2 件のときに片方を返すと、先読み中の隣 Area の領域でカメラを clamp する。
            Assert.IsFalse(AreaCameraRegionSetRegistry.TryGetSingle(out _),
                "2 件では当て推量しない（§4.3）。");
        }

        // ---------------------------------------------------------------- 提供点

        [Test]
        public void CameraOwnerProvider_IsOwnerMatched()
        {
            var first = new FakeCameraOwner();
            var second = new FakeCameraOwner();
            var intruder = new object();

            Assert.IsFalse(AreaCameraOwnerProvider.HasOwner);
            Assert.IsFalse(AreaCameraOwnerProvider.TrySetCurrent(null, first), "所有者を添えない指定は通らない。");
            Assert.IsFalse(AreaCameraOwnerProvider.TrySetCurrent(_owner, null), "中身の無い指定も通らない。");

            Assert.IsTrue(AreaCameraOwnerProvider.TrySetCurrent(_owner, first));
            Assert.AreSame(first, AreaCameraOwnerProvider.Current);

            Assert.IsFalse(AreaCameraOwnerProvider.TrySetCurrent(intruder, second), "別の所有者は奪えない。");
            Assert.AreSame(first, AreaCameraOwnerProvider.Current);

            AreaCameraOwnerProvider.ReleaseIfOwner(intruder);
            Assert.AreSame(first, AreaCameraOwnerProvider.Current, "他人の解除は効かない（§5.2）。");

            AreaCameraOwnerProvider.ReleaseIfOwner(_owner);
            Assert.IsFalse(AreaCameraOwnerProvider.HasOwner);
        }

        // ---------------------------------------------------------------- 計算と適用の分離

        /// <summary>
        /// <b>到着点の計算は実カメラを動かさない</b>（付録 A.3／§7.1「事前に境界位置へ瞬間移動させない」）。
        ///
        /// 計算と適用が同じ入口だと、「到着点を知りたい」だけの呼び出しでカメラが跳ぶ。
        /// 現行 Rig は <c>AreaContext.Prepared</c> で <c>SnapToTarget()</c> を呼ぶので、
        /// スライド前に跳ばせない形が要る。
        /// </summary>
        [Test]
        public void ComputingTheArrivalPoint_DoesNotMoveTheCamera()
        {
            AreaCameraRig rig = NewRig(out Transform target);
            Vector3 before = rig.transform.position;

            target.position = new Vector3(7f, 0f, -3f);

            Assert.IsTrue(rig.TryComputeFocus(out Vector3 focus), "到着点は計算できる。");
            Assert.AreEqual(before, rig.transform.position, "計算だけでは Rig を動かさない。");
            Assert.AreEqual(0, rig.SnapCount, "配置回数も増えない。");

            // 適用は別の入口。
            rig.SnapToTarget();

            Assert.AreEqual(1, rig.SnapCount);
            Assert.AreEqual(focus.x, rig.transform.position.x, 0.001f, "適用すると計算どおりの位置へ動く。");
            Assert.AreEqual(focus.z, rig.transform.position.z, 0.001f);
        }

        [Test]
        public void ComputingTheArrivalPoint_FailsWhenTheRigIsNotWired()
        {
            var go = new GameObject("Rig_unwired");
            _spawned.Add(go);
            AreaCameraRig rig = go.AddComponent<AreaCameraRig>();

            Assert.IsFalse(rig.IsWired, "前提：未配線。");
            Assert.IsFalse(rig.TryComputeFocus(out _), "未配線なら計算しない（当て推量の位置を返さない）。");
        }

        // ---------------------------------------------------------------- 結び直し

        /// <summary>
        /// <b>同じ Scene に居る別の Area へも結び直る</b>（P5.5 付録 A.5／§4.3）。
        ///
        /// これは<b>実際に踏んだ欠陥の据え置き</b>である。当初は「結び先が変わったか」を
        /// 領域集合の <c>SceneHandle</c> で判定していた。ところが Single 読込で A を捨てて
        /// B を載せると Unity は<b>同じ Scene handle を使い回す</b>ことがあり、
        /// そのとき「すでに結び付いている」と誤判定して<b>破棄済みの A の追従対象を
        /// 持ち続けた</b>（B 到着後も結び先が A のまま、<c>IsWired</c> が false）。
        ///
        /// <b>handle の衝突を実 Scene で狙って起こすことはできない</b>——割り当ては
        /// それまでに何枚読んだかで変わるので、全件実行では出るのに絞った実行では出ない。
        /// ここでは<b>同じ Scene に 2 つの領域集合を置く</b>ことで handle 一致を
        /// 確実に作り、参照の同一性で判定していることを決定的に固定する。
        /// </summary>
        [Test]
        public void TheResidentRig_RebindsToAnotherAreaEvenWhenTheSceneHandleIsTheSame()
        {
            AreaCameraRigHost host = NewHost();

            AreaCameraRegionSet first = NewWiredRegionSet(out Transform firstTarget);
            AreaCameraRegionSet second = NewWiredRegionSet(out Transform secondTarget);
            Assert.AreEqual(first.SceneHandle, second.SceneHandle,
                "前提：同じ Scene なので handle が一致する（これが実 Scene での取り違えの再現）。");
            Assert.AreNotSame(firstTarget, secondTarget, "前提：追従対象は別物。");

            // 1 件だけ載っている状態を作って結び付ける（2 件では当て推量しない。§4.3）。
            AreaCameraRegionSetRegistry.ClearForTests();
            AreaCameraRegionSetRegistry.Register(first);

            Vector3 before = host.Rig.transform.position;
            Assert.IsTrue(host.TryBindActiveArea(), "1 件目へ結び付く。");
            Assert.AreEqual(1, host.BindCount);
            Assert.AreSame(firstTarget, BoundTarget(host), "追従対象は 1 件目のもの。");

            // <b>Bind は Snap ではない</b>（付録 A.10）。結び付けただけでは実カメラへ触らない。
            // 入口配置が終わる前に置くと、保存位置を基準に配置してしまう。
            Assert.AreEqual(before, host.Rig.transform.position, "結び付けただけでは Rig を動かさない。");
            Assert.AreEqual(0, host.Rig.SnapCount, "配置回数も増えない。");
            Assert.AreEqual(0, host.ApplyCount, "適用回数も増えない。");
            Assert.IsTrue(host.ArrivalPending, "適用は保留される（準備完了を待つ）。");
            Assert.IsTrue(host.Rig.FollowSuspended, "保留中は通常追従も止める。");

            // 準備が終われば適用でき、保留と追従の停止が解ける。
            first.Context.BeginInitialize(new StableId("area_test"), new StableId("entry_test"));
            first.Context.MarkPrepared();
            Assert.IsTrue(first.Context.IsPrepared, "前提：準備完了が立つ。");

            host.ApplyArrival();
            Assert.AreEqual(1, host.Rig.SnapCount, "準備が終わってから即時配置する。");
            Assert.AreEqual(1, host.ApplyCount);
            Assert.IsFalse(host.ArrivalPending, "保留が解ける。");
            Assert.IsFalse(host.Rig.FollowSuspended, "通常追従が戻る。");

            Assert.IsTrue(host.TryBindActiveArea(), "同じ集合なら何度呼んでも通る。");
            Assert.AreEqual(1, host.BindCount, "同じ集合では結び直さない（参照を触らない）。");

            // 入れ替える（実 Scene の Commit に相当）。handle は変わらない。
            AreaCameraRegionSetRegistry.ClearForTests();
            AreaCameraRegionSetRegistry.Register(second);

            Assert.IsTrue(host.TryBindActiveArea(), "2 件目へ結び直す。");
            Assert.AreEqual(2, host.BindCount, "handle が同じでも結び直す（付録 A.5）。");
            Assert.AreSame(secondTarget, BoundTarget(host), "追従対象が 2 件目へ差し替わる。");
        }

        // ---------------------------------------------------------------- 補助

        /// <summary>常駐 Rig の宿を最小構成で組む（Prefab を使わずに配線する）。</summary>
        private AreaCameraRigHost NewHost()
        {
            var rootGo = new GameObject("ResidentCameraRig");
            _spawned.Add(rootGo);

            AreaCameraRig rig = NewRig(out _);
            rig.transform.SetParent(rootGo.transform, false);
            Camera camera = rig.GetComponentInChildren<Camera>(true);

            AreaCameraRigHost host = rootGo.AddComponent<AreaCameraRigHost>();
            var so = new UnityEditor.SerializedObject(host);
            so.FindProperty("_rig").objectReferenceValue = rig;
            so.FindProperty("_camera").objectReferenceValue = camera;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.IsFalse(host.IsWired, "前提：まだどの Area にも結び付いていない。");
            return host;
        }

        /// <summary>追従対象・既定領域まで揃えた領域集合（<see cref="AreaRoot"/> は使わない）。</summary>
        private AreaCameraRegionSet NewWiredRegionSet(out Transform followTarget)
        {
            var targetGo = new GameObject("FollowTarget");
            _spawned.Add(targetGo);
            followTarget = targetGo.transform;

            var regionGo = new GameObject("DefaultRegion");
            _spawned.Add(regionGo);
            AreaCameraRegion region = regionGo.AddComponent<AreaCameraRegion>();
            region.Configure(new StableId("region_set_test"), 0, new Vector2(40f, 40f));

            var contextGo = new GameObject("AreaContext");
            _spawned.Add(contextGo);
            AreaContext context = contextGo.AddComponent<AreaContext>();

            AreaCameraRegionSet set = NewRegionSet();
            set.EditorSet(null, followTarget, region, null, null, context);
            Assert.IsFalse(set.IsReadyForArrival, "前提：まだ入口配置が終わっていない（付録 A.10）。");
            return set;
        }

        /// <summary>Rig が実際に掴んでいる追従対象（private な配線の照合用）。</summary>
        private static Transform BoundTarget(AreaCameraRigHost host)
        {
            System.Reflection.FieldInfo field = typeof(AreaCameraRig).GetField(
                "_target",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field, "AreaCameraRig._target が見つからない（名前が変わった）。");
            return field.GetValue(host.Rig) as Transform;
        }

        private AreaCameraRig NewRig(out Transform target)
        {
            var rigGo = new GameObject("CameraRig");
            _spawned.Add(rigGo);
            AreaCameraRig rig = rigGo.AddComponent<AreaCameraRig>();

            var cameraGo = new GameObject("Main Camera");
            cameraGo.transform.SetParent(rigGo.transform, false);
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;

            var targetGo = new GameObject("Target");
            _spawned.Add(targetGo);
            target = targetGo.transform;

            var regionGo = new GameObject("DefaultRegion");
            _spawned.Add(regionGo);
            AreaCameraRegion region = regionGo.AddComponent<AreaCameraRegion>();
            region.Configure(new StableId("region_test"), 0, new Vector2(60f, 60f));

            rig.Bind(target, camera, region);
            Assert.IsTrue(rig.IsWired, "前提：配線が揃っている。");
            return rig;
        }

        private AreaCameraRegionSet NewRegionSet()
        {
            var go = new GameObject("AreaCameraRegionSet");
            _spawned.Add(go);
            return go.AddComponent<AreaCameraRegionSet>();
        }

        /// <summary>提供点の所有者一致だけを見るための最小実装。</summary>
        private sealed class FakeCameraOwner : IAreaCameraOwner
        {
            public bool IsWired => true;
            public StableId BoundArea => default;
            public int BindCount => 0;
            public int ApplyCount => 0;
            public bool TryBindActiveArea() => true;
            public bool TryComputeArrivalPoint(out Vector3 point)
            {
                point = default;
                return false;
            }

            public bool TryComputeArrivalPoint(
                AreaInstanceHandle destination, Vector3 arrivalPosition, out Vector3 point)
            {
                point = default;
                return false;
            }

            public void ApplyArrival()
            {
            }

            public bool BeginSlide(Vector3 to, float seconds) => false;

            public bool TickSlide(float unscaledDeltaTime) => false;

            public void EndSlide()
            {
            }

            public void CancelSlide()
            {
            }

            public bool IsSliding => false;

            public float SlideEased => 0f;

            public bool TryGetRigPosition(out Vector3 position)
            {
                position = default;
                return false;
            }
        }
    }
}
