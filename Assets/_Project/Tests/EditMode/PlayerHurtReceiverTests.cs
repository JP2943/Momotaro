using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-01：被弾経路（<see cref="PlayerVitalsHolder.ReceiveHit"/>）が <see cref="IPlayerHurtReaction"/> を用いて
    /// (1) 実 HP ダメージ 1 以上でだけ Hurt を起動し、(2) Guard／JG／有効 Step では起動せず、(3) 被弾後無敵 0.50 秒の間は
    /// 通常 Damage（ガード不能・Steppable=false を含む）を無効化し、(4) 無敵終了後は再被弾でき、(5) 致死（HP0）では
    /// Hurt を起動しない（Defeated 優先の準備境界）ことを検証する（仕様書 §3.1/§3.2/Table3）。
    /// </summary>
    public sealed class PlayerHurtReceiverTests
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

        private sealed class FakeGuardState : MonoBehaviour, IGuardState
        {
            public bool Guarding;
            public Vector3 Fwd = Vector3.forward;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
        }

        private sealed class FakeJustGuardState : MonoBehaviour, IJustGuardState
        {
            public bool Can;
            public bool CanJustGuard => Can;
            public void NotifyJustGuardSuccess() { }
        }

        private sealed class FakeEvade : MonoBehaviour, IEvadeState
        {
            public bool Inv;
            public bool IsInvincible => Inv;
        }

        private sealed class Recorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
        }

        private PlayerData MakePlayerData(int maxHp, float defense, int maxStamina)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetField(d, "_maxHp", maxHp);
            SetField(d, "_defense", defense);
            SetField(d, "_maxStamina", maxStamina);
            return d;
        }

        private sealed class Setup
        {
            public PlayerVitalsHolder Holder;
            public PlayerHitReaction Reaction;
            public FakeGuardState Guard;
            public FakeJustGuardState JustGuard;
            public FakeEvade Evade;
            public Recorder Rec;
        }

        private Setup MakePlayer(int maxHp = 100, float defense = 0f, int maxStamina = 100)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var guard = go.AddComponent<FakeGuardState>();
            var justGuard = go.AddComponent<FakeJustGuardState>();
            var evade = go.AddComponent<FakeEvade>();
            var reaction = go.AddComponent<PlayerHitReaction>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", MakePlayerData(maxHp, defense, maxStamina));
            go.SetActive(true);

            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return new Setup { Holder = holder, Reaction = reaction, Guard = guard, JustGuard = justGuard, Evade = evade, Rec = rec };
        }

        private static HitInfo Damaging(IDamageable target, float preDefenseHp, int id = 1)
        {
            // 正面・ガード可能・JG 可能の通常命中。ガード/JG/Step を無効にしていれば Damage 経路へ入る。
            return new HitInfo(null, target, -Vector3.forward, Vector3.zero, new HitDamage(preDefenseHp, 0f, 0f),
                true, true, HitId.Single(id));
        }

        private static HitInfo UnblockableUnsteppable(IDamageable target, float preDefenseHp, int id)
        {
            // ガード不能・JG 不能・Steppable=false の通常命中（被弾後無敵の網羅性検証用）。
            return new HitInfo(null, target, -Vector3.forward, Vector3.zero, new HitDamage(preDefenseHp, 0f, 0f),
                0f, 0f, guardable: false, justGuardable: false, isJustGuardCounter: false,
                defenseIgnoreRatio: 0f, stunHpMultiplierOverride: 0f, steppable: false, hitId: HitId.Single(id));
        }

        [Test]
        public void RealDamage_BeginsHurt_AndReducesHp()
        {
            var s = MakePlayer(maxHp: 100, defense: 0f);
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));

            Assert.AreEqual(HitResultKind.Damage, s.Rec.Received[0].Kind);
            Assert.AreEqual(90, s.Holder.Vitals.Health.Current, "10 ダメージで 90。");
            Assert.IsTrue(s.Reaction.IsHurt, "実ダメージ 1 以上で Hurt 起動。");
            Assert.IsTrue(s.Reaction.IsPostHitInvincible, "同時に被弾後無敵開始。");
        }

        [Test]
        public void ZeroAppliedDamage_DoesNotBeginHurt()
        {
            // 高防御で丸め後 0 になる命中（4 × 防御補正 0.1 = 0.4 → 四捨五入 0）：Damage 結果でも実減少 1 未満なら Hurt は起動しない。
            var s = MakePlayer(maxHp: 100, defense: 1000f);
            s.Holder.ReceiveHit(Damaging(s.Holder, 4f));

            Assert.AreEqual(HitResultKind.Damage, s.Rec.Received[0].Kind);
            Assert.AreEqual(0f, s.Rec.Received[0].AppliedDamage.Hp, "実減少 0。");
            Assert.AreEqual(100, s.Holder.Vitals.Health.Current, "HP は減らない。");
            Assert.IsFalse(s.Reaction.IsHurt, "実ダメージ 1 未満では Hurt 非発生。");
        }

        [Test]
        public void Guarded_DoesNotBeginHurt()
        {
            var s = MakePlayer(maxHp: 100, defense: 0f, maxStamina: 100);
            s.Guard.Guarding = true;
            s.Guard.Fwd = Vector3.forward; // 正面ガード（攻撃は -forward から）
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));

            Assert.AreEqual(HitResultKind.Guard, s.Rec.Received[0].Kind, "防御成功。");
            Assert.IsFalse(s.Reaction.IsHurt, "ガード成功では Hurt 非発生。");
        }

        [Test]
        public void JustGuarded_DoesNotBeginHurt()
        {
            var s = MakePlayer(maxHp: 100, defense: 0f);
            s.Guard.Guarding = true;      // 前方 180°判定にガード方向を用いる
            s.Guard.Fwd = Vector3.forward;
            s.JustGuard.Can = true;
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));

            Assert.AreEqual(HitResultKind.JustGuard, s.Rec.Received[0].Kind, "JG 成立。");
            Assert.IsFalse(s.Reaction.IsHurt, "JG 成立では Hurt 非発生。");
        }

        [Test]
        public void StepInvincible_Evades_DoesNotBeginHurt()
        {
            var s = MakePlayer(maxHp: 100, defense: 0f);
            s.Evade.Inv = true; // ステップ I-frame（Steppable な攻撃を回避）
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f));

            Assert.AreEqual(HitResultKind.Evade, s.Rec.Received[0].Kind, "ステップ無敵で回避。");
            Assert.IsFalse(s.Reaction.IsHurt, "有効ステップでは Hurt 非発生。");
            Assert.AreEqual(100, s.Holder.Vitals.Health.Current, "HP は減らない。");
        }

        [Test]
        public void PostHitInvincible_NullifiesFollowUp_IncludingUnblockableUnsteppable()
        {
            var s = MakePlayer(maxHp: 100, defense: 0f);
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f)); // HP 90・無敵開始
            Assert.AreEqual(90, s.Holder.Vitals.Health.Current);

            // 追撃：ガード不能・Steppable=false でも被弾後無敵で無効化される。
            s.Holder.ReceiveHit(UnblockableUnsteppable(s.Holder, 50f, id: 2));

            Assert.AreEqual(HitResultKind.Evade, s.Rec.Received[1].Kind, "被弾後無敵は種別に依らず無効化。");
            Assert.AreEqual(90, s.Holder.Vitals.Health.Current, "追撃で HP は減らない。");
        }

        [Test]
        public void AfterInvincibilityExpires_TakesDamageAgain()
        {
            var s = MakePlayer(maxHp: 100, defense: 0f);
            s.Holder.ReceiveHit(Damaging(s.Holder, 10f)); // HP 90・無敵
            s.Reaction.Tick(0.50f); // 無敵ちょうど終了
            Assert.IsFalse(s.Reaction.IsPostHitInvincible);

            s.Holder.ReceiveHit(Damaging(s.Holder, 10f, id: 3)); // 再被弾可能
            Assert.AreEqual(80, s.Holder.Vitals.Health.Current, "無敵終了後は再度被弾する。");
            Assert.IsTrue(s.Reaction.IsHurt, "再被弾で Hurt 再起動。");
        }

        [Test]
        public void LethalHit_DoesNotBeginHurt_ButStillDamageResult()
        {
            var s = MakePlayer(maxHp: 5, defense: 0f);
            s.Holder.ReceiveHit(Damaging(s.Holder, 100f)); // HP0

            Assert.AreEqual(0, s.Holder.Vitals.Health.Current);
            Assert.AreEqual(HitResultKind.Damage, s.Rec.Received[0].Kind, "致死でも Damage 結果は出る。");
            Assert.IsFalse(s.Reaction.IsHurt, "HP0 は Hurt に入らず Defeated 優先（P3.5-02 の準備境界）。");
        }

        [Test]
        public void GuardBrokenThenDamage_BeginsHurt_AndDiscardsRemainingBreak()
        {
            var s = MakePlayer(maxHp: 100, defense: 0f, maxStamina: 20);
            s.Holder.ConsumeStamina(20f); // スタミナ 0 → ガードブレイク
            Assert.IsTrue(s.Holder.IsGuardBroken, "ブレイク中。");

            s.Holder.ReceiveHit(Damaging(s.Holder, 10f)); // ブレイク中の実ダメージ

            Assert.IsTrue(s.Reaction.IsHurt, "ブレイク中被弾で Hurt へ遷移。");
            Assert.IsFalse(s.Holder.IsGuardBroken, "残存 Break 時間は破棄され、GuardBreak へ戻らない。");
        }
    }
}
