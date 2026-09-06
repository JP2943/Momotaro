using Momotaro.Gameplay.Companion;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03：仲間の戦闘判断（<see cref="CompanionEngagement"/>）と攻撃設定 Snapshot（<see cref="CompanionAttackSettings"/>）を
    /// 検証する。接近・待機・攻撃の切り分け、間合いの境目で往復しないこと、参加できない状態・攻撃を持たない構成では
    /// 敵へ寄っていかないことを固定する。純粋関数のため決定的に検証できる。
    /// </summary>
    public sealed class CompanionEngagementTests
    {
        private const float UseRange = 2f;
        private const float UseAngle = 60f;

        // 攻撃開始距離は 2.0 × 0.7 = 1.4（使用距離 2.0 は判定が届く距離で、攻撃開始の条件ではない）。
        private static CompanionAttackSettings Settings(float useRange = UseRange, float useAngle = UseAngle)
        {
            return new CompanionAttackSettings(useRange, useAngle, 1f, 0.2f, 0.1f, 0.3f);
        }

        private static CompanionEngageDecision Decide(
            float distance, float angle = 0f, float cooldown = 0f, bool hasTarget = true, bool canEngage = true)
        {
            CompanionAttackSettings settings = Settings();
            return CompanionEngagement.Decide(hasTarget, canEngage, distance, angle, settings, cooldown);
        }

        // ---- 攻撃 ----

        [Test]
        public void InRange_WithCooldownReady_Attacks()
        {
            Assert.AreEqual(CompanionEngageDecision.Attack, Decide(distance: 1.0f));
        }

        [Test]
        public void AtExactAttackStartDistance_Attacks()
        {
            Assert.AreEqual(CompanionEngageDecision.Attack, Decide(distance: UseRange * CompanionAttackSettings.AttackStartRatio),
                "攻撃開始距離ちょうどからは攻撃できる。");
        }

        [Test]
        public void AtUseRange_ChasesInstead()
        {
            Assert.AreEqual(CompanionEngageDecision.Chase, Decide(distance: UseRange),
                "判定が届くだけの距離では攻撃しない（予兆中に離れられて空振りするため、内側まで詰める）。");
        }

        [Test]
        public void InRange_WhileCoolingDown_Holds()
        {
            Assert.AreEqual(CompanionEngageDecision.Hold, Decide(distance: 1.0f, cooldown: 0.5f),
                "クールダウン中は間合いで待つ（下がらない・押し込まない）。");
        }

        [Test]
        public void OutOfAngle_DoesNotAttack()
        {
            Assert.AreEqual(CompanionEngageDecision.Hold, Decide(distance: 1.0f, angle: UseAngle + 1f),
                "使用角度の外では攻撃しない（次 Tick で向き直る）。");
        }

        [Test]
        public void ZeroUseAngle_MeansNoAngleLimit()
        {
            CompanionAttackSettings settings = Settings(useAngle: 0f);

            Assert.AreEqual(CompanionEngageDecision.Attack,
                CompanionEngagement.Decide(true, true, 1.0f, 179f, settings, 0f), "0 は角度制限なしとして扱う。");
        }

        // ---- 接近・待機 ----

        [Test]
        public void BeyondUseRange_Chases()
        {
            Assert.AreEqual(CompanionEngageDecision.Chase, Decide(distance: 5f));
        }

        [Test]
        public void BetweenAttackStartAndUseRange_Chases()
        {
            // 攻撃開始距離（1.4）より遠く、判定が届く距離（2.0）の内側。まだ振らずに詰める。
            Assert.AreEqual(CompanionEngageDecision.Chase, Decide(distance: 1.7f));
            Assert.AreEqual(CompanionEngageDecision.Chase, Decide(distance: 1.7f, cooldown: 0.5f));
        }

        [Test]
        public void InsideAttackStartDistance_WhileCoolingDown_Holds()
        {
            Assert.AreEqual(CompanionEngageDecision.Hold, Decide(distance: 1.0f, cooldown: 0.5f));
        }

        [Test]
        public void AttackStartDistance_LeavesReachMargin()
        {
            CompanionAttackSettings settings = Settings();

            Assert.Less(settings.AttackStartDistance, settings.UseRange,
                "攻撃を始める距離は、判定が届く距離より内側でなければならない。"
                + "同じにすると、予兆のあいだに対象が少しでも離れた時点で必ず空振りする。");
            Assert.AreEqual(UseRange * CompanionAttackSettings.AttackStartRatio, settings.AttackStartDistance, 1e-4f);
        }

        // ---- 参加できない場合 ----

        [Test]
        public void NoTarget_IsIdle()
        {
            Assert.AreEqual(CompanionEngageDecision.Idle, Decide(distance: 1f, hasTarget: false));
        }

        [Test]
        public void CannotEngage_IsIdle()
        {
            Assert.AreEqual(CompanionEngageDecision.Idle, Decide(distance: 1f, canEngage: false),
                "ダウン・ひるみ・退場中は戦闘判断をしない（追従へ委ねる）。");
        }

        [Test]
        public void WithoutAttack_DoesNotChase()
        {
            Assert.AreEqual(CompanionEngageDecision.Idle,
                CompanionEngagement.Decide(true, true, 5f, 0f, CompanionAttackSettings.None, 0f),
                "攻撃を持たない構成では敵へ寄っていかない（殴られるだけになるため）。");
        }

        // ---- 設定 Snapshot ----

        [Test]
        public void Settings_FromNullData_HasNoAttack()
        {
            CompanionAttackSettings settings = CompanionAttackSettings.From(null);

            Assert.IsFalse(settings.HasAttack, "Data 未割当なら攻撃しない（既定値で殴らない）。");
            Assert.AreEqual(0f, settings.TotalSeconds, 1e-4f);
        }

        [Test]
        public void Settings_ClampNegativeValues()
        {
            var settings = new CompanionAttackSettings(-1f, -1f, -1f, -1f, -1f, -1f);

            Assert.AreEqual(0f, settings.UseRange, 1e-4f);
            Assert.AreEqual(0f, settings.CooldownSeconds, 1e-4f);
            Assert.AreEqual(0f, settings.TotalSeconds, 1e-4f);
            Assert.IsFalse(settings.HasAttack);
        }

        [Test]
        public void Settings_HasAttack_RequiresActiveAndRange()
        {
            Assert.IsFalse(new CompanionAttackSettings(2f, 60f, 1f, 0.2f, 0f, 0.3f).HasAttack, "判定時間が無い。");
            Assert.IsFalse(new CompanionAttackSettings(0f, 60f, 1f, 0.2f, 0.1f, 0.3f).HasAttack, "射程が無い。");
            Assert.IsTrue(new CompanionAttackSettings(2f, 60f, 1f, 0.2f, 0.1f, 0.3f).HasAttack);
        }
    }
}
