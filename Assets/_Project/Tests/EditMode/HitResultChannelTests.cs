using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-01 受入修正：命中結果の通知契約（購読・配信・スナップショット走査）を検証する。
    /// </summary>
    public sealed class HitResultChannelTests
    {
        private sealed class RecordingListener : IHitResultListener
        {
            public readonly List<HitResultKind> Received = new List<HitResultKind>();

            public void OnHitResult(in HitResult result)
            {
                Received.Add(result.Kind);
            }
        }

        private static HitResult AnyDamage()
        {
            return HitResult.Rejected(HitId.Single(1), null, null);
        }

        [Test]
        public void Publish_DeliversToAllListeners()
        {
            var channel = new HitResultChannel();
            var a = new RecordingListener();
            var b = new RecordingListener();
            channel.AddListener(a);
            channel.AddListener(b);

            channel.Publish(AnyDamage());

            Assert.AreEqual(1, a.Received.Count);
            Assert.AreEqual(1, b.Received.Count);
            Assert.AreEqual(HitResultKind.Rejected, a.Received[0]);
        }

        [Test]
        public void AddListener_IgnoresNullAndDuplicates()
        {
            var channel = new HitResultChannel();
            var a = new RecordingListener();
            channel.AddListener(null);
            channel.AddListener(a);
            channel.AddListener(a);
            Assert.AreEqual(1, channel.ListenerCount);
        }

        [Test]
        public void RemoveListener_StopsDelivery()
        {
            var channel = new HitResultChannel();
            var a = new RecordingListener();
            channel.AddListener(a);
            channel.RemoveListener(a);

            channel.Publish(AnyDamage());
            Assert.AreEqual(0, a.Received.Count);
        }

        [Test]
        public void Publish_IsSafeWhenListenerUnsubscribesDuringNotify()
        {
            var channel = new HitResultChannel();
            SelfRemovingListener remover = null;
            remover = new SelfRemovingListener(channel, () => remover);
            var other = new RecordingListener();
            channel.AddListener(remover);
            channel.AddListener(other);

            Assert.DoesNotThrow(() => channel.Publish(AnyDamage()));
            Assert.AreEqual(1, other.Received.Count, "通知中の購読解除で他の購読者への配信は継続。");
        }

        private sealed class SelfRemovingListener : IHitResultListener
        {
            private readonly HitResultChannel _channel;
            private readonly System.Func<IHitResultListener> _self;

            public SelfRemovingListener(HitResultChannel channel, System.Func<IHitResultListener> self)
            {
                _channel = channel;
                _self = self;
            }

            public void OnHitResult(in HitResult result)
            {
                _channel.RemoveListener(_self());
            }
        }
    }
}
