using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03 後半：仲間の戦闘駆動（<see cref="CompanionCombatController"/>）を検証する。接近・攻撃開始・判定の適用と、
    /// <b>誤射しないこと</b>（主人公・仲間・自分自身に当てない）、<b>ひるみ・ダウンで判定が即座に消えること</b>、
    /// クールダウンを守ること、無効化で後始末することを固定する。
    ///
    /// 命中は既存の <see cref="IDamageable.ReceiveHit"/> 経路をそのまま使う（仲間専用の Damage 経路を作らない）。
    /// これにより敵側の <c>EnemyThreatTracker</c> が仲間の与ダメージを既存のまま獲得ヘイトへ変換できる。
    /// 時間は <see cref="CompanionCombatController.TickCombat"/> へ外部注入するため、決定的に検証できる。
    /// </summary>
    public sealed class CompanionCombatControllerTests
    {
        private const float Startup = 0.2f;
        private const float Active = 0.1f;
        private const float Recovery = 0.3f;
        private const float Cooldown = 1f;
        private const float UseRange = 2f;
        private const float AttackPower = 60f;
        private const float HpMultiplier = 0.8f;

        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

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
            PerceptionTargetRegistry.Clear();
        }

        // ---- 補助 ----

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        /// <summary>敵役（狙われる側・被弾側・攻撃対象）。既存の契約だけを実装し、敵 AI は用いない。</summary>
        private sealed class FakeEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward { get; set; } = Vector3.forward;

            public int DamageableId => GetInstanceID();
            public int ActorId => GetInstanceID();
            public Vector3 Position => transform.position;
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat => 0f;
            public float AcquiredThreatMultiplier => 1f;

            public int ReceivedHits { get; private set; }
            public HitInfo LastHit { get; private set; }

            public void ReceiveHit(in HitInfo hit)
            {
                ReceivedHits++;
                LastHit = hit;
            }
        }

        /// <summary>味方役（誤射の検証用）。仲間・主人公はどちらも仲間の攻撃対象にならない。</summary>
        private sealed class FakeFriendly : MonoBehaviour, ICombatActor, IDamageable
        {
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;
            public int DamageableId => GetInstanceID();
            public int ReceivedHits { get; private set; }

            public void ReceiveHit(in HitInfo hit) => ReceivedHits++;
        }

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", UseRange);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", Cooldown);
            SetPrivateField(attack, "_startupSeconds", Startup);
            SetPrivateField(attack, "_activeSeconds", Active);
            SetPrivateField(attack, "_recoverySeconds", Recovery);
            SetPrivateField(attack, "_hpMultiplier", HpMultiplier);
            SetPrivateField(attack, "_poiseDamage", 6f);
            SetPrivateField(attack, "_flinchPower", 10f);
            return attack;
        }

        private CompanionData MakeData(AttackData attack)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_attackPower", AttackPower);
            SetPrivateField(data, "_basicAttack", attack);
            return data;
        }

        private sealed class Companion
        {
            public GameObject Root;
            public CompanionActor Actor;
            public CompanionTargetTracker Tracker;
            public CompanionCombatController Combat;
            public CompanionFollowController Follow;
        }

        private Companion MakeCompanion(AttackData attack, Vector3 position = default)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = position;

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData(attack));
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var motor = go.AddComponent<CompanionMotor>(); // Rigidbody は RequireComponent で付く。
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);

            var follow = go.AddComponent<CompanionFollowController>();
            follow.Bind(null, actor, motor);

            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);
            InvokePrivate(combat, "OnEnable"); // EditMode では自動で呼ばれないことがあるため明示する。

            return new Companion { Root = go, Actor = actor, Tracker = tracker, Combat = combat, Follow = follow };
        }

        private FakeEnemy MakeEnemy(Vector3 position)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;
            var enemy = go.AddComponent<FakeEnemy>();

            // 既定では原点（仲間の位置）を向かせる。背後補正（HP ×1.1／体幹 ×1.5）が意図せず混ざらないようにする。
            Vector3 toOrigin = -position;
            enemy.Forward = toOrigin.sqrMagnitude > 1e-6f ? toOrigin.normalized : Vector3.back;

            PerceptionTargetRegistry.Register(enemy);
            return enemy;
        }

        /// <summary>索敵 → 戦闘の順に 1 Tick 進める（実機の Update 相当）。</summary>
        private static void Tick(Companion companion, float deltaTime)
        {
            companion.Tracker.TickTargeting();
            companion.Combat.TickCombat(deltaTime);
        }

        /// <summary>攻撃を開始し、判定段まで進める。</summary>
        private static void EnterActive(Companion companion)
        {
            Tick(companion, 0f);
            Assert.IsTrue(companion.Combat.IsAttacking, "前提：攻撃が始まっている。");
            Tick(companion, Startup);
            Assert.AreEqual(CompanionAttackPhase.Active, companion.Combat.AttackState.Phase, "前提：判定中。");
        }

        // ---- 対象なし ----

        [Test]
        public void NoTarget_IsIdle()
        {
            Companion companion = MakeCompanion(MakeAttack());

            Tick(companion, 0.1f);

            Assert.AreEqual(CompanionEngageDecision.Idle, companion.Combat.Decision);
            Assert.IsFalse(companion.Combat.IsEngaged, "戦闘していないので追従が Motor を握ったままでよい。");
        }

        [Test]
        public void WithoutAttackData_DoesNotChase()
        {
            Companion companion = MakeCompanion(null);
            MakeEnemy(new Vector3(0f, 0f, 5f));

            Tick(companion, 0.1f);

            Assert.AreEqual(CompanionEngageDecision.Idle, companion.Combat.Decision,
                "攻撃を持たない構成では敵へ寄っていかない。");
        }

        // ---- 接近 ----

        [Test]
        public void DistantTarget_Chases()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 5f));

            Tick(companion, 0.1f);

            Assert.AreEqual(CompanionEngageDecision.Chase, companion.Combat.Decision);
            Assert.AreEqual(CompanionState.Chase, companion.Actor.State, "接近中は Chase 状態になる。");
            Assert.IsTrue(companion.Combat.IsEngaged);
        }

        [Test]
        public void WhileEngaged_FollowYieldsMotor()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 5f));

            Tick(companion, 0.1f);

            Assert.IsTrue(companion.Follow.IsYieldingToCombat,
                "戦闘中は追従が移動を譲る（両方が移動先を書くと隊列と敵の間で震える）。");
        }

        [Test]
        public void Chase_FacesTarget()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(5f, 0f, 0f)); // 右方向。

            Tick(companion, 0.1f);

            Assert.AreEqual(1f, companion.Actor.Forward.x, 1e-3f, "対象の方を向く。");
        }

        // ---- 攻撃 ----

        [Test]
        public void TargetInRange_StartsAttack()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 1f));

            Tick(companion, 0f);

            Assert.IsTrue(companion.Combat.IsAttacking);
            Assert.AreEqual(1, companion.Combat.AttackCount);
            Assert.AreEqual(CompanionState.AttackPrepare, companion.Actor.State, "予兆の状態へ入る。");
            Assert.IsFalse(companion.Combat.AttackState.IsHitboxActive, "予兆中は判定を出さない。");
        }

        [Test]
        public void Attack_EntersActiveState()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 1f));

            EnterActive(companion);

            Assert.AreEqual(CompanionState.AttackActive, companion.Actor.State);
        }

        [Test]
        public void Attack_HitsEnemyOncePerSwing()
        {
            Companion companion = MakeCompanion(MakeAttack());
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f));
            EnterActive(companion);

            Assert.IsTrue(companion.Combat.TryApplyHit(enemy, enemy, Vector3.zero));
            Assert.IsFalse(companion.Combat.TryApplyHit(enemy, enemy, Vector3.zero),
                "同一 Swing で同一対象は 1 回だけ（多重ヒットしない）。");
            Assert.AreEqual(1, enemy.ReceivedHits);
            Assert.AreEqual(1, companion.Combat.HitCount);
        }

        [Test]
        public void Attack_UsesDataNumbers()
        {
            Companion companion = MakeCompanion(MakeAttack());
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f));
            EnterActive(companion);

            companion.Combat.TryApplyHit(enemy, enemy, Vector3.zero);

            // 攻撃側寄与（防御適用前）＝ 攻撃力 60 × 技倍率 0.8 × 0.1 ＝ 4.8。防御補正は被弾側で適用される。
            Assert.AreEqual(AttackPower * HpMultiplier * HpDamageCalculator.BaseScale,
                enemy.LastHit.Damage.Hp, 1e-3f, "HP は Data の攻撃力・技倍率から作る（コード直書きの数値を使わない）。");
            Assert.AreEqual(6f, enemy.LastHit.Damage.Poise, 1e-3f, "体幹ダメージは Data の値。");
            Assert.AreSame(companion.Actor, enemy.LastHit.Attacker,
                "攻撃者は仲間 Actor（敵のヘイトが仲間へ帰属するために必要）。");
        }

        [Test]
        public void Attack_AppliesBackHitBonus()
        {
            Companion companion = MakeCompanion(MakeAttack());
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f));
            enemy.Forward = Vector3.forward; // 仲間へ背を向けている。
            EnterActive(companion);

            companion.Combat.TryApplyHit(enemy, enemy, Vector3.zero);

            float front = AttackPower * HpMultiplier * HpDamageCalculator.BaseScale;
            Assert.AreEqual(front * HpDamageCalculator.BackMultiplier, enemy.LastHit.Damage.Hp, 1e-3f,
                "背後攻撃の HP 補正（×1.1）は主人公と同じ計算を通す。");
            Assert.AreEqual(6f * PoiseDamageCalculator.BackMultiplier, enemy.LastHit.Damage.Poise, 1e-3f,
                "体幹の状況補正（背後 ×1.5）も同じ。");
        }

        [Test]
        public void Attack_DoesNotHitFriendly()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 1f));
            EnterActive(companion);

            var friendlyGo = new GameObject("Friendly");
            _spawned.Add(friendlyGo);
            var friendly = friendlyGo.AddComponent<FakeFriendly>();

            friendly.Faction = CombatFaction.Player;
            Assert.IsFalse(companion.Combat.TryApplyHit(friendly, friendly, Vector3.zero), "主人公には当てない。");

            friendly.Faction = CombatFaction.Ally;
            Assert.IsFalse(companion.Combat.TryApplyHit(friendly, friendly, Vector3.zero), "他の仲間にも当てない。");

            friendly.Faction = CombatFaction.Neutral;
            Assert.IsFalse(companion.Combat.TryApplyHit(friendly, friendly, Vector3.zero), "中立にも当てない。");

            Assert.AreEqual(0, friendly.ReceivedHits);
        }

        [Test]
        public void Attack_DoesNotHitUnknownFaction()
        {
            Companion companion = MakeCompanion(MakeAttack());
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f));
            EnterActive(companion);

            Assert.IsFalse(companion.Combat.TryApplyHit(enemy, null, Vector3.zero),
                "陣営の分からない対象は殴らない（曖昧なものへ当てない）。");
        }

        [Test]
        public void Attack_DoesNotHitSelf()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 1f));
            var self = companion.Root.AddComponent<FakeEnemy>(); // 同一ルート上の被弾受け口。
            EnterActive(companion);

            Assert.IsFalse(companion.Combat.TryApplyHit(self, self, Vector3.zero), "自分自身には当てない。");
            Assert.AreEqual(0, self.ReceivedHits);
        }

        [Test]
        public void Hit_OutsideActive_IsRejected()
        {
            Companion companion = MakeCompanion(MakeAttack());
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f));

            Tick(companion, 0f); // 予兆中。

            Assert.IsFalse(companion.Combat.TryApplyHit(enemy, enemy, Vector3.zero), "判定段の外では命中しない。");
        }

        // ---- クールダウン ----

        [Test]
        public void AfterAttack_WaitsForCooldown()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 1f));

            Tick(companion, 0f);                        // 開始。
            Tick(companion, Startup + Active + Recovery); // 完了。

            Assert.IsFalse(companion.Combat.IsAttacking, "攻撃が終わっている。");
            Assert.AreEqual(Cooldown, companion.Combat.CooldownRemaining, 1e-3f);

            Tick(companion, 0.1f);
            Assert.AreEqual(CompanionEngageDecision.Hold, companion.Combat.Decision, "クールダウン中は間合いで待つ。");
            Assert.AreEqual(1, companion.Combat.AttackCount, "続けて 2 発目を出さない。");

            Tick(companion, Cooldown);
            Assert.AreEqual(2, companion.Combat.AttackCount, "クールダウンが明ければ次を出す。");
        }

        // ---- 中断・後始末 ----

        [Test]
        public void Stagger_RemovesHitboxImmediately()
        {
            Companion companion = MakeCompanion(MakeAttack());
            FakeEnemy enemy = MakeEnemy(new Vector3(0f, 0f, 1f));
            EnterActive(companion);

            // 被弾でひるむ（P4-04 の被弾解決を待たず、状態遷移だけで判定が消えることを固定する）。
            companion.Actor.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered);

            Assert.IsFalse(companion.Combat.IsAttacking, "Tick を待たずその場で中断する。");
            Assert.IsFalse(companion.Combat.TryApplyHit(enemy, enemy, Vector3.zero),
                "ひるんだ仲間の判定が 1 フレーム残って敵を殴ることはない。");
            Assert.AreEqual(0, enemy.ReceivedHits);
        }

        [Test]
        public void Down_StopsEngaging()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 5f));
            Tick(companion, 0.1f);
            Assert.IsTrue(companion.Combat.IsEngaged, "前提：接近している。");

            companion.Actor.ForceHitState(CompanionState.Down, CompanionStateChangeReason.Defeated);
            Tick(companion, 0.1f);

            Assert.AreEqual(CompanionEngageDecision.Idle, companion.Combat.Decision);
            Assert.IsFalse(companion.Combat.IsEngaged, "ダウン中は移動も攻撃もしない。");
        }

        [Test]
        public void Away_StopsEngaging()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 1f));
            Tick(companion, 0f);
            Assert.IsTrue(companion.Combat.IsAttacking, "前提：攻撃中。");

            companion.Actor.RequestState(CompanionState.Away, CompanionStateChangeReason.Left);

            Assert.IsFalse(companion.Combat.IsAttacking, "退場で攻撃を中断する。");
        }

        [Test]
        public void Disable_ClearsEngagement()
        {
            Companion companion = MakeCompanion(MakeAttack());
            MakeEnemy(new Vector3(0f, 0f, 1f));
            Tick(companion, 0f);

            InvokePrivate(companion.Combat, "OnDisable");

            Assert.IsFalse(companion.Combat.IsAttacking, "無効化・Scene 離脱で判定を残さない。");
            Assert.AreEqual(CompanionEngageDecision.Idle, companion.Combat.Decision);
        }
    }
}
