using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-08C（敵側）：<see cref="EnemyAttackSwingSePresenter"/> が敵の判定（Active）立ち上がりで、敵タイプ鍵＋攻撃分類に応じた
    /// 攻撃 SE を 1 回鳴らすことを検証する。通常/強/ガード不能/弓発射の引き当て、共通（強＝通常）設定、Active 継続での非再発火、
    /// 未登録鍵・非攻撃での不発、複数体の独立発火、SE 未割当での無例外を確認する。
    /// </summary>
    public sealed class EnemyAttackSwingSePresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeEnemy : IAttackSwingSource, IEnemySlashVisual
        {
            public bool Active;
            public int Stage;
            public string Key = "Small";
            public bool IsSwingHitboxActive => Active;
            public int SwingStage => Stage;
            public Vector3 SwingCenter => Vector3.zero;
            public Vector3 SwingHalfExtents => Vector3.zero;
            public Vector3 SwingForward => Vector3.forward;
            public string SlashVfxKey => Key;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private EnemyAttackSwingSePresenter New(out CombatSePlayer se)
        {
            var go = new GameObject("EnemySwingSe");
            _spawned.Add(go);
            var p = go.AddComponent<EnemyAttackSwingSePresenter>();

            var seGo = new GameObject("EnemyCombatSePlayer");
            _spawned.Add(seGo);
            se = seGo.AddComponent<CombatSePlayer>();
            p.Se = se;

            p.Entries = new[]
            {
                new EnemyAttackSwingSePresenter.EnemySeEntry { key = "Small", normalSeId = "SE_Enemy_Swordsman" },
                new EnemyAttackSwingSePresenter.EnemySeEntry
                {
                    key = "Medium",
                    normalSeId = "SE_Enemy_Samurai",
                    heavySeId = "SE_Enemy_Samurai", // 通常と共通
                    unblockableSeId = "SE_Enemy_Samurai_Thrust",
                },
            };
            p.ProjectileSeId = "SE_Enemy_Bow";
            return p;
        }

        [Test]
        public void MeleeNormal_RisingEdge_PlaysKeyedSe_Once()
        {
            EnemyAttackSwingSePresenter p = New(out CombatSePlayer se);
            var e = new FakeEnemy { Key = "Small", Stage = AttackSwing.EnemyMeleeNormal, Active = false };
            p.Bind(new IAttackSwingSource[] { e });

            p.Tick();
            Assert.AreEqual(0, p.PlayCount, "Active 前は鳴らさない。");

            e.Active = true;
            p.Tick();
            Assert.AreEqual(1, p.PlayCount, "Active 立ち上がりで 1 回。");
            Assert.AreEqual("SE_Enemy_Swordsman", p.LastSeId);
            Assert.AreEqual("SE_Enemy_Swordsman", se.LastRequestedSeId);

            p.Tick();
            p.Tick();
            Assert.AreEqual(1, p.PlayCount, "Active 継続では再発火しない。");
        }

        [Test]
        public void SamuraiHeavy_SharesNormalSe_UnblockableUsesThrust()
        {
            EnemyAttackSwingSePresenter p = New(out CombatSePlayer se);
            var e = new FakeEnemy { Key = "Medium" };
            p.Bind(new IAttackSwingSource[] { e });

            e.Stage = AttackSwing.EnemyMeleeHeavy; e.Active = true; p.Tick();
            Assert.AreEqual("SE_Enemy_Samurai", p.LastSeId, "強攻撃は通常と共通 SE。");

            e.Active = false; p.Tick();
            e.Stage = AttackSwing.EnemyMeleeUnblockable; e.Active = true; p.Tick();
            Assert.AreEqual("SE_Enemy_Samurai_Thrust", p.LastSeId, "ガード不能は Thrust SE。");
            Assert.AreEqual(2, p.PlayCount);
        }

        [Test]
        public void Projectile_PlaysBow_RegardlessOfKey()
        {
            EnemyAttackSwingSePresenter p = New(out CombatSePlayer se);
            var e = new FakeEnemy { Key = "Ranged", Stage = AttackSwing.EnemyProjectile, Active = true };
            p.Bind(new IAttackSwingSource[] { e });

            p.Tick();
            Assert.AreEqual("SE_Enemy_Bow", p.LastSeId, "飛び道具は敵タイプ非依存で弓 SE。");
            Assert.AreEqual(1, p.PlayCount);
        }

        [Test]
        public void UnknownKey_And_NonAttackStage_DoNotFire()
        {
            EnemyAttackSwingSePresenter p = New(out CombatSePlayer se);
            var unknown = new FakeEnemy { Key = "Boss", Stage = AttackSwing.EnemyMeleeNormal, Active = true };
            var charge = new FakeEnemy { Key = "Medium", Stage = 0, Active = true }; // 突進/非攻撃相当
            p.Bind(new IAttackSwingSource[] { unknown, charge });

            p.Tick();
            Assert.AreEqual(0, p.PlayCount, "未登録鍵・非対象段では鳴らさない。");
        }

        [Test]
        public void TwoEnemies_FireIndependently()
        {
            EnemyAttackSwingSePresenter p = New(out CombatSePlayer se);
            var a = new FakeEnemy { Key = "Small", Stage = AttackSwing.EnemyMeleeNormal };
            var b = new FakeEnemy { Key = "Medium", Stage = AttackSwing.EnemyMeleeNormal };
            p.Bind(new IAttackSwingSource[] { a, b });

            a.Active = true; p.Tick();          // a 発火のみ
            Assert.AreEqual(1, p.PlayCount);
            b.Active = true; p.Tick();          // b 発火のみ（a は継続で再発火なし）
            Assert.AreEqual(2, p.PlayCount);
            Assert.AreEqual("SE_Enemy_Samurai", p.LastSeId);
        }

        [Test]
        public void NoSePlayer_NoException_StillCounts()
        {
            EnemyAttackSwingSePresenter p = New(out CombatSePlayer se);
            p.Se = null;
            var e = new FakeEnemy { Key = "Small", Stage = AttackSwing.EnemyMeleeNormal, Active = true };
            p.Bind(new IAttackSwingSource[] { e });

            Assert.DoesNotThrow(() => p.Tick(), "SE 再生器未割当でも無例外。");
            Assert.AreEqual(1, p.PlayCount);
        }
    }
}
