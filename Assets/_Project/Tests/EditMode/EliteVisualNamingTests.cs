using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Presentation.Enemy;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-09：強敵（侍骸骨）の Animator State 命名解決（§9.3）。Elite スタイルは移動＝Move、攻撃を分類別に
    /// NormalAttack／HeavyOverhead／UnguardableThrust へ解決する。Basic（剣士/弓兵）の Walk/Attack 命名は後方互換で不変。
    /// Idle/Hurt/Stun/Down は共通。侍骸骨 Controller の State 名（Move_/NormalAttack_/HeavyOverhead_/UnguardableThrust_）と一致する。
    /// </summary>
    public sealed class EliteVisualNamingTests
    {
        private const EnemyVisualFacing Down = EnemyVisualFacing.Down;

        [Test]
        public void Elite_Movement_UsesMove()
        {
            Assert.AreEqual("Move_Down", EnemyVisualNames.StateName(EnemyState.Chase, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Normal));
            Assert.AreEqual("Move_Down", EnemyVisualNames.StateName(EnemyState.Reposition, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Normal));
        }

        [Test]
        public void Elite_Attacks_ResolveByClass()
        {
            Assert.AreEqual("NormalAttack_Down",
                EnemyVisualNames.StateName(EnemyState.AttackPrepare, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Normal));
            Assert.AreEqual("HeavyOverhead_Down",
                EnemyVisualNames.StateName(EnemyState.AttackActive, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Heavy));
            Assert.AreEqual("UnguardableThrust_Down",
                EnemyVisualNames.StateName(EnemyState.AttackRecovery, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Unblockable));
            Assert.AreEqual("NormalAttack_Down",
                EnemyVisualNames.StateName(EnemyState.AttackActive, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Charge),
                "突進は通常攻撃モーションを流用。");
        }

        [Test]
        public void Elite_CommonStates_SameAsBasic()
        {
            Assert.AreEqual("Idle_Down", EnemyVisualNames.StateName(EnemyState.Idle, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Normal));
            Assert.AreEqual("Hurt_Down", EnemyVisualNames.StateName(EnemyState.Stagger, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Normal));
            Assert.AreEqual("Stun_Down", EnemyVisualNames.StateName(EnemyState.Stunned, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Normal));
            Assert.AreEqual("Down", EnemyVisualNames.StateName(EnemyState.Down, Down, EnemyVisualNamingStyle.Elite, EnemyAttackClass.Normal));
        }

        [Test]
        public void Basic_Backward_Compatible()
        {
            Assert.AreEqual("Walk_Down", EnemyVisualNames.StateName(EnemyState.Chase, Down), "既存 Basic は Walk。");
            Assert.AreEqual("Attack_Down", EnemyVisualNames.StateName(EnemyState.AttackActive, Down), "既存 Basic は Attack。");
            Assert.AreEqual("Walk_Down", EnemyVisualNames.StateName(EnemyState.Chase, Down, EnemyVisualNamingStyle.Basic, EnemyAttackClass.Heavy));
        }
    }
}
