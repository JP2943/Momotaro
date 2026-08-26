using System.Collections.Generic;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05B：<see cref="CameraShakePresenter"/> が対象を短時間だけ揺らし、満了・Stop で基準座標へ戻すことを検証する。
    /// 揺れの決定性（同一シードで同一オフセット）、上限丸め、0 要求の無視、Disable 相当の復帰を確認する。
    /// </summary>
    public sealed class CameraShakePresenterTests
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

        private CameraShakePresenter New()
        {
            var go = new GameObject("Shake");
            _spawned.Add(go);
            return go.AddComponent<CameraShakePresenter>();
        }

        [Test]
        public void Shake_OffsetsThenRestores()
        {
            CameraShakePresenter p = New();
            Vector3 basePos = p.transform.localPosition;

            p.Shake(0.2f, 0.1f);
            Assert.IsTrue(p.IsShaking);
            p.Tick(0.01f);
            Assert.AreNotEqual(basePos, p.transform.localPosition, "揺れ中は基準からずれる。");

            p.Tick(0.1f); // 満了。
            Assert.IsFalse(p.IsShaking);
            Assert.AreEqual(basePos, p.transform.localPosition, "満了で基準へ復帰。");
        }

        [Test]
        public void Deterministic_SameSeed_SameOffset()
        {
            CameraShakePresenter a = New();
            CameraShakePresenter b = New();

            a.Shake(0.3f, 0.1f);
            b.Shake(0.3f, 0.1f);
            a.Tick(0.02f);
            b.Tick(0.02f);

            Assert.AreEqual(a.transform.localPosition, b.transform.localPosition, "同一シードで揺れは決定的。");
        }

        [Test]
        public void Magnitude_ClampedToMax_PerAxis()
        {
            CameraShakePresenter p = New();
            p.Shake(10f, 0.1f); // 上限(0.4)へ丸め。
            p.Tick(0.001f);
            Vector3 off = p.transform.localPosition;
            Assert.LessOrEqual(Mathf.Abs(off.x), 0.4f + 1e-4f, "軸ごとに上限内。");
            Assert.LessOrEqual(Mathf.Abs(off.y), 0.4f + 1e-4f);
        }

        [Test]
        public void ZeroRequest_Ignored()
        {
            CameraShakePresenter p = New();
            p.Shake(0f, 0.1f);
            p.Shake(0.2f, 0f);
            Assert.IsFalse(p.IsShaking, "強さ・秒数が 0 以下なら無処理。");
        }

        [Test]
        public void Stop_RestoresBase()
        {
            CameraShakePresenter p = New();
            Vector3 basePos = p.transform.localPosition;
            p.Shake(0.3f, 0.2f);
            p.Tick(0.02f);
            Assert.AreNotEqual(basePos, p.transform.localPosition);

            p.Stop();
            Assert.IsFalse(p.IsShaking);
            Assert.AreEqual(basePos, p.transform.localPosition, "Stop で基準へ戻す。");
        }
    }
}
