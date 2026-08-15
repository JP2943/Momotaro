using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-02：音刺激（<see cref="NoiseCatalog"/> 半径・<see cref="NoiseChannel"/> 採番/配信・<see cref="NoiseStimulus"/> 保持）を
    /// 検証する（§4.2/Table 8）。純粋・再現可能。
    /// </summary>
    public sealed class EnemyNoiseTests
    {
        private sealed class FakeListener : INoiseListener
        {
            public int Count;
            public NoiseStimulus Last;
            public void OnNoise(in NoiseStimulus stimulus) { Count++; Last = stimulus; }
        }

        [Test]
        public void Catalog_RadiiMatchTable8()
        {
            Assert.AreEqual(3.0f, NoiseCatalog.Radius(NoiseKind.Step), 1e-4f);
            Assert.AreEqual(4.0f, NoiseCatalog.Radius(NoiseKind.Attack), 1e-4f);
            Assert.AreEqual(3.0f, NoiseCatalog.Radius(NoiseKind.SpecialCharge), 1e-4f);
            Assert.AreEqual(8.0f, NoiseCatalog.Radius(NoiseKind.SpecialActivate), 1e-4f);
            Assert.AreEqual(6.0f, NoiseCatalog.Radius(NoiseKind.EnemyAlertVoice), 1e-4f);
            Assert.AreEqual(5.0f, NoiseCatalog.Radius(NoiseKind.FlurryOrArt), 1e-4f);
            Assert.AreEqual(0f, NoiseCatalog.Radius(NoiseKind.Movement), 1e-4f, "通常移動は半径なし。");
            Assert.AreEqual(6.0f, NoiseCatalog.AlertShareRadius, 1e-4f);
        }

        [Test]
        public void Channel_NextStimulusId_Increments()
        {
            var c = new NoiseChannel();
            int a = c.NextStimulusId();
            int b = c.NextStimulusId();
            Assert.AreNotEqual(a, b, "刺激 ID は一意に採番される。");
            Assert.Greater(b, a);
        }

        [Test]
        public void Channel_PublishesToSubscribers_AndStopsAfterRemove()
        {
            var c = new NoiseChannel();
            var l = new FakeListener();
            c.AddListener(l);
            c.AddListener(l); // 重複登録は無視
            Assert.AreEqual(1, c.ListenerCount);

            c.Publish(new NoiseStimulus(c.NextStimulusId(), 42, new Vector3(1, 0, 2), 4f, 0f, NoiseKind.Attack, 0));
            Assert.AreEqual(1, l.Count);
            Assert.AreEqual(NoiseKind.Attack, l.Last.Kind);
            Assert.AreEqual(42, l.Last.SourceActorId);
            Assert.AreEqual(new Vector3(1, 0, 2), l.Last.Position);

            c.RemoveListener(l);
            c.Publish(new NoiseStimulus(c.NextStimulusId(), 1, Vector3.zero, 3f, 0f, NoiseKind.Step, 0));
            Assert.AreEqual(1, l.Count, "解除後は配信されない。");
        }

        [Test]
        public void Stimulus_RetainsFields()
        {
            var s = new NoiseStimulus(7, 99, new Vector3(3, 0, 4), 6f, 12.5f, NoiseKind.EnemyAlertVoice, 1);
            Assert.AreEqual(7, s.StimulusId);
            Assert.AreEqual(99, s.SourceActorId);
            Assert.AreEqual(6f, s.Radius, 1e-4f);
            Assert.AreEqual(12.5f, s.TimeStamp, 1e-4f);
            Assert.AreEqual(NoiseKind.EnemyAlertVoice, s.Kind);
            Assert.AreEqual(1, s.ShareGeneration);
        }
    }
}
