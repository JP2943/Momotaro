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

        // ---------------------------------------------------------------- 補助

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

            public void ApplyArrival()
            {
            }
        }
    }
}
