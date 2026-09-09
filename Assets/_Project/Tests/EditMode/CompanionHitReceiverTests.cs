using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-04：仲間の被弾解決（<see cref="CompanionVitals"/> と <see cref="CompanionHitReceiver"/>）を検証する。
    ///
    /// 固定するのは、主人公・敵と<b>同じ解決順</b>に載っていること（退場・ダウン中は受け付けない、同一命中は 1 回だけ、
    /// 被弾後無敵、回避、ガード、ダメージの順）、ひるみとダウンが状態へ反映されること、そしてダウンから復帰することの 3 点。
    ///
    /// 命中は仲間専用の経路ではなく既存の <see cref="IDamageable.ReceiveHit"/> をそのまま通す。時間は
    /// <see cref="CompanionHitReceiver.TickVitals"/> へ注入するため、Editor の描画間隔に依存しない。
    /// </summary>
    public sealed class CompanionHitReceiverTests
    {
        private const int MaxHp = 100;
        private const float FlinchResistance = 40f;
        private const float FlinchSeconds = 0.8f;
        private const float InvincibleSeconds = 0.5f;
        private const float RecoverySeconds = 5f;

        private readonly List<Object> _spawned = new List<Object>();
        private int _nextHitId;

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

        /// <summary>攻撃者役（陣営と向きだけを持つ）。</summary>
        private sealed class FakeAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;
        }

        private sealed class ResultRecorder : IHitResultListener
        {
            public readonly List<HitResult> Results = new List<HitResult>();

            public void OnHitResult(in HitResult result) => Results.Add(result);
        }

        /// <summary>指定した防御状態を返すだけの部品（P4-04b の実装が入るまでの検証用）。</summary>
        private sealed class FakeDefense : MonoBehaviour, ICompanionDefenseState
        {
            public bool IsGuarding { get; set; }
            public bool IsEvadeInvulnerable { get; set; }
        }

        private CompanionData MakeData(int maxHp = MaxHp, float defense = 0f)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", maxHp);
            SetPrivateField(data, "_defense", defense);
            SetPrivateField(data, "_flinchResistance", FlinchResistance);
            SetPrivateField(data, "_flinchSeconds", FlinchSeconds);
            SetPrivateField(data, "_postHitInvincibleSeconds", InvincibleSeconds);
            SetPrivateField(data, "_leaveRecoverySeconds", RecoverySeconds);
            SetPrivateField(data, "_reviveHpRatio", 0.5f);
            return data;
        }

        private (CompanionActor actor, CompanionHitReceiver receiver, ResultRecorder log) MakeCompanion(
            int maxHp = MaxHp, float defense = 0f)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData(maxHp, defense));
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            var log = new ResultRecorder();
            receiver.Results.AddListener(log);
            return (actor, receiver, log);
        }

        private FakeAttacker MakeAttacker()
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            return go.AddComponent<FakeAttacker>();
        }

        /// <summary>命中を組み立てる。既定は正面（仲間の前方 +Z へ向かう向き）から入る通常攻撃。</summary>
        private HitInfo MakeHit(
            ICombatActor attacker, IDamageable target, float hp = 20f, float flinch = 0f,
            bool guardable = true, bool steppable = true, Vector3? direction = null, HitId? id = null)
        {
            // 攻撃の進行方向。仲間が +Z を向いているとき、-Z 方向へ進む攻撃＝正面から来る攻撃。
            Vector3 dir = direction ?? Vector3.back;
            return new HitInfo(
                attacker, target, dir, Vector3.zero,
                new HitDamage(hp, 0f, flinch),
                guardStaminaDamage: 0f,
                justGuardPoiseDamage: 0f,
                guardable: guardable,
                justGuardable: false,
                isJustGuardCounter: false,
                defenseIgnoreRatio: 0f,
                stunHpMultiplierOverride: 0f,
                steppable: steppable,
                hitId: id ?? HitId.Single(++_nextHitId));
        }

        // ---- 基本のダメージ ----

        [Test]
        public void Damage_ReducesHpAndPublishesResult()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 20f));

            Assert.AreEqual(MaxHp - 20, receiver.CurrentHp, "防御 0 なら攻撃側寄与がそのまま入る。");
            Assert.AreEqual(1, log.Results.Count);
            Assert.AreEqual(HitResultKind.Damage, log.Results[0].Kind);
            Assert.AreEqual(20f, log.Results[0].AppliedDamage.Hp, 1e-3f, "結果には実際に減った量を載せる。");
        }

        [Test]
        public void Damage_AppliesDefense()
        {
            // 防御 100 → 補正 100/(100+100)=0.5。攻撃側寄与 20 → 10。
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion(defense: 100f);
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 20f));

            Assert.AreEqual(MaxHp - 10, receiver.CurrentHp, "防御は被弾側で適用する（攻撃側は防御適用前の値を渡す）。");
        }

        // ---- 解決順 ----

        [Test]
        public void SameHitId_IsAcceptedOnlyOnce()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            HitId id = HitId.Single(1234);

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f, id: id));
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f, id: id));

            Assert.AreEqual(MaxHp - 10, receiver.CurrentHp,
                "同一命中は 1 回だけ（直接命中と肩代わり転送がどちらの順で届いても二重に入らない）。");
            Assert.AreEqual(1, log.Results.Count, "2 回目は結果も出さない。");
        }

        [Test]
        public void PostHitInvincible_RejectsFollowUp()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f));
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f)); // 別の命中だが無敵中。

            Assert.AreEqual(MaxHp - 10, receiver.CurrentHp, "被弾後無敵の間は HP が減らない。");
            Assert.AreEqual(HitResultKind.Evade, log.Results[1].Kind);
        }

        [Test]
        public void PostHitInvincible_ExpiresAfterItsDuration()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f));

            receiver.TickVitals(InvincibleSeconds + 0.01f);
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f));

            Assert.AreEqual(MaxHp - 20, receiver.CurrentHp, "無敵が明ければ再び被弾する。");
        }

        [Test]
        public void EvadeInvulnerable_RejectsHit()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeDefense defense = receiver.gameObject.AddComponent<FakeDefense>();
            defense.IsEvadeInvulnerable = true;
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 30f));

            Assert.AreEqual(MaxHp, receiver.CurrentHp, "回避の無敵で命中を無効化する。");
            Assert.AreEqual(HitResultKind.Evade, log.Results[0].Kind);
        }

        [Test]
        public void EvadeInvulnerable_DoesNotStopUnsteppableHit()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeDefense defense = receiver.gameObject.AddComponent<FakeDefense>();
            defense.IsEvadeInvulnerable = true;
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 30f, steppable: false));

            Assert.AreEqual(MaxHp - 30, receiver.CurrentHp, "ステップで避けられない攻撃は無敵を貫通する（敵・主人公と同じ）。");
        }

        [Test]
        public void Guard_BlocksFrontalGuardableHit()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeDefense defense = receiver.gameObject.AddComponent<FakeDefense>();
            defense.IsGuarding = true;
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 30f));

            Assert.AreEqual(MaxHp, receiver.CurrentHp, "正面からのガード可能な攻撃は防げる。");
            Assert.AreEqual(HitResultKind.Guard, log.Results[0].Kind);
        }

        [Test]
        public void Guard_DoesNotBlockUnguardableHit()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeDefense defense = receiver.gameObject.AddComponent<FakeDefense>();
            defense.IsGuarding = true;
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 30f, guardable: false));

            Assert.AreEqual(MaxHp - 30, receiver.CurrentHp, "ガード不能攻撃は構えていても通る。");
        }

        [Test]
        public void Guard_DoesNotBlockBackAttack()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeDefense defense = receiver.gameObject.AddComponent<FakeDefense>();
            defense.IsGuarding = true;
            FakeAttacker attacker = MakeAttacker();

            // 仲間は +Z を向いている。+Z へ進む攻撃＝背後から来る攻撃。
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 30f, direction: Vector3.forward));

            Assert.AreEqual(MaxHp - 30, receiver.CurrentHp, "背後からの攻撃はガードできない（前方 180°のみ）。");
        }

        // ---- ひるみ ----

        [Test]
        public void Flinch_EntersStaggerState()
        {
            (CompanionActor actor, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 1f, flinch: FlinchResistance));

            Assert.AreEqual(CompanionState.Stagger, actor.State, "ひるみ耐性に達したらひるむ。");
        }

        [Test]
        public void Flinch_ReturnsToFollowWhenItEnds()
        {
            (CompanionActor actor, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 1f, flinch: FlinchResistance));
            Assert.AreEqual(CompanionState.Stagger, actor.State, "前提：ひるんでいる。");

            receiver.TickVitals(FlinchSeconds + 0.01f);

            Assert.AreEqual(CompanionState.Follow, actor.State, "ひるみが明けたら追従へ戻る。");
        }

        [Test]
        public void SmallHit_DoesNotFlinch()
        {
            (CompanionActor actor, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 5f, flinch: FlinchResistance - 1f));

            Assert.AreEqual(CompanionState.Follow, actor.State, "耐性未満の蓄積ではひるまない。");
        }

        // ---- ダウンと復帰 ----

        [Test]
        public void LethalHit_EntersDown()
        {
            (CompanionActor actor, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: MaxHp));

            Assert.AreEqual(0, receiver.CurrentHp);
            Assert.AreEqual(CompanionState.Down, actor.State, "HP0 でダウンする。");
            Assert.AreEqual(HitResultKind.Damage, log.Results[0].Kind, "致命打自体は Damage として通知する。");
        }

        [Test]
        public void Down_IgnoresFurtherHits()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: MaxHp));
            int resultsAfterDown = log.Results.Count;

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f));

            Assert.AreEqual(resultsAfterDown, log.Results.Count, "倒れている間は結果も通知も出さない（死体を撃たない）。");
        }

        [Test]
        public void Down_RecoversAfterDelay()
        {
            (CompanionActor actor, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: MaxHp));

            receiver.TickVitals(RecoverySeconds - 0.1f);
            Assert.AreEqual(CompanionState.Down, actor.State, "復帰待ちの間はダウンのまま。");

            receiver.TickVitals(0.2f);

            Assert.AreEqual(CompanionState.Follow, actor.State, "復帰したら追従へ戻る（仲間のダウンは終端ではない）。");
            Assert.AreEqual(MaxHp / 2, receiver.CurrentHp, "Data の割合で HP を戻す。");
        }

        [Test]
        public void Recovered_CanBeHitAgain()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: MaxHp));
            receiver.TickVitals(RecoverySeconds + 0.1f);

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 10f));

            Assert.AreEqual((MaxHp / 2) - 10, receiver.CurrentHp, "復帰後は通常どおり被弾する。");
        }

        // ---- 退場 ----

        [Test]
        public void Away_IgnoresHits()
        {
            (CompanionActor actor, CompanionHitReceiver receiver, ResultRecorder log) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            actor.RequestState(CompanionState.Away, CompanionStateChangeReason.Left);

            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 30f));

            Assert.AreEqual(MaxHp, receiver.CurrentHp, "退場中は場に居ないので被弾しない。");
            Assert.AreEqual(0, log.Results.Count);
        }

        // ---- 初期化 ----

        [Test]
        public void ResetVitals_RestoresFullHp()
        {
            (CompanionActor _, CompanionHitReceiver receiver, ResultRecorder _) = MakeCompanion();
            FakeAttacker attacker = MakeAttacker();
            receiver.ReceiveHit(MakeHit(attacker, receiver, hp: 30f));

            receiver.ResetVitals();

            Assert.AreEqual(MaxHp, receiver.CurrentHp);
            Assert.IsFalse(receiver.Vitals.IsPostHitInvincible, "無敵も残さない。");
        }
    }
}
