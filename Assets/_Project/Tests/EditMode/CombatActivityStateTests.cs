using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-05 受入修正：攻撃行動フェーズの読み取り専用契約 <see cref="ICombatActivityState"/> と
    /// <see cref="CombatActionPhase"/> を検証する。体幹の攻撃中補正(×1.5)は Startup/Active のみ対象で、
    /// 背後(×1.5)との合成は MAX（×2.25 にはしない）。攻撃中補正は HP／ひるみに影響せず、防御は体幹に影響しない。
    /// 実命中経路と同じ「対象状態の取得」（GetComponentInParent&lt;ICombatActivityState&gt;()）も検証する。
    /// </summary>
    public sealed class CombatActivityStateTests
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
            SetField(e, "_poiseMax", 100f);
            SetField(e, "_flinchResistance", 60f);
            return e;
        }

        private CombatDummy MakeDummy(int maxHp, float defense)
        {
            var go = new GameObject("Dummy");
            _spawned.Add(go);
            go.SetActive(false);
            var dummy = go.AddComponent<CombatDummy>();
            SetField(dummy, "_data", MakeEnemy(maxHp, defense));
            go.SetActive(true);
            return dummy;
        }

        // === 契約：フェーズ → 攻撃中補正の対象か ===

        [Test]
        public void Phase_IsPoiseVulnerable_OnlyStartupAndActive()
        {
            Assert.IsFalse(CombatActionPhase.None.IsPoiseVulnerable(), "None は対象外。");
            Assert.IsTrue(CombatActionPhase.Startup.IsPoiseVulnerable(), "Startup は対象。");
            Assert.IsTrue(CombatActionPhase.Active.IsPoiseVulnerable(), "Active は対象。");
            Assert.IsFalse(CombatActionPhase.Recovery.IsPoiseVulnerable(), "Recovery は対象外。");
        }

        [Test]
        public void Dummy_ExposesContract_ByActionPhase()
        {
            var dummy = MakeDummy(100, 20f);

            dummy.SetActionPhase(CombatActionPhase.None);
            Assert.IsFalse(dummy.IsPoiseVulnerableAction, "None → false。");

            dummy.SetActionPhase(CombatActionPhase.Startup);
            Assert.IsTrue(dummy.IsPoiseVulnerableAction, "Startup → true。");

            dummy.SetActionPhase(CombatActionPhase.Active);
            Assert.IsTrue(dummy.IsPoiseVulnerableAction, "Active → true。");

            dummy.SetActionPhase(CombatActionPhase.Recovery);
            Assert.IsFalse(dummy.IsPoiseVulnerableAction, "Recovery → false。");
        }

        [Test]
        public void Dummy_ResetState_ReturnsPhaseToNone()
        {
            var dummy = MakeDummy(100, 20f);
            dummy.SetActionPhase(CombatActionPhase.Active);
            Assert.IsTrue(dummy.IsPoiseVulnerableAction);

            dummy.ResetState();
            Assert.AreEqual(CombatActionPhase.None, dummy.ActionPhase, "ResetState で None へ戻る。");
            Assert.IsFalse(dummy.IsPoiseVulnerableAction);
        }

        // === 状況補正テーブル（基礎体幹 8）：正面/背後 × None/Startup/Active/Recovery ===

        private static float Poise(bool back, CombatActionPhase phase)
        {
            float mult = PoiseDamageCalculator.SituationalMultiplier(back, phase.IsPoiseVulnerable());
            return PoiseDamageCalculator.Compute(8f, mult, 1f, 1f);
        }

        [Test]
        public void SituationalTable_FrontAndBack_TimesPhases()
        {
            Assert.AreEqual(8f, Poise(false, CombatActionPhase.None), 1e-4f, "正面+None → ×1.0 → 8。");
            Assert.AreEqual(12f, Poise(true, CombatActionPhase.None), 1e-4f, "背後+None → ×1.5 → 12。");
            Assert.AreEqual(12f, Poise(false, CombatActionPhase.Startup), 1e-4f, "正面+Startup → ×1.5 → 12。");
            Assert.AreEqual(12f, Poise(false, CombatActionPhase.Active), 1e-4f, "正面+Active → ×1.5 → 12。");
            Assert.AreEqual(8f, Poise(false, CombatActionPhase.Recovery), 1e-4f, "正面+Recovery → ×1.0 → 8。");

            // 背後 と Active/Startup の合成は MAX（×2.25 = 18 にはしない）。
            Assert.AreEqual(12f, Poise(true, CombatActionPhase.Startup), 1e-4f, "背後+Startup → MAX(1.5,1.5) → 12（18 ではない）。");
            Assert.AreEqual(12f, Poise(true, CombatActionPhase.Active), 1e-4f, "背後+Active → MAX(1.5,1.5) → 12（18 ではない）。");
            Assert.AreEqual(12f, Poise(true, CombatActionPhase.Recovery), 1e-4f, "背後+Recovery → ×1.5 → 12。");
        }

        // === 実命中経路と同じ「対象状態の取得」経路（GetComponentInParent） ===

        [Test]
        public void RealHitPath_RetrievesTargetActing_ViaGetComponentInParent()
        {
            var dummy = MakeDummy(100, 20f);
            dummy.SetActionPhase(CombatActionPhase.Active);

            // PollHitbox と同じ取得手順：col.GetComponentInParent<ICombatActivityState>()。
            var activity = dummy.GetComponentInParent<ICombatActivityState>();
            Assert.IsNotNull(activity, "契約を親から取得できる。");

            bool targetActing = activity != null && activity.IsPoiseVulnerableAction;
            float mult = PoiseDamageCalculator.SituationalMultiplier(false, targetActing);
            Assert.AreEqual(1.5f, mult, 1e-4f, "正面+Active（取得経由）→ ×1.5。");
            Assert.AreEqual(12f, PoiseDamageCalculator.Compute(8f, mult, 1f, 1f), 1e-4f);
        }

        [Test]
        public void RealHitPath_NoContract_MeansNoActingCorrection()
        {
            // 契約を持たないオブジェクト → 取得 null → 攻撃中補正なし。
            var bare = new GameObject("Bare");
            _spawned.Add(bare);

            var activity = bare.GetComponentInParent<ICombatActivityState>();
            Assert.IsNull(activity, "契約が無ければ null。");

            bool targetActing = activity != null && activity.IsPoiseVulnerableAction;
            Assert.IsFalse(targetActing, "契約無し → 攻撃中補正の対象にしない。");
            Assert.AreEqual(1f, PoiseDamageCalculator.SituationalMultiplier(false, targetActing), 1e-4f, "正面・補正無し → ×1.0。");
            Assert.AreEqual(1.5f, PoiseDamageCalculator.SituationalMultiplier(true, targetActing), 1e-4f, "背後は契約無しでも ×1.5。");
        }

        // === 独立性：攻撃中補正は HP／ひるみに影響せず、防御は体幹に影響しない ===

        private static HitInfo Hit(CombatDummy target, float preDefenseHp, float poise, float flinch)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero,
                new HitDamage(preDefenseHp, poise, flinch), true, true, HitId.Single(1));
        }

        [Test]
        public void ActingCorrection_DoesNotAffectHpOrFlinch()
        {
            // 攻撃中補正は体幹のみ（攻撃側で Poise に乗る）。HP・ひるみは同じ入力なら結果も同じ。
            var a = MakeDummy(100, 20f);
            var b = MakeDummy(100, 20f);

            // 同じ HP 寄与(10)・同じひるみ(20)。体幹だけ補正の有無で 8 と 12 に分ける。
            a.ReceiveHit(Hit(a, 10f, 8f, 20f));
            b.ReceiveHit(Hit(b, 10f, 12f, 20f));

            Assert.AreEqual(a.CurrentHp, b.CurrentHp, "攻撃中補正は HP に影響しない（どちらも 92）。");
            Assert.AreEqual(92, a.CurrentHp);
            Assert.AreEqual(a.FlinchAccumulation, b.FlinchAccumulation, 1e-4f, "攻撃中補正はひるみに影響しない。");
            // 体幹だけが異なる。
            Assert.AreEqual(92f, a.CurrentPoise, 1e-4f, "補正無し → 体幹 8 減。");
            Assert.AreEqual(88f, b.CurrentPoise, 1e-4f, "補正あり → 体幹 12 減。");
        }

        [Test]
        public void Defense_DoesNotAffectPoise()
        {
            // 体幹は固定系（攻撃力・防御は無関係）。防御が違っても同じ体幹ダメージなら同じだけ減る。
            var lowDef = MakeDummy(100, 0f);
            var highDef = MakeDummy(100, 40f);

            lowDef.ReceiveHit(Hit(lowDef, 10f, 8f, 20f));
            highDef.ReceiveHit(Hit(highDef, 10f, 8f, 20f));

            Assert.AreEqual(lowDef.CurrentPoise, highDef.CurrentPoise, 1e-4f, "防御は体幹に影響しない（どちらも 8 減）。");
            Assert.AreEqual(92f, lowDef.CurrentPoise, 1e-4f);
            // HP は防御で変わる（体幹と独立）ことも確認。
            Assert.AreNotEqual(lowDef.CurrentHp, highDef.CurrentHp, "HP は防御で変わる。");
        }
    }
}
