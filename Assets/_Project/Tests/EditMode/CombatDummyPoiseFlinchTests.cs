using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-05：CombatDummy の体幹・ひるみ・スタン統合（体幹減算、体幹0でスタン、スタン中 HP×1.25、ひるみ蓄積/発生、
    /// AppliedDamage の実適用量）を検証する。回復・保持の時間挙動は Poise/FlinchState 単体テストが担う。
    /// </summary>
    public sealed class CombatDummyPoiseFlinchTests
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

        private CombatDummy MakeDummy(int maxHp, float defense, float poiseMax, float flinchResistance)
        {
            var e = ScriptableObject.CreateInstance<EnemyData>();
            _spawned.Add(e);
            SetField(e, "_maxHp", maxHp);
            SetField(e, "_defense", defense);
            SetField(e, "_poiseMax", poiseMax);
            SetField(e, "_flinchResistance", flinchResistance);
            SetField(e, "_stunSeconds", 3f);

            var go = new GameObject("Dummy");
            _spawned.Add(go);
            go.SetActive(false);
            var dummy = go.AddComponent<CombatDummy>();
            SetField(dummy, "_data", e);
            go.SetActive(true);
            return dummy;
        }

        private static HitInfo Hit(CombatDummy target, float hp, float poise, float flinch)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero,
                new HitDamage(hp, poise, flinch), true, true, HitId.Single(1));
        }

        [Test]
        public void ReceiveHit_ReducesPoise_AndReportsAppliedPoise()
        {
            var dummy = MakeDummy(100, 0f, 100f, 60f);
            var recorder = new Recorder();
            dummy.Results.AddListener(recorder);

            dummy.ReceiveHit(Hit(dummy, 0f, 15f, 0f));

            Assert.AreEqual(85f, dummy.CurrentPoise, 1e-4f, "100 - 15 = 85。");
            Assert.AreEqual(15f, recorder.Received[0].AppliedDamage.Poise, 1e-4f);
        }

        [Test]
        public void PoiseZero_Stuns_ThenHpDamageIs1_25x()
        {
            var dummy = MakeDummy(200, 0f, 100f, 60f);

            dummy.ReceiveHit(Hit(dummy, 0f, 100f, 0f)); // 体幹0 → スタン
            Assert.IsTrue(dummy.IsStunned);
            Assert.AreEqual(0f, dummy.CurrentPoise);

            var recorder = new Recorder();
            dummy.Results.AddListener(recorder);
            dummy.ReceiveHit(Hit(dummy, 10f, 0f, 0f)); // スタン中の HP 命中（防御0）

            // ResolveFinal(10, defense0, stun1.25) = 12.5 → 13。
            Assert.AreEqual(13f, recorder.Received[0].AppliedDamage.Hp, "スタン中は HP ×1.25（13）。");
        }

        [Test]
        public void NotStunned_HpDamageIsNormal()
        {
            var dummy = MakeDummy(200, 0f, 100f, 60f);
            var recorder = new Recorder();
            dummy.Results.AddListener(recorder);
            dummy.ReceiveHit(Hit(dummy, 10f, 0f, 0f)); // 非スタン（防御0）
            Assert.AreEqual(10f, recorder.Received[0].AppliedDamage.Hp, "非スタンは等倍（10）。");
        }

        [Test]
        public void Flinch_TriggersAtResistance()
        {
            var dummy = MakeDummy(100, 0f, 100f, 60f);
            dummy.ReceiveHit(Hit(dummy, 0f, 0f, 30f));
            Assert.IsFalse(dummy.IsFlinching, "耐性未満はひるまない。");
            Assert.AreEqual(30f, dummy.FlinchAccumulation, 1e-4f);

            dummy.ReceiveHit(Hit(dummy, 0f, 0f, 30f)); // 累計 60 >= 60
            Assert.IsTrue(dummy.IsFlinching, "耐性到達でひるみ。");
        }

        private sealed class Recorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
        }
    }
}
