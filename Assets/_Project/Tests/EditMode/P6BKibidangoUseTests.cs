using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data.Characters;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P6B 09〜14：きびだんごの使用動作（P6B 仕様 §6〜§8）。<b>本物の主人公部品</b>（PlayerStateController・PlayerVitalsHolder・
    /// PlayerHitReaction・PlayerMotor）と<b>本物の受け口</b>（CampaignKibidangoUse ＋ 出荷 P6B カタログの Session）を繋ぎ、
    /// 入力は既存の入力状態（PlayerInputState）の押下エッジから流す。時間は Tick へ注入し、確定は LateUpdate と同じ入口
    /// （ResolveItemUseCommit）を呼ぶ——同じフレームの被弾がその間に入る順序を再現する。
    /// </summary>
    public sealed class P6BKibidangoUseTests
    {
        private const string CatalogPath = "Assets/_Project/Data/Tests/Phase6B/SO_AreaCatalog_P6B.asset";

        private readonly List<Object> _spawned = new List<Object>();
        private AreaCatalog _catalog;
        private GameSessionState _session;
        private PlayerInputState _input;
        private PlayerStateController _player;
        private PlayerVitalsHolder _vitals;
        private PlayerHitReaction _reaction;
        private PlayerMotor _motor;
        private CampaignKibidangoUse _service;
        private int _hitSeq;

        [SetUp]
        public void SetUp()
        {
            GameModeProvider.Current = null;
            GameplayClockProvider.Current = null;
            KibidangoUseProvider.Current = null;

            var data = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(CatalogPath);
            Assert.IsNotNull(data, CatalogPath);
            Assert.IsTrue(AreaCatalog.TryBuild(data, out _catalog, out IReadOnlyList<string> errors), string.Join("/", errors));
            Assert.IsTrue(_catalog.Campaign.TryGetShrine(new StableId("shrine_p6_a"), out ShrineInfo shrine));
            _session = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(_session.InitializeNewAdventure("adv_kibidango", shrine, 3, 3));

            var playerData = AssetDatabase.LoadAssetAtPath<PlayerData>("Assets/_Project/Data/Player/SO_Player_Momotaro.asset");
            var go = new GameObject("Player");
            _spawned.Add(go);
            _vitals = go.AddComponent<PlayerVitalsHolder>();
            SetPrivate(_vitals, "_data", playerData);
            _reaction = go.AddComponent<PlayerHitReaction>();
            _motor = go.AddComponent<PlayerMotor>();
            _player = go.AddComponent<PlayerStateController>();
            SetPrivate(_player, "_motor", _motor);

            _input = new PlayerInputState();
            PlayerInputProvider.Current = _input;
            _service = new CampaignKibidangoUse(() => _session, () => _catalog.Campaign);
            _player.ItemUseOverride = _service;
            Frame(0.02f);
        }

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
            PlayerInputProvider.Current = null;
            GameplayClockProvider.Current = null;
            KibidangoUseProvider.Current = null;
        }

        // ================================================================ P6B 09：開始条件

        /// <summary>HP 満タン・残数 0・同フレームの攻撃・ステップ中は開始しない。長押しで連続使用しない。</summary>
        [Test]
        public void Start_RejectsFullHpEmptyStockSameFrameActionAndBusy_HoldDoesNotRepeat()
        {
            Press(() => _input.SetUseItem(true), () => _input.SetUseItem(false));
            Assert.IsFalse(_player.IsUsingItem);
            Assert.AreEqual(ItemUseRejection.HpFull, _player.LastItemUseRejection, "HP が最大なら開始しない。");

            SetHp(40);
            _session.TryConsumeKibidango(3);
            Press(() => _input.SetUseItem(true), () => _input.SetUseItem(false));
            Assert.AreEqual(ItemUseRejection.OutOfStock, _player.LastItemUseRejection, "残数 0。");
            _session.RefillKibidango(3);

            // 同じフレームに攻撃も押された：既存行動を優先し、使用は開始しない（押下も溜めない）。
            _input.SetAttack(true);
            _input.SetUseItem(true);
            Frame(0.02f);
            _input.SetAttack(false);
            _input.SetUseItem(false);
            Assert.IsFalse(_player.IsUsingItem);
            Assert.AreEqual(ItemUseRejection.Busy, _player.LastItemUseRejection);
            Frame(0.02f);
            Assert.IsFalse(_player.IsUsingItem, "押下を溜めて次のフレームに開始しない。");

            // ステップ中。
            _input.SetMove(Vector2.right);
            Press(() => _input.SetStep(true), () => _input.SetStep(false));
            Assert.IsTrue(_player.IsStepping);
            Press(() => _input.SetUseItem(true), () => _input.SetUseItem(false));
            Assert.AreEqual(ItemUseRejection.Busy, _player.LastItemUseRejection, "回避中は開始しない。");
            _input.SetMove(Vector2.zero);
            Run(1.0f);

            // 長押し：1 回だけ。
            _input.SetUseItem(true);
            Run(5.0f);
            Assert.AreEqual(1, _player.ItemUseStartCount, "押しっぱなしで連続使用しない。");
            Assert.AreEqual(1, _player.ItemUseCommitCount);
            Assert.AreEqual(2, _session.Kibidango);
            _input.SetUseItem(false);
        }

        // ================================================================ P6B 10：2 秒・1.5 秒確定・20%・禁止行動・無敵なし

        [Test]
        public void Use_CommitsOnceAt1_5_EndsAt2_0_SlowMove_NoInvulnerability_DropsForbiddenInputs()
        {
            SetHp(40);
            Press(() => _input.SetUseItem(true), () => _input.SetUseItem(false));
            Assert.IsTrue(_player.IsUsingItem);
            Assert.AreEqual(PlayerState.UseItem, _player.Current);
            Assert.AreEqual(0.2f, _motor.SpeedMultiplier, 1e-5f, "歩行は 20%。");
            Assert.IsFalse(_player.IsInvincible, "使用で無敵にしない。");
            Assert.IsFalse(_player.IsFreeToTravel, "通常エリア遷移を受け付けない。");
            Assert.IsFalse(_player.CanInteract, "Interact を受け付けない。");

            // 使用中の攻撃・ステップ・追加使用は受け付けず、溜めない。
            Press(() => _input.SetAttack(true), () => _input.SetAttack(false));
            Press(() => _input.SetStep(true), () => _input.SetStep(false));
            Press(() => _input.SetUseItem(true), () => _input.SetUseItem(false));
            _input.SetGuard(true);
            RunUntil(1.40f);
            Assert.AreEqual(0, _player.ItemUseCommitCount, "1.5 秒より前は確定しない。");
            Assert.AreEqual(40, _vitals.CurrentHp);
            Assert.AreEqual(3, _session.Kibidango);

            RunUntil(1.52f);
            Assert.AreEqual(1, _player.ItemUseCommitCount, "1.5 秒で確定。");
            Assert.AreEqual(90, _vitals.CurrentHp, "回復 50（基礎）。");
            Assert.AreEqual(2, _session.Kibidango, "回復と同じ更新で 1 個減る。");
            Assert.IsTrue(_player.IsUsingItem, "確定後 0.5 秒は同じ制限のまま。");
            Assert.AreEqual(PlayerState.UseItem, _player.Current);

            RunUntil(2.04f);
            Assert.IsFalse(_player.IsUsingItem, "2.0 秒で終わる。");
            Assert.AreEqual(1, _player.ItemUseCompleteCount);
            Assert.AreEqual(1, _player.ItemUseStartCount, "使用中の追加押下は使われない。");
            Frame(0.02f);
            Assert.AreNotEqual(PlayerState.Step, _player.Current, "使用中のステップ押下を終了後に発火させない。");
            Assert.AreNotEqual(PlayerState.GuardIdle, _player.Current, "押しっぱなしのガードは一度離すまで構えない。");
            _input.SetGuard(false);
            Frame(0.02f);
            _input.SetGuard(true);
            Frame(0.02f);
            Assert.AreEqual(PlayerState.GuardIdle, _player.Current, "離して押し直せば構える。");
            _input.SetGuard(false);
            Frame(0.02f);
            Assert.AreEqual(1f, _motor.SpeedMultiplier, 1e-5f);
            Assert.AreEqual(1, _service.CommitCount);
        }

        // ================================================================ P6B 11：被弾と中断

        /// <summary>
        /// 確定前の実被弾で中断（回復も消費もしない）。確定後の被弾では回復・消費を保持。同じフレームの被弾と 1.5 秒到達は被弾が優先。
        /// 被弾後無敵で拒否された接触は中断理由にしない。かばう（守護）が完全に肩代わりすれば継続する。
        /// </summary>
        [Test]
        public void Hits_InterruptBeforeCommit_KeepAfter_SameFrameHitWins_IgnoredContactsContinue()
        {
            // 1) 確定前の被弾 → 中断・回復なし・消費なし。
            SetHp(60);
            StartUse();
            RunUntil(1.0f);
            Hit(30f);
            Frame(0.02f);
            Assert.IsFalse(_player.IsUsingItem, "被弾で中断。");
            Assert.AreEqual(1, _player.ItemUseInterruptCount);
            Assert.AreEqual(3, _session.Kibidango, "消費しない。");
            Assert.Less(_vitals.CurrentHp, 60, "被弾は通常どおり受ける。");
            int afterHit = _vitals.CurrentHp;

            // 2) 被弾後無敵の間の接触は中断理由にならない（無敵の残りで使用を始め、すぐ接触させる）。
            Run(0.32f); // 硬直 0.30 は抜け、被弾後無敵（0.50）は残る
            Assert.IsTrue(_reaction.IsPostHitInvincible);
            StartUse();
            Hit(30f);
            Frame(0.02f);
            Assert.IsTrue(_player.IsUsingItem, "無敵で拒否された接触は中断しない。");
            Assert.AreEqual(afterHit, _vitals.CurrentHp);

            // 3) 確定後の被弾 → 回復・消費は保持し、被弾は通常どおり。
            RunUntil(1.55f);
            Assert.AreEqual(1, _player.ItemUseCommitCount);
            int healed = _vitals.CurrentHp;
            Assert.AreEqual(Mathf.Min(_vitals.MaxHp, afterHit + 50), healed);
            Assert.AreEqual(2, _session.Kibidango);
            Run(0.6f); // 被弾後無敵が切れるのを待つ（使用は 2.0 で終わる）
            Assert.IsFalse(_player.IsUsingItem);

            StartUse();
            RunUntil(1.55f);
            Assert.AreEqual(2, _player.ItemUseCommitCount);
            int beforeLate = _vitals.CurrentHp;
            Hit(30f);
            Frame(0.02f);
            Assert.AreEqual(1, _session.Kibidango, "確定済みの消費は取り消さない。");
            Assert.Less(_vitals.CurrentHp, beforeLate, "その後の被弾は通常どおり。");
            Run(1.0f);

            // 4) 同じフレームで 1.5 秒到達と被弾 → 被弾が優先（回復・消費なし）。
            SetHp(50);
            StartUse();
            RunUntil(1.45f);
            _player.Tick(0.1f); // このフレームで 1.5 秒を越える（確定は LateUpdate 相当へ予約）
            Hit(20f);           // 同じフレームの敵の Update の命中
            _player.ResolveItemUseCommit();
            Assert.AreEqual(2, _player.ItemUseCommitCount, "同フレームの被弾が優先で確定しない。");
            Assert.AreEqual(1, _session.Kibidango);
            Assert.IsFalse(_player.IsUsingItem);
            Run(1.0f);

            // 5) かばう（守護）が完全に肩代わり → 主人公に被弾が成立しないので継続し、確定する。
            var guardian = new FakeGuardian();
            _vitals.SetGuardianResolver(new FakeResolver(guardian));
            StartUse();
            RunUntil(1.0f);
            Hit(30f);
            Assert.AreEqual(1, guardian.Received, "守護者が受けた。");
            RunUntil(1.55f);
            Assert.IsTrue(_player.IsUsingItem);
            Assert.AreEqual(3, _player.ItemUseCommitCount, "かばわれた使用は確定する。");
            Assert.AreEqual(0, _session.Kibidango);
        }

        // ================================================================ P6B 12：時間（Pause・凍結・長いフレーム）と開始後の満タン

        [Test]
        public void Time_PauseAndFreezeStop_LongFrameCommitsOnce_FullHpAfterStartClamps_StolenStockAborts()
        {
            SetHp(40);
            StartUse();
            RunUntil(1.0f);

            // Pause（入力が閉じる＝メニュー）：経過は止まり、使用状態のまま。再開で続きから。
            _input.SetActive(false);
            Run(3.0f);
            Assert.IsTrue(_player.IsUsingItem);
            Assert.AreEqual(1.0f, _player.ItemUseElapsed, 0.03f, "Pause 中は進まない。");
            Assert.AreEqual(0, _player.ItemUseCommitCount);
            _input.SetActive(true);

            // Gameplay 時計の凍結（遷移・ヒットストップ相当）：Tick が進めない。
            var clock = new FrozenClock();
            GameplayClockProvider.Current = clock;
            Run(3.0f);
            Assert.AreEqual(1.0f, _player.ItemUseElapsed, 0.03f, "凍結中は進まない。");
            GameplayClockProvider.Current = null;
            _player.Tick(0f);
            Assert.AreEqual(1.0f, _player.ItemUseElapsed, 0.03f, "経過 0 のフレームは進まない（ヒットストップ）。");

            // 開始後に別経路で満タン → 継続し、1.5 秒で 1 個消費・HP は最大で頭打ち。
            SetHp(_vitals.MaxHp);
            RunUntil(1.55f);
            Assert.AreEqual(1, _player.ItemUseCommitCount);
            Assert.AreEqual(_vitals.MaxHp, _vitals.CurrentHp, "最大で頭打ち。");
            Assert.AreEqual(2, _session.Kibidango, "満タンでも 1 個消費。");
            Run(1.0f);

            // 長いフレーム：1 回で確定と終了の両方を跨いでも確定は 1 回。
            SetHp(30);
            StartUse();
            Frame(3.0f);
            Assert.AreEqual(2, _player.ItemUseCommitCount, "跨いでも 1 回。");
            Assert.IsFalse(_player.IsUsingItem);
            Assert.AreEqual(80, _vitals.CurrentHp);
            Assert.AreEqual(1, _session.Kibidango);
            Frame(0.02f);
            Assert.AreEqual(2, _player.ItemUseCommitCount);

            // 確定時に残数が無い（別経路で奪われた）→ 回復も消費もせず中断し、診断する。
            SetHp(30);
            StartUse();
            _session.TryConsumeKibidango(_session.Kibidango);
            RunUntil(1.6f);
            Assert.AreEqual(KibidangoCommitResult.OutOfStock, _player.LastItemCommitResult);
            Assert.AreEqual(30, _vitals.CurrentHp, "回復しない。");
            Assert.IsFalse(_player.IsUsingItem);
            Assert.AreEqual(1, _service.OutOfStockCount);
        }

        // ================================================================ P6B 14：確定と保存

        /// <summary>
        /// 開始では保存要求を出さない。確定で版が進み保存要求が 1 件——要求の時点で HP と残数は両方とも確定後の値
        /// （食い違った組を採取させない）。
        /// </summary>
        [Test]
        public void Commit_RequestsOneSave_WithHpAndStockConsistent()
        {
            SetHp(40);
            var seen = new List<string>();
            _session.Changes.AutosaveRequested += r => seen.Add(r.Reason + ":" + _vitals.CurrentHp + "/" + _session.Kibidango);
            long revision = _session.Changes.Revision;
            StartUse();
            RunUntil(1.4f);
            Assert.AreEqual(0, seen.Count, "開始では保存要求を出さない。");
            Assert.AreEqual(revision, _session.Changes.Revision);
            RunUntil(1.52f);
            Assert.AreEqual(1, seen.Count, "確定で 1 件。");
            Assert.AreEqual("kibidango_used:90/2", seen[0], "要求の時点で HP と残数が揃っている。");
            Assert.Greater(_session.Changes.Revision, revision, "版が進む。");
        }

        // ================================================================ P6B 13：境界

        /// <summary>
        /// 使用中は出入口が要求を出さない（溜めも進めない）ので、断られて「離れ直し待ち」にならない。
        /// 終了後は外向き入力を続けたまま 0.15 秒で要求できる。
        /// </summary>
        [Test]
        public void ExitGate_NoRequestWhileUsing_ThenRequestsWithoutRelease()
        {
            var go = new GameObject("Gate");
            _spawned.Add(go);
            go.AddComponent<BoxCollider>().isTrigger = true;
            AreaExitGate gate = go.AddComponent<AreaExitGate>();
            gate.Configure(new StableId("area_p6_b"), new StableId("entry_p5_b_from_a"), Vector3.right);
            gate.SetPlayerInside(true);

            for (int i = 0; i < 30; i++)
            {
                Assert.IsFalse(gate.Tick(0.1f, Vector3.right, requestAllowed: false), "使用中は要求しない。");
            }

            Assert.IsTrue(gate.IsArmed, "離れ直し待ちにならない。");
            Assert.AreEqual(0, gate.RequestCount);
            Assert.IsFalse(gate.Tick(0.1f, Vector3.right, requestAllowed: true));
            Assert.IsTrue(gate.Tick(0.1f, Vector3.right, requestAllowed: true), "外向き入力を続けたまま要求できる。");
            Assert.AreEqual(1, gate.RequestCount);
        }

        // ================================================================ P6B 02：適用範囲（JG の体幹反射は対象外）

        /// <summary>
        /// 刀・通常体幹の成長倍率を掛けた主人公でも、ジャストガードの体幹反射は<b>攻撃側の設定値のまま</b>（倍率の対象外）。
        /// </summary>
        [Test]
        public void GrowthMultipliers_DoNotTouchJustGuardReflection()
        {
            _player.SetGrowthMultipliers(1.2f, 1.1f);
            var attackerGo = new GameObject("Attacker");
            _spawned.Add(attackerGo);
            RecordingAttacker attacker = attackerGo.AddComponent<RecordingAttacker>();
            _input.SetGuard(true);
            Frame(0.02f);
            Assert.IsTrue(_player.CanJustGuard, "前提：JG の受付窓が開いている。");

            Vector3 dir = -_player.GuardForward;
            var hit = new HitInfo(attacker, _vitals, dir, _vitals.transform.position, new HitDamage(20f, 0f, 0f),
                10f, 25f, true, true, HitId.Single(++_hitSeq + 6000));
            _vitals.ReceiveHit(hit);
            Assert.AreEqual(1, attacker.Received, "JG が成立して反射が届く。");
            Assert.AreEqual(25f, attacker.LastPoise, 1e-4f, "JG の体幹反射は成長倍率の対象外。");
            _input.SetGuard(false);
        }

        private sealed class RecordingAttacker : MonoBehaviour, IDamageable, ICombatActor
        {
            public int Received;
            public float LastPoise;
            public int DamageableId => GetInstanceID();
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public int ActorId => GetInstanceID();
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;

            public void ReceiveHit(in HitInfo hit)
            {
                Received++;
                LastPoise = hit.Damage.Poise;
            }
        }

        // ================================================================ 補助

        private void StartUse()
        {
            int before = _player.ItemUseStartCount;
            Press(() => _input.SetUseItem(true), () => _input.SetUseItem(false));
            Assert.AreEqual(before + 1, _player.ItemUseStartCount, "使用を開始する（拒否=" + _player.LastItemUseRejection + "）。");
        }

        private void SetHp(int hp) => _vitals.Vitals.Health.SetCurrent(hp);

        private void Press(Action down, Action up)
        {
            down();
            Frame(0.02f);
            up();
            Frame(0.02f);
        }

        /// <summary>1 フレーム：硬直 → 主人公 → LateUpdate 相当の確定。</summary>
        private void Frame(float dt)
        {
            _reaction.Tick(dt);
            _player.Tick(dt);
            _player.ResolveItemUseCommit();
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += 0.02f)
            {
                Frame(0.02f);
            }
        }

        /// <summary>使用の経過がこの秒数に達するまで進める。</summary>
        private void RunUntil(float elapsed)
        {
            int guard = 0;
            while (_player.IsUsingItem && _player.ItemUseElapsed < elapsed - 1e-4f && guard++ < 1000)
            {
                Frame(0.02f);
            }
        }

        private void Hit(float hp)
        {
            var hit = new HitInfo(null, _vitals, Vector3.forward, _vitals.transform.position,
                new HitDamage(hp, 0f, 0f), false, false, HitId.Single(++_hitSeq + 5000));
            _vitals.ReceiveHit(hit);
        }

        private static void SetPrivate(object target, string field, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private sealed class FrozenClock : IGameplayClockSource
        {
            public bool IsFrozen => true;
        }

        private sealed class FakeGuardian : IGuardianReceiver
        {
            public int Received;
            public int DamageableId => 77001;
            public Vector3 WorldPosition => Vector3.zero;
            public bool CanTakeOver => true;
            public void ReceiveHit(in HitInfo hit) => Received++;

            public bool TryReceiveTransferredHit(in HitInfo hit)
            {
                Received++;
                return true;
            }
        }

        private sealed class FakeResolver : IGuardianResolver
        {
            private readonly IGuardianReceiver _guardian;

            public FakeResolver(IGuardianReceiver guardian) => _guardian = guardian;

            public bool TryResolveGuardian(in HitInfo hit, out IGuardianReceiver guardian)
            {
                guardian = _guardian;
                return true;
            }

            public void NotifyTransferred(in HitInfo transferred, IGuardianReceiver guardian)
            {
            }
        }
    }
}
