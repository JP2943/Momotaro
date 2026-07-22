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
    /// P2-07：主人公の被弾経路（<see cref="PlayerVitalsHolder"/>）でのガードブレイクとスタミナ回復を検証する。ガードの固定消費で
    /// スタミナ 0 到達 → ブレイク（行動不能）、ブレイク中の被 HP ダメージ ×1.25、ブレイク終了で 25% 回復、非ガード中は
    /// <see cref="PlayerVitalsHolder.Tick"/> で回復、ガード中は停止。
    /// </summary>
    public sealed class PlayerStaminaBreakTests
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

        private PlayerData MakePlayerData(int maxHp, float defense, int maxStamina)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetField(d, "_maxHp", maxHp);
            SetField(d, "_defense", defense);
            SetField(d, "_maxStamina", maxStamina);
            return d;
        }

        private sealed class FakeGuardState : MonoBehaviour, IGuardState
        {
            public bool Guarding = true;
            public Vector3 Fwd = Vector3.forward;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
        }

        private sealed class Recorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
        }

        private (PlayerVitalsHolder holder, FakeGuardState guard, Recorder rec) MakePlayer(
            int maxHp = 100, float defense = 20f, int maxStamina = 100)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var guard = go.AddComponent<FakeGuardState>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", MakePlayerData(maxHp, defense, maxStamina));
            go.SetActive(true);
            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return (holder, guard, rec);
        }

        private static HitInfo FrontGuardHit(IDamageable t, float guardStamina)
        {
            return new HitInfo(null, t, -Vector3.forward, Vector3.zero, new HitDamage(10f, 0f, 0f),
                guardStamina, true, true, HitId.Single(1));
        }

        private static HitInfo BackHit(IDamageable t, float preDefenseHp)
        {
            return new HitInfo(null, t, Vector3.forward, Vector3.zero, new HitDamage(preDefenseHp, 0f, 0f),
                0f, true, true, HitId.Single(2));
        }

        [Test]
        public void GuardConsumingToZero_TriggersGuardBreak()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 20);
            holder.ReceiveHit(FrontGuardHit(holder, 20f)); // 20 → 0

            Assert.AreEqual(HitResultKind.Guard, rec.Received[0].Kind, "その一撃は防御。");
            Assert.AreEqual(0, holder.Vitals.Stamina.Current, "スタミナ 0。");
            Assert.IsTrue(holder.IsGuardBroken, "0 到達でガードブレイク。");
        }

        [Test]
        public void BrokenPlayer_TakesIncreasedHpDamage()
        {
            var (holder, _, rec) = MakePlayer(maxHp: 100, defense: 20f, maxStamina: 20);
            holder.ReceiveHit(FrontGuardHit(holder, 20f)); // ブレイク
            Assert.IsTrue(holder.IsGuardBroken);

            holder.ReceiveHit(BackHit(holder, 10f)); // 貫通：通常8 → ×1.25 = 10
            Assert.AreEqual(HitResultKind.Damage, rec.Received[1].Kind);
            Assert.AreEqual(90, holder.Vitals.Health.Current, "ブレイク中は被HP×1.25（8→10）。");
        }

        [Test]
        public void Break_Recovers25Percent_After1_5Seconds()
        {
            var (holder, _, _) = MakePlayer(maxStamina: 20);
            holder.ReceiveHit(FrontGuardHit(holder, 20f)); // ブレイク
            holder.Tick(1.5f); // ブレイク終了 → 20 の 25% = 5

            Assert.IsFalse(holder.IsGuardBroken, "1.5 秒で行動不能終了。");
            Assert.AreEqual(5, holder.Vitals.Stamina.Current, "最大の 25% 回復。");
        }

        [Test]
        public void Stamina_Regenerates_WhenNotGuarding()
        {
            var (holder, guard, _) = MakePlayer(maxStamina: 100);
            holder.ReceiveHit(FrontGuardHit(holder, 20f)); // 80（ガード中）
            Assert.AreEqual(80, holder.Vitals.Stamina.Current);

            guard.Guarding = false;      // ガード解除
            holder.Tick(1.5f);           // 待機1.0＋0.5×25 = 12.5 → 92.5 → 丸め 93
            Assert.AreEqual(93, holder.Vitals.Stamina.Current, "非ガード中は回復する。");
        }

        [Test]
        public void Stamina_RegenBlocked_WhileGuarding()
        {
            var (holder, _, _) = MakePlayer(maxStamina: 100);
            holder.ReceiveHit(FrontGuardHit(holder, 20f)); // 80
            holder.Tick(1.5f);           // ガード中（Guarding=true のまま）→ 停止
            Assert.AreEqual(80, holder.Vitals.Stamina.Current, "ガード中は回復しない。");
        }
    }
}
