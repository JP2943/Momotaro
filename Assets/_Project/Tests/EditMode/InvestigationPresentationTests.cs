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
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Input;
using Momotaro.Presentation.Companion;
using Momotaro.Presentation.Hud;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-07B：探索の入力仲介・表示代理・地点マーカー・短文 UI・明示開始入力を、本物の駆動と調停役に繋いで検証する
    /// （v1.0 §4.2 押下 1 回で 1 依頼、§5.2 表示代理と通常表示の所有権、§11 文字で識別、§13.1 明示開始。要求 E03・E20・P03）。
    ///
    /// 主人公入力は本物の <see cref="PlayerInputState"/>（Interact のラッチ）を使い、Input System のデバイスだけを外す。
    /// 表示は SpriteRenderer／TextMesh の enabled・text・color を読む（EditMode では LateUpdate が走らないので Refresh を直接呼ぶ）。
    /// </summary>
    public sealed class InvestigationPresentationTests : CompanionActivityFixture
    {
        private const float InteractRange = 1.5f;
        private const float ContinueRange = 3.0f;
        private const float Arrival = 0.2f;
        private const float MoveTimeout = 3.0f;
        private const float InvestigateSeconds = 1.0f;
        private const float ReturnTimeout = 1.0f;
        private const float MoveSpeed = 5f;

        private static readonly StableId InumaruId = new StableId("companion_inumaru");

        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
            PlayerInputProvider.Current = null;
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
            PlayerInputProvider.Current = null;
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
            public bool IsClear(Vector3 from, Vector3 to) => Clear;
        }

        private sealed class EventLog : IInvestigationListener
        {
            public int Accepted, Rejected, Completed, Interrupted;
            public InvestigationRejectReason LastReject;

            public void OnInvestigationAccepted(in InvestigationAccepted e) => Accepted++;
            public void OnInvestigationRejected(in InvestigationRejected e) { Rejected++; LastReject = e.Reason; }
            public void OnInvestigationCompleted(in InvestigationCompleted e) => Completed++;
            public void OnInvestigationInterrupted(in InvestigationInterrupted e) => Interrupted++;
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
            public CompanionGuardianController Guardian;
            public InvestigationSettingsData Settings;
            public CompanionPlaceholderPresenter Normal;
            public SpriteRenderer NormalBody;
            public SpriteRenderer NormalArrow;
            public CompanionInvestigationProxyPresenter Proxy;
            public SpriteRenderer ProxyBody;
            public SpriteRenderer ProxyArrow;
            public TextMesh Label;
            public List<CompanionInvestigationPoint> Points = new List<CompanionInvestigationPoint>();

            public void Tick(float dt)
            {
                Driver.TickInvestigation(dt);
                Movement.EndFrame();
                Proxy.Refresh();
            }

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
            return data;
        }

        private SpriteRenderer MakeRenderer(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            _spawned.Add(r.sprite);
            return r;
        }

        private TextMesh MakeLabel(string name, Transform parent)
        {
            var anchor = new GameObject(name + "Anchor");
            anchor.transform.SetParent(parent, false);
            var go = new GameObject(name);
            go.transform.SetParent(anchor.transform, false);
            return go.AddComponent<TextMesh>();
        }

        private CompanionInvestigationPoint MakePoint(Rig rig, string id, Vector3 position, StableId? required = null)
        {
            var go = new GameObject("Point_" + id);
            _spawned.Add(go);
            go.transform.position = position;
            var point = go.AddComponent<CompanionInvestigationPoint>();
            point.Configure(new StableId(id), required ?? InumaruId, new StableId("discovery_" + id), rig.Settings);
            point.ConfigureText("調べる", "犬がいれば何か見つかりそうだ", "ここは調べ終えた");
            InvokePrivate(point, "OnEnable");
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
            rig.Guardian = go.AddComponent<CompanionGuardianController>();
            rig.Guardian.Bind(rig.Actor);
            rig.Driver = go.AddComponent<CompanionInvestigationController>();
            rig.Driver.Bind(rig.Actor, rig.States, rig.Movement);
            InvokePrivate(rig.Driver, "OnEnable");

            // 表示：通常表示と表示代理（Prefab Builder と同じ形）。
            rig.NormalBody = MakeRenderer("Sprite", go.transform);
            rig.NormalArrow = MakeRenderer("DirectionArrow", go.transform);
            rig.Normal = go.AddComponent<CompanionPlaceholderPresenter>();
            rig.Normal.Bind(rig.Actor, rig.NormalBody, rig.NormalArrow);
            InvokePrivate(rig.Normal, "OnEnable");

            var proxyAnchor = new GameObject("InvestigationProxy");
            proxyAnchor.transform.SetParent(go.transform, false);
            rig.ProxyBody = MakeRenderer("ProxyBody", proxyAnchor.transform);
            rig.ProxyArrow = MakeRenderer("ProxyArrow", go.transform);
            rig.Label = MakeLabel("InvestigationLabel", go.transform);
            rig.Proxy = go.AddComponent<CompanionInvestigationProxyPresenter>();
            rig.Proxy.Bind(rig.Driver, rig.Normal, proxyAnchor.transform, rig.ProxyBody, rig.ProxyArrow, rig.Label);
            InvokePrivate(rig.Proxy, "OnEnable");

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

        private InvestigationInteractInput MakeInput(Rig rig, PlayerInputState state)
        {
            var go = new GameObject("InvestigationInput");
            _spawned.Add(go);
            var input = go.AddComponent<InvestigationInteractInput>();
            input.Bind(rig.Coordinator);
            input.SetInput(state);
            return input;
        }

        private InvestigationPointMarker MakeMarker(Rig rig, CompanionInvestigationPoint point)
        {
            SpriteRenderer ring = MakeRenderer("Ring", point.transform);
            TextMesh label = MakeLabel("Label", point.transform);
            var marker = point.gameObject.AddComponent<InvestigationPointMarker>();
            marker.Bind(point, rig.Record, rig.Coordinator, ring, label);
            InvokePrivate(marker, "OnEnable");
            return marker;
        }

        private InvestigationPromptHud MakeHud(Rig rig, TrialStageController stage, params InvestigationPointMarker[] markers)
        {
            var go = new GameObject("InvestigationHud");
            _spawned.Add(go);
            var hud = go.AddComponent<InvestigationPromptHud>();
            hud.Bind(rig.Coordinator, stage, markers);
            InvokePrivate(hud, "OnEnable");
            return hud;
        }

        /// <summary>表示代理を出発位置から調査位置まで歩かせる（Proxy モードは駆動が位置を積分する）。</summary>
        private static void WalkProxyToArrival(Rig rig, int maxTicks = 60)
        {
            for (int i = 0; i < maxTicks && rig.Driver.Phase == InvestigationPhase.Moving; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.AreEqual(InvestigationPhase.Investigating, rig.Driver.Phase, "前提：代理が調査位置へ到着する。");
        }

        // ================================================================
        // 入力仲介：押下 1 回で 1 依頼（§4.2、P03 の EditMode 側）
        // ================================================================

        [Test]
        public void InteractInput_OnePress_YieldsOneRequest_AndHoldDoesNotRepeat()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            var state = new PlayerInputState();
            InvestigationInteractInput input = MakeInput(rig, state);

            state.SetInteract(true);
            Assert.IsTrue(input.TickInput(), "押下エッジで依頼が出る。");
            Assert.AreEqual(1, input.RequestCount);
            Assert.AreEqual(1, rig.Log.Accepted, "受理通知 1 回。");
            Assert.IsFalse(state.InteractPressed, "押下は消費済み。");

            // 押しっぱなし：フレームが進んでも再依頼しない。
            state.SetInteract(true);
            Assert.IsFalse(input.TickInput(), "保持では再実行しない。");
            Assert.IsFalse(input.TickInput());
            Assert.AreEqual(1, input.RequestCount);
            Assert.AreEqual(1, rig.Log.Accepted);

            // 離して押し直す＝別の押下。実行中なので拒否されるが「1 押下 1 依頼」として理由通知が出る。
            state.SetInteract(false);
            state.SetInteract(true);
            Assert.IsTrue(input.TickInput(), "新しい押下エッジは 1 依頼として通る。");
            Assert.AreEqual(2, input.RequestCount);
            Assert.AreEqual(1, rig.Log.Rejected, "実行中の再依頼は拒否通知。");
            Assert.AreEqual(InvestigationRejectReason.CompanionBusy, rig.Log.LastReject);
            Assert.IsTrue(rig.Driver.IsBusy, "現在の依頼は壊れない。");
        }

        [Test]
        public void InteractInput_PressWithoutTarget_IsDiscarded_AndNotCarriedToTheNextTarget()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 10f); // 範囲外。
            var state = new PlayerInputState();
            InvestigationInteractInput input = MakeInput(rig, state);

            state.SetInteract(true);
            Assert.IsFalse(input.TickInput(), "対象が無い押下は実行しない。");
            Assert.AreEqual(1, input.DiscardedCount);
            Assert.AreEqual(0, input.RequestCount);
            Assert.IsFalse(state.InteractPressed, "古い押下を持ち越さない。");
            Assert.AreEqual(0, rig.Log.Rejected, "対象の無い押下では理由通知も出さない（既存操作のまま）。");

            // 範囲内へ来ても、押し直さなければ依頼は出ない。
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            Assert.IsFalse(input.TickInput());
            Assert.AreEqual(0, input.RequestCount);
            Assert.IsFalse(rig.Driver.IsBusy);
        }

        [Test]
        public void InteractInput_ConsumesOnlyInteract_StepLatchIsUntouched()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            var state = new PlayerInputState();
            InvestigationInteractInput input = MakeInput(rig, state);

            state.SetStep(true);
            state.SetAttack(true);
            state.SetInteract(true);
            Assert.IsTrue(input.TickInput());

            Assert.IsTrue(state.ConsumeStepPressed(), "Step の押下はそのまま残る（Interact から Step を二重起動も横取りもしない）。");
            Assert.IsTrue(state.ConsumeAttackPressed(), "Attack の押下もそのまま。");
        }

        [Test]
        public void InteractInput_ClosedGate_DoesNotLatch_AndReopeningDoesNotFire()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            var state = new PlayerInputState();
            InvestigationInteractInput input = MakeInput(rig, state);

            state.SetActive(false); // Pause／会話／Loading：新規入力は受け付けない（§4.3）。
            state.SetInteract(true);
            Assert.IsFalse(state.InteractPressed, "ゲートが閉じている間はラッチしない。");
            Assert.IsFalse(input.TickInput());

            state.SetActive(true);
            Assert.IsFalse(input.TickInput(), "再開時に押しっぱなしが誤発火しない。");
            Assert.AreEqual(0, input.RequestCount);

            state.SetInteract(false);
            state.SetInteract(true);
            Assert.IsTrue(input.TickInput(), "再開後の新しい押下は通る。");
        }

        [Test]
        public void InteractInput_UsesPlayerInputProvider_WhenNoOverride()
        {
            Rig rig = MakeRig();
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            var state = new PlayerInputState();
            PlayerInputProvider.Current = state;
            InvestigationInteractInput input = MakeInput(rig, null);

            state.SetInteract(true);
            Assert.IsTrue(input.TickInput(), "既定の入力源は主人公入力の供給元（実 Scene と同じ経路）。");
            Assert.AreEqual(1, rig.Log.Accepted);
        }

        // ================================================================
        // 表示代理：Down／Away の犬丸が調べに行く姿（§5.2、E03・E20）
        // ================================================================

        [Test]
        public void ProxyPresenter_DownCompanion_ShowsProxy_SuppressesNormal_ThenRestoresDownVisual()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            rig.Actor.ResetState(CompanionState.Down);
            rig.Normal.Suppressed = false;
            rig.Proxy.Refresh();
            int hpBefore = rig.Receiver.CurrentHp;
            Assert.IsTrue(rig.NormalBody.enabled, "前提：Down は暗い色で見える。");

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "加入済みなら Down でも受理。");
            rig.Proxy.Refresh();

            Assert.AreEqual(InvestigationMode.Proxy, rig.Driver.Mode);
            Assert.IsTrue(rig.Proxy.ProxyShown, "表示代理が出る。");
            Assert.IsTrue(rig.ProxyBody.enabled);
            Assert.IsTrue(rig.ProxyArrow.enabled);
            Assert.IsTrue(rig.Normal.Suppressed, "通常表示は抑制される。");
            Assert.IsFalse(rig.NormalBody.enabled, "同時に 2 体描かない。");
            Assert.IsFalse(rig.NormalArrow.enabled);
            Assert.AreEqual("移動", rig.Proxy.LabelText, "進行段を文字で示す。");
            Assert.AreEqual(rig.Driver.ProxyPosition, rig.ProxyBody.transform.parent.position, "代理は駆動の位置に描く。");
            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "戦闘 Actor は Down のまま（Investigate へ遷移させない）。");

            WalkProxyToArrival(rig);
            Assert.AreEqual("調査中", rig.Proxy.LabelText);

            rig.Tick(InvestigateSeconds + 0.05f);
            Assert.AreEqual(1, rig.Log.Completed, "完了。");
            Assert.AreEqual(InvestigationPhase.Returning, rig.Driver.Phase);
            Assert.AreEqual("帰還", rig.Proxy.LabelText);
            Assert.IsTrue(rig.Proxy.ProxyShown, "帰還中も代理を描く。");

            for (int i = 0; i < 60 && rig.Driver.IsBusy; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.IsFalse(rig.Driver.IsBusy, "引き渡し完了。");
            Assert.IsFalse(rig.Proxy.ProxyShown, "代理を消す。");
            Assert.IsFalse(rig.ProxyBody.enabled);
            Assert.AreEqual(string.Empty, rig.Proxy.LabelText, "ラベルも消す。");
            Assert.IsFalse(rig.Normal.Suppressed, "表示の所有権を返す。");
            Assert.IsTrue(rig.NormalBody.enabled, "Down の通常表示へ戻る（無条件の表示ではなく状態に従う）。");
            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Down), rig.NormalBody.color);
            Assert.AreEqual(CompanionState.Down, rig.Actor.State, "探索のための回復は起きない。");
            Assert.AreEqual(hpBefore, rig.Receiver.CurrentHp, "戦闘 HP は探索で変わらない。");
            Assert.IsTrue(rig.Record.Record.IsInvestigated(point.PointId));
        }

        [Test]
        public void ProxyPresenter_AwayCompanion_RestoreKeepsNormalHidden()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            rig.Actor.ResetState(CompanionState.Away);
            rig.Proxy.Refresh();
            Assert.IsFalse(rig.NormalBody.enabled, "前提：退場中は描かない。");

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "加入済みなら退場中でも受理。");
            rig.Proxy.Refresh();
            Assert.IsTrue(rig.Proxy.ProxyShown);
            Assert.AreEqual(rig.Player.Position, rig.ProxyBody.transform.parent.position, "退場中は主人公の位置から代理が出る。");

            WalkProxyToArrival(rig);
            rig.Tick(InvestigateSeconds + 0.05f);
            for (int i = 0; i < 60 && rig.Driver.IsBusy; i++)
            {
                rig.Tick(0.05f);
            }

            Assert.IsFalse(rig.Proxy.ProxyShown);
            Assert.IsFalse(rig.Normal.Suppressed);
            Assert.IsFalse(rig.NormalBody.enabled, "もともと非表示（Away）なら、無条件に表示へ戻さない。");
            Assert.AreEqual(CompanionState.Away, rig.Actor.State);
        }

        [Test]
        public void ProxyPresenter_BodyMode_ShowsPhaseLabelOverTheActor_WithoutProxy()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            rig.Proxy.Refresh();

            Assert.AreEqual(InvestigationMode.Body, rig.Driver.Mode);
            Assert.IsFalse(rig.Proxy.ProxyShown, "本体が行くときは代理を出さない。");
            Assert.IsFalse(rig.Normal.Suppressed, "通常表示はそのまま（緑＝調査中の色）。");
            Assert.IsTrue(rig.NormalBody.enabled);
            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Investigate), rig.NormalBody.color);
            Assert.AreEqual("移動", rig.Proxy.LabelText, "本体でも進行段の文字を出す。");
            Assert.AreEqual(rig.Actor.WorldPosition.x, rig.Label.transform.parent.position.x, 1e-4f, "ラベルは本体の頭上。");

            rig.ArriveAt(point.ApproachPosition);
            rig.Tick(0.05f);
            Assert.AreEqual("調査中", rig.Proxy.LabelText);

            rig.Tick(InvestigateSeconds + 0.05f);
            Assert.IsFalse(rig.Driver.IsBusy, "本体は帰還段を持たず完了で追従へ。");
            Assert.AreEqual(string.Empty, rig.Proxy.LabelText);
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State);
        }

        [Test]
        public void ProxyPresenter_InterruptDuringProxy_ReturnsOwnershipImmediately()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            rig.Actor.ResetState(CompanionState.Down);

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            rig.Proxy.Refresh();
            Assert.IsTrue(rig.Proxy.ProxyShown);

            rig.Driver.NotifyCombatStart(); // 明示開始（P4-08R）と同じ経路。
            rig.Proxy.Refresh();

            Assert.IsFalse(rig.Driver.IsBusy);
            Assert.IsFalse(rig.Proxy.ProxyShown, "中断で代理を消す。");
            Assert.IsFalse(rig.Normal.Suppressed, "所有権を返す。");
            Assert.IsTrue(rig.NormalBody.enabled, "Down の表示へ戻る。");
            Assert.AreEqual(1, rig.Log.Interrupted);
        }

        // ================================================================
        // 通常表示：守護成立の短い表示（v1.0 §8.5。Protect は一瞬なので通知で持つ）
        // ================================================================

        [Test]
        public void PlaceholderPresenter_GuardianTransfer_FlashesProtectColor_ThenReturnsToStateColor()
        {
            Rig rig = MakeRig();
            rig.Normal.Suppressed = false;
            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Follow), rig.NormalBody.color, "前提：追従色。");

            rig.Guardian.Transfers.Publish(new GuardianTransferEvent(new HitId(1, 0), null, null, rig.Receiver, Vector3.zero));

            Assert.IsTrue(rig.Normal.IsProtectFlashing);
            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Protect), rig.NormalBody.color, "成立直後は守護色。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "状態は追従のまま（表示だけが持つ）。");

            rig.Normal.TickProtectFlash(0.2f);
            InvokePrivate(rig.Normal, "ApplyState");
            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Protect), rig.NormalBody.color, "時間内は保つ。");

            rig.Normal.TickProtectFlash(0.5f);
            InvokePrivate(rig.Normal, "ApplyState");
            Assert.IsFalse(rig.Normal.IsProtectFlashing);
            Assert.AreEqual(CompanionStateColors.Resolve(CompanionState.Follow), rig.NormalBody.color, "満了で状態色へ戻る。");
        }

        // ================================================================
        // 地点マーカー・短文 UI（§11「調べられる・移動中・調査中・発見・拒否理由・調査済み」）
        // ================================================================

        [Test]
        public void Marker_ShowsUnknown_Prompt_Investigating_Done()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            InvestigationPointMarker marker = MakeMarker(rig, point);
            rig.Player.Position = new Vector3(0f, 0f, 10f);

            marker.Refresh();
            Assert.AreEqual(InvestigationTexts.UnknownMark, marker.LabelText, "遠いうちは「？」。");
            Assert.IsNotNull(marker.Ring.sprite);

            marker.SetCandidate(true, InvestigationRejectReason.None, string.Empty);
            marker.Refresh();
            Assert.AreEqual(InvestigationTexts.Prompt("調べる"), marker.LabelText, "候補なら入力の案内。");
            Assert.IsTrue(marker.LabelText.Contains("E"), "割当（E／南ボタン）を含む。");

            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            marker.Refresh();
            Assert.IsTrue(marker.IsBeingInvestigated);
            Assert.AreEqual("調査中", marker.LabelText);

            rig.ArriveAt(point.ApproachPosition);
            rig.Tick(0.05f);
            rig.Tick(InvestigateSeconds + 0.05f);
            marker.SetCandidate(false, InvestigationRejectReason.None, string.Empty);
            marker.Refresh();
            Assert.IsFalse(marker.IsBeingInvestigated);
            Assert.AreEqual(InvestigationTexts.InvestigatedMark, marker.LabelText, "調査済みは「済」。");
        }

        [Test]
        public void Marker_UnrecruitedCandidate_ShowsHint()
        {
            Rig rig = MakeRig(default, recruited: false);
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            InvestigationPointMarker marker = MakeMarker(rig, point);

            marker.SetCandidate(true, InvestigationRejectReason.CompanionNotRecruited, point.MissingCompanionHint);
            marker.Refresh();

            Assert.AreEqual("犬がいれば何か見つかりそうだ", marker.LabelText, "未加入ヒントを地点の文で出す。");
        }

        [Test]
        public void PromptHud_ShowsPromptForTheCandidate_AndDistributesItToMarkers()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            CompanionInvestigationPoint near = MakePoint(rig, "point_near", new Vector3(0f, 0f, 1f));
            CompanionInvestigationPoint far = MakePoint(rig, "point_far", new Vector3(0f, 0f, 20f));
            InvestigationPointMarker nearMarker = MakeMarker(rig, near);
            InvestigationPointMarker farMarker = MakeMarker(rig, far);
            InvestigationPromptHud hud = MakeHud(rig, null, nearMarker, farMarker);
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);

            hud.Refresh();
            nearMarker.Refresh();
            farMarker.Refresh();

            Assert.AreEqual(InvestigationTexts.Prompt("調べる"), hud.PromptLine, "案内行は候補の Prompt。");
            Assert.AreEqual(InvestigationRejectReason.None, hud.LastPeekReason);
            Assert.AreEqual(InvestigationTexts.Prompt("調べる"), nearMarker.LabelText, "候補のマーカーだけに案内が出る。");
            Assert.AreEqual(InvestigationTexts.UnknownMark, farMarker.LabelText);
            Assert.AreEqual(string.Empty, hud.StartLine, "試遊段階の無い Scene では開始行を出さない。");

            rig.Player.Position = new Vector3(0f, 0f, 10f);
            hud.Refresh();
            Assert.AreEqual(string.Empty, hud.PromptLine, "対象が無ければ案内を出さない。");
        }

        [Test]
        public void PromptHud_ShowsTransientMessages_ForAcceptedRejectedCompletedInterrupted()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            InvestigationPromptHud hud = MakeHud(rig, null, MakeMarker(rig, point));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted);
            Assert.AreEqual(InvestigationTexts.Accepted, hud.MessageLine, "受理の短文。");

            rig.Coordinator.TryRequest(); // 実行中の再依頼 → 拒否。
            Assert.AreEqual(InvestigationTexts.Reject(InvestigationRejectReason.CompanionBusy, string.Empty), hud.MessageLine, "拒否理由の短文。");

            rig.ArriveAt(point.ApproachPosition);
            rig.Tick(0.05f);
            rig.Tick(InvestigateSeconds + 0.05f);
            Assert.AreEqual(InvestigationTexts.Completed, hud.MessageLine, "発見の短文。");

            hud.Tick(1.0f);
            Assert.AreEqual(InvestigationTexts.Completed, hud.MessageLine, "表示時間内は残る。");
            hud.Tick(1.5f);
            Assert.AreEqual(string.Empty, hud.MessageLine, "表示時間で消える。");

            // 中断の短文。
            Assert.IsTrue(rig.Coordinator.Peek(out _) == InvestigationRejectReason.AlreadyInvestigated, "前提：調査済み。");
            CompanionInvestigationPoint other = MakePoint(rig, "point_b", new Vector3(0f, 0f, 1.2f));
            rig.Player.Position = new Vector3(0f, 0f, 1.1f);
            InvestigationRequestResult result = rig.Coordinator.TryRequest();
            Assert.IsTrue(result.Accepted, "前提：別地点を受理（" + result.Reason + "）。");
            Assert.AreSame(other, result.Point);
            rig.Driver.NotifyCombatStart();
            Assert.AreEqual(InvestigationTexts.Interrupt(InvestigationInterruptReason.CombatStarted), hud.MessageLine, "中断理由の短文。");
        }

        [Test]
        public void PromptHud_UnrecruitedCandidate_ShowsHintOnPromptLine_AndRejectionUsesPointText()
        {
            Rig rig = MakeRig(default, recruited: false);
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            InvestigationPromptHud hud = MakeHud(rig, null, MakeMarker(rig, point));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);

            hud.Refresh();
            Assert.AreEqual(point.MissingCompanionHint, hud.PromptLine, "未加入は案内行にヒント。");

            rig.Coordinator.TryRequest();
            Assert.AreEqual(point.MissingCompanionHint, hud.MessageLine, "押しても同じヒント（記録・成功通知なし）。");
            Assert.AreEqual(0, rig.Log.Accepted);
            Assert.IsFalse(rig.Record.Record.IsInvestigated(point.PointId));
        }

        // ================================================================
        // 明示的な戦闘開始（§13.1）
        // ================================================================

        [Test]
        public void CombatStartInput_OnePress_RequestsOnce_AndHudDropsTheStartLine()
        {
            Rig rig = MakeRig(new Vector3(0f, 0f, -1f));
            CompanionInvestigationPoint point = MakePoint(rig, "point_a", new Vector3(0f, 0f, 1f));
            rig.Player.Position = new Vector3(0f, 0f, 0.5f);
            rig.Actor.ResetState(CompanionState.Down);

            var stageGo = new GameObject("TrialStage");
            _spawned.Add(stageGo);
            var stage = stageGo.AddComponent<TrialStageController>();
            stage.Bind(null, rig.Coordinator);
            InvestigationPromptHud hud = MakeHud(rig, stage, MakeMarker(rig, point));

            var startGo = new GameObject("TrialCombatStartInput");
            _spawned.Add(startGo);
            var start = startGo.AddComponent<TrialCombatStartInput>();
            start.Bind(stage);
            bool pressed = false;
            start.SetPressedSource(() => pressed);

            hud.Refresh();
            Assert.AreEqual(InvestigationTexts.CombatStartPrompt, hud.StartLine, "開始前は開始行を出す。");

            Assert.IsTrue(rig.Coordinator.TryRequest().Accepted, "前提：調査中（代理）。");
            rig.Proxy.Refresh();
            Assert.IsTrue(rig.Proxy.ProxyShown);

            Assert.IsFalse(start.TickInput(), "押していなければ何もしない。");
            pressed = true;
            Assert.IsTrue(start.TickInput(), "押下で戦闘開始を要求する。");
            Assert.IsTrue(stage.EncounterRequested);
            Assert.AreEqual(1, start.StartCount);
            rig.Proxy.Refresh();
            Assert.IsFalse(rig.Driver.IsBusy, "開始時に探索を解放する（§13.1）。");
            Assert.IsFalse(rig.Proxy.ProxyShown, "代理も消える。");

            Assert.IsFalse(start.TickInput(), "開始後の押下は無視（二重開始なし）。");
            Assert.AreEqual(1, start.StartCount);

            hud.Refresh();
            Assert.AreEqual(string.Empty, hud.StartLine, "開始後は開始行を消す。");
        }
    }
}
