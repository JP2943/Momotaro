using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-02：被弾経路（<see cref="PlayerVitalsHolder.ReceiveHit"/>）が致死（HP0 到達）で一度だけ Defeated を確定・通知し
    /// （<see cref="PlayerDefeatChannel"/>）、以後の追撃は HP・結果・通知を重複発行しないことを検証する（仕様書 §4.1）。
    /// </summary>
    public sealed class PlayerDefeatReceiverTests
    {
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

        private static void SetField(object target, string name, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private sealed class HitRecorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
        }

        private sealed class DefeatRecorder : IPlayerDefeatListener
        {
            public readonly List<PlayerDefeatedEvent> Received = new List<PlayerDefeatedEvent>();
            public void OnPlayerDefeated(in PlayerDefeatedEvent defeated) => Received.Add(defeated);
        }

        private PlayerData MakeData(int maxHp)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetField(d, "_maxHp", maxHp);
            SetField(d, "_defense", 0f);
            SetField(d, "_maxStamina", 100);
            return d;
        }

        private (PlayerVitalsHolder holder, HitRecorder hits, DefeatRecorder defeats) MakePlayer(int maxHp)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", MakeData(maxHp));
            var hits = new HitRecorder();
            var defeats = new DefeatRecorder();
            holder.Results.AddListener(hits);
            holder.Defeats.AddListener(defeats);
            return (holder, hits, defeats);
        }

        private static HitInfo Hit(IDamageable target, float preDefenseHp, int id = 1)
        {
            return new HitInfo(null, target, -Vector3.forward, Vector3.zero, new HitDamage(preDefenseHp, 0f, 0f),
                true, true, HitId.Single(id));
        }

        [Test]
        public void LethalHit_LatchesDefeated_NotifiesOnce_AndStillPublishesDamage()
        {
            var s = MakePlayer(maxHp: 30);
            s.holder.ReceiveHit(Hit(s.holder, 30f)); // HP0

            Assert.IsTrue(s.holder.IsDefeated, "致死で Defeated 確定。");
            Assert.AreEqual(0, s.holder.Vitals.Health.Current, "HP0。");
            Assert.AreEqual(1, s.defeats.Received.Count, "死亡通知は一度だけ。");
            Assert.AreEqual(s.holder.DamageableId, s.defeats.Received[0].PlayerId);
            Assert.AreEqual(HitResultKind.Damage, s.hits.Received[s.hits.Received.Count - 1].Kind, "致死を与えた Hit 自体は Damage 結果を出す。");
            Assert.AreEqual(30f, s.hits.Received[s.hits.Received.Count - 1].AppliedDamage.Hp, "実適用 = 残 HP 30。");
        }

        [Test]
        public void Overkill_DefeatsOnce_AppliedEqualsRemaining()
        {
            var s = MakePlayer(maxHp: 5);
            s.holder.ReceiveHit(Hit(s.holder, 100f));

            Assert.IsTrue(s.holder.IsDefeated);
            Assert.AreEqual(0, s.holder.Vitals.Health.Current);
            Assert.AreEqual(1, s.defeats.Received.Count, "過剰 Damage でも通知一度。");
            Assert.AreEqual(5f, s.hits.Received[s.hits.Received.Count - 1].AppliedDamage.Hp, "実適用 = 残 HP 5。");
        }

        [Test]
        public void AdditionalHitAfterDefeat_NoHpNoResultNoRenotify()
        {
            var s = MakePlayer(maxHp: 5);
            s.holder.ReceiveHit(Hit(s.holder, 100f)); // kill
            int hitsAfterKill = s.hits.Received.Count;

            s.holder.ReceiveHit(Hit(s.holder, 8f, id: 2)); // 追撃

            Assert.AreEqual(0, s.holder.Vitals.Health.Current, "追撃で HP は動かない。");
            Assert.AreEqual(hitsAfterKill, s.hits.Received.Count, "追撃で HitResult を出さない。");
            Assert.AreEqual(1, s.defeats.Received.Count, "死亡通知を重複発行しない。");
        }

        [Test]
        public void SameFrameMultipleHits_NotifyOnce()
        {
            var s = MakePlayer(maxHp: 5);
            s.holder.ReceiveHit(Hit(s.holder, 100f, id: 1)); // 致死
            s.holder.ReceiveHit(Hit(s.holder, 100f, id: 2)); // 同フレーム相当の追撃

            Assert.AreEqual(1, s.defeats.Received.Count, "同一フレーム複数 Hit でも通知一度。");
            Assert.AreEqual(1, s.hits.Received.Count, "2 発目は結果も出さない。");
        }

        [Test]
        public void NonLethalDamage_DoesNotDefeat()
        {
            var s = MakePlayer(maxHp: 100);
            s.holder.ReceiveHit(Hit(s.holder, 10f));

            Assert.IsFalse(s.holder.IsDefeated, "非致死では Defeated にならない。");
            Assert.AreEqual(0, s.defeats.Received.Count, "非致死では死亡通知しない。");
            Assert.AreEqual(90, s.holder.Vitals.Health.Current);
        }

        [Test]
        public void ExactZero_Defeats()
        {
            var s = MakePlayer(maxHp: 10);
            s.holder.ReceiveHit(Hit(s.holder, 10f)); // ちょうど 0
            Assert.IsTrue(s.holder.IsDefeated, "HP がちょうど 0 でも Defeated。");
            Assert.AreEqual(1, s.defeats.Received.Count);
        }
    }
}
