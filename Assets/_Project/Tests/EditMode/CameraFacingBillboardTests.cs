using System.Collections.Generic;
using System.Reflection;
using Momotaro.Presentation.Characters;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// Phase2 表示基盤修正（正対＋Depth 安定化）：<see cref="CameraFacingBillboard"/> の検証。
    /// カメラ正対・Depth Offset（-camera.forward 方向・累積なし・基準アンカーから再計算）・Scale/他 Root 非干渉・
    /// null 安全・Disable/Enable・Prefab 配置を確認する。オフセット計算は純粋関数として分離検証する。
    /// </summary>
    public sealed class CameraFacingBillboardTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";
        private const float AngleEps = 0.01f;
        private const float PosEps = 1e-4f;

        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private GameObject NewGo(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        private Camera NewCamera(Vector3 euler)
        {
            var go = NewGo("Cam");
            var cam = go.AddComponent<Camera>();
            go.transform.rotation = Quaternion.Euler(euler);
            return cam;
        }

        private static void Invoke(object target, string method)
        {
            typeof(CameraFacingBillboard).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);
        }

        // ---- 純粋関数 ----

        [Test]
        public void Compute_ZeroOffset_ReturnsAnchor()
        {
            var anchor = new Vector3(1f, 2f, 3f);
            Assert.AreEqual(anchor, CameraFacingBillboard.ComputeDisplayPosition(anchor, Vector3.forward, 0f));
        }

        [Test]
        public void Compute_PositiveOffset_MovesOppositeCameraForward()
        {
            var anchor = new Vector3(1f, 2f, 3f);
            var fwd = new Vector3(0f, -0.7071f, 0.7071f);
            Vector3 result = CameraFacingBillboard.ComputeDisplayPosition(anchor, fwd, 0.5f);
            Vector3 delta = result - anchor;
            Assert.Less(Vector3.Distance(delta, -fwd * 0.5f), PosEps, "変位は -camera.forward * offset。");
        }

        // ---- コンポーネント ----

        [Test]
        public void ZeroOffset_DoesNotMoveVisualRootFromAnchor()
        {
            var visual = NewGo("VisualRoot");
            var bb = visual.AddComponent<CameraFacingBillboard>();
            bb.SetCamera(NewCamera(new Vector3(45f, 0f, 0f)));
            bb.SetDepthOffset(0f);

            bb.AlignToCamera();

            Assert.Less(Vector3.Distance(visual.transform.position, Vector3.zero), PosEps, "Offset 0 は基準位置から動かない。");
        }

        [Test]
        public void PositiveOffset_MovesTowardCamera_InNegativeForward()
        {
            var visual = NewGo("VisualRoot");
            var bb = visual.AddComponent<CameraFacingBillboard>();
            Camera cam = NewCamera(new Vector3(45f, 0f, 0f));
            bb.SetCamera(cam);
            bb.SetDepthOffset(0.5f);

            bb.AlignToCamera();

            Vector3 expected = Vector3.zero - cam.transform.forward * 0.5f;
            Assert.Less(Vector3.Distance(visual.transform.position, expected), PosEps);

            Vector3 delta = visual.transform.position; // anchor is origin
            Assert.Greater(Vector3.Dot(delta.normalized, -cam.transform.forward.normalized), 0.999f, "カメラ側(-forward)へ。");
            Assert.AreEqual(0.5f, delta.magnitude, 1e-3f, "変位量 = offset。");
        }

        [Test]
        public void Offset_DoesNotAccumulateOverMultipleFrames()
        {
            var visual = NewGo("VisualRoot");
            var bb = visual.AddComponent<CameraFacingBillboard>();
            bb.SetCamera(NewCamera(new Vector3(45f, 0f, 0f)));
            bb.SetDepthOffset(0.5f);

            bb.AlignToCamera();
            Vector3 p1 = visual.transform.position;
            bb.AlignToCamera();
            bb.AlignToCamera();
            Vector3 p2 = visual.transform.position;

            Assert.Less(Vector3.Distance(p1, p2), PosEps, "毎回基準から再計算するため累積しない。");
        }

        [Test]
        public void FollowsParentMovement_KeepingBaseAnchor()
        {
            var charRoot = NewGo("CharacterRoot");
            var visual = NewGo("VisualRoot");
            visual.transform.SetParent(charRoot.transform, false); // base local = (0,0,0)
            var bb = visual.AddComponent<CameraFacingBillboard>();
            Camera cam = NewCamera(new Vector3(45f, 0f, 0f));
            bb.SetCamera(cam);
            bb.SetDepthOffset(0.5f);

            charRoot.transform.position = new Vector3(3f, 0f, 2f);
            bb.AlignToCamera();

            Vector3 expected = new Vector3(3f, 0f, 2f) - cam.transform.forward * 0.5f;
            Assert.Less(Vector3.Distance(visual.transform.position, expected), PosEps, "親移動を基準に反映して追従。");
        }

        [Test]
        public void OffsetDirection_UpdatesAfterCameraRotationChange()
        {
            var visual = NewGo("VisualRoot");
            var bb = visual.AddComponent<CameraFacingBillboard>();
            Camera cam = NewCamera(new Vector3(45f, 0f, 0f));
            bb.SetCamera(cam);
            bb.SetDepthOffset(0.5f);

            cam.transform.rotation = Quaternion.Euler(20f, 50f, 0f);
            bb.AlignToCamera();

            Vector3 expected = Vector3.zero - cam.transform.forward * 0.5f;
            Assert.Less(Vector3.Distance(visual.transform.position, expected), PosEps);
            Assert.Less(Quaternion.Angle(visual.transform.rotation, cam.transform.rotation), AngleEps, "回転も追従。");
        }

        [Test]
        public void RotationMatchesCamera()
        {
            var visual = NewGo("VisualRoot");
            var bb = visual.AddComponent<CameraFacingBillboard>();
            Camera cam = NewCamera(new Vector3(45f, 0f, 0f));
            bb.SetCamera(cam);
            bb.AlignToCamera();
            Assert.Less(Quaternion.Angle(visual.transform.rotation, cam.transform.rotation), AngleEps);
        }

        [Test]
        public void LocalScale_IsUnchanged()
        {
            var visual = NewGo("VisualRoot");
            var bb = visual.AddComponent<CameraFacingBillboard>();
            bb.SetCamera(NewCamera(new Vector3(45f, 0f, 0f)));
            bb.SetDepthOffset(0.5f);

            bb.AlignToCamera();
            Assert.AreEqual(Vector3.one, visual.transform.localScale, "Scale 補正はしない。");
        }

        [Test]
        public void ParentAndSiblingRoots_PositionRotationScale_Unchanged()
        {
            var charRoot = NewGo("CharacterRoot");
            var visual = NewGo("VisualRoot");
            var shadow = NewGo("ShadowRoot");
            visual.transform.SetParent(charRoot.transform, false);
            shadow.transform.SetParent(charRoot.transform, false);
            var bb = visual.AddComponent<CameraFacingBillboard>();
            bb.SetCamera(NewCamera(new Vector3(45f, 0f, 0f)));
            bb.SetDepthOffset(0.5f);

            Vector3 rootPos = charRoot.transform.position, rootScl = charRoot.transform.localScale;
            Quaternion rootRot = charRoot.transform.rotation;
            Vector3 shPos = shadow.transform.position, shScl = shadow.transform.localScale;
            Quaternion shRot = shadow.transform.rotation;

            bb.AlignToCamera();

            Assert.Less(Vector3.Distance(rootPos, charRoot.transform.position), PosEps, "Character Root 位置不変。");
            Assert.Less(Quaternion.Angle(rootRot, charRoot.transform.rotation), AngleEps, "Character Root 回転不変。");
            Assert.AreEqual(rootScl, charRoot.transform.localScale, "Character Root Scale 不変。");
            Assert.Less(Vector3.Distance(shPos, shadow.transform.position), PosEps, "ShadowRoot 位置不変。");
            Assert.Less(Quaternion.Angle(shRot, shadow.transform.rotation), AngleEps, "ShadowRoot 回転不変。");
            Assert.AreEqual(shScl, shadow.transform.localScale, "ShadowRoot Scale 不変。");
        }

        [Test]
        public void NullCamera_DoesNotThrow_NorMoveOrRotate()
        {
            var visual = NewGo("VisualRoot");
            var bb = visual.AddComponent<CameraFacingBillboard>();
            bb.SetCamera(null);
            bb.SetDepthOffset(0.5f);

            Vector3 pos = visual.transform.position;
            Quaternion rot = visual.transform.rotation;
            Assert.DoesNotThrow(() => bb.AlignToCamera());
            Assert.Less(Vector3.Distance(pos, visual.transform.position), PosEps);
            Assert.Less(Quaternion.Angle(rot, visual.transform.rotation), AngleEps);
        }

        [Test]
        public void AfterOnEnable_AnchorAndOffsetRemainCorrect()
        {
            var charRoot = NewGo("CharacterRoot");
            var visual = NewGo("VisualRoot");
            visual.transform.SetParent(charRoot.transform, false);
            var bb = visual.AddComponent<CameraFacingBillboard>();
            Camera cam = NewCamera(new Vector3(45f, 0f, 0f));
            bb.SetCamera(cam);
            bb.SetDepthOffset(0.5f);

            bb.AlignToCamera();
            Vector3 p1 = visual.transform.position;

            Invoke(bb, "OnEnable"); // Disable/Enable 相当
            bb.AlignToCamera();
            Vector3 p2 = visual.transform.position;

            Assert.Less(Vector3.Distance(p1, p2), PosEps, "Enable 後も基準位置とオフセットが維持される。");
        }

        [Test]
        public void Prefab_HasBillboardOnVisualRootOnly_AndNoMissingScripts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Prefab 読込失敗: " + PrefabPath);

            Transform visualRoot = prefab.transform.Find("VisualRoot");
            Assert.IsNotNull(visualRoot, "VisualRoot が見つからない。");
            Assert.IsNotNull(visualRoot.GetComponent<CameraFacingBillboard>(), "VisualRoot に Billboard が存在する。");

            Assert.IsNull(prefab.GetComponent<CameraFacingBillboard>(), "Character Root には付けない。");
            Transform physics = prefab.transform.Find("PhysicsRoot");
            Transform shadow = prefab.transform.Find("ShadowRoot");
            if (physics != null)
            {
                Assert.IsNull(physics.GetComponent<CameraFacingBillboard>(), "PhysicsRoot には付けない。");
            }

            if (shadow != null)
            {
                Assert.IsNull(shadow.GetComponent<CameraFacingBillboard>(), "ShadowRoot には付けない。");
            }

            foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
            {
                Assert.IsNotNull(c, "Missing スクリプト参照がある。");
            }
        }
    }
}
