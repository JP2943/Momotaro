using System.Collections.Generic;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-06：<see cref="SlashVfxPlacement"/> の純関数を検証する。カメラ無しは「SwingCenter＋高さ・回転 identity」（billboard/DepthOffset なし）、
    /// カメラ有りは「持ち上げ点をカメラ側へ DepthOffset 逃がした位置・カメラ回転」を返すこと（俯瞰カメラでの沈み込み・床/壁の深度欠け対策）。
    /// </summary>
    public sealed class SlashVfxPlacementTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        [Test]
        public void NoCamera_ReturnsAnchorOnly_Identity()
        {
            SlashVfxPlacement.Compute(new Vector3(2f, 0.5f, 3f), null, 1.1f, 0.5f,
                out Vector3 pos, out Quaternion rot);

            Assert.AreEqual(2f, pos.x, 1e-4f);
            Assert.AreEqual(1.6f, pos.y, 1e-4f, "カメラ無し：高さのみ加算（DepthOffset なし）。");
            Assert.AreEqual(3f, pos.z, 1e-4f);
            Assert.Less(Quaternion.Angle(rot, Quaternion.identity), 0.01f, "カメラ無しは billboard しない（identity）。");
        }

        [Test]
        public void WithCamera_AppliesDepthTowardCamera_AndCameraRotation()
        {
            var camGo = new GameObject("Cam");
            _spawned.Add(camGo);
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 12f, -14f), Quaternion.Euler(45f, 0f, 0f));
            var cam = camGo.AddComponent<Camera>();

            SlashVfxPlacement.Compute(new Vector3(2f, 0.3f, 1f), cam, 0.9f, 0.5f,
                out Vector3 pos, out Quaternion rot);

            Vector3 anchor = new Vector3(2f, 0.3f + 0.9f, 1f);
            Vector3 expected = anchor - cam.transform.forward * 0.5f;
            Assert.AreEqual(expected.x, pos.x, 1e-4f);
            Assert.AreEqual(expected.y, pos.y, 1e-4f);
            Assert.AreEqual(expected.z, pos.z, 1e-4f);
            Assert.Less(Quaternion.Angle(rot, cam.transform.rotation), 0.01f, "カメラ回転へ正対。");
        }
    }
}
