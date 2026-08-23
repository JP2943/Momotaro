using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-01：共通 Runtime <see cref="EnemyVitals"/>（CombatDummy から抽出）の被弾数値適用・スタン・ひるみ・撃破・
    /// 復帰状態フラグを検証する。純粋クラスとして MonoBehaviour 非依存で再現する。
    /// </summary>
    public sealed class EnemyVitalsTests
    {
        private sealed class FakeConfig : IEnemyVitalsConfig
        {
            public int MaxHp { get; set; } = 100;
            public float Defense { get; set; } = 0f;
            public float PoiseMax { get; set; } = 100f;
            public float PoiseRecoveryDelaySeconds { get; set; } = 3f;
            public float PoiseRecoveryRatioPerSecond { get; set; } = 0.08f;
            public float PoiseDamageMultiplier { get; set; } = 1f;
            public float StunSeconds { get; set; } = 3f;
            public float FlinchResistance { get; set; } = 60f;
            public float FlinchSeconds { get; set; } = 0.8f;
        }

        private static HitInfo Hit(float hp, float poise, float flinch, int id = 1)
        {
            return new HitInfo(null, null, Vector3.forward, Vector3.zero,
                new HitDamage(hp, poise, flinch), true, true, HitId.Single(id));
        }

        [Test]
        public void Apply_HpDamage_WithDefense_ReducesHp()
        {
            var v = new EnemyVitals(new FakeConfig { MaxHp = 100, Defense = 20f });
            EnemyVitals.HitApplication app = v.Apply(Hit(30f, 0f, 0f));

            Assert.Greater(app.Applied.Hp, 0, "HP ダメージが入る。");
            Assert.Less(app.Applied.Hp, 30, "防御で軽減される（生値未満）。");
            Assert.AreEqual(100 - app.Applied.Hp, v.CurrentHp, "現在 HP は実減少分だけ減る。");
            Assert.IsFalse(app.NewlyDefeated);
        }

        [Test]
        public void Apply_PoiseDepletion_TriggersStun()
        {
            var v = new EnemyVitals(new FakeConfig { PoiseMax = 20f });
            EnemyVitals.HitApplication app = v.Apply(Hit(0f, 25f, 0f));

            Assert.IsTrue(v.IsStunned, "体幹 0 でスタン。");
            Assert.IsTrue(app.NewlyStunned, "新規スタンが報告される。");
        }

        [Test]
        public void Apply_Flinch_AccumulatesAndTriggers()
        {
            var v = new EnemyVitals(new FakeConfig { FlinchResistance = 10f });
            EnemyVitals.HitApplication app = v.Apply(Hit(0f, 0f, 15f));

            Assert.IsTrue(v.IsFlinching, "耐性超過でひるみ。");
            Assert.IsTrue(app.NewlyFlinching, "新規ひるみが報告される。");
        }

        [Test]
        public void Apply_LethalHp_ReportsNewlyDefeated()
        {
            var v = new EnemyVitals(new FakeConfig { MaxHp = 10, Defense = 0f });
            EnemyVitals.HitApplication app = v.Apply(Hit(100f, 0f, 0f));

            Assert.IsTrue(v.IsDefeated, "HP0 で撃破。");
            Assert.IsTrue(app.NewlyDefeated, "新規撃破が報告される。");
            Assert.AreEqual(0, v.CurrentHp);
        }

        [Test]
        public void ResetState_RestoresFull()
        {
            var v = new EnemyVitals(new FakeConfig { MaxHp = 100, PoiseMax = 50f });
            v.Apply(Hit(40f, 0f, 0f));
            v.ResetState();

            Assert.AreEqual(100, v.CurrentHp, "HP 全快。");
            Assert.AreEqual(50f, v.CurrentPoise, 1e-4f, "体幹全快。");
            Assert.IsFalse(v.IsStunned);
            Assert.IsFalse(v.IsFlinching);
        }

        [Test]
        public void NullConfig_UsesMinimalDefaults()
        {
            var v = new EnemyVitals(null);
            Assert.AreEqual(1, v.MaxHp, "null 設定は最小既定 HP1。");
            Assert.IsFalse(v.IsDefeated);
        }
    }
}
