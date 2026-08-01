using Momotaro.Gameplay.Enemy;
using Momotaro.Presentation.Enemy;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 敵スプライト受入：状態＋Facing → Animator State 名の解決（<see cref="EnemyVisualNames"/>）と前方→4 方向
    /// （<see cref="EnemyFacingResolver"/>）を検証する。Down は Facing 非依存で正面固定。純粋・再現可能。
    /// </summary>
    public sealed class EnemyVisualNamesTests
    {
        [Test]
        public void FacingResolver_MapsCardinals()
        {
            Assert.AreEqual(EnemyVisualFacing.Down, EnemyFacingResolver.FromForward(new Vector3(0, 0, -1)));
            Assert.AreEqual(EnemyVisualFacing.Up, EnemyFacingResolver.FromForward(new Vector3(0, 0, 1)));
            Assert.AreEqual(EnemyVisualFacing.Right, EnemyFacingResolver.FromForward(new Vector3(1, 0, 0)));
            Assert.AreEqual(EnemyVisualFacing.Left, EnemyFacingResolver.FromForward(new Vector3(-1, 0, 0)));
            Assert.AreEqual(EnemyVisualFacing.Down, EnemyFacingResolver.FromForward(Vector3.zero), "静止は正面 Down。");
        }

        [Test]
        public void StateName_MapsGameplayStatesToVisual()
        {
            Assert.AreEqual("Idle_Down", EnemyVisualNames.StateName(EnemyState.Idle, EnemyVisualFacing.Down));
            Assert.AreEqual("Idle_Left", EnemyVisualNames.StateName(EnemyState.Alert, EnemyVisualFacing.Left));
            Assert.AreEqual("Idle_Up", EnemyVisualNames.StateName(EnemyState.Suspicious, EnemyVisualFacing.Up));
            Assert.AreEqual("Walk_Right", EnemyVisualNames.StateName(EnemyState.Chase, EnemyVisualFacing.Right));
            Assert.AreEqual("Walk_Down", EnemyVisualNames.StateName(EnemyState.Return, EnemyVisualFacing.Down));
            Assert.AreEqual("Attack_Up", EnemyVisualNames.StateName(EnemyState.AttackPrepare, EnemyVisualFacing.Up));
            Assert.AreEqual("Attack_Left", EnemyVisualNames.StateName(EnemyState.AttackActive, EnemyVisualFacing.Left));
            Assert.AreEqual("Attack_Right", EnemyVisualNames.StateName(EnemyState.AttackRecovery, EnemyVisualFacing.Right));
            Assert.AreEqual("Hurt_Down", EnemyVisualNames.StateName(EnemyState.Stagger, EnemyVisualFacing.Down));
            Assert.AreEqual("Stun_Left", EnemyVisualNames.StateName(EnemyState.Stunned, EnemyVisualFacing.Left));
        }

        [Test]
        public void DownState_IsFacingIndependent()
        {
            Assert.AreEqual("Down", EnemyVisualNames.StateName(EnemyState.Down, EnemyVisualFacing.Down));
            Assert.AreEqual("Down", EnemyVisualNames.StateName(EnemyState.Down, EnemyVisualFacing.Left));
            Assert.AreEqual("Down", EnemyVisualNames.StateName(EnemyState.Down, EnemyVisualFacing.Right));
            Assert.AreEqual("Down", EnemyVisualNames.StateName(EnemyState.Down, EnemyVisualFacing.Up));
        }
    }
}
