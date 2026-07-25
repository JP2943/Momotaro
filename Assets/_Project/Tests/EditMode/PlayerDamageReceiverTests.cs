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
    /// P2-04 受入修正：PlayerVitalsHolder が共通の <see cref="IDamageable"/> として被弾し、PlayerData の防御を
    /// 用いて HP を減算、実適用量（Clamp 込み）を型付き <see cref="HitResult"/> で通知することを検証する。
    /// 体幹・ひるみ・死亡は対象外（Poise/Flinch=0）。SO 原本は変更しない。
    /// </summary>
    public sealed class PlayerDamageReceiverTests
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

        private PlayerData MakePlayerData(int maxHp, float defense)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetField(d, "_maxHp", maxHp);
            SetField(d, "_defense", defense);
            return d;
        }

        private PlayerVitalsHolder MakeHolder(int maxHp, float defense, out PlayerData data)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            var holder = go.AddComponent<PlayerVitalsHolder>();
            data = MakePlayerData(maxHp, defense);
            SetField(holder, "_data", data);
            return holder;
        }

        private static HitInfo Hit(PlayerVitalsHolder target, float preDefenseHp)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero,
                new HitDamage(preDefenseHp, 8f, 20f), true, true, HitId.Single(1));
        }

        private sealed class Recorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
        }

        [Test]
        public void ReceiveHit_UsesPlayerDefense_AndReducesHealth()
        {
            PlayerVitalsHolder holder = MakeHolder(100, 20f, out _);

            // 攻撃側寄与 10 → 防御20 → 最終 8。
            holder.ReceiveHit(Hit(holder, 10f));

            Assert.AreEqual(92, holder.Vitals.Health.Current, "100 - 8 = 92。");
        }

        [Test]
        public void AppliedDamage_EqualsActualReduction_WithZeroPoiseFlinch()
        {
            PlayerVitalsHolder holder = MakeHolder(100, 20f, out _);
            var recorder = new Recorder();
            holder.Results.AddListener(recorder);

            holder.ReceiveHit(Hit(holder, 10f));

            Assert.AreEqual(1, recorder.Received.Count);
            Assert.AreEqual(HitResultKind.Damage, recorder.Received[0].Kind);
            Assert.AreEqual(8f, recorder.Received[0].AppliedDamage.Hp, "実減少量 8。");
            Assert.AreEqual(0f, recorder.Received[0].AppliedDamage.Poise);
            Assert.AreEqual(0f, recorder.Received[0].AppliedDamage.Flinch);
            Assert.AreSame(holder, recorder.Received[0].Target);
        }

        [Test]
        public void Overkill_AppliedEqualsRemainingHp_AndClampsToZero()
        {
            PlayerVitalsHolder holder = MakeHolder(5, 0f, out _); // HP5・防御0
            var recorder = new Recorder();
            holder.Results.AddListener(recorder);

            holder.ReceiveHit(Hit(holder, 8f)); // 最終8 だが残 HP 5

            Assert.AreEqual(0, holder.Vitals.Health.Current, "HP は 0 未満にならない。");
            Assert.AreEqual(5f, recorder.Received[0].AppliedDamage.Hp, "実適用 = 残 HP の 5。");
        }

        [Test]
        public void HittingZeroHp_AppliesZero_ButStillDamageResult()
        {
            PlayerVitalsHolder holder = MakeHolder(5, 0f, out _);
            holder.ReceiveHit(Hit(holder, 100f)); // 0 へ

            var recorder = new Recorder();
            holder.Results.AddListener(recorder);
            holder.ReceiveHit(Hit(holder, 8f)); // HP0 への追撃

            Assert.AreEqual(0f, recorder.Received[0].AppliedDamage.Hp);
            Assert.AreEqual(HitResultKind.Damage, recorder.Received[0].Kind);
        }

        [Test]
        public void ScriptableObjectOriginal_IsNotModified()
        {
            PlayerVitalsHolder holder = MakeHolder(100, 20f, out PlayerData data);
            float defBefore = data.Defense;
            int hpBefore = data.MaxHp;

            for (int i = 0; i < 3; i++)
            {
                holder.ReceiveHit(Hit(holder, 10f));
            }

            Assert.AreEqual(defBefore, data.Defense, "PlayerData の防御は不変。");
            Assert.AreEqual(hpBefore, data.MaxHp, "PlayerData の最大 HP は不変。");
        }
    }
}
