using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy.Screen;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-07：画面内制御の検証（§8.2）。<see cref="OffscreenAttackPolicy"/> の分類別開始可否と、<see cref="ViewportBounds"/> の
    /// 境界・余白・カメラ前後判定を決定的に確認する。純粋・再現可能。
    /// </summary>
    public sealed class ScreenControlTests
    {
        // ---- OffscreenAttackPolicy ----

        [Test]
        public void OnScreen_AllClassesCanStart()
        {
            foreach (EnemyAttackClass cls in System.Enum.GetValues(typeof(EnemyAttackClass)))
            {
                Assert.IsTrue(OffscreenAttackPolicy.CanStart(cls, false, isOnScreen: true, offscreenWarningAvailable: false),
                    "画面内は全分類が開始可: " + cls);
            }
        }

        [Test]
        public void Offscreen_HeavyAndUnblockable_Denied()
        {
            Assert.IsFalse(OffscreenAttackPolicy.CanStart(EnemyAttackClass.Heavy, false, false, false));
            Assert.IsFalse(OffscreenAttackPolicy.CanStart(EnemyAttackClass.Unblockable, false, false, false));
        }

        [Test]
        public void Offscreen_Melee_Denied()
        {
            Assert.IsFalse(OffscreenAttackPolicy.CanStart(EnemyAttackClass.Normal, false, false, false),
                "近接は画面内に入ってから開始。");
            Assert.IsFalse(OffscreenAttackPolicy.CanStart(EnemyAttackClass.Charge, false, false, false));
        }

        [Test]
        public void Offscreen_Projectile_RequiresWarning()
        {
            // 警告必須で警告不可 → 開始不可。警告可 → 開始可。
            Assert.IsFalse(OffscreenAttackPolicy.CanStart(EnemyAttackClass.Projectile, true, false, offscreenWarningAvailable: false));
            Assert.IsTrue(OffscreenAttackPolicy.CanStart(EnemyAttackClass.Projectile, true, false, offscreenWarningAvailable: true));
            // 警告不要指定なら画面外でも可（データ裁量）。
            Assert.IsTrue(OffscreenAttackPolicy.CanStart(EnemyAttackClass.Projectile, false, false, offscreenWarningAvailable: false));
        }

        // ---- ViewportBounds ----

        [Test]
        public void Viewport_InsideFront_IsOnScreen()
        {
            Assert.IsTrue(ViewportBounds.IsInside(new Vector2(0.5f, 0.5f), inFront: true, margin01: 0f));
        }

        [Test]
        public void Viewport_BehindCamera_IsOffScreen()
        {
            Assert.IsFalse(ViewportBounds.IsInside(new Vector2(0.5f, 0.5f), inFront: false, margin01: 0.1f),
                "カメラ背面は常に画面外。");
        }

        [Test]
        public void Viewport_Boundary_NoMargin()
        {
            Assert.IsTrue(ViewportBounds.IsInside(new Vector2(1.0f, 0.5f), true, 0f), "境界ちょうどは画面内。");
            Assert.IsFalse(ViewportBounds.IsInside(new Vector2(1.01f, 0.5f), true, 0f), "境界外は画面外。");
        }

        [Test]
        public void Viewport_Margin_ExtendsBounds()
        {
            Assert.IsTrue(ViewportBounds.IsInside(new Vector2(1.04f, 0.5f), true, 0.05f), "余白内は画面内。");
            Assert.IsFalse(ViewportBounds.IsInside(new Vector2(1.06f, 0.5f), true, 0.05f), "余白外は画面外。");
            Assert.IsTrue(ViewportBounds.IsInside(new Vector2(-0.03f, 0.5f), true, 0.05f), "負側の余白内。");
            Assert.IsFalse(ViewportBounds.IsInside(new Vector2(-0.06f, 0.5f), true, 0.05f), "負側の余白外。");
        }

        // ---- ScreenBoundsProvider fallback ----

        [Test]
        public void Provider_NullAdapter_TreatsAsOnScreen()
        {
            IScreenBoundsProbe prev = ScreenBoundsProvider.Current;
            ScreenBoundsProvider.Current = null;
            Assert.IsTrue(ScreenBoundsProvider.IsOnScreen(Vector3.zero), "アダプタ未設定は画面内扱いで進行。");
            ScreenBoundsProvider.Current = prev;
        }
    }
}
