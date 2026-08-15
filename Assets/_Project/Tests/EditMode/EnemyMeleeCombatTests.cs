using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-05：近接敵の攻撃が Phase 2 の命中解決を通り、Guard／JG／Step／被弾で正しい結果になること、JG 成立で攻撃者
    /// （<see cref="EnemyActor"/>）の体幹が 15〜20 削られること（Phase 2 の返却経由）、近接 Data が §9.1 に沿うことを検証する。
    /// </summary>
    public sealed class EnemyMeleeCombatTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
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

        private sealed class FakeDefenses : MonoBehaviour, IGuardState, IJustGuardState, IEvadeState
        {
            public bool Guarding;
            public bool CanJG;
            public bool Invincible;
            public Vector3 Fwd = Vector3.forward;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
            public bool CanJustGuard => CanJG;
            public void NotifyJustGuardSuccess() => CanJG = false;
            public bool IsInvincible => Invincible;
        }

        private sealed class Recorder : IHitResultListener
        {
            public HitResult Last;
            public bool Got;
            public void OnHitResult(in HitResult r) { Got = true; Last = r; }
        }

        private (PlayerVitalsHolder holder, FakeDefenses def, Recorder rec) MakePlayer()
        {
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetField(data, "_maxHp", 100);
            SetField(data, "_defense", 0f);

            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var def = go.AddComponent<FakeDefenses>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", data);
            go.SetActive(true);
            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return (holder, def, rec);
        }

        private EnemyActor MakeEnemyAttacker(float poiseMax)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 100);
            SetField(arch, "_attackPower", 40f);
            SetField(arch, "_poiseMax", poiseMax);

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.SetActive(false);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            go.SetActive(true);
            return actor;
        }

        private EnemyAttackSnapshot MeleeSnapshot()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            _spawned.Add(d);
            SetField(d, "_hpMultiplier", 1.0f);
            SetField(d, "_poiseDamage", 10f);
            SetField(d, "_flinchPower", 30f);
            SetField(d, "_guardStaminaCost", 12f);
            SetField(d, "_justGuardPoiseReturn", 18f);
            SetField(d, "_guardable", true);
            SetField(d, "_justGuardable", true);
            SetField(d, "_steppable", true);
            return EnemyAttackSnapshot.From(d);
        }

        // 攻撃者→対象。JG/Guard の前方判定に合わせ、攻撃方向は -forward（対象の GuardForward=forward と正対）。
        private HitInfo EnemyHit(EnemyActor attacker, IDamageable target)
        {
            return EnemyHitFactory.Build(MeleeSnapshot(), attacker.Archetype.AttackPower, attacker, target,
                -Vector3.forward, Vector3.zero, HitId.Single(1));
        }

        [Test]
        public void NormalHit_AppliesDamage()
        {
            var (holder, def, rec) = MakePlayer();
            var enemy = MakeEnemyAttacker(60f);

            holder.ReceiveHit(EnemyHit(enemy, holder));

            Assert.AreEqual(HitResultKind.Damage, rec.Last.Kind, "無防御は被弾。");
            Assert.Less(holder.Vitals.Health.Current, holder.Vitals.Health.Max);
        }

        [Test]
        public void Guarding_ResultsInGuard()
        {
            var (holder, def, rec) = MakePlayer();
            def.Guarding = true;
            var enemy = MakeEnemyAttacker(60f);

            holder.ReceiveHit(EnemyHit(enemy, holder));

            Assert.AreEqual(HitResultKind.Guard, rec.Last.Kind, "前方ガードは防御成功。");
            Assert.AreEqual(holder.Vitals.Health.Max, holder.Vitals.Health.Current, "HP は減らない。");
        }

        [Test]
        public void StepInvincibility_ResultsInEvade()
        {
            var (holder, def, rec) = MakePlayer();
            def.Invincible = true;
            var enemy = MakeEnemyAttacker(60f);

            holder.ReceiveHit(EnemyHit(enemy, holder));

            Assert.AreEqual(HitResultKind.Evade, rec.Last.Kind, "ステップ無敵は回避。");
        }

        [Test]
        public void JustGuard_ReflectsPoiseToAttacker_15to20()
        {
            var (holder, def, rec) = MakePlayer();
            def.CanJG = true;
            var enemy = MakeEnemyAttacker(60f);
            float poiseBefore = enemy.CurrentPoise;

            holder.ReceiveHit(EnemyHit(enemy, holder));

            Assert.AreEqual(HitResultKind.JustGuard, rec.Last.Kind, "JG 成立。");
            float reduced = poiseBefore - enemy.CurrentPoise;
            Assert.GreaterOrEqual(reduced, 15f, "攻撃者体幹の削り >= 15（Phase 2 の返却経由）。");
            Assert.LessOrEqual(reduced, 20f, "攻撃者体幹の削り <= 20。");
        }

        [Test]
        public void MeleeArchetype_MatchesMeleeSpec()
        {
            var arch = AssetDatabase_LoadMelee();
            Assert.IsNotNull(arch, "近接 Prototype archetype が見つからない。");
            Assert.AreEqual(1, arch.AttackCount, "近接敵は通常攻撃 1 種のみ。");
            EnemyAttackData atk = arch.Attack(0);
            Assert.AreEqual(EnemyAttackClass.Normal, atk.AttackClass, "通常攻撃。");
            Assert.IsTrue(atk.Guardable && atk.JustGuardable && atk.Steppable, "Guard／JG／Step 可。");
            Assert.GreaterOrEqual(atk.PrepareSeconds, 0.25f, "予兆 0.25 秒以上。");
            Assert.GreaterOrEqual(atk.JustGuardPoiseReturn, 15f, "JG 反射 15〜20。");
            Assert.LessOrEqual(atk.JustGuardPoiseReturn, 20f);
            Assert.IsFalse(arch.CanGuard, "近接敵は高度なガードを持たない。");
            Assert.IsFalse(arch.CanEvade, "近接敵は回避を持たない。");
        }

        private static EnemyArchetypeData AssetDatabase_LoadMelee()
        {
            return UnityEditor.AssetDatabase.LoadAssetAtPath<EnemyArchetypeData>(
                "Assets/_Project/Data/Enemies/SO_Enemy_Melee_Prototype.asset");
        }
    }
}
