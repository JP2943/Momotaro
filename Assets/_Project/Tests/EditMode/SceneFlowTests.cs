using Momotaro.Gameplay.View;
using Momotaro.Infrastructure.SceneFlow;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    public sealed class SceneFlowTests
    {
        [Test]
        public void TransitionGuard_TryBegin_SucceedsOnceThenBlocks()
        {
            var guard = new TransitionGuard();

            Assert.IsTrue(guard.TryBegin(), "初回は開始できる");
            Assert.IsTrue(guard.IsActive);
            Assert.IsFalse(guard.TryBegin(), "進行中は二重開始不可（多重要求を無視）");
        }

        [Test]
        public void TransitionGuard_AfterEnd_CanBeginAgain()
        {
            var guard = new TransitionGuard();
            guard.TryBegin();
            guard.End();

            Assert.IsFalse(guard.IsActive);
            Assert.IsTrue(guard.TryBegin(), "End 後は再開できる");
        }

        [Test]
        public void SceneNames_AreDistinctAndNonEmpty()
        {
            string[] names = { SceneNames.Bootstrap, SceneNames.Launcher, SceneNames.Loading, SceneNames.VsField };
            foreach (string n in names)
            {
                Assert.IsFalse(string.IsNullOrEmpty(n));
            }

            CollectionAssert.AllItemsAreUnique(names);
            Assert.AreEqual("SCN_System_Bootstrap", SceneNames.Bootstrap);
            Assert.AreEqual("SCN_VS_Field", SceneNames.VsField);
        }

        [Test]
        public void NullScreenFader_Alpha_IsClampedToUnitRange()
        {
            var fader = new NullScreenFader();

            fader.Alpha = 2f;
            Assert.AreEqual(1f, fader.Alpha);

            fader.Alpha = -1f;
            Assert.AreEqual(0f, fader.Alpha);

            fader.Alpha = 0.5f;
            Assert.AreEqual(0.5f, fader.Alpha, 0.0001f);
        }
    }
}
