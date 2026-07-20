using System.Collections.Generic;
using Momotaro.Gameplay.Vitals;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class VitalTests
    {
        [Test]
        public void Constructor_DefaultsCurrentToMax()
        {
            var v = new Vital(100);
            Assert.AreEqual(100, v.Max);
            Assert.AreEqual(100, v.Current);
        }

        [Test]
        public void Constructor_ClampsInitialCurrent()
        {
            Assert.AreEqual(50, new Vital(100, 50).Current);
            Assert.AreEqual(100, new Vital(100, 999).Current);
            Assert.AreEqual(0, new Vital(100, -10).Current);
        }

        [Test]
        public void Change_ClampsBetweenZeroAndMax()
        {
            var v = new Vital(100, 50);
            v.Change(-70);
            Assert.AreEqual(0, v.Current);
            v.Change(999);
            Assert.AreEqual(100, v.Current);
        }

        [Test]
        public void SetCurrent_Clamps()
        {
            var v = new Vital(100);
            v.SetCurrent(30);
            Assert.AreEqual(30, v.Current);
            v.SetCurrent(-5);
            Assert.AreEqual(0, v.Current);
        }

        [Test]
        public void Changed_FiresWithPreviousCurrentMax_OnlyWhenChanged()
        {
            var v = new Vital(100);
            var events = new List<VitalChanged>();
            v.Changed += events.Add;

            v.Change(-40); // 100 -> 60
            v.SetCurrent(60); // no change, no event

            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(100, events[0].Previous);
            Assert.AreEqual(60, events[0].Current);
            Assert.AreEqual(100, events[0].Max);
        }

        [Test]
        public void SetMax_Lower_ClampsCurrentAndNotifies()
        {
            var v = new Vital(100);
            var events = new List<VitalChanged>();
            v.Changed += events.Add;

            v.SetMax(40);
            Assert.AreEqual(40, v.Max);
            Assert.AreEqual(40, v.Current, "現在値が新Maxへ Clamp される");
            Assert.AreEqual(1, events.Count);
        }

        [Test]
        public void SetMax_Higher_KeepsCurrent_ButNotifiesMaxChange()
        {
            var v = new Vital(100, 30);
            var events = new List<VitalChanged>();
            v.Changed += events.Add;

            v.SetMax(200);
            Assert.AreEqual(200, v.Max);
            Assert.AreEqual(30, v.Current, "現在値は保持");
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(200, events[0].Max);
        }

        [Test]
        public void SetMax_Same_DoesNotNotify()
        {
            var v = new Vital(100);
            int count = 0;
            v.Changed += _ => count++;
            v.SetMax(100);
            Assert.AreEqual(0, count);
        }

        [Test]
        public void Ratio_IsCurrentOverMax()
        {
            var v = new Vital(200, 50);
            Assert.AreEqual(0.25f, v.Ratio, 1e-5f);
        }
    }
}
