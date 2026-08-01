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
    /// P3-04 修正2：Steppable が命中解決へ反映されることを検証する（§6.3）。ステップ無敵中でも Steppable=false の攻撃は
    /// 貫通し（回避されず被弾）、Steppable=true（既定）は従来どおりステップ無敵で回避される。既存 <see cref="HitInfo"/> の
    /// 既定は true（後方互換）。
    /// </summary>
    public sealed class EnemyAttackSteppableTests
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

        private sealed class FakeEvade : MonoBehaviour, IEvadeState
        {
            public bool Invincible;
            public bool IsInvincible => Invincible;
        }

        private sealed class ResultSpy : IHitResultListener
        {
            public HitResult Last;
            public bool Got;
            public void OnHitResult(in HitResult result) { Got = true; Last = result; }
        }

        private (PlayerVitalsHolder holder, FakeEvade evade, ResultSpy spy) MakePlayer()
        {
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetField(data, "_maxHp", 100);
            SetField(data, "_defense", 0f);

            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var evade = go.AddComponent<FakeEvade>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", data);
            go.SetActive(true);

            var spy = new ResultSpy();
            holder.Results.AddListener(spy);
            return (holder, evade, spy);
        }

        private static HitInfo Hit(IDamageable target, bool steppable)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero, new HitDamage(30f, 0f, 0f),
                guardStaminaDamage: 0f, justGuardPoiseDamage: 0f, guardable: true, justGuardable: true,
                isJustGuardCounter: false, defenseIgnoreRatio: 0f, stunHpMultiplierOverride: 0f, steppable: steppable,
                hitId: HitId.Single(1));
        }

        [Test]
        public void DefaultHitInfo_IsSteppable()
        {
            var h = new HitInfo(null, null, Vector3.forward, Vector3.zero, new HitDamage(1f, 0f, 0f), true, true, HitId.Single(1));
            Assert.IsTrue(h.Steppable, "既存コンストラクタの既定は Steppable=true。");
        }

        [Test]
        public void SteppableAttack_DuringInvincibility_IsEvaded()
        {
            var (holder, evade, spy) = MakePlayer();
            evade.Invincible = true;

            holder.ReceiveHit(Hit(holder, steppable: true));

            Assert.IsTrue(spy.Got);
            Assert.AreEqual(HitResultKind.Evade, spy.Last.Kind, "Steppable 攻撃はステップ無敵で回避。");
            Assert.AreEqual(holder.Vitals.Health.Max, holder.Vitals.Health.Current, "HP は減らない。");
        }

        [Test]
        public void UnsteppableAttack_DuringInvincibility_PiercesAndDamages()
        {
            var (holder, evade, spy) = MakePlayer();
            evade.Invincible = true;

            holder.ReceiveHit(Hit(holder, steppable: false));

            Assert.IsTrue(spy.Got);
            Assert.AreEqual(HitResultKind.Damage, spy.Last.Kind, "Steppable=false はステップ無敵を貫通して被弾。");
            Assert.Less(holder.Vitals.Health.Current, holder.Vitals.Health.Max, "HP が減る。");
        }
    }
}
