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
    /// P2-06：主人公の通常ガード解決を、実際の被弾経路（<see cref="PlayerVitalsHolder.ReceiveHit"/> が
    /// <c>GetComponentInParent&lt;IGuardState&gt;()</c> でガード状態を取得する経路）で検証する。前方 180°＋Guardable＋ガード中で
    /// HP0＋固定スタミナ消費、背後・ガード不能・非ガードは貫通。残量超過一撃も防御。飛び道具でも同じ Context で成立する。
    /// </summary>
    public sealed class PlayerGuardResolutionTests
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

        private PlayerData MakePlayerData(int maxHp, float defense, int maxStamina)
        {
            var d = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(d);
            SetField(d, "_maxHp", maxHp);
            SetField(d, "_defense", defense);
            SetField(d, "_maxStamina", maxStamina);
            return d;
        }

        /// <summary>被弾側のガード状態を差し込む検証用ダブル（同一 GameObject へ付与）。</summary>
        private sealed class FakeGuardState : MonoBehaviour, IGuardState
        {
            public bool Guarding;
            public Vector3 Fwd = Vector3.forward;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
        }

        private sealed class Recorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
        }

        private (PlayerVitalsHolder holder, FakeGuardState guard, Recorder rec) MakePlayer(
            int maxHp = 100, float defense = 20f, int maxStamina = 100, bool guarding = true, Vector3? guardForward = null)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var guard = go.AddComponent<FakeGuardState>();
            guard.Guarding = guarding;
            guard.Fwd = guardForward ?? Vector3.forward;
            var holder = go.AddComponent<PlayerVitalsHolder>();
            SetField(holder, "_data", MakePlayerData(maxHp, defense, maxStamina));
            go.SetActive(true);

            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return (holder, guard, rec);
        }

        private static HitInfo GuardHit(IDamageable target, float preDefenseHp, float guardStamina, Vector3 attackDir, bool guardable = true)
        {
            return new HitInfo(null, target, attackDir, Vector3.zero, new HitDamage(preDefenseHp, 0f, 0f),
                guardStamina, guardable, true, HitId.Single(1));
        }

        [Test]
        public void FrontAttack_WhileGuarding_BlocksHp_AndConsumesStamina()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 100);
            int hp0 = holder.Vitals.Health.Current;

            // 正面攻撃：attackDirection は対象へ向かって -GuardForward。
            holder.ReceiveHit(GuardHit(holder, 10f, 20f, -Vector3.forward));

            Assert.AreEqual(HitResultKind.Guard, rec.Received[0].Kind, "防御成功。");
            Assert.AreEqual(0f, rec.Received[0].AppliedDamage.Hp, "防御成功時 HP ダメージ 0。");
            Assert.AreEqual(hp0, holder.Vitals.Health.Current, "HP は減らない。");
            Assert.AreEqual(80, holder.Vitals.Stamina.Current, "固定スタミナ 20 を消費。");
        }

        [Test]
        public void BackAttack_WhileGuarding_Pierces_AndDamagesHp()
        {
            var (holder, _, rec) = MakePlayer(maxHp: 100, defense: 20f);
            // 背後攻撃：attackDirection は対象へ向かって +GuardForward。
            holder.ReceiveHit(GuardHit(holder, 10f, 20f, Vector3.forward));

            Assert.AreEqual(HitResultKind.Damage, rec.Received[0].Kind, "背後は貫通。");
            Assert.AreEqual(92, holder.Vitals.Health.Current, "10×防御20 → 8 減で 92。");
            Assert.AreEqual(100, holder.Vitals.Stamina.Current, "貫通ではスタミナ消費なし。");
        }

        [Test]
        public void UnguardableAttack_WhileGuarding_Pierces()
        {
            var (holder, _, rec) = MakePlayer(maxHp: 100, defense: 20f);
            holder.ReceiveHit(GuardHit(holder, 10f, 20f, -Vector3.forward, guardable: false));

            Assert.AreEqual(HitResultKind.Damage, rec.Received[0].Kind, "ガード不能は前方でも貫通。");
            Assert.AreEqual(92, holder.Vitals.Health.Current);
        }

        [Test]
        public void FrontAttack_WhileNotGuarding_Pierces()
        {
            var (holder, _, rec) = MakePlayer(maxHp: 100, defense: 20f, guarding: false);
            holder.ReceiveHit(GuardHit(holder, 10f, 20f, -Vector3.forward));

            Assert.AreEqual(HitResultKind.Damage, rec.Received[0].Kind, "非ガード中は貫通。");
            Assert.AreEqual(92, holder.Vitals.Health.Current);
        }

        [Test]
        public void OverStaminaHit_StillGuardsThisHit_StaminaStopsAtZero()
        {
            var (holder, _, rec) = MakePlayer(maxStamina: 5); // 残 5 に対し固定 20
            int hp0 = holder.Vitals.Health.Current;

            holder.ReceiveHit(GuardHit(holder, 10f, 20f, -Vector3.forward));

            Assert.AreEqual(HitResultKind.Guard, rec.Received[0].Kind, "残量超過でもこの一撃は防御。");
            Assert.AreEqual(hp0, holder.Vitals.Health.Current, "HP は減らない。");
            Assert.AreEqual(0, holder.Vitals.Stamina.Current, "スタミナは 0 で止まる（ブレイクは P2-07）。");
        }

        [Test]
        public void ProjectileContext_FrontProjectile_IsGuarded_SameAsMelee()
        {
            // 飛び道具でも入力は同じ（攻撃の進行方向）。前方から来る飛び道具は防御可。
            var (holder, _, rec) = MakePlayer(maxStamina: 100);
            holder.ReceiveHit(GuardHit(holder, 5f, 10f, -Vector3.forward));

            Assert.AreEqual(HitResultKind.Guard, rec.Received[0].Kind, "前方の飛び道具は近接と同じく防御。");
            Assert.AreEqual(90, holder.Vitals.Stamina.Current, "固定スタミナ 10 を消費。");
        }

        [Test]
        public void FixedGuardDirection_UsesGuardForward_NotWorldAxis()
        {
            // ガード方向を +X に固定。+X 正面から来る攻撃を防御でき、-X（背後）は貫通する。
            var (holder, _, rec) = MakePlayer(maxStamina: 100, guardForward: Vector3.right);
            holder.ReceiveHit(GuardHit(holder, 10f, 20f, -Vector3.right)); // +X 正面
            Assert.AreEqual(HitResultKind.Guard, rec.Received[0].Kind, "+X ガードで +X 正面は防御。");
        }

        [Test]
        public void PlayerStateController_ImplementsGuardState_GuardForwardMatchesForward()
        {
            Assert.IsTrue(typeof(IGuardState).IsAssignableFrom(typeof(PlayerStateController)),
                "PlayerStateController は IGuardState を実装する。");

            var go = new GameObject("PSC");
            _spawned.Add(go);
            go.SetActive(false);
            var psc = go.AddComponent<PlayerStateController>();

            Assert.IsFalse(psc.IsGuarding, "初期状態(Idle)ではガードしていない。");
            Assert.AreEqual(psc.Forward, psc.GuardForward, "GuardForward は Forward（固定した前方）と一致する。");
        }
    }
}
