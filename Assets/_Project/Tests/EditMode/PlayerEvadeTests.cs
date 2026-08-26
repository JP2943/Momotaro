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
    /// P2-09：無敵（ステップ I-frame）中の被弾が最優先で回避される（<see cref="HitResultKind.Evade"/>・HP/スタミナ不変）ことを、
    /// 実際の被弾経路（<see cref="PlayerVitalsHolder.ReceiveHit"/> が <c>GetComponentInParent&lt;IEvadeState&gt;()</c> で
    /// 無敵状態を取得する経路）で検証する。無敵は明示状態として評価する（Renderer 点滅に依存しない）。
    /// </summary>
    public sealed class PlayerEvadeTests
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

        // 無敵とジャスト回避受付を独立に制御できるフェイク（P3.5-09）。
        private sealed class FakeJustEvade : MonoBehaviour, IEvadeState, IJustEvadeState
        {
            public bool Inv;
            public bool JustWindow;
            public float CounterPoise = 20f;
            public int SuccessNotified;
            public bool IsInvincible => Inv;
            public bool CanJustEvade => JustWindow;
            public float JustEvadeCounterPoise => CounterPoise;
            public void NotifyJustEvadeSuccess() => SuccessNotified++;
        }

        // 反射カウンター（体幹）と強制ひるみを受け取る攻撃者フェイク。
        private sealed class FakeAttacker : ICombatActor, IDamageable, IForcedFlinchReceiver
        {
            public int ReceivedCount;
            public float ReflectedPoise;
            public bool ReflectedIsCounter;
            public float FlinchSeconds = -1f;
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
            public int DamageableId => 999;
            public void ReceiveHit(in HitInfo hit)
            {
                ReceivedCount++;
                ReflectedPoise = hit.Damage.Poise;
                ReflectedIsCounter = hit.IsJustGuardCounter;
            }

            public void ForceFlinch(float seconds) => FlinchSeconds = seconds;
        }

        private (PlayerVitalsHolder holder, FakeJustEvade je, Recorder rec) MakePlayerJustEvade(bool invincible, bool justWindow)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var je = go.AddComponent<FakeJustEvade>();
            je.Inv = invincible;
            je.JustWindow = justWindow;
            var holder = go.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetField(data, "_maxHp", 100);
            SetField(data, "_defense", 20f);
            SetField(data, "_maxStamina", 100);
            SetField(holder, "_data", data);
            go.SetActive(true);
            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return (holder, je, rec);
        }

        private static HitInfo HitFrom(ICombatActor attacker, IDamageable target)
        {
            return new HitInfo(attacker, target, -Vector3.forward, Vector3.zero, new HitDamage(10f, 0f, 0f),
                true, true, HitId.Single(2));
        }

        private (PlayerVitalsHolder holder, FakeEvade evade, Recorder rec) MakePlayer(bool invincible)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var evade = go.AddComponent<FakeEvade>();
            evade.Inv = invincible;
            var holder = go.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetField(data, "_maxHp", 100);
            SetField(data, "_defense", 20f);
            SetField(data, "_maxStamina", 100);
            SetField(holder, "_data", data);
            go.SetActive(true);
            var rec = new Recorder();
            holder.Results.AddListener(rec);
            return (holder, evade, rec);
        }

        private static HitInfo Hit(IDamageable t)
        {
            return new HitInfo(null, t, -Vector3.forward, Vector3.zero, new HitDamage(10f, 0f, 0f),
                true, true, HitId.Single(1));
        }

        [Test]
        public void Invincible_EvadesHit_NoHpNoStamina()
        {
            var (holder, _, rec) = MakePlayer(invincible: true);
            int hp0 = holder.Vitals.Health.Current;
            int st0 = holder.Vitals.Stamina.Current;

            holder.ReceiveHit(Hit(holder));

            Assert.AreEqual(HitResultKind.Evade, rec.Received[0].Kind, "無敵中は回避。");
            Assert.AreEqual(0f, rec.Received[0].AppliedDamage.Hp, "適用 HP 0。");
            Assert.AreEqual(hp0, holder.Vitals.Health.Current, "HP は減らない。");
            Assert.AreEqual(st0, holder.Vitals.Stamina.Current, "スタミナも変化なし。");
        }

        [Test]
        public void NotInvincible_TakesDamage()
        {
            var (holder, _, rec) = MakePlayer(invincible: false);
            holder.ReceiveHit(Hit(holder));

            Assert.AreEqual(HitResultKind.Damage, rec.Received[0].Kind, "無敵でなければ通常被弾。");
            Assert.AreEqual(92, holder.Vitals.Health.Current, "10×防御20 → 8 減で 92。");
        }

        // ---- ジャスト回避（P3.5-09。無敵かつタイト窓での回避＝弾き回避。体幹反射＋強制ひるみ＋専用結果） ----

        [Test]
        public void JustEvade_Window_ReflectsPoise_ForcesFlinch_AndPublishesJustEvade()
        {
            var (holder, je, rec) = MakePlayerJustEvade(invincible: true, justWindow: true);
            je.CounterPoise = 25f;
            int hp0 = holder.Vitals.Health.Current;
            var attacker = new FakeAttacker();

            holder.ReceiveHit(HitFrom(attacker, holder));

            Assert.AreEqual(HitResultKind.JustEvade, rec.Received[0].Kind, "無敵かつ受付窓中はジャスト回避。");
            Assert.AreEqual(hp0, holder.Vitals.Health.Current, "HP は減らない。");
            Assert.AreEqual(1, je.SuccessNotified, "成立を 1 回通知（窓クローズ）。");
            Assert.AreEqual(1, attacker.ReceivedCount, "攻撃者へ反射カウンターが 1 回。");
            Assert.AreEqual(25f, attacker.ReflectedPoise, 1e-4f, "設定した体幹反射量を反射。");
            Assert.IsTrue(attacker.ReflectedIsCounter, "反射はカウンター扱い（再ガード不可・回復待機）。");
            Assert.AreEqual(0.35f, attacker.FlinchSeconds, 1e-4f, "近接攻撃者へ強制ひるみ（反撃猶予）。");
        }

        [Test]
        public void Invincible_ButOutsideJustWindow_IsPlainEvade_NoCounter()
        {
            var (holder, je, rec) = MakePlayerJustEvade(invincible: true, justWindow: false);
            var attacker = new FakeAttacker();

            holder.ReceiveHit(HitFrom(attacker, holder));

            Assert.AreEqual(HitResultKind.Evade, rec.Received[0].Kind, "窓外の無敵回避は通常回避。");
            Assert.AreEqual(0, je.SuccessNotified, "成立通知なし。");
            Assert.AreEqual(0, attacker.ReceivedCount, "反射カウンターなし。");
            Assert.AreEqual(-1f, attacker.FlinchSeconds, 1e-4f, "強制ひるみなし。");
        }
    }
}
