using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-11：仮フィードバックの解決（<see cref="CombatFeedbackMap"/>）と表示 Clamp（<see cref="HudDisplay"/>）を検証する。
    /// 種別ごとの VFX/SE ID・HitStop 要求と、表示値の 0..Max クランプを確認する（純粋・Gameplay 非依存）。
    /// </summary>
    public sealed class CombatFeedbackTests
    {
        [Test]
        public void Map_Damage_HasHitAndHitStop()
        {
            CombatFeedbackCue c = CombatFeedbackMap.Resolve(HitResultKind.Damage);
            Assert.AreEqual("VFX_Hit_Normal", c.VfxId);
            Assert.AreEqual("SE_Hit_Normal", c.SeId);
            Assert.Greater(c.HitStopSeconds, 0f, "被弾はヒットストップ要求あり。");
        }

        [Test]
        public void Map_JustGuard_HasStrongerHitStopThanGuard()
        {
            float jg = CombatFeedbackMap.Resolve(HitResultKind.JustGuard).HitStopSeconds;
            float guard = CombatFeedbackMap.Resolve(HitResultKind.Guard).HitStopSeconds;
            Assert.Greater(jg, guard, "JG はヒットストップで通常ガードと区別（強め）。");
        }

        [Test]
        public void Map_Evade_NoHitStop_ButHasSe()
        {
            CombatFeedbackCue c = CombatFeedbackMap.Resolve(HitResultKind.Evade);
            Assert.AreEqual(0f, c.HitStopSeconds, "回避はヒットストップなし。");
            Assert.AreEqual("SE_Evade", c.SeId);
        }

        [Test]
        public void Map_Rejected_IsNone()
        {
            CombatFeedbackCue c = CombatFeedbackMap.Resolve(HitResultKind.Rejected);
            Assert.AreEqual(string.Empty, c.VfxId);
            Assert.AreEqual(string.Empty, c.SeId);
            Assert.AreEqual(0f, c.HitStopSeconds);
        }

        [Test]
        public void HudDisplay_Clamp_ToRange()
        {
            Assert.AreEqual(0, HudDisplay.Clamp(-5, 100), "負値は 0。");
            Assert.AreEqual(100, HudDisplay.Clamp(150, 100), "上限超過は Max。");
            Assert.AreEqual(42, HudDisplay.Clamp(42, 100), "範囲内はそのまま。");
            Assert.AreEqual(0, HudDisplay.Clamp(10, -1), "Max 負なら 0。");
        }

        [Test]
        public void HudDisplay_ClampFloat_ToRange()
        {
            // 体幹（float）表示用 Clamp：負値・上限超過・範囲内・Max 負。
            Assert.AreEqual(0f, HudDisplay.Clamp(-3.5f, 100f), 1e-4f, "負値は 0。");
            Assert.AreEqual(100f, HudDisplay.Clamp(150.7f, 100f), 1e-4f, "上限超過は Max。");
            Assert.AreEqual(42.5f, HudDisplay.Clamp(42.5f, 100f), 1e-4f, "範囲内はそのまま。");
            Assert.AreEqual(0f, HudDisplay.Clamp(10f, -1f), 1e-4f, "Max 負なら 0。");
        }
    }
}
