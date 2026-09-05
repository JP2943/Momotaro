using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-01：守護／「かばう」の割込みが、主人公の既存命中解決のどこに入るかを固定する。
    ///
    /// 解決順は「死亡後無視 → 被弾後無敵 → Step Evade／JustEvade → JustGuard → Guard → 肩代わり → 通常 Damage」で、
    /// 回避・JG・ガードが成立した攻撃は肩代わりしない。守護者が未配線・不在・引き受け不可なら従来どおり主人公が被弾する
    /// （＝既存挙動が変わらないことの回帰）。成立時は主人公の HP・Hurt・ヒットバック・<see cref="HitResultChannel"/> を
    /// 一切変化させず、専用の <see cref="GuardianTransferEvent"/> だけを 1 回発行する。
    /// </summary>
    public sealed class PlayerGuardianTransferTests
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

        // ---- テスト用スタブ ----

        private sealed class FakeGuardian : IGuardianReceiver
        {
            public int DamageableId => 777;
            public Vector3 WorldPosition { get; set; } = new Vector3(2f, 0f, 0f);
            public bool CanTakeOver { get; set; } = true;
            public readonly List<HitInfo> Received = new List<HitInfo>();

            public void ReceiveHit(in HitInfo hit) => Received.Add(hit);
        }

        private sealed class FakeResolver : MonoBehaviour, IGuardianResolver
        {
            public IGuardianReceiver Guardian;
            public int ResolveCalls;
            public int NotifyCalls;
            public HitInfo? LastNotified;

            public bool TryResolveGuardian(in HitInfo hit, out IGuardianReceiver guardian)
            {
                ResolveCalls++;
                guardian = Guardian;
                return Guardian != null;
            }

            public void NotifyTransferred(in HitInfo transferred, IGuardianReceiver guardian)
            {
                NotifyCalls++;
                LastNotified = transferred;
            }
        }

        private sealed class FakeGuardStateComponent : MonoBehaviour, IGuardState
        {
            public bool Guarding;
            public Vector3 Fwd = Vector3.forward;
            public bool IsGuarding => Guarding;
            public Vector3 GuardForward => Fwd;
        }

        private sealed class FakeEvadeComponent : MonoBehaviour, IEvadeState
        {
            public bool Inv;
            public bool IsInvincible => Inv;
        }

        private sealed class HitRecorder : IHitResultListener
        {
            public readonly List<HitResult> Received = new List<HitResult>();
            public void OnHitResult(in HitResult result) => Received.Add(result);
        }

        private sealed class TransferRecorder : IGuardianTransferListener
        {
            public readonly List<GuardianTransferEvent> Received = new List<GuardianTransferEvent>();
            public void OnGuardianTransfer(in GuardianTransferEvent transfer) => Received.Add(transfer);
        }

        private sealed class FakeAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;
        }

        // ---- 生成ヘルパ ----

        private T NewComponent<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private PlayerVitalsHolder MakePlayer(int maxHp = 100, float defense = 0f)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            var holder = go.AddComponent<PlayerVitalsHolder>();

            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetField(data, "_maxHp", maxHp);
            SetField(data, "_defense", defense);
            SetField(holder, "_data", data);
            return holder;
        }

        private static HitInfo Hit(IDamageable target, ICombatActor attacker, float hp = 20f,
            bool guardable = false, bool justGuardable = false, Vector3 direction = default)
        {
            return new HitInfo(attacker, target,
                direction == default ? Vector3.forward : direction,
                Vector3.zero, new HitDamage(hp, 0f, 0f), guardable, justGuardable, HitId.Single(1));
        }

        // ---- 未配線＝既存挙動が変わらない ----

        [Test]
        public void NoResolver_PlayerTakesDamage_AsBefore()
        {
            PlayerVitalsHolder player = MakePlayer();
            var recorder = new HitRecorder();
            player.Results.AddListener(recorder);

            player.ReceiveHit(Hit(player, null));

            Assert.AreEqual(80, player.Vitals.Health.Current, "守護者が未配線なら従来どおり主人公が被弾する。");
            Assert.AreEqual(1, recorder.Received.Count);
            Assert.AreEqual(HitResultKind.Damage, recorder.Received[0].Kind);
        }

        [Test]
        public void ResolverWithoutGuardian_FallsBackToPlayerDamage()
        {
            PlayerVitalsHolder player = MakePlayer();
            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            resolver.Guardian = null;

            player.ReceiveHit(Hit(player, null));

            Assert.AreEqual(1, resolver.ResolveCalls, "最終 Damage の直前に 1 回だけ問い合わせる。");
            Assert.AreEqual(0, resolver.NotifyCalls, "成立していないので副作用通知はしない。");
            Assert.AreEqual(80, player.Vitals.Health.Current);
        }

        [Test]
        public void GuardianThatCannotTakeOver_FallsBackToPlayerDamage()
        {
            PlayerVitalsHolder player = MakePlayer();
            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            var guardian = new FakeGuardian { CanTakeOver = false };
            resolver.Guardian = guardian;

            player.ReceiveHit(Hit(player, null));

            Assert.AreEqual(0, guardian.Received.Count, "Down・退場中の守護者へは渡さない。");
            Assert.AreEqual(0, resolver.NotifyCalls);
            Assert.AreEqual(80, player.Vitals.Health.Current, "主人公が通常どおり被弾する。");
        }

        // ---- 肩代わり成立 ----

        [Test]
        public void Transfer_LeavesPlayerUntouched_AndDeliversOnce()
        {
            PlayerVitalsHolder player = MakePlayer();
            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            var guardian = new FakeGuardian();
            resolver.Guardian = guardian;

            var hits = new HitRecorder();
            var transfers = new TransferRecorder();
            player.Results.AddListener(hits);
            player.GuardianTransfers.AddListener(transfers);

            player.ReceiveHit(Hit(player, null));

            Assert.AreEqual(100, player.Vitals.Health.Current, "主人公の HP は変化しない。");
            Assert.AreEqual(0, hits.Received.Count, "主人公の HitResultChannel へは代用結果を流さない。");
            Assert.AreEqual(1, guardian.Received.Count, "守護者へ 1 回だけ渡す。");
            Assert.AreEqual(1, transfers.Received.Count, "専用通知は 1 回だけ。");
            Assert.AreEqual(1, resolver.NotifyCalls, "成立を Resolver へ 1 回だけ通知する（クールダウン消費等）。");
        }

        [Test]
        public void Transfer_RebuildsHitForGuardian()
        {
            PlayerVitalsHolder player = MakePlayer();
            player.transform.position = Vector3.zero;
            FakeAttacker attacker = NewComponent<FakeAttacker>("Attacker");
            attacker.transform.position = new Vector3(-4f, 0f, 0f);

            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            var guardian = new FakeGuardian { WorldPosition = new Vector3(2f, 0f, 0f) };
            resolver.Guardian = guardian;

            HitInfo original = Hit(player, attacker);
            player.ReceiveHit(original);

            Assert.AreEqual(1, guardian.Received.Count);
            HitInfo t = guardian.Received[0];
            Assert.AreSame(guardian, t.Target, "対象は守護者。");
            Assert.AreEqual(guardian.WorldPosition, t.HitPoint, "接触点は守護者位置。");
            Assert.AreEqual(Vector3.right, t.AttackDirection, "方向は攻撃者→守護者で再計算する。");
            Assert.AreEqual(original.HitId, t.HitId, "HitId は維持（受け手側の重複排除の鍵）。");
            Assert.AreEqual(original.Damage.Hp, t.Damage.Hp, 1e-4f, "ダメージ Snapshot は維持。");
        }

        [Test]
        public void Transfer_CarriesHitIdAndParticipants_InNotification()
        {
            PlayerVitalsHolder player = MakePlayer();
            FakeAttacker attacker = NewComponent<FakeAttacker>("Attacker");
            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            var guardian = new FakeGuardian();
            resolver.Guardian = guardian;

            var transfers = new TransferRecorder();
            player.GuardianTransfers.AddListener(transfers);

            player.ReceiveHit(Hit(player, attacker));

            GuardianTransferEvent e = transfers.Received[0];
            Assert.AreEqual(HitId.Single(1), e.HitId);
            Assert.AreSame(attacker, e.Attacker);
            Assert.AreSame(player, e.Protected);
            Assert.AreSame(guardian, e.Guardian);
            Assert.AreEqual(guardian.WorldPosition, e.HitPoint, "演出は守護者位置へ出す。");
        }

        [Test]
        public void Transfer_DoesNotDefeatPlayer_EvenWithLethalDamage()
        {
            PlayerVitalsHolder player = MakePlayer(maxHp: 10);
            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            resolver.Guardian = new FakeGuardian();

            player.ReceiveHit(Hit(player, null, hp: 999f));

            Assert.AreEqual(10, player.Vitals.Health.Current);
            Assert.IsFalse(player.IsDefeated, "肩代わりされた致死攻撃で主人公は死亡しない。");
        }

        // ---- 上位の防御が成立した攻撃は肩代わりしない ----

        [Test]
        public void GuardSuccess_DoesNotTransfer()
        {
            PlayerVitalsHolder player = MakePlayer();
            FakeGuardStateComponent guard = player.gameObject.AddComponent<FakeGuardStateComponent>();
            guard.Guarding = true;
            guard.Fwd = Vector3.forward;

            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            resolver.Guardian = new FakeGuardian();

            var hits = new HitRecorder();
            player.Results.AddListener(hits);

            // 正面からの通常ガード可能攻撃（attackDirection は対象へ向かって -GuardForward）。
            player.ReceiveHit(Hit(player, null, guardable: true, direction: Vector3.back));

            Assert.AreEqual(0, resolver.ResolveCalls, "ガードが成立した攻撃は肩代わりの判定に入らない。");
            Assert.AreEqual(1, hits.Received.Count);
            Assert.AreEqual(HitResultKind.Guard, hits.Received[0].Kind);
            Assert.AreEqual(100, player.Vitals.Health.Current);
        }

        [Test]
        public void EvadeSuccess_DoesNotTransfer()
        {
            PlayerVitalsHolder player = MakePlayer();
            FakeEvadeComponent evade = player.gameObject.AddComponent<FakeEvadeComponent>();
            evade.Inv = true;

            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            resolver.Guardian = new FakeGuardian();

            var hits = new HitRecorder();
            player.Results.AddListener(hits);

            player.ReceiveHit(Hit(player, null));

            Assert.AreEqual(0, resolver.ResolveCalls, "回避で無効化された攻撃は肩代わりの判定に入らない。");
            Assert.AreEqual(HitResultKind.Evade, hits.Received[0].Kind);
        }

        [Test]
        public void GuardBreak_StillAllowsTransfer()
        {
            PlayerVitalsHolder player = MakePlayer();
            FakeGuardStateComponent guard = player.gameObject.AddComponent<FakeGuardStateComponent>();
            guard.Guarding = true; // 構えてはいるが GuardBreak 中はガードが成立しない。
            guard.Fwd = Vector3.forward;

            FakeResolver resolver = player.gameObject.AddComponent<FakeResolver>();
            var guardian = new FakeGuardian();
            resolver.Guardian = guardian;

            // スタミナを 0 まで削って GuardBreak を発生させる（既定 MaxStamina=100 を超える量を一度に消費）。
            player.ConsumeStamina(1000f);
            Assert.IsTrue(player.IsGuardBroken, "前提：GuardBreak 中。");

            player.ReceiveHit(Hit(player, null, guardable: true, direction: Vector3.back));

            Assert.AreEqual(1, guardian.Received.Count, "GuardBreak 中こそ肩代わりの対象になる。");
            Assert.AreEqual(100, player.Vitals.Health.Current);
        }
    }
}
