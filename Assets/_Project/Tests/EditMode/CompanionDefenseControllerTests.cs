using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-04b：仲間のガード／回避判断（<see cref="CompanionDefenseController"/>）を検証する。
    ///
    /// 固定するのは「観測可能な危険に反応すること」（入力を読まない）、「ガード不能な危険には回避を選ぶこと」、
    /// 「クールダウンで連続できないこと」、「倒れた・ひるんだ・退場した瞬間に構えを解くこと」、そして
    /// 「Data で能力を切れること」。危険観測は Fake を注入するため、物理にもフレームにも依存しない。
    /// </summary>
    public sealed class CompanionDefenseControllerTests : CompanionActivityFixture
    {
        private const float GuardCooldown = 3f;
        private const float EvadeCooldown = 4f;

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

        /// <summary>危険を任意に出せる観測（入力ではなく「観測結果」を差し替える）。</summary>
        private sealed class FakeDanger : IEnemyDangerSense
        {
            public bool HasDanger { get; set; }
            public bool Unblockable { get; set; }
            public int SenseCount { get; private set; }

            public EnemyDangerStimulus Sense(Vector3 selfPosition, Vector3 selfForward, int selfDamageableId)
            {
                SenseCount++;
                return HasDanger
                    ? new EnemyDangerStimulus(selfPosition + Vector3.forward * 2f, Vector3.back, Unblockable)
                    : EnemyDangerStimulus.None;
            }
        }

        private CompanionData MakeData(bool canGuard = true, bool canEvade = true)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_canGuard", canGuard);
            SetPrivateField(data, "_canEvade", canEvade);
            SetPrivateField(data, "_guardCooldownSeconds", GuardCooldown);
            SetPrivateField(data, "_evadeCooldownSeconds", EvadeCooldown);
            return data;
        }

        private (CompanionActor actor, CompanionDefenseController defense, FakeDanger danger) MakeCompanion(
            bool canGuard = true, bool canEvade = true)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData(canGuard, canEvade));
            actor.ResetState(CompanionState.Follow);

            var defense = go.AddComponent<CompanionDefenseController>();
            defense.Bind(actor);

            var danger = new FakeDanger();
            defense.SetDangerSense(danger);
            return (actor, defense, danger);
        }

        // ---- ガード ----

        [Test]
        public void NoDanger_DoesNotGuard()
        {
            (CompanionActor actor, CompanionDefenseController defense, FakeDanger _) = MakeCompanion();

            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsGuarding, "危険が無ければ構えない。");
            Assert.AreEqual(CompanionState.Follow, actor.State);
        }

        [Test]
        public void Danger_StartsGuard()
        {
            (CompanionActor actor, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;

            defense.TickDefense(0.1f);

            Assert.IsTrue(defense.IsGuarding, "観測した危険に対して構える（入力ではなく攻撃の予兆に反応する）。");
            Assert.AreEqual(CompanionState.Guard, actor.State);
        }

        [Test]
        public void DangerGone_ReleasesGuard()
        {
            (CompanionActor _, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            defense.TickDefense(0.1f);
            Assert.IsTrue(defense.IsGuarding, "前提：構えている。");

            danger.HasDanger = false;
            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsGuarding, "危険が去ったら構えを解く（構えっぱなしにしない）。");
        }

        [Test]
        public void Guard_HasCooldown()
        {
            (CompanionActor _, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            defense.TickDefense(0.1f);
            danger.HasDanger = false;
            defense.TickDefense(0.1f); // 解除 → クールダウン開始。

            danger.HasDanger = true;
            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsGuarding, "クールダウン中は再び構えられない。");

            defense.TickDefense(GuardCooldown);
            defense.TickDefense(0.1f);

            Assert.IsTrue(defense.IsGuarding, "クールダウンが明ければ構え直せる。");
        }

        // ---- 回避 ----

        [Test]
        public void UnblockableDanger_EvadesInsteadOfGuarding()
        {
            (CompanionActor actor, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            danger.Unblockable = true;

            defense.TickDefense(0.1f);

            Assert.IsTrue(defense.IsEvadeInvulnerable, "ガード不能な危険には回避で応じる。");
            Assert.IsFalse(defense.IsGuarding, "構えても意味が無いので構えない。");
            Assert.AreEqual(CompanionState.Evade, actor.State);
        }

        [Test]
        public void Evade_InvulnerabilityIsShort()
        {
            (CompanionActor _, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            danger.Unblockable = true;
            defense.TickDefense(0.1f);
            Assert.IsTrue(defense.IsEvadeInvulnerable, "前提：無敵中。");

            defense.TickDefense(EnemyEvadeAbility.DefaultInvulnerableSeconds);

            Assert.IsFalse(defense.IsEvadeInvulnerable, "無敵は短い（避け続けられない）。");
        }

        [Test]
        public void Evade_CannotRepeatDuringCooldown()
        {
            (CompanionActor _, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            danger.Unblockable = true;
            defense.TickDefense(0.1f);
            defense.TickDefense(EnemyEvadeAbility.DefaultInvulnerableSeconds); // 無敵終了 → クールダウン。

            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsEvadeInvulnerable, "連続回避はできない。");
        }

        // ---- 能力の有無 ----

        [Test]
        public void WithoutGuardAbility_DoesNotGuard()
        {
            (CompanionActor _, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion(canGuard: false);
            danger.HasDanger = true;

            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsGuarding, "Data でガードを切れる。");
        }

        [Test]
        public void WithoutEvadeAbility_GuardsAgainstUnblockable()
        {
            (CompanionActor _, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion(canEvade: false);
            danger.HasDanger = true;
            danger.Unblockable = true;

            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsEvadeInvulnerable, "Data で回避を切れる。");
            Assert.IsTrue(defense.IsGuarding, "回避が使えないなら、せめて構える。");
        }

        // ---- 状態による抑制 ----

        [TestCase(CompanionState.Down)]
        [TestCase(CompanionState.Stagger)]
        [TestCase(CompanionState.Away)]
        public void CannotDefend_WhileDownStaggerOrAway(CompanionState state)
        {
            (CompanionActor actor, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            defense.TickDefense(0.1f);
            Assert.IsTrue(defense.IsGuarding, "前提：構えている。");

            actor.ResetState(state);
            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsGuarding, state + " へ入った時点で構えを解く（構えたまま倒れない）。");
        }

        [Test]
        public void Disable_ClearsDefense()
        {
            (CompanionActor _, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            defense.TickDefense(0.1f);

            MethodInfo m = typeof(CompanionDefenseController)
                .GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m);
            m.Invoke(defense, null);

            Assert.IsFalse(defense.IsGuarding, "無効化・Scene 離脱で構えを残さない。");
        }

        // ---- 被弾側との接続 ----

        [Test]
        public void GuardingCompanion_BlocksIncomingHit()
        {
            (CompanionActor actor, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            var receiver = actor.gameObject.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);
            danger.HasDanger = true;
            defense.TickDefense(0.1f);
            Assert.IsTrue(defense.IsGuarding, "前提：構えている。");

            int hpBefore = receiver.CurrentHp;
            receiver.ReceiveHit(MakeFrontalHit(receiver));

            Assert.AreEqual(hpBefore, receiver.CurrentHp,
                "判断（構える）と解決（防ぐ）が繋がっている。片方だけ動いても意味が無い。");
        }

        private static Momotaro.Gameplay.Combat.HitInfo MakeFrontalHit(Momotaro.Gameplay.Combat.IDamageable target)
        {
            return new Momotaro.Gameplay.Combat.HitInfo(
                null, target, Vector3.back, Vector3.zero,
                new Momotaro.Gameplay.Combat.HitDamage(30f, 0f, 0f),
                guardable: true, justGuardable: false,
                hitId: Momotaro.Gameplay.Combat.HitId.Single(9001));
        }

        // ---- Guard → Evade（P4-FIX-R2。レビュー R2-07） ----

        /// <summary>
        /// ガード中にガード不能な危険へ切り替わったら、<b>旧ガードを解いてから</b>回避へ入る。
        /// 以前は解かずに回避を始めていて、IsGuarding と IsEvading が同時に立ち、無敵の切れ目に旧ガードが働いた。
        /// </summary>
        [Test]
        public void GuardThenUnblockableDanger_ReleasesGuardAndEvades()
        {
            (CompanionActor actor, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            danger.HasDanger = true;
            defense.TickDefense(0.1f);
            Assert.IsTrue(defense.IsGuarding, "前提：構えている。");

            danger.Unblockable = true;
            defense.TickDefense(0.1f);

            Assert.IsFalse(defense.IsGuarding, "回避へ移るときに旧ガード能力を解いている。");
            Assert.IsTrue(defense.IsEvading, "回避が始まっている。");
            Assert.AreEqual(CompanionState.Evade, actor.State);
            Assert.Greater(defense.Guard.CooldownRemaining, 0f, "解いたガードはクールダウンへ入る（解除扱い）。");

            // 回避の動作が終われば通常状態へ戻る（能力も所有権も残らない）。
            danger.HasDanger = false;
            defense.TickDefense(EnemyEvadeAbility.DefaultInvulnerableSeconds + 0.05f);
            defense.TickDefense(0.01f);
            Assert.IsFalse(defense.IsEvading);
            Assert.IsFalse(defense.IsGuarding);
            Assert.AreEqual(CompanionState.Follow, actor.State, "回避終了後に正常復帰する。");
        }

        /// <summary>
        /// 行動を奪われたら（被弾の強制遷移）、ガード能力も同じ呼び出しの中で解かれる。
        /// 受け口は状態ではなく能力を読むので、状態だけ変わって能力が残ると次の命中を旧ガードが防ぐ。
        /// </summary>
        [Test]
        public void ForcedHitDuringGuard_ReleasesGuardAbilitySynchronously()
        {
            (CompanionActor actor, CompanionDefenseController defense, FakeDanger danger) = MakeCompanion();
            var states = actor.GetComponent<CompanionStateArbiter>();
            danger.HasDanger = true;
            defense.TickDefense(0.1f);
            Assert.IsTrue(defense.IsGuarding, "前提：構えている。");

            states.ForceHit(CompanionState.Stagger, CompanionStateChangeReason.Staggered);

            Assert.IsFalse(defense.IsGuarding, "奪われた瞬間に能力を手放している（Tick を待たない）。");
            Assert.AreEqual(CompanionState.Stagger, actor.State, "状態は奪った側のもの。防御側が触らない。");
        }
    }
}
