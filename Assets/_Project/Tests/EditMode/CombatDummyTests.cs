using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-04：CombatDummy が命中の攻撃側寄与（防御適用前 HP）へ自身の防御を適用し、Vital（HP）へ減算し、
    /// HP を 0..Max へ Clamp、撃破/Reset、型付き HitResult 通知を行うことを検証する。
    /// </summary>
    public sealed class CombatDummyTests
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

        private EnemyData MakeEnemy(int maxHp, float defense)
        {
            var e = ScriptableObject.CreateInstance<EnemyData>();
            _spawned.Add(e);
            SetField(e, "_maxHp", maxHp);
            SetField(e, "_defense", defense);
            return e;
        }

        private CombatDummy MakeDummy(int maxHp, float defense)
        {
            var go = new GameObject("Dummy");
            _spawned.Add(go);
            go.SetActive(false);               // _data 設定後に Awake させる
            var dummy = go.AddComponent<CombatDummy>();
            SetField(dummy, "_data", MakeEnemy(maxHp, defense));
            go.SetActive(true);                // ここで Awake → HP=Max 構築
            return dummy;
        }

        private static HitInfo Hit(CombatDummy target, float preDefenseHp)
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
        public void ReceiveHit_AppliesDefenseAndSubtractsHp()
        {
            var dummy = MakeDummy(100, 20f);
            Assert.AreEqual(100, dummy.CurrentHp);
            Assert.AreEqual(100, dummy.MaxHp);

            // 攻撃側寄与 10（=100×1.0×0.1）→ 対象防御20で最終 8。
            dummy.ReceiveHit(Hit(dummy, 10f));

            Assert.AreEqual(92, dummy.CurrentHp, "100 - 8 = 92。");
        }

        [Test]
        public void ReceiveHit_PublishesTypedDamageResult()
        {
            var dummy = MakeDummy(100, 20f);
            var recorder = new Recorder();
            dummy.Results.AddListener(recorder);

            dummy.ReceiveHit(Hit(dummy, 10f));

            Assert.AreEqual(1, recorder.Received.Count);
            Assert.AreEqual(HitResultKind.Damage, recorder.Received[0].Kind);
            Assert.AreEqual(8f, recorder.Received[0].AppliedDamage.Hp, "適用 HP は実減少量 8。");
            Assert.AreEqual(0f, recorder.Received[0].AppliedDamage.Poise, "P2-04 では体幹は未適用（0）。");
            Assert.AreEqual(0f, recorder.Received[0].AppliedDamage.Flinch, "P2-04 ではひるみは未適用（0）。");
            Assert.AreSame(dummy, recorder.Received[0].Target);
        }

        [Test]
        public void AppliedDamage_EqualsActualHpReduced_OnOverkill()
        {
            var dummy = MakeDummy(5, 0f); // HP5・防御0
            var recorder = new Recorder();
            dummy.Results.AddListener(recorder);

            dummy.ReceiveHit(Hit(dummy, 8f)); // 防御0 → 最終8 だが残 HP は 5

            Assert.AreEqual(0, dummy.CurrentHp, "HP は 0 で止まる。");
            Assert.AreEqual(5f, recorder.Received[0].AppliedDamage.Hp, "実適用 = 残 HP の 5。");
        }

        [Test]
        public void AppliedDamage_IsZero_WhenHittingDefeatedDummy()
        {
            var dummy = MakeDummy(5, 0f);
            dummy.ReceiveHit(Hit(dummy, 100f)); // 0 へ
            Assert.IsTrue(dummy.IsDefeated);

            var recorder = new Recorder();
            dummy.Results.AddListener(recorder);
            dummy.ReceiveHit(Hit(dummy, 8f)); // HP0 への追撃

            Assert.AreEqual(0f, recorder.Received[0].AppliedDamage.Hp, "HP0 への追撃の実適用は 0。");
            Assert.AreEqual(HitResultKind.Damage, recorder.Received[0].Kind, "結果種別は Damage のまま。");
        }

        [Test]
        public void MultipleHits_Accumulate()
        {
            var dummy = MakeDummy(100, 20f);
            dummy.ReceiveHit(Hit(dummy, 10f));
            dummy.ReceiveHit(Hit(dummy, 10f));
            Assert.AreEqual(84, dummy.CurrentHp, "100 - 8 - 8 = 84。");
        }

        [Test]
        public void Hp_IsClampedToZero_AndMarksDefeated()
        {
            var dummy = MakeDummy(100, 20f);
            dummy.ReceiveHit(Hit(dummy, 100000f)); // 過大ダメージ
            Assert.AreEqual(0, dummy.CurrentHp, "HP は 0 未満にならない（Clamp）。");
            Assert.IsTrue(dummy.IsDefeated);
        }

        [Test]
        public void ResetHp_RestoresToMax()
        {
            var dummy = MakeDummy(100, 20f);
            dummy.ReceiveHit(Hit(dummy, 100000f));
            Assert.IsTrue(dummy.IsDefeated);

            dummy.ResetHp();
            Assert.AreEqual(100, dummy.CurrentHp);
            Assert.IsFalse(dummy.IsDefeated);
        }

        [Test]
        public void ZeroDefenseDummy_TakesFullContribution()
        {
            var dummy = MakeDummy(100, 0f);
            dummy.ReceiveHit(Hit(dummy, 10f)); // 防御0 → 補正1.0 → 10
            Assert.AreEqual(90, dummy.CurrentHp);
        }
    }
}
