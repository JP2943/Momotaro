using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Data.Exploration;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Player;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-07A（作り直し）：地点への Interact 依頼 → 犬丸の調査 → 完了記録・通知、を純粋モデル＋本物の駆動で検証する
    /// （v1.0 §4〜§6・§8、c8c0ddf §5。要求 ID E01・E02・E04〜E14・E21・E22 と、探索中の競合）。
    ///
    /// 主人公は <see cref="IInteractActor"/> の Fake（位置・向き・Interact 可否を自由に置く）。地点・記録・加入供給元・
    /// 調停役・駆動は本物。移動は EditMode では物理が進まないので、本体モードでは Transform を手で運ぶ。
    /// </summary>
    public sealed class CompanionInvestigationTests : CompanionActivityFixture
    {
        private const float InteractRange = 1.5f;
        private const float ContinueRange = 3.0f;
        private const float Arrival = 0.2f;
        private const float MoveTimeout = 3.0f;
        private const float InvestigateSeconds = 1.0f;
        private const float ReturnTimeout = 1.0f;
        private const float MoveSpeed = 5f;
        private const int CompanionMaxHp = 40;
        private const float RecoverySeconds = 2f;

        private static readonly StableId InumaruId = new StableId("companion_inumaru");
        private static readonly StableId OtherId = new StableId("companion_saruwaka");

        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
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
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
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

        private sealed class FakePlayer : IInteractActor
        {
            public Vector3 Position { get; set; }
            public Vector3 Forward { get; set; } = Vector3.forward;
            public bool CanInteract { get; set; } = true;
        }

        private sealed class FakeProbe : IObstacleProbe
        {
            public bool Clear { get; set; } = true;
            public int Calls { get; private set; }

            public bool IsClear(Vector3 from, Vector3 to)
            {
                Calls++;
                return Clear;
            }
        }

        private sealed class EventLog : IInvestigationListener
        {
            public readonly List<InvestigationAccepted> Accepted = new List<InvestigationAccepted>();
            public readonly List<InvestigationRejected> Rejected = new List<InvestigationRejected>();
            public readonly List<InvestigationCompleted> Completed = new List<InvestigationCompleted>();
            public readonly List<InvestigationInterrupted> Interrupted = new List<InvestigationInterrupted>();

            public void OnInvestigationAccepted(in InvestigationAccepted e) => Accepted.Add(e);
            public void OnInvestigationRejected(in InvestigationRejected e) => Rejected.Add(e);
            public void OnInvestigationCompleted(in InvestigationCompleted e) => Completed.Add(e);
            public void OnInvestigationInterrupted(in InvestigationInterrupted e) => Interrupted.Add(e);
        }

        /// <summary>完了通知のコールバックの中から再入する購読者（R3-01）。</summary>
        private sealed class ReentrantListener : IInvestigationListener
        {
            public System.Action OnCompleted;
            public int CompletedCount { get; private set; }

            public void OnInvestigationAccepted(in InvestigationAccepted e) { }
            public void OnInvestigationRejected(in InvestigationRejected e) { }
            public void OnInvestigationInterrupted(in InvestigationInterrupted e) { }

            public void OnInvestigationCompleted(in InvestigationCompleted e)
            {
                CompletedCount++;
                OnCompleted?.Invoke();
            }
        }

        private sealed class FakeEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.back;
            public int DamageableId => GetInstanceID();
            public int ActorId => GetInstanceID();
            public Vector3 Position => transform.position;
            public bool IsActive => true;
            public bool IsDown => false;
            public float BaseThreat => 0f;
            public float AcquiredThreatMultiplier => 1f;
            public void ReceiveHit(in HitInfo hit) { }
        }

        private sealed class FakeHost : MonoBehaviour, IGuardianHost
        {
            public void SetGuardianResolver(IGuardianResolver resolver) { }
            public void ClearGuardianResolver(IGuardianResolver expected) { }
        }

        /// <summary>主人公のガード状態（本物の <see cref="PlayerVitalsHolder"/> に差して防御分岐を通す）。</summary>
        private sealed class FakeGuardState : MonoBehaviour, IGuardState
        {
            public bool IsGuarding { get; set; }
            public Vector3 GuardForward { get; set; } = Vector3.back;
        }

        /// <summary>主人公のステップ無敵（同上）。</summary>
        private sealed class FakeEvadeState : MonoBehaviour, IEvadeState
        {
            public bool IsInvincible { get; set; }
        }

        /// <summary>
        /// 命中結果を観測した<b>その時点</b>で探索がまだ走っていたかを記録する（R3-02 の受入）。
        /// 「解放されたか」ではなく「解決より先に解放されたか」を見るため、結果購読の中で読む。
        /// </summary>
        private sealed class BusyAtResult : IHitResultListener
        {
            private readonly CompanionInvestigationController _driver;

            public BusyAtResult(CompanionInvestigationController driver)
            {
                _driver = driver;
            }

            public int Count { get; private set; }
            public HitResultKind LastKind { get; private set; }
            public bool BusyWhenObserved { get; private set; }
            public bool ProxyShownWhenObserved { get; private set; }

            public void OnHitResult(in HitResult result)
            {
                Count++;
                LastKind = result.Kind;
                BusyWhenObserved |= _driver.IsBusy;
                ProxyShownWhenObserved |= _driver.ProxyVisible;
            }
        }

        private sealed class Rig
        {
            public FakePlayer Player;
            public FakeProbe Probe;
            public EventLog Log;
            public InvestigationCoordinator Coordinator;
            public CompanionRosterContext Roster;
            public InvestigationRecordHolder Record;
            public CompanionActor Actor;
            public CompanionStateArbiter States;
            public CompanionMovementArbiter Movement;
            public CompanionInvestigationController Driver;
            public CompanionHitReceiver Receiver;
            public InvestigationSettingsData Settings;
            public List<CompanionInvestigationPoint> Points = new List<CompanionInvestigationPoint>();

            public void Tick(float dt)
            {
                Driver.TickInvestigation(dt);
                Movement.EndFrame();
            }

            /// <summary>本体モードの移動を手で代行する（EditMode では Motor の物理が進まない）。</summary>
            public void ArriveAt(Vector3 position)
            {
                Actor.transform.position = position;
            }
        }

        private InvestigationSettingsData MakeSettings()
        {
            var data = ScriptableObject.CreateInstance<InvestigationSettingsData>();
            _spawned.Add(data);
            SetPrivateField(data, "_interactRange", InteractRange);
            SetPrivateField(data, "_continueRange", ContinueRange);
            SetPrivateField(data, "_arrivalDistance", Arrival);
            SetPrivateField(data, "_moveTimeoutSeconds", MoveTimeout);
            SetPrivateField(data, "_investigationSeconds", InvestigateSeconds);
            SetPrivateField(data, "_returnVisualTimeoutSeconds", ReturnTimeout);
            return data;
        }

        private CompanionData MakeCompanionData()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", 2f);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", 1f);
            SetPrivateField(attack, "_startupSeconds", 0.2f);
            SetPrivateField(attack, "_activeSeconds", 0.2f);
            SetPrivateField(attack, "_recoverySeconds", 0.2f);
            SetPrivateField(attack, "_hpMultiplier", 1f);

            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_id", InumaruId);
            SetPrivateField(data, "_moveSpeed", MoveSpeed);
            SetPrivateField(data, "_attackPower", 50f);
            SetPrivateField(data, "_basicAttack", attack);
            SetPrivateField(data, "_guardianRange", 3f);
            SetPrivateField(data, "_guardianCooldownSeconds", 6f);
            SetPrivateField(data, "_maxHp", CompanionMaxHp);
            SetPrivateField(data, "_defense", 0f);
            SetPrivateField(data, "_leaveRecoverySeconds", RecoverySeconds);
            return data;
        }

        /// <summary>本物の被弾経路で犬丸を倒す（Down にする）。復帰時計もここから動き出す。</summary>
        private static void DownTheCompanion(Rig rig)
        {
            rig.Receiver.ReceiveHit(new HitInfo(
                null, rig.Receiver, Vector3.back, Vector3.zero, new HitDamage(CompanionMaxHp, 0f, 0f),
                guardable: false, justGuardable: false, hitId: HitId.Single(9001)));

            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "前提：HP0 で倒れている。");
        }

        private CompanionInvestigationPoint MakePoint(
            Rig rig, string id, Vector3 position, StableId? required = null, string discovery = "discovery_x")
        {
            var go = new GameObject("Point_" + id);
            _spawned.Add(go);
            go.transform.position = position;
            var point = go.AddComponent<CompanionInvestigationPoint>();
            point.Configure(new StableId(id), required ?? InumaruId, new StableId(discovery), rig.Settings);
            point.ConfigureText("調べる", "犬がいれば", "調べ終えた");
            InvokePrivate(point, "OnEnable"); // 登録（EditMode では自動で走らない）。
            rig.Points.Add(point);
            return point;
        }

        private Rig MakeRig(Vector3 companionPosition = default, bool recruited = true)
        {
            var rig = new Rig { Player = new FakePlayer(), Probe = new FakeProbe(), Log = new EventLog() };
            rig.Settings = MakeSettings();

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = companionPosition;
            rig.Actor = go.AddComponent<CompanionActor>();
            rig.Actor.SetData(MakeCompanionData());
            rig.Actor.ResetState(CompanionState.Follow);
            rig.States = go.GetComponent<CompanionStateArbiter>();
            go.AddComponent<CompanionMotor>();
            rig.Movement = go.GetComponent<CompanionMovementArbiter>();
            rig.Receiver = go.AddComponent<CompanionHitReceiver>();
            rig.Receiver.Bind(rig.Actor);
            rig.Driver = go.AddComponent<CompanionInvestigationController>();
            rig.Driver.Bind(rig.Actor, rig.States, rig.Movement);
            InvokePrivate(rig.Driver, "OnEnable");

            var systems = new GameObject("Investigation");
            _spawned.Add(systems);
            rig.Roster = systems.AddComponent<CompanionRosterContext>();
            if (recruited)
            {
                rig.Roster.SetRecruited(InumaruId);
            }

            rig.Record = systems.AddComponent<InvestigationRecordHolder>();
            rig.Coordinator = systems.AddComponent<InvestigationCoordinator>();
            rig.Coordinator.Bind(null, rig.Roster, rig.Record, rig.Driver);
            rig.Coordinator.SetInteractor(rig.Player);
            rig.Coordinator.SetObstacleProbe(rig.Probe);
            rig.Coordinator.Events.AddListener(rig.Log);
            return rig;
        }

        /// <summary>依頼を受理させ、本体を調査位置へ運ぶところまで。</summary>
        private static InvestigationRequest AcceptAndArrive(Rig rig, CompanionInvestigationPoint point)
        {
            InvestigationRequestResult result = rig.Coordinator.TryRequest();
            Assert.IsTrue(result.Accepted, "前提：受理される（" + result.Reason + "）。");
            rig.ArriveAt(point.ApproachPosition);
            rig.Tick(0.05f); // 到着判定 → Investigating。
            Assert.AreEqual(InvestigationPhase.Investigating, rig.Driver.Phase, "前提：調査中。");
            return rig.Driver.CurrentRequest;
        }

        /// <summary>
        /// 本物の主人公被弾入口を作り、調停役へ購読させる（R3-02）。Fake の守護判断先ではなく
        /// <see cref="PlayerVitalsHolder.ReceiveHit"/> を実際に通すためのもの。
        /// </summary>
        private PlayerVitalsHolder MakePlayerVitals(Rig rig, Vector3 position)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.transform.position = position;

            var holder = go.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", 100);
            SetPrivateField(data, "_defense", 0f);
            SetPrivateField(holder, "_data", data);

            rig.Coordinator.SetPlayerHitSource(holder);
            return holder;
        }

        /// <summary>主人公へ 1 発入れる（既定はガード不可・素の Damage）。</summary>
        private static HitInfo HitOnPlayer(PlayerVitalsHolder player, int id, bool guardable = false)
        {
            return new HitInfo(
                null, player, Vector3.forward, Vector3.zero, new HitDamage(10f, 0f, 0f),
                guardable: guardable, justGuardable: false, hitId: HitId.Single(id));
        }

        // ================================================================
        // E01：正常な依頼
        // ================================================================

        [Test]
        public void NormalRequest_MovesInvestigatesCompletes_AndHandsBackToFollow()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);

            InvestigationRequestResult result = rig.Coordinator.TryRequest();

            Assert.IsTrue(result.Accepted, "受理される（" + result.Reason + "）。");
            Assert.AreEqual(1, result.RequestId);
            Assert.AreEqual(1, rig.Log.Accepted.Count);
            Assert.AreEqual(InvestigationMode.Body, rig.Driver.Mode, "平常時は本体が調べに行く。");
            Assert.AreEqual(InvestigationPhase.Moving, rig.Driver.Phase);
            Assert.AreEqual(CompanionState.Investigate, rig.Actor.State, "本体は探索状態。");
            Assert.AreEqual(CompanionActionOwner.Investigate, rig.States.CurrentOwner, "行動の所有権を探索が持つ。");
            Assert.AreEqual(CompanionMovementOwner.Investigate, rig.Movement.Owner, "開始した Tick から移動の所有権も持つ（追従が歩き出さない）。");

            rig.ArriveAt(point.ApproachPosition);
            rig.Tick(0.05f);
            Assert.AreEqual(InvestigationPhase.Investigating, rig.Driver.Phase, "到着で調査へ。");

            rig.Tick(InvestigateSeconds * 0.5f);
            Assert.AreEqual(0, rig.Log.Completed.Count, "時間満了まで完了しない。");

            rig.Tick(InvestigateSeconds * 0.6f);

            Assert.AreEqual(1, rig.Log.Completed.Count, "完了通知は 1 回。");
            InvestigationCompleted done = rig.Log.Completed[0];
            Assert.AreEqual(point.PointId, done.PointId);
            Assert.AreEqual(1, done.RequestId);
            Assert.AreEqual(InumaruId, done.CompanionId);
            Assert.AreEqual(new StableId("discovery_x"), done.DiscoveryId);
            Assert.IsTrue(rig.Record.Record.IsInvestigated(point.PointId), "Scene 記録に調査済みが残る。");
            Assert.IsFalse(rig.Driver.IsBusy, "本体は帰還段を持たず、完了で引き渡す。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "追従へ戻る。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "所有権が残らない。");
            Assert.AreEqual(CompanionMovementOwner.None, rig.Movement.Owner, "移動の所有権も返す。");
            Assert.AreEqual(0, rig.Log.Interrupted.Count);
        }

        // ================================================================
        // E02：未加入
        // ================================================================

        [Test]
        public void Unrecruited_ShowsHintOnly_NoRecordNoCompletion()
        {
            Rig rig = MakeRig(recruited: false);
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;

            InvestigationRequestResult result = rig.Coordinator.TryRequest();

            Assert.IsFalse(result.Accepted);
            Assert.AreEqual(InvestigationRejectReason.CompanionNotRecruited, result.Reason);
            Assert.AreEqual(1, rig.Log.Rejected.Count);
            Assert.AreEqual("犬がいれば", rig.Log.Rejected[0].Text, "未加入用ヒントを返す。");
            Assert.IsFalse(rig.Driver.IsBusy, "駆動へは渡さない。");
            Assert.IsFalse(rig.Record.Record.IsInvestigated(point.PointId), "調査済みにしない。");
            Assert.AreEqual(0, rig.Log.Completed.Count);
        }

        /// <summary>加入資格は戦闘状態と分離する：Down でも加入済みなら受理する（表示代理で調べる）。</summary>
        [Test]
        public void DownButRecruited_IsAccepted_ByProxy_WithoutTouchingCombatValues()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);
            int hp = rig.Receiver.CurrentHp;

            InvestigationRequestResult result = rig.Coordinator.TryRequest();

            Assert.IsTrue(result.Accepted, "加入済みなら Down でも受理する（" + result.Reason + "）。");
            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode, "戦闘 Actor ではなく表示代理で調べる。");
            Assert.IsTrue(rig.Driver.ProxyVisible);
            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "Down を解除しない（戦闘上の生存状態へ戻さない）。");

            // 代理が進み、到着し、完了する。本体には触らない。
            for (int i = 0; i < 40 && rig.Driver.Phase != InvestigationPhase.Returning; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.AreEqual(1, rig.Log.Completed.Count, "代理でも完了する。");
            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "完了しても Down のまま。");
            Assert.AreEqual(hp, rig.Receiver.CurrentHp, "探索で HP を回復しない。");
            Assert.IsTrue(rig.Record.Record.IsInvestigated(point.PointId));
            Assert.AreEqual(InvestigationPhase.Returning, rig.Driver.Phase, "成功後は代理が帰還する。");

            rig.Tick(ReturnTimeout + 0.1f);
            Assert.IsFalse(rig.Driver.IsBusy, "帰還の上限で引き渡す。");
            Assert.IsFalse(rig.Driver.ProxyVisible, "表示の所有権を返す。");
        }

        // ================================================================
        // E04：戦闘中・結果画面
        // ================================================================

        [Test]
        public void InCombat_IsRejected_AndNothingIsReserved()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;

            ActivityFake.Current = CompanionActivity.Fighting;
            InvestigationRequestResult fighting = rig.Coordinator.TryRequest();
            Assert.IsFalse(fighting.Accepted);
            Assert.AreEqual(InvestigationRejectReason.InCombat, fighting.Reason);

            ActivityFake.Current = CompanionActivity.Concluded; // 結果画面。
            Assert.AreEqual(InvestigationRejectReason.InCombat, rig.Coordinator.TryRequest().Reason, "結果画面も不可。");

            ActivityFake.Current = CompanionActivity.Paused(false);
            Assert.AreEqual(InvestigationRejectReason.InputClosed, rig.Coordinator.TryRequest().Reason, "Pause は新規入力を受け付けない。");

            ActivityFake.Current = CompanionActivity.FreeRoam;
            rig.Tick(0.1f);
            Assert.IsFalse(rig.Driver.IsBusy, "拒否した依頼を予約していない（戦闘が終わっても勝手に始まらない）。");
            Assert.AreEqual(0, rig.Log.Accepted.Count);
        }

        [Test]
        public void PlayerBusy_IsRejected()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            rig.Player.CanInteract = false;

            Assert.AreEqual(InvestigationRejectReason.PlayerBusy, rig.Coordinator.TryRequest().Reason);
            Assert.IsFalse(rig.Driver.IsBusy);
        }

        // ================================================================
        // E05：複数地点の決定的な選択
        // ================================================================

        [Test]
        public void MultiplePoints_SelectsNearest_ThenForward_ThenIdOrder()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint far = MakePoint(rig, "point_far", new Vector3(0f, 0f, 1.4f));
            CompanionInvestigationPoint behind = MakePoint(rig, "point_b_behind", new Vector3(0f, 0f, -1f));
            CompanionInvestigationPoint ahead = MakePoint(rig, "point_c_ahead", new Vector3(0f, 0f, 1f));
            CompanionInvestigationPoint aheadToo = MakePoint(rig, "point_a_ahead", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            rig.Player.Forward = Vector3.forward;

            Assert.AreEqual(InvestigationRejectReason.None, rig.Coordinator.Peek(out IInvestigationPoint chosen));

            Assert.AreNotSame(far, chosen, "遠い地点は選ばない。");
            Assert.AreNotSame(behind, chosen, "同距離なら前方を優先する。");
            Assert.AreSame(aheadToo, chosen, "距離も前方も同じなら PointId の辞書順（point_a_ahead < point_c_ahead）。");

            // 同じ入力なら何度でも同じ答え。
            rig.Coordinator.Peek(out IInvestigationPoint again);
            Assert.AreSame(chosen, again);
        }

        [Test]
        public void OutOfRange_IsRejected()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, InteractRange + 0.5f));
            rig.Player.Position = Vector3.zero;

            Assert.AreEqual(InvestigationRejectReason.NoPointInRange, rig.Coordinator.TryRequest().Reason);
        }

        [Test]
        public void UnreachablePoint_IsRejectedBeforeAcceptance()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            rig.Probe.Clear = false; // 壁越し。

            InvestigationRequestResult result = rig.Coordinator.TryRequest();

            Assert.AreEqual(InvestigationRejectReason.Unreachable, result.Reason, "到達不能は「使用可能」に含めない。");
            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.Greater(rig.Probe.Calls, 0, "実際に障害物判定を通している。");
        }

        // ================================================================
        // E06：連打・再送
        // ================================================================

        [Test]
        public void RequestWhileBusy_IsRejected_AndCurrentRequestIsKept()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint a = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            MakePoint(rig, "point_b", new Vector3(1.2f, 0f, 0f));
            rig.Player.Position = Vector3.zero;

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            InvestigationRequest current = rig.Driver.CurrentRequest;

            InvestigationRequestResult second = rig.Coordinator.TryRequest();
            InvestigationRequestResult third = rig.Coordinator.TryRequest();

            Assert.AreEqual(InvestigationRejectReason.CompanionBusy, second.Reason, "実行中は拒否し、列を持たない。");
            Assert.AreEqual(InvestigationRejectReason.CompanionBusy, third.Reason);
            Assert.AreSame(current, rig.Driver.CurrentRequest, "現在の依頼を壊さない。");
            Assert.AreEqual(1, rig.Coordinator.LastRequestId, "RequestId は増えない（二重起動なし）。");
            Assert.AreEqual(1, rig.Log.Accepted.Count);
            Assert.AreEqual(a.PointId, current.PointId);
        }

        // ================================================================
        // E07：完了地点の再調査・Disable／Enable
        // ================================================================

        [Test]
        public void CompletedPoint_IsNotInvestigatedAgain_EvenAfterDisableEnable()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            AcceptAndArrive(rig, point);
            rig.Tick(InvestigateSeconds + 0.1f);
            Assert.AreEqual(1, rig.Log.Completed.Count, "前提：完了した。");

            InvestigationRequestResult again = rig.Coordinator.TryRequest();
            Assert.AreEqual(InvestigationRejectReason.AlreadyInvestigated, again.Reason, "調査済み文言を返し、再発行しない。");
            Assert.AreEqual("調べ終えた", rig.Log.Rejected[0].Text);

            InvokePrivate(point, "OnDisable");
            InvokePrivate(point, "OnEnable");
            Assert.AreEqual(InvestigationRejectReason.AlreadyInvestigated, rig.Coordinator.TryRequest().Reason,
                "Disable／再 Enable で記録は消えない。");
            Assert.AreEqual(1, rig.Log.Completed.Count, "成功通知は 1 回のまま。");
        }

        // ================================================================
        // E08：地点破棄・継続範囲外・加入取消
        // ================================================================

        [Test]
        public void PointDestroyedMidRequest_AbortsAndReleases()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);

            InvokePrivate(point, "OnDisable");
            Object.DestroyImmediate(point.gameObject);
            rig.Tick(0.05f);

            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.AreEqual(1, rig.Log.Interrupted.Count);
            Assert.AreEqual(InvestigationInterruptReason.PointLost, rig.Log.Interrupted[0].Reason);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "参照と所有権を解放して追従へ戻る。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner);
            Assert.AreEqual(0, rig.Log.Completed.Count);
        }

        [Test]
        public void PlayerLeavesContinueRange_AbortsUnfinished()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            AcceptAndArrive(rig, point);

            rig.Player.Position = new Vector3(0f, 0f, 1f + ContinueRange + 0.5f);
            rig.Tick(0.05f);

            Assert.AreEqual(InvestigationInterruptReason.PlayerLeftRange, rig.Driver.LastInterruptReason);
            Assert.IsFalse(rig.Record.Record.IsInvestigated(point.PointId), "未完了のまま。");
            Assert.IsFalse(rig.Driver.IsBusy);
        }

        [Test]
        public void RecruitLostBeforeCompletion_AbortsAtConfirmation()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            AcceptAndArrive(rig, point);

            rig.Roster.Remove(InumaruId);
            rig.Tick(InvestigateSeconds + 0.1f);

            Assert.AreEqual(InvestigationInterruptReason.RecruitLost, rig.Driver.LastInterruptReason);
            Assert.IsFalse(rig.Record.Record.IsInvestigated(point.PointId));
            Assert.AreEqual(0, rig.Log.Completed.Count);
        }

        // ================================================================
        // E09：移動タイムアウト
        // ================================================================

        [Test]
        public void MoveTimeout_AbortsWithoutLeavingOwnership()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);

            rig.Tick(MoveTimeout + 0.1f); // 本体は動かない（物理が進まない）＝到達失敗。

            Assert.AreEqual(InvestigationInterruptReason.MoveTimeout, rig.Driver.LastInterruptReason);
            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "ロックを残さない。");
            Assert.AreEqual(CompanionMovementOwner.None, rig.Movement.Owner);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State);

            // 次の依頼を受けられる。
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "中断後に再依頼できる。");
        }

        /// <summary>受付後に遮られた表示代理は壁抜けせず中断し、次の依頼を受けられる（P02 後半の純粋版）。</summary>
        [Test]
        public void ProxyBlockedAfterAcceptance_AbortsWithoutPassingThroughWalls()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1.2f));
            rig.Player.Position = Vector3.zero;
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode);
            Vector3 before = rig.Driver.ProxyPosition;

            rig.Probe.Clear = false; // 受付後に壁が入った。
            rig.Tick(0.1f);

            Assert.AreEqual(InvestigationInterruptReason.Blocked, rig.Driver.LastInterruptReason);
            Assert.AreEqual(before, rig.Driver.ProxyPosition, "遮られた歩みは進めない（壁抜けしない）。");
            Assert.IsFalse(rig.Driver.IsBusy);

            rig.Probe.Clear = true;
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "次の依頼を受け付けられる。");
        }

        // ================================================================
        // E10：完了と戦闘開始が同じ Tick
        // ================================================================

        [Test]
        public void CompletionAndCombatStartInSameTick_CombatWins_NoRecord()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            AcceptAndArrive(rig, point);
            rig.Tick(InvestigateSeconds * 0.9f);

            ActivityFake.Current = CompanionActivity.Fighting; // この Tick で戦闘が始まった。
            rig.Tick(InvestigateSeconds); // 時間としては満了する。

            Assert.AreEqual(0, rig.Log.Completed.Count, "戦闘開始が優先され、未完了。");
            Assert.IsFalse(rig.Record.Record.IsInvestigated(point.PointId));
            Assert.AreEqual(InvestigationInterruptReason.CombatStarted, rig.Driver.LastInterruptReason);
            Assert.IsFalse(rig.Driver.IsBusy);
        }

        /// <summary>明示の戦闘開始要求は同期的に依頼を解放する（P4-08R の開始操作。呼び出しが戻った時点で解放済み）。</summary>
        [Test]
        public void ExplicitCombatStart_ReleasesSynchronously()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);

            rig.Coordinator.InterruptAllForCombat();

            Assert.IsFalse(rig.Driver.IsBusy, "呼び出しが戻った時点で解放されている。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner);
            Assert.AreEqual(CompanionMovementOwner.None, rig.Movement.Owner);
            Assert.AreEqual(InvestigationInterruptReason.CombatStarted, rig.Log.Interrupted[0].Reason);
        }

        // ================================================================
        // E11：成功後の帰還中断
        // ================================================================

        [Test]
        public void InterruptDuringReturn_KeepsSuccess_AndEmitsNoCancel()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            for (int i = 0; i < 40 && rig.Driver.Phase != InvestigationPhase.Returning; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.AreEqual(InvestigationPhase.Returning, rig.Driver.Phase, "前提：成功して帰還中。");
            Assert.AreEqual(1, rig.Log.Completed.Count);

            rig.Coordinator.InterruptAllForCombat();

            Assert.IsFalse(rig.Driver.IsBusy, "表示を撤収する。");
            Assert.IsTrue(rig.Record.Record.IsInvestigated(point.PointId), "調査済みは取り消さない。");
            Assert.AreEqual(0, rig.Log.Interrupted.Count, "成功後に Canceled を追加発行しない。");
            Assert.AreEqual(1, rig.Log.Completed.Count, "成功は 1 回。");
        }

        // ================================================================
        // E12：中断した古い依頼からの完了
        // ================================================================

        [Test]
        public void StaleCompletion_FromAbortedRequest_IsIgnored()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            InvestigationRequest old = rig.Driver.CurrentRequest;
            rig.Coordinator.InterruptAllForCombat();
            Assert.IsTrue(old.Terminated, "前提：中断済み。");

            // 新しい依頼を出す。
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            InvestigationRequest current = rig.Driver.CurrentRequest;
            Assert.AreNotSame(old, current);

            // 古い依頼の遅延完了が届いても無視される。
            bool confirmed = rig.Coordinator.TryConfirmCompletion(rig.Driver, old, out InvestigationInterruptReason failure);

            Assert.IsFalse(confirmed);
            Assert.AreEqual(InvestigationInterruptReason.CompletionRejected, failure);
            Assert.IsFalse(rig.Record.Record.IsInvestigated(point.PointId), "古い依頼で調査済みにしない。");
            Assert.AreEqual(0, rig.Log.Completed.Count);
            Assert.AreSame(current, rig.Driver.CurrentRequest, "新しい依頼へ影響しない。");
            Assert.IsTrue(rig.Driver.IsBusy);
        }

        // ================================================================
        // E13：Pause → Resume、dt=0
        // ================================================================

        [Test]
        public void Pause_FreezesProgress_AndResumesTheSameRequest()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            AcceptAndArrive(rig, point);
            InvestigationRequest request = rig.Driver.CurrentRequest;
            float before = rig.Driver.Progress;

            ActivityFake.Current = CompanionActivity.Paused(false);
            rig.Tick(InvestigateSeconds * 2f);
            rig.Tick(0f);

            Assert.AreEqual(before, rig.Driver.Progress, 1e-4f, "Pause 中は時間が止まる。");
            Assert.AreSame(request, rig.Driver.CurrentRequest, "依頼を保持する。");
            Assert.AreEqual(0, rig.Log.Completed.Count);

            ActivityFake.Current = CompanionActivity.FreeRoam;
            rig.Tick(0f);
            Assert.AreSame(request, rig.Driver.CurrentRequest, "dt=0 で二重開始しない。");
            rig.Tick(InvestigateSeconds + 0.1f);
            Assert.AreEqual(1, rig.Log.Completed.Count, "同じ依頼で続行して完了する。");
            Assert.AreEqual(1, rig.Log.Accepted.Count, "受理通知は 1 回のまま。");
        }

        // ================================================================
        // E14：会話・イベント
        // ================================================================

        [Test]
        public void DialogueOrEvent_AbortsAndDoesNotResume()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            AcceptAndArrive(rig, point);

            ActivityFake.Current = CompanionActivity.Interrupted(false);
            rig.Tick(0.05f);
            Assert.AreEqual(InvestigationInterruptReason.EventStarted, rig.Driver.LastInterruptReason);
            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "表示と参照を解除している。");

            ActivityFake.Current = CompanionActivity.FreeRoam;
            rig.Tick(InvestigateSeconds + 0.1f);
            Assert.IsFalse(rig.Driver.IsBusy, "終了後に勝手に再開しない（再操作が要る）。");
            Assert.AreEqual(0, rig.Log.Completed.Count);
        }

        // ================================================================
        // E21：実行中の SO 編集
        // ================================================================

        [Test]
        public void SettingsEditedMidRequest_DoNotAffectTheCurrentRequest()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            AcceptAndArrive(rig, point);

            SetPrivateField(rig.Settings, "_investigationSeconds", InvestigateSeconds * 10f);

            Assert.AreEqual(InvestigateSeconds, rig.Driver.CurrentRequest.Settings.InvestigationSeconds, 1e-4f,
                "現行依頼の Snapshot は変わらない。");
            rig.Tick(InvestigateSeconds + 0.1f);
            Assert.AreEqual(1, rig.Log.Completed.Count, "元の秒数で完了する。");

            Assert.AreEqual(InvestigateSeconds * 10f, point.Settings.InvestigationSeconds, 1e-4f, "次の依頼からは新しい値。");
        }

        // ================================================================
        // E22：null・破棄済み参照
        // ================================================================

        [Test]
        public void MissingWiring_IsRejectedWithoutException()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;

            // 加入供給元が無い。
            Object.DestroyImmediate(rig.Roster);
            InvestigationRequestResult result = rig.Coordinator.TryRequest();
            Assert.AreEqual(InvestigationRejectReason.NotWired, result.Reason);
            Assert.IsFalse(rig.Driver.IsBusy);

            // 駆動へ直接 null を渡しても例外を出さない。
            Assert.IsFalse(rig.Driver.TryBegin(null, 1, rig.Coordinator, rig.Player, out _, out InvestigationRejectReason reason));
            Assert.AreEqual(InvestigationRejectReason.NotWired, reason);

            // 依頼が無い状態の Tick・中断・完了は無害。
            rig.Driver.TickInvestigation(0.1f);
            rig.Driver.NotifyCombatStart();
            Assert.IsFalse(rig.Coordinator.TryConfirmCompletion(rig.Driver, null, out _));
        }

        // ================================================================
        // c8c0ddf §5：探索中の競合
        // ================================================================

        /// <summary>
        /// 調査中は索敵が敵を拾っても接近・攻撃を始めない（危険候補だけでは探索を解かない）。
        /// 戦闘本体への実命中は同期的に探索を解放し、その同じ呼び出しの中で命中を解決する。解放後は通常どおり戦う。
        /// </summary>
        [Test]
        public void DuringInvestigation_AutoCombatIsDenied_AndRealHitReleasesSynchronously()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;

            GameObject go = rig.Actor.gameObject;
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(rig.Actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(rig.Actor, go.GetComponent<CompanionMotor>(), tracker);
            InvokePrivate(combat, "OnEnable");

            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 1f);
            PerceptionTargetRegistry.Register(enemyGo.AddComponent<FakeEnemy>());

            AcceptAndArrive(rig, point);

            tracker.TickTargeting();
            combat.TickCombat(0.05f);
            Assert.IsFalse(combat.IsAttacking, "調査中は攻撃を始めない。");
            Assert.AreEqual(CompanionState.Investigate, rig.Actor.State, "接近（Chase）で探索を奪わない。");
            Assert.IsTrue(rig.Driver.IsBusy);

            int hpBefore = rig.Receiver.CurrentHp;
            rig.Receiver.ReceiveHit(new HitInfo(
                null, rig.Receiver, Vector3.back, Vector3.zero, new HitDamage(10f, 0f, 0f),
                guardable: false, justGuardable: false, hitId: HitId.Single(77)));

            Assert.IsFalse(rig.Driver.IsBusy, "実命中で探索が同期的に解放されている（受け口から戻った時点）。");
            Assert.AreEqual(InvestigationInterruptReason.CompanionHit, rig.Driver.LastInterruptReason);
            Assert.AreEqual(hpBefore - 10, rig.Receiver.CurrentHp, "命中はそのまま解決される（代理が被害を吸わない）。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "解放後は通常状態。");

            tracker.TickTargeting();
            combat.TickCombat(0f);
            Assert.IsTrue(combat.IsAttacking, "解放後は通常どおり戦える。");
        }

        /// <summary>
        /// 主人公への実命中では、被弾入口が解決より先に探索を解放し、解放後の状態（Follow）で守護を評価する。
        ///
        /// <b>本物の <see cref="PlayerVitalsHolder"/> を通す</b>（R3-02）。守護側を直接呼ぶテストでは、
        /// 主人公が Guard／JG／有効 Step で先に return する経路を検証できない（それらは守護評価まで到達しない）。
        /// </summary>
        [Test]
        public void RealHitOnThePlayer_ReleasesInvestigationFirst_ThenProtects()
        {
            Rig rig = MakeRig(companionPosition: new Vector3(0f, 0f, 0.5f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            PlayerVitalsHolder player = MakePlayerVitals(rig, Vector3.zero);

            var guardian = rig.Actor.gameObject.AddComponent<CompanionGuardianController>();
            guardian.Bind(rig.Actor, rig.Receiver, player.transform);
            InvokePrivate(guardian, "OnEnable");

            AcceptAndArrive(rig, point);
            Assert.AreEqual(CompanionState.Investigate, rig.Actor.State);
            int playerHp = player.Vitals.Health.Current;
            int dogHp = rig.Receiver.CurrentHp;

            player.ReceiveHit(HitOnPlayer(player, 88));

            Assert.IsFalse(rig.Driver.IsBusy, "主人公への実命中で探索が先に解放される。");
            Assert.AreEqual(InvestigationInterruptReason.CompanionHit, rig.Driver.LastInterruptReason);
            Assert.AreEqual(1, guardian.TransferCount, "解放後の資格（Follow・範囲内・CD 無し）で庇える。");
            Assert.AreEqual(playerHp, player.Vitals.Health.Current, "肩代わりが成立したので主人公は削れない。");
            Assert.AreEqual(dogHp - 10, rig.Receiver.CurrentHp, "犬丸が代わりに受ける。");
        }

        /// <summary>
        /// 主人公が<b>ガードで弾いた</b>命中でも探索は解放される（R3-02）。守護評価まで到達しない経路。
        /// 解放は結果購読者が観測する時点で既に終わっている（＝解決より前）。
        /// </summary>
        [Test]
        public void PlayerGuardsTheHit_ReleasesInvestigationBeforeTheResultIsPublished()
        {
            Rig rig = MakeRig(companionPosition: new Vector3(0f, 0f, 0.5f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            PlayerVitalsHolder player = MakePlayerVitals(rig, Vector3.zero);
            var guard = player.gameObject.AddComponent<FakeGuardState>();
            guard.IsGuarding = true;
            guard.GuardForward = Vector3.back; // 攻撃方向 +Z の正面。

            AcceptAndArrive(rig, point);
            var observer = new BusyAtResult(rig.Driver);
            player.Results.AddListener(observer);

            player.ReceiveHit(HitOnPlayer(player, 89, guardable: true));

            Assert.AreEqual(1, observer.Count, "結果は 1 件。");
            Assert.AreEqual(HitResultKind.Guard, observer.LastKind, "主人公はガードで解決した（守護評価へ進まない）。");
            Assert.IsFalse(observer.BusyWhenObserved, "結果を観測した時点で探索は解放済み。");
            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.AreEqual(InvestigationInterruptReason.CompanionHit, rig.Driver.LastInterruptReason);
            Assert.AreEqual(1, rig.Log.Interrupted.Count, "未完了の中断通知は 1 回。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "探索状態を抜ける。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "行動の所有権を返す。");
            player.Results.RemoveListener(observer);
        }

        /// <summary>主人公が<b>ステップ無敵で避けた</b>命中でも探索は解放される（R3-02）。</summary>
        [Test]
        public void PlayerStepsTheHit_StillReleasesTheInvestigation()
        {
            Rig rig = MakeRig(companionPosition: new Vector3(0f, 0f, 0.5f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            PlayerVitalsHolder player = MakePlayerVitals(rig, Vector3.zero);
            player.gameObject.AddComponent<FakeEvadeState>().IsInvincible = true;

            AcceptAndArrive(rig, point);
            var observer = new BusyAtResult(rig.Driver);
            player.Results.AddListener(observer);

            player.ReceiveHit(HitOnPlayer(player, 90));

            Assert.AreEqual(HitResultKind.Evade, observer.LastKind, "主人公は無敵で回避した。");
            Assert.IsFalse(observer.BusyWhenObserved, "それでも探索は解決より先に解放されている。");
            Assert.AreEqual(InvestigationInterruptReason.CompanionHit, rig.Driver.LastInterruptReason);
            player.Results.RemoveListener(observer);
        }

        /// <summary>
        /// <b>表示代理</b>の探索も、主人公への実命中で解放される（R3-02）。
        /// 代理は行動の所有権を持たないので、所有権を返すだけの経路では解放できなかった。
        /// </summary>
        [Test]
        public void RealHitOnThePlayer_ReleasesTheProxyInvestigationToo()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            PlayerVitalsHolder player = MakePlayerVitals(rig, Vector3.zero);
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "前提：Down でも代理で受理。");
            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode);
            Assert.IsTrue(rig.Driver.ProxyVisible);
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "代理は行動の所有権を持たない。");

            var observer = new BusyAtResult(rig.Driver);
            player.Results.AddListener(observer);

            player.ReceiveHit(HitOnPlayer(player, 91));

            Assert.IsFalse(observer.BusyWhenObserved, "結果を観測した時点で代理の依頼は解放済み。");
            Assert.IsFalse(observer.ProxyShownWhenObserved, "代理表示も同じ時点で消えている。");
            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.IsFalse(rig.Driver.ProxyVisible);
            Assert.AreEqual(InvestigationInterruptReason.CompanionHit, rig.Driver.LastInterruptReason);
            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "戦闘状態は触らない（解放は所有権と表示の話）。");
            player.Results.RemoveListener(observer);
        }

        /// <summary>
        /// 代理探索の最中に自然復帰した本体が撃たれた場合も解放する（R3-02 の受入 3 つ目）。
        /// 復帰後は本体が探索の所有権を持っていないので、受け口が駆動を直接止める必要がある。
        /// </summary>
        [Test]
        public void RealHitOnTheRevivedBody_ReleasesTheProxyInvestigation()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;

            DownTheCompanion(rig);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "前提：代理で受理。");
            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode);

            rig.Receiver.TickVitals(RecoverySeconds + 0.1f); // 自然復帰。
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "前提：本体は復帰している。");
            Assert.IsTrue(rig.Driver.IsBusy, "前提：代理の調査は続く。");

            rig.Receiver.ReceiveHit(new HitInfo(
                null, rig.Receiver, Vector3.back, Vector3.zero, new HitDamage(5f, 0f, 0f),
                guardable: false, justGuardable: false, hitId: HitId.Single(92)));

            Assert.IsFalse(rig.Driver.IsBusy, "復帰した本体への実命中で代理の依頼も解放される。");
            Assert.IsFalse(rig.Driver.ProxyVisible);
            Assert.AreEqual(InvestigationInterruptReason.CompanionHit, rig.Driver.LastInterruptReason);
        }

        /// <summary>自動ガードの危険候補だけでは探索を解かない（許可表で拒否）。</summary>
        [Test]
        public void DangerCandidateAlone_DoesNotReleaseInvestigation()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            AcceptAndArrive(rig, point);

            bool started = rig.States.TryStartAction(
                CompanionActionOwner.Defense, CompanionActionKind.AutoGuard, CompanionState.Guard,
                CompanionStateChangeReason.DefensiveAction, out _);

            Assert.IsFalse(started, "調査中の自動ガード開始は拒否。");
            Assert.IsTrue(rig.Driver.IsBusy, "探索は続く。");
            Assert.AreEqual(CompanionState.Investigate, rig.Actor.State);
        }

        // ================================================================
        // R3-01：完了通知からの再入（購読者が撤収・別依頼開始・Disable を行う）
        // ================================================================

        /// <summary>
        /// 完了通知の<b>コールバックの中</b>で戦闘を開始しても例外にならず、成功は取り消されない（R3-01）。
        ///
        /// 代理での完了は、通知から戻ったあとに帰還段へ入る。通知の購読者が撤収していると、
        /// 戻った先の依頼はもう解放済みで、そのまま進めると NullReferenceException になる。
        /// </summary>
        [Test]
        public void CompletionCallback_ThatStartsCombat_DoesNotThrow_AndKeepsTheSuccess()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode, "前提：帰還段を持つ代理モード。");

            var reentry = new ReentrantListener { OnCompleted = () => rig.Coordinator.InterruptAllForCombat() };
            rig.Coordinator.Events.AddListener(reentry);

            for (int i = 0; i < 60 && rig.Driver.IsBusy; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.AreEqual(1, reentry.CompletedCount, "完了通知は 1 回。");
            Assert.AreEqual(1, rig.Log.Completed.Count);
            Assert.IsTrue(rig.Record.Record.IsInvestigated(point.PointId), "成功の記録は残る。");
            Assert.AreEqual(0, rig.Log.Interrupted.Count, "成功後の撤収で中断通知を足さない。");
            Assert.IsFalse(rig.Driver.IsBusy, "通知の中で解放済み。古い呼び出しが進み直さない。");
            Assert.IsFalse(rig.Driver.ProxyVisible);
            rig.Coordinator.Events.RemoveListener(reentry);
        }

        /// <summary>
        /// 完了通知の中で<b>旧依頼を撤収して別依頼を始めた</b>とき、古い呼び出しの続きが新依頼を壊さない（R3-01）。
        /// 依頼の同一性を確かめないと、本体モードでは古い <c>Finish()</c> が新依頼の所有権ごと解放してしまう。
        /// </summary>
        [Test]
        public void CompletionCallback_ThatStartsANewRequest_DoesNotLetTheOldCallFinishIt()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint first = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            CompanionInvestigationPoint second = MakePoint(rig, "point_b", new Vector3(10f, 0f, 1f));
            rig.Player.Position = Vector3.zero;

            var reentry = new ReentrantListener();
            reentry.OnCompleted = () =>
            {
                rig.Driver.NotifyRealHit();                    // 旧依頼を撤収する（成功済みなので中断通知は出ない）。
                rig.Player.Position = new Vector3(10f, 0f, 0.5f); // 2 つ目の地点の前へ移る。
                Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "通知の中から次の依頼を始める。");
            };
            rig.Coordinator.Events.AddListener(reentry);

            AcceptAndArrive(rig, first);
            rig.Tick(InvestigateSeconds + 0.1f);

            Assert.AreEqual(1, reentry.CompletedCount);
            Assert.IsTrue(rig.Record.Record.IsInvestigated(first.PointId), "1 件目の成功は残る。");
            Assert.IsFalse(rig.Record.Record.IsInvestigated(second.PointId), "2 件目はまだ未完了。");
            Assert.IsTrue(rig.Driver.IsBusy, "新依頼が生きている（古い呼び出しに終わらされない）。");
            Assert.AreEqual(2, rig.Driver.CurrentRequest.RequestId, "現在の依頼は 2 件目。");
            Assert.AreEqual(InvestigationPhase.Moving, rig.Driver.Phase, "新依頼は始まったばかり（帰還・完了させない）。");
            Assert.AreEqual(CompanionActionOwner.Investigate, rig.States.CurrentOwner, "新依頼の所有権が残る。");
            Assert.AreEqual(0, rig.Log.Interrupted.Count, "成功後の撤収で中断通知を足さない。");
            rig.Coordinator.Events.RemoveListener(reentry);
        }

        /// <summary>完了通知の中で駆動を無効化しても例外にならず、成功の記録・通知は 1 回のまま（R3-01）。</summary>
        [Test]
        public void CompletionCallback_ThatDisablesTheDriver_DoesNotThrow()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);

            var reentry = new ReentrantListener { OnCompleted = () => InvokePrivate(rig.Driver, "OnDisable") };
            rig.Coordinator.Events.AddListener(reentry);

            for (int i = 0; i < 60 && rig.Driver.IsBusy; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.AreEqual(1, rig.Log.Completed.Count, "成功通知は 1 回。");
            Assert.AreEqual(0, rig.Log.Interrupted.Count, "成功後の解放で中断通知を足さない。");
            Assert.IsTrue(rig.Record.Record.IsInvestigated(point.PointId));
            Assert.IsFalse(rig.Driver.IsBusy);
            rig.Coordinator.Events.RemoveListener(reentry);
        }

        // ================================================================
        // R3-03（E20）：代理探索中の自然復帰で、見えない本体が動き出さない
        // ================================================================

        /// <summary>
        /// 復帰待ちの終盤から代理探索を始め、<b>自然復帰の時刻をまたいでも</b>本体が通常行動へ戻らない（R3-03）。
        /// HP・復帰時計は仕様どおり進み（探索は戦闘値に触れない）、探索が終わってから本体が再判断する。
        /// </summary>
        [Test]
        public void NaturalRevive_DuringProxyInvestigation_KeepsTheHiddenBodyStill()
        {
            Rig rig = MakeRig();
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;

            GameObject go = rig.Actor.gameObject;
            var leader = new GameObject("Leader");
            _spawned.Add(leader);
            leader.transform.position = new Vector3(20f, 0f, 0f); // 追従なら必ず歩き出す（ワープ距離も超える）位置。
            var follow = go.AddComponent<CompanionFollowController>();
            follow.Bind(leader.transform, rig.Actor, go.GetComponent<CompanionMotor>());
            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(rig.Actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(rig.Actor, go.GetComponent<CompanionMotor>(), tracker);
            InvokePrivate(combat, "OnEnable");

            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 0.5f);
            PerceptionTargetRegistry.Register(enemyGo.AddComponent<FakeEnemy>());

            DownTheCompanion(rig);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "復帰待ちのうちに代理探索を始める。");
            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode);

            Vector3 bodyBefore = rig.Actor.WorldPosition;
            int warpsBefore = go.GetComponent<CompanionMotor>().WarpCount;

            // 自然復帰の時刻をまたぐ。探索は続いたまま。
            rig.Receiver.TickVitals(RecoverySeconds + 0.1f);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "HP・状態は仕様どおり復帰する。");
            Assert.Greater(rig.Receiver.CurrentHp, 0, "復帰で HP が戻る（探索は触っていない）。");
            Assert.IsTrue(rig.Driver.IsBusy, "探索は続行する。");
            Assert.IsTrue(rig.Driver.ProxyVisible, "代理は出たまま。");

            var followDecisions = new List<CompanionFollowDecision>();
            var combatDecisions = new List<CompanionEngageDecision>();
            for (int i = 0; i < 10; i++)
            {
                follow.TickFollow(0.05f);
                tracker.TickTargeting();
                combat.TickCombat(0.05f);
                rig.Tick(0.05f);
                followDecisions.Add(follow.Decision);
                combatDecisions.Add(combat.Decision);
            }

            Assert.IsTrue(follow.IsYieldingToInvestigation, "追従は探索へ譲っている。");
            Assert.IsTrue(combat.IsInvestigating, "戦闘側も探索中と見ている。");
            Assert.IsFalse(followDecisions.Contains(CompanionFollowDecision.Move), "追従の移動判断が出ない。");
            Assert.IsFalse(followDecisions.Contains(CompanionFollowDecision.Warp), "距離超過のワープ判断も出ない。");
            CollectionAssert.DoesNotContain(combatDecisions, CompanionEngageDecision.Chase, "敵へ寄り始めない。");
            CollectionAssert.DoesNotContain(combatDecisions, CompanionEngageDecision.Attack, "殴り始めない。");
            Assert.IsFalse(combat.IsEngaged, "戦闘側が移動を握らない（追従も戦闘へ譲らない）。");
            Assert.IsFalse(combat.IsAttacking, "見えない本体が殴り始めない。");
            Assert.AreEqual(bodyBefore, rig.Actor.WorldPosition, "本体は動かない。");
            Assert.AreEqual(warpsBefore, go.GetComponent<CompanionMotor>().WarpCount, "距離超過のワープもしない。");
            Assert.IsFalse(go.GetComponent<CompanionMotor>().HasMoveTarget, "追従の移動意図も出ない。");
            Assert.AreNotEqual(CompanionMovementOwner.Follow, rig.Movement.Owner, "追従が移動を握らない。");
            Assert.IsTrue(rig.Driver.ProxyVisible, "二重表示にしない（代理だけが出ている）。");

            // 探索が終われば、復帰後の本体状態に従って通常行動へ戻る。
            for (int i = 0; i < 80 && rig.Driver.IsBusy; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.IsFalse(rig.Driver.IsBusy, "前提：引き渡し完了。");
            Assert.IsFalse(rig.Driver.ProxyVisible);
            follow.TickFollow(0.05f);
            Assert.IsFalse(follow.IsYieldingToInvestigation, "譲るのをやめる。");
            Assert.AreNotEqual(CompanionFollowDecision.Hold, follow.Decision, "隊列へ戻る判断を再開する。");
        }

        // ================================================================
        // R3-04（E21）：移動速度も受付時の Snapshot
        // ================================================================

        /// <summary>
        /// 代理の移動速度は受付時に固定される（R3-04）。実行中に Data を変えても現行依頼の進みは変わらず、
        /// 次の依頼から新しい値になる。
        /// </summary>
        [Test]
        public void ProxyMoveSpeedEditedMidRequest_DoesNotChangeTheCurrentRequest_ButAppliesToTheNext()
        {
            // 代理は倒れている本体の位置から出る。遠くに倒しておけば、移動している最中の速度を測れる。
            Rig rig = MakeRig(companionPosition: new Vector3(0f, 0f, -40f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            SetPrivateField(rig.Settings, "_moveTimeoutSeconds", 1000f); // 時間切れではなく速度だけを見る。
            rig.States.ForceHit(CompanionState.Down, CompanionStateChangeReason.Defeated);

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode);
            Assert.AreEqual(MoveSpeed, rig.Driver.CurrentRequest.MoveSpeed, 1e-4f, "受付時の速度を写し取る。");

            Vector3 before = rig.Driver.ProxyPosition;
            rig.Tick(0.1f);
            float movedBefore = (rig.Driver.ProxyPosition - before).magnitude;
            Assert.AreEqual(MoveSpeed * 0.1f, movedBefore, 1e-3f, "前提：Data の速度で進む。");

            SetPrivateField(rig.Actor.Data, "_moveSpeed", MoveSpeed * 4f); // 実行中に原本を編集。

            Vector3 mid = rig.Driver.ProxyPosition;
            rig.Tick(0.1f);
            float movedAfter = (rig.Driver.ProxyPosition - mid).magnitude;
            Assert.AreEqual(movedBefore, movedAfter, 1e-3f, "現行依頼の速度は変わらない。");
            Assert.AreEqual(MoveSpeed, rig.Driver.CurrentRequest.MoveSpeed, 1e-4f);

            // 撤収して次の依頼を出すと、新しい値が効く。
            rig.Driver.NotifyRealHit();
            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            Assert.AreEqual(MoveSpeed * 4f, rig.Driver.CurrentRequest.MoveSpeed, 1e-4f, "次の依頼から新しい速度。");

            Vector3 next = rig.Driver.ProxyPosition;
            rig.Tick(0.1f);
            Assert.AreEqual(MoveSpeed * 4f * 0.1f, (rig.Driver.ProxyPosition - next).magnitude, 1e-3f);
        }

        /// <summary>本体モードでも同じ：実行中に Data を変えても、Motor へ渡る速度は受付時のまま（R3-04）。</summary>
        [Test]
        public void BodyMoveSpeedEditedMidRequest_DoesNotChangeTheCurrentRequest()
        {
            Rig rig = MakeRig(companionPosition: new Vector3(0f, 0f, -10f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = Vector3.zero;
            SetPrivateField(rig.Settings, "_moveTimeoutSeconds", 1000f);

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            Assert.AreEqual(InvestigationMode.Body, rig.Driver.Mode);
            CompanionMotor motor = rig.Actor.GetComponent<CompanionMotor>();
            Assert.AreEqual(MoveSpeed, motor.MoveSpeed, 1e-4f, "前提：受付時の速度で歩き出す。");

            SetPrivateField(rig.Actor.Data, "_moveSpeed", MoveSpeed * 4f);
            rig.Tick(0.1f);

            Assert.AreEqual(MoveSpeed, rig.Driver.CurrentRequest.MoveSpeed, 1e-4f, "依頼の速度は不変。");
            Assert.AreEqual(MoveSpeed, motor.MoveSpeed, 1e-4f, "Motor へ渡る速度も不変（Data を読み直さない）。");
        }

        // ================================================================
        // 純粋モデル：記録・進行
        // ================================================================

        [Test]
        public void Record_MarksOnce_AndClearResets()
        {
            var record = new InvestigationRecord();
            var id = new StableId("point_x");

            Assert.IsFalse(record.IsInvestigated(id));
            Assert.IsTrue(record.TryMarkInvestigated(id));
            Assert.IsFalse(record.TryMarkInvestigated(id), "2 回目は false（完了は 1 回だけ）。");
            Assert.IsTrue(record.IsInvestigated(id));
            Assert.IsFalse(record.TryMarkInvestigated(default), "空の ID は記録しない。");

            record.Clear();
            Assert.IsFalse(record.IsInvestigated(id));
        }

        [Test]
        public void Settings_ClampsInvalidValues_AndDetectsUnusable()
        {
            var s = new InvestigationSettings(float.NaN, -1f, float.PositiveInfinity, 3f, 1f, 1f);
            Assert.AreEqual(0f, s.InteractRange);
            Assert.AreEqual(0f, s.ContinueRange);
            Assert.AreEqual(0f, s.ArrivalDistance);
            Assert.IsFalse(s.IsUsable, "受付距離 0 は使えない。");
            Assert.IsFalse(InvestigationSettings.From(null).IsUsable);
            Assert.IsTrue(InvestigationSettings.From(MakeSettings()).IsUsable);
        }
    }
}
