using System.Collections;
using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Input;
using Momotaro.Presentation.Companion;
using Momotaro.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P4-08R：Builder が出荷する<b>実 Scene（SCN_Phase4_CompanionTrial）と実 Prefab（犬丸）</b>を読み込み、
    /// 探索 → 明示的な戦闘開始 → 4 Wave → Retry の通し受入を行う（v1.0 §13.1・§14.2。要求 P01〜P06・P09〜P11、R10）。
    ///
    /// 入力は<b>本物の経路</b>で入れる：Input System に仮想キーボードを足し、E／Enter の状態イベントを流す
    /// → IA_Momotaro の Interact → PlayerInputAdapter → PlayerInputState のラッチ → InvestigationInteractInput → 調停役。
    /// 常駐サービス（BootstrapRoot：GameMode・Input）は各テストで作り直し、終わったら消す（静的状態を後続へ残さない）。
    ///
    /// Scene は Build Settings に登録されている必要がある（Retry の再読込にも必要）。未登録なら Skip ではなく<b>失敗</b>にする
    /// （Skip は「検証していない」を意味するため。CLAUDE.md）。登録は Builder（build-companion-trial）が行う。
    /// </summary>
    public sealed class CompanionTrialScenePlayTests
    {
        private const string SceneName = "SCN_Phase4_CompanionTrial";
        private const int ExpectedTotalVirtue = 94;

        private static readonly Vector3 LeftPoint = new Vector3(-3.5f, 0f, -8f);
        private static readonly Vector3 WalledPoint = new Vector3(0f, 0f, -11f);

        private Keyboard _keyboard;
        private GameObject _bootstrap;
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private float _prevTimeScale;

        // ---- 実 Scene の主要参照 ----

        private sealed class Refs
        {
            public PlayerStateController Player;
            public PlayerRoot PlayerRoot;
            public PlayerVitalsHolder PlayerVitals;
            public PlayerProgressHolder Progress;
            public CompanionActor Actor;
            public CompanionStateArbiter States;
            public CompanionMovementArbiter Movement;
            public CompanionInvestigationController Driver;
            public CompanionHitReceiver Receiver;
            public CompanionPlaceholderPresenter Normal;
            public CompanionInvestigationProxyPresenter Proxy;
            public InvestigationCoordinator Coordinator;
            public InvestigationRecordHolder Record;
            public InvestigationInteractInput Input;
            public InvestigationPromptHud Hud;
            public TrialStageController Stage;
            public TrialCombatStartInput StartInput;
            public WaveRunner Waves;
            public CombatSessionController Session;
            public CombatOutcomeController Outcome;
            public List<CompanionInvestigationPoint> Points = new List<CompanionInvestigationPoint>();
            public List<InvestigationPointMarker> Markers = new List<InvestigationPointMarker>();
        }

        private sealed class EventLog : IInvestigationListener
        {
            public int Accepted, Rejected, Completed, Interrupted;
            public InvestigationRejectReason LastReject;
            public InvestigationInterruptReason LastInterrupt;

            public void OnInvestigationAccepted(in InvestigationAccepted e) => Accepted++;
            public void OnInvestigationRejected(in InvestigationRejected e) { Rejected++; LastReject = e.Reason; }
            public void OnInvestigationCompleted(in InvestigationCompleted e) => Completed++;
            public void OnInvestigationInterrupted(in InvestigationInterrupted e) { Interrupted++; LastInterrupt = e.Reason; }
        }

        private sealed class DamageLog : IHitResultListener
        {
            public readonly Dictionary<HitId, int> DamagePerHit = new Dictionary<HitId, int>();
            public int Results;

            public void OnHitResult(in HitResult result)
            {
                Results++;
                if (result.Kind == HitResultKind.Damage)
                {
                    DamagePerHit.TryGetValue(result.HitId, out int n);
                    DamagePerHit[result.HitId] = n + 1;
                }
            }
        }

        private sealed class FakeAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;
        }

        // ================================================================
        // 準備・後始末
        // ================================================================

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _prevTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();

            // 常駐サービスは自前で作り直す（前のテストが破棄・提供点を null にしていても、この経路で必ず動く）。
            if (BootstrapRoot.HasInstance)
            {
                Object.DestroyImmediate(BootstrapRoot.Instance.gameObject);
                yield return null;
            }

            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            _bootstrap = new GameObject("[BootstrapRoot:P4Trial]");
            _bootstrap.AddComponent<BootstrapRoot>();
            yield return null; // Start → RunBootstrap。
            Assert.IsTrue(BootstrapRoot.HasInstance && BootstrapRoot.Instance.BootstrapSucceeded, "常駐サービスが起動する。");
            Assert.IsNotNull(GameModeProvider.Current, "GameMode の提供点が入る。");
            Assert.IsNotNull(PlayerInputProvider.Current, "主人公入力の提供点が入る（IA_Momotaro が project-wide actions）。");

            _keyboard = InputSystem.AddDevice<Keyboard>("P4TrialKeyboard");
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Time.timeScale = _prevTimeScale;

            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _spawned.Clear();

            if (_keyboard != null)
            {
                InputSystem.RemoveDevice(_keyboard);
                _keyboard = null;
            }

            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && active.name == SceneName)
            {
                foreach (GameObject root in active.GetRootGameObjects())
                {
                    if (root != null)
                    {
                        Object.DestroyImmediate(root); // 遅延破棄だと次テストの 1 フレーム目まで残るため即時。
                    }
                }
            }

            if (_bootstrap != null)
            {
                Object.DestroyImmediate(_bootstrap); // Dispose で GameMode／Input の提供点が外れる。
                _bootstrap = null;
            }

            GameModeProvider.Current = null;
            PlayerInputProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
            yield return null;
        }

        // ---- 補助 ----

        private static IEnumerator LoadTrial()
        {
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(SceneName),
                "試遊 Scene が Build Settings に未登録です（" + SceneName + "）。Retry の再読込にも必要なので、"
                + "「Momotaro / Phase 4 / Generate Companion Trial」（ブリッジ build-companion-trial）で生成・登録してください。");
            yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
            yield return null; // Awake/OnEnable/Start。
            yield return null; // GameplaySceneMode → Exploration、入力 Map の切替、HUD の構築。
        }

        private static List<T> All<T>() where T : Object
        {
            return new List<T>(Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        private static T One<T>(string label) where T : Object
        {
            List<T> list = All<T>();
            Assert.AreEqual(1, list.Count, label + " は 1 つ（実際 " + list.Count + "）。");
            return list[0];
        }

        private static Refs Collect()
        {
            var r = new Refs
            {
                Player = One<PlayerStateController>("主人公"),
                PlayerVitals = One<PlayerVitalsHolder>("主人公 Vitals"),
                Progress = One<PlayerProgressHolder>("進行データ"),
                Actor = One<CompanionActor>("犬丸"),
                Coordinator = One<InvestigationCoordinator>("探索の調停役"),
                Record = One<InvestigationRecordHolder>("調査記録"),
                Input = One<InvestigationInteractInput>("探索の入力仲介"),
                Hud = One<InvestigationPromptHud>("探索の短文 UI"),
                Stage = One<TrialStageController>("試遊段階"),
                StartInput = One<TrialCombatStartInput>("戦闘開始の入力"),
                Waves = One<WaveRunner>("Wave"),
                Session = One<CombatSessionController>("戦闘 Session"),
                Outcome = One<CombatOutcomeController>("結果統合"),
            };
            r.PlayerRoot = r.Player.GetComponentInParent<PlayerRoot>() ?? One<PlayerRoot>("主人公ルート");
            r.States = r.Actor.GetComponent<CompanionStateArbiter>();
            r.Movement = r.Actor.GetComponent<CompanionMovementArbiter>();
            r.Driver = r.Actor.GetComponent<CompanionInvestigationController>();
            r.Receiver = r.Actor.GetComponent<CompanionHitReceiver>();
            r.Normal = r.Actor.GetComponent<CompanionPlaceholderPresenter>();
            r.Proxy = r.Actor.GetComponent<CompanionInvestigationProxyPresenter>();
            r.Points = All<CompanionInvestigationPoint>();
            r.Markers = All<InvestigationPointMarker>();
            Assert.IsNotNull(r.Driver, "実 Prefab に探索の駆動がある。");
            Assert.IsNotNull(r.Proxy, "実 Prefab に表示代理がある。");
            Assert.AreEqual(4, r.Points.Count, "地点は 4 つ（正常 2・壁 1・未加入 1）。");
            return r;
        }

        /// <summary>主人公を瞬間移動させる（Rigidbody と Transform の両方。追従の犬丸は自力で追いつく）。</summary>
        private static IEnumerator Teleport(Refs r, Vector3 position)
        {
            Rigidbody body = r.PlayerRoot != null ? r.PlayerRoot.Body : null;
            if (body != null)
            {
                body.position = position;
                body.linearVelocity = Vector3.zero;
            }

            r.PlayerRoot.transform.position = position;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        private IEnumerator Press(Key key, int holdFrames = 1)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            for (int i = 0; i < holdFrames; i++)
            {
                yield return null;
            }
        }

        private IEnumerator Release()
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, float timeoutRealtime, string what, System.Func<string> diagnose = null)
        {
            float deadline = Time.realtimeSinceStartup + timeoutRealtime;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                {
                    Assert.Fail("時間内に成立しなかった: " + what + (diagnose != null ? " " + diagnose() : string.Empty));
                }

                yield return null;
            }
        }

        private static string Diagnose(Refs r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(" mode=").Append(GameModeProvider.Current != null ? GameModeProvider.Current.Current.ToString() : "null");
            sb.Append(" session=").Append(r.Session.State).Append(" wave=").Append(r.Waves.CurrentWave);
            sb.Append(" player=").Append(r.PlayerRoot.transform.position).Append('/').Append(r.Player.Current);
            sb.Append(" hp=").Append(r.PlayerVitals.Vitals.Health.Current);
            sb.Append(" inumaru=").Append(r.Actor.WorldPosition).Append('/').Append(r.Actor.State);
            foreach (EnemyActor e in All<EnemyActor>())
            {
                sb.Append(" enemy[").Append(e.name).Append("]=").Append(e.transform.position).Append('/').Append(e.State)
                    .Append(" hp=").Append(e.CurrentHp);
            }

            sb.Append(" perception=").Append(PerceptionTargetRegistry.Count);
            return sb.ToString();
        }

        private static CompanionInvestigationPoint PointAt(Refs r, Vector3 position)
        {
            foreach (CompanionInvestigationPoint p in r.Points)
            {
                if ((p.Position - position).sqrMagnitude < 0.01f)
                {
                    return p;
                }
            }

            Assert.Fail("地点が見つからない: " + position);
            return null;
        }

        private static InvestigationPointMarker MarkerOf(Refs r, CompanionInvestigationPoint point)
        {
            foreach (InvestigationPointMarker m in r.Markers)
            {
                if (ReferenceEquals(m.Point, point))
                {
                    return m;
                }
            }

            Assert.Fail("マーカーが見つからない: " + point.name);
            return null;
        }

        /// <summary>主人公側（+Z）から地点を調べられる位置：地点から Interact 範囲内（1.5m）に立つ。</summary>
        private static Vector3 StandNear(Vector3 point) => point + new Vector3(0f, 0f, 1.3f);

        private GameObject SpawnWall(Vector3 center, Vector3 size, Vector3 forward)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TestWall";
            go.transform.position = center;
            go.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            go.transform.localScale = size;
            go.layer = CombatLayers.WallLayer >= 0 ? CombatLayers.WallLayer : 0;
            _spawned.Add(go);
            Physics.SyncTransforms();
            return go;
        }

        private void KnockDown(Refs r)
        {
            var attackerGo = new GameObject("FakeAttacker");
            _spawned.Add(attackerGo);
            var attacker = attackerGo.AddComponent<FakeAttacker>();
            var hit = new HitInfo(attacker, r.Receiver, Vector3.back, r.Actor.WorldPosition,
                new HitDamage(9999f, 0f, 0f), guardable: false, justGuardable: false, HitId.Single(777001));
            r.Receiver.ReceiveHit(hit);
        }

        private static void KillAllEnemies()
        {
            foreach (EnemyActor enemy in All<EnemyActor>())
            {
                if (enemy != null && !enemy.IsDefeated)
                {
                    enemy.ReceiveHit(new HitInfo(null, enemy, Vector3.forward, enemy.transform.position,
                        new HitDamage(9999f, 0f, 0f), guardable: false, justGuardable: false,
                        HitId.Single(900000 + enemy.GetInstanceID() % 90000)));
                }
            }
        }

        // ================================================================
        // P01／P09／R01：起動直後は自由探索
        // ================================================================

        [UnityTest]
        public IEnumerator Load_StartsInFreeExploration_WithoutEnemies_AndShowsPrompts()
        {
            yield return LoadTrial();
            Refs r = Collect();

            Assert.IsFalse(r.Stage.EncounterRequested, "起動直後は戦闘を始めていない。");
            Assert.AreEqual(0, All<EnemyActor>().Count, "敵はまだ生成しない（§13.1）。");
            Assert.IsTrue(CompanionActivityProvider.Activity.CanInvestigate, "開始前の Preparing は自由探索として供給される。");
            Assert.AreEqual(InvestigationTexts.CombatStartPrompt, r.Hud.StartLine, "開始操作の案内が出る。");
            Assert.AreEqual(0, r.Record.Record.Count, "調査済み記録は空。");
            Assert.IsFalse(r.Proxy.ProxyShown, "表示代理は出ていない。");
            Assert.IsTrue(r.Normal.Body != null && r.Normal.Body.enabled, "犬丸の通常表示が見える。");

            foreach (InvestigationPointMarker marker in r.Markers)
            {
                Assert.IsNotNull(marker.Ring.sprite, marker.name + "：輪が描ける。");
                Assert.IsFalse(string.IsNullOrEmpty(marker.LabelText), marker.name + "：文字が出ている。");
            }

            Assert.AreEqual(4, InvestigationPointRegistry.Count, "地点 4 つがレジストリに登録される。");
            Assert.AreEqual(0, r.Actor.IllegalTransitionCount);
        }

        // ================================================================
        // P01／P03：実キーボード E → 1 押下 1 依頼 → 実 Prefab の犬丸が調べて完了
        // ================================================================

        [UnityTest]
        public IEnumerator RealKeyE_OnePressOneRequest_AndTheRealCompanionCompletesTheInvestigation()
        {
            yield return LoadTrial();
            Refs r = Collect();
            var log = new EventLog();
            r.Coordinator.Events.AddListener(log);
            CompanionInvestigationPoint point = PointAt(r, LeftPoint);
            InvestigationPointMarker marker = MarkerOf(r, point);

            yield return Teleport(r, StandNear(LeftPoint));
            yield return WaitUntil(() => r.Coordinator.Peek(out IInvestigationPoint p) == InvestigationRejectReason.None && ReferenceEquals(p, point),
                3f, "左の地点が候補になる（追従の犬丸が追いついて主人公が Idle に戻る）");
            Assert.AreEqual(InvestigationTexts.Prompt(point.Prompt), r.Hud.PromptLine, "案内行に「調べる」。");

            // 1 押下。
            yield return Press(Key.E, holdFrames: 2);
            Assert.AreEqual(1, log.Accepted, "実キー E 1 押下で依頼 1 回。");
            Assert.AreEqual(1, r.Input.RequestCount);
            Assert.IsTrue(r.Driver.IsBusy);
            Assert.AreEqual(InvestigationMode.Body, r.Driver.Mode, "平常時は本体が調べに行く。");
            Assert.AreEqual(CompanionState.Investigate, r.Actor.State);
            Assert.AreEqual(InvestigationTexts.Accepted, r.Hud.MessageLine, "受理の短文。");

            // 押しっぱなし：再依頼も拒否通知も出ない。Step 等の二重起動も無い。
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(1, log.Accepted, "保持では再実行しない。");
            Assert.AreEqual(0, log.Rejected, "保持で拒否通知も出ない。");
            Assert.AreEqual(1, r.Input.RequestCount);
            Assert.AreNotEqual(PlayerState.Step, r.Player.Current, "Interact から Step を起動しない。");
            yield return Release();

            // 実 Motor で調査位置へ歩き、約 1 秒調べて完了する。
            yield return WaitUntil(() => log.Completed == 1, 8f, "調査の完了（実 Motor の移動＋調査 1 秒）");
            Assert.AreEqual(0, log.Interrupted);
            Assert.IsTrue(r.Record.Record.IsInvestigated(point.PointId), "Scene 記録に調査済み。");
            Assert.AreEqual(InvestigationTexts.Completed, r.Hud.MessageLine, "発見の短文。");
            yield return null;
            Assert.AreEqual(InvestigationTexts.InvestigatedMark, marker.LabelText, "マーカーが「済」。");
            Assert.IsFalse(r.Driver.IsBusy);
            Assert.AreNotEqual(CompanionState.Investigate, r.Actor.State, "本体は探索状態を抜ける。");
            Assert.AreNotEqual(CompanionMovementOwner.Investigate, r.Movement.Owner, "移動の所有権を返す。");
            Assert.AreEqual(0, r.Actor.IllegalTransitionCount);

            // 調査済み地点をもう一度：調査済み文言、記録・成功通知の重複なし。
            yield return Press(Key.E, holdFrames: 2);
            yield return Release();
            Assert.AreEqual(1, log.Completed, "成功通知は 1 回だけ。");
            Assert.AreEqual(1, log.Rejected, "2 回目は拒否通知。");
            Assert.AreEqual(InvestigationRejectReason.AlreadyInvestigated, log.LastReject);
            Assert.AreEqual(point.CompletedText, r.Hud.MessageLine, "調査済み文言。");
            r.Coordinator.Events.RemoveListener(log);
        }

        // ================================================================
        // P02：実壁。受付前は到達不可として拒否、受付後は壁抜けせず中断、次の依頼を受付可能
        // ================================================================

        [UnityTest]
        public IEnumerator RealWall_RejectsBeforeAcceptance_AndInterruptsAfterAcceptance_WithoutPassingThrough()
        {
            yield return LoadTrial();
            Refs r = Collect();
            var log = new EventLog();
            r.Coordinator.Events.AddListener(log);

            // 1. Builder が置いた壁の向こうの地点：範囲内に立てるが、直線が壁で遮られる → 受付前に拒否。
            CompanionInvestigationPoint walled = PointAt(r, WalledPoint);
            yield return Teleport(r, WalledPoint + new Vector3(0f, 0f, 1.4f));
            yield return WaitUntil(() => r.Player.Current == PlayerState.Idle || r.Player.Current == PlayerState.Move, 2f, "主人公が操作可能");
            yield return null;

            InvestigationRejectReason peek = r.Coordinator.Peek(out IInvestigationPoint candidate);
            Assert.AreSame(walled, candidate, "壁の地点が文脈上の候補。");
            Assert.AreEqual(InvestigationRejectReason.Unreachable, peek, "実壁で到達不可。");

            InvestigationRequestResult rejected = r.Coordinator.TryRequest();
            Assert.IsFalse(rejected.Accepted);
            Assert.AreEqual(InvestigationRejectReason.Unreachable, rejected.Reason);
            Assert.AreEqual(1, log.Rejected);
            Assert.IsFalse(r.Driver.IsBusy, "予約しない。");
            Assert.AreEqual(InvestigationTexts.Reject(InvestigationRejectReason.Unreachable, string.Empty), r.Hud.MessageLine);

            // 2. 正常地点を受理させ、受理後に犬丸と地点の間へ実壁を置く → 壁抜けせずタイムアウトで中断。
            CompanionInvestigationPoint left = PointAt(r, LeftPoint);
            yield return Teleport(r, StandNear(LeftPoint));
            yield return WaitUntil(() => r.Coordinator.Peek(out IInvestigationPoint p) == InvestigationRejectReason.None && ReferenceEquals(p, left),
                3f, "左の地点が候補になる");

            InvestigationRequestResult accepted = r.Coordinator.TryRequest();
            Assert.IsTrue(accepted.Accepted, "前提：受理（" + accepted.Reason + "）。");
            Assert.AreEqual(InvestigationMode.Body, r.Driver.Mode);

            Vector3 from = r.Actor.WorldPosition;
            Vector3 to = left.ApproachPosition;
            Vector3 dir = to - from;
            dir.y = 0f;
            Assert.Greater(dir.magnitude, 0.6f, "前提：犬丸は地点から離れている。");
            Vector3 wallCenter = from + dir.normalized * Mathf.Min(0.6f, dir.magnitude * 0.5f) + Vector3.up * 0.5f;
            SpawnWall(wallCenter, new Vector3(4f, 1f, 0.3f), dir.normalized);

            yield return WaitUntil(() => log.Interrupted >= 1 || log.Completed >= 1, 6f, "受理後の実壁で中断（移動タイムアウト 3 秒）");
            Assert.AreEqual(0, log.Completed, "壁越しに完了しない。");
            Assert.AreEqual(1, log.Interrupted);
            Assert.IsTrue(
                log.LastInterrupt == InvestigationInterruptReason.MoveTimeout || log.LastInterrupt == InvestigationInterruptReason.Blocked,
                "理由は移動タイムアウトか遮断（実際: " + log.LastInterrupt + "）。");
            Vector3 companionOffset = r.Actor.WorldPosition - wallCenter;
            companionOffset.y = 0f;
            Assert.Less(Vector3.Dot(companionOffset, dir.normalized), 0f, "犬丸は壁の手前側に居る（壁抜けしない）。");
            Assert.IsFalse(r.Driver.IsBusy, "所有権とロックを残さない。");
            Assert.AreNotEqual(CompanionMovementOwner.Investigate, r.Movement.Owner);
            Assert.IsFalse(r.Record.Record.IsInvestigated(left.PointId), "未完了のまま。");

            // 3. 壁を除けば次の依頼を受け付ける。
            foreach (GameObject go in _spawned)
            {
                if (go != null && go.name == "TestWall")
                {
                    Object.DestroyImmediate(go);
                }
            }

            Physics.SyncTransforms();
            yield return null;
            yield return WaitUntil(() => r.Coordinator.Peek(out IInvestigationPoint p) == InvestigationRejectReason.None && ReferenceEquals(p, left),
                3f, "再依頼が可能になる");
            InvestigationRequestResult again = r.Coordinator.TryRequest();
            Assert.IsTrue(again.Accepted, "次の依頼を受け付ける（" + again.Reason + "）。");
            Assert.AreEqual(2, log.Accepted);
            Assert.AreEqual(0, r.Actor.IllegalTransitionCount);
            r.Coordinator.Events.RemoveListener(log);
        }

        // ================================================================
        // P04／E03：HP0 の犬丸が表示代理で調べ、戦闘 HP は回復しない
        // ================================================================

        [UnityTest]
        public IEnumerator DownedCompanion_InvestigatesByProxy_WithoutHealing_AndRestoresDisplayOwnership()
        {
            yield return LoadTrial();
            Refs r = Collect();
            var log = new EventLog();
            r.Coordinator.Events.AddListener(log);
            CompanionInvestigationPoint point = PointAt(r, LeftPoint);

            KnockDown(r);
            yield return null;
            Assert.AreEqual(0, r.Receiver.CurrentHp, "前提：HP 0。");
            Assert.AreEqual(CompanionState.Down, r.Actor.State, "前提：ダウン。");

            yield return Teleport(r, StandNear(LeftPoint));
            yield return WaitUntil(() => r.Coordinator.Peek(out IInvestigationPoint p) == InvestigationRejectReason.None && ReferenceEquals(p, point),
                3f, "左の地点が候補になる（ダウン中でも加入済みなら受理できる）");

            InvestigationRequestResult result = r.Coordinator.TryRequest();
            Assert.IsTrue(result.Accepted, "加入済みならダウン中でも受理（" + result.Reason + "）。");
            Assert.AreEqual(InvestigationMode.Proxy, r.Driver.Mode, "本体を起こさず表示代理で調べる。");
            Assert.AreEqual(CompanionState.Down, r.Actor.State, "戦闘 Actor は Down のまま。");
            yield return null;
            Assert.IsTrue(r.Proxy.ProxyShown, "表示代理が描かれる。");
            Assert.IsTrue(r.Normal.Suppressed, "通常表示は抑制。");
            Assert.IsFalse(r.Normal.Body.enabled, "同時に 2 体描かない。");
            Assert.IsFalse(string.IsNullOrEmpty(r.Proxy.LabelText), "進行段の文字が出る。");

            yield return WaitUntil(() => log.Completed == 1, 8f, "表示代理の調査完了");
            Assert.AreEqual(0, r.Receiver.CurrentHp, "完了しても戦闘 HP は回復しない。");
            Assert.IsTrue(r.Record.Record.IsInvestigated(point.PointId));

            yield return WaitUntil(() => !r.Driver.IsBusy, 5f, "帰還して引き渡し完了");
            yield return null;
            Assert.IsFalse(r.Proxy.ProxyShown, "代理を消す。");
            Assert.IsFalse(r.Normal.Suppressed, "表示の所有権を返す。");
            Assert.AreEqual(0, r.Receiver.CurrentHp, "探索のための回復は起きない。");
            Assert.IsTrue(r.Actor.State == CompanionState.Down || r.Actor.State == CompanionState.Recovering,
                "戦闘状態は探索と独立に進む（実際: " + r.Actor.State + "）。");
            Assert.AreEqual(0, r.Actor.IllegalTransitionCount);
            r.Coordinator.Events.RemoveListener(log);
        }

        // ================================================================
        // P05：調査途中に実キー Enter で戦闘開始 → 依頼・代理・所有権を解放して戦闘へ
        // ================================================================

        [UnityTest]
        public IEnumerator RealKeyEnter_DuringInvestigation_StartsCombat_AndReleasesTheInvestigation()
        {
            yield return LoadTrial();
            Refs r = Collect();
            var log = new EventLog();
            r.Coordinator.Events.AddListener(log);
            CompanionInvestigationPoint point = PointAt(r, LeftPoint);

            yield return Teleport(r, StandNear(LeftPoint));
            yield return WaitUntil(() => r.Coordinator.Peek(out IInvestigationPoint p) == InvestigationRejectReason.None && ReferenceEquals(p, point),
                3f, "左の地点が候補になる");
            Assert.IsTrue(r.Coordinator.TryRequest().Accepted, "前提：受理。");
            Assert.AreEqual(CompanionState.Investigate, r.Actor.State);
            yield return null;

            yield return Press(Key.Enter, holdFrames: 2);
            yield return Release();

            Assert.IsTrue(r.Stage.EncounterRequested, "実キー Enter で戦闘開始を要求。");
            Assert.AreEqual(1, r.StartInput.StartCount);
            Assert.AreEqual(1, log.Interrupted, "探索は開始時に同期的に中断。");
            Assert.AreEqual(InvestigationInterruptReason.CombatStarted, log.LastInterrupt);
            Assert.IsFalse(r.Driver.IsBusy);
            Assert.AreNotEqual(CompanionState.Investigate, r.Actor.State, "探索状態を抜ける。");
            Assert.AreNotEqual(CompanionMovementOwner.Investigate, r.Movement.Owner, "移動の所有権を返す。");
            Assert.AreNotEqual(CompanionActionOwner.Investigate, r.States.CurrentOwner, "行動の所有権を返す。");
            Assert.IsFalse(r.Proxy.ProxyShown);
            Assert.IsFalse(r.Record.Record.IsInvestigated(point.PointId), "未完了のまま。");
            Assert.IsFalse(CompanionActivityProvider.Activity.CanInvestigate, "以後は戦闘中として供給される。");
            Assert.AreEqual(string.Empty, r.Hud.StartLine, "開始行が消える。");

            yield return WaitUntil(() => All<EnemyActor>().Count > 0, 5f, "Wave1 の敵生成");
            Assert.AreEqual(1, r.Waves.CurrentWave, "Wave1。");
            Assert.AreEqual(InvestigationRejectReason.InCombat, r.Coordinator.Peek(out _), "戦闘中は依頼を拒否。");

            // 2 回目の Enter は無視（二重開始しない）。
            yield return Press(Key.Enter, holdFrames: 2);
            yield return Release();
            Assert.AreEqual(1, r.StartInput.StartCount);
            Assert.AreEqual(0, r.Actor.IllegalTransitionCount);
            r.Coordinator.Events.RemoveListener(log);
        }

        // ================================================================
        // P06：実 Prefab（防御＋守護）と実 Hitbox（Wave1 の敵の攻撃）。同一 HitId の重複被害なし、不正遷移なし
        // ================================================================

        [UnityTest]
        public IEnumerator RealCombat_WithRealEnemyHitboxes_NoDuplicateDamagePerHit_NoIllegalTransitions()
        {
            yield return LoadTrial();
            Refs r = Collect();
            var companionLog = new DamageLog();
            var playerLog = new DamageLog();
            r.Receiver.Results.AddListener(companionLog);
            r.PlayerVitals.Results.AddListener(playerLog);
            int playerHpStart = r.PlayerVitals.Vitals.Health.Current;

            // 犬丸の攻撃だけ止める（Wave1 の近接 1 体は犬丸が先に倒してしまい、敵の攻撃が一度も出ないことがある）。
            // 追従・防御（Guard／Evade）・守護（転送）・被弾は実機のまま。検証したいのは被害側の重複と行動の排他。
            var companionAttack = r.Actor.GetComponent<CompanionCombatController>();
            Assert.IsNotNull(companionAttack);
            companionAttack.enabled = false;

            Assert.IsTrue(r.Stage.RequestCombatStart(), "戦闘開始。");
            yield return WaitUntil(() => All<EnemyActor>().Count > 0, 5f, "Wave1 の敵生成");

            // 主人公を湧き位置（SpawnPoint_0＝(0,0,4)）の近くへ置き、敵の索敵範囲に入れる。以後は無操作で立ち、
            // 敵の実攻撃（実 Hitbox）を受ける。犬丸は追従で隣に来て、防御・守護を実機どおり判断する。
            yield return Teleport(r, new Vector3(0f, 0f, 1.5f));
            yield return null;

            // 実キー J（Attack）を 1 回。攻撃音（半径 4m）で敵が気付き、接近して実攻撃を出す（試遊で人がやる手順と同じ）。
            yield return Press(Key.J, holdFrames: 2);
            yield return Release();
            yield return WaitUntil(() => companionLog.Results + playerLog.Results >= 1, 20f,
                "敵の実攻撃が主人公か犬丸に届く。", () => Diagnose(r));
            float until = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < until)
            {
                Assert.AreEqual(0, r.Actor.IllegalTransitionCount, "同時行動・不正遷移なし。");
                yield return null;
            }

            foreach (KeyValuePair<HitId, int> kv in companionLog.DamagePerHit)
            {
                Assert.AreEqual(1, kv.Value, "犬丸：同一 HitId の被害は 1 回（転送と直撃の重複なし）: " + kv.Key);
            }

            foreach (KeyValuePair<HitId, int> kv in playerLog.DamagePerHit)
            {
                Assert.AreEqual(1, kv.Value, "主人公：同一 HitId の被害は 1 回: " + kv.Key);
                Assert.IsFalse(companionLog.DamagePerHit.ContainsKey(kv.Key), "同じ命中が主人公と犬丸の両方へ被害を出さない: " + kv.Key);
            }

            Assert.GreaterOrEqual(companionLog.Results + playerLog.Results, 1);
            Assert.AreEqual(0, r.Actor.IllegalTransitionCount);
            Assert.IsTrue(r.PlayerVitals.Vitals.Health.Current <= playerHpStart, "主人公 HP は増えない。");
            r.Receiver.Results.RemoveListener(companionLog);
            r.PlayerVitals.Results.RemoveListener(playerLog);
        }

        // ================================================================
        // P10／P09：調査 → 明示開始 → 4 Wave → 徳 94 → Retry → 自由探索へ戻り、調査済みも徳も残らない
        // ================================================================

        [UnityTest]
        public IEnumerator FourWaves_Victory94_Retry_ReturnsToFreeExploration_WithoutResidue()
        {
            yield return LoadTrial();
            Refs r = Collect();
            CompanionInvestigationPoint point = PointAt(r, LeftPoint);
            TrialStageController firstStage = r.Stage;

            // 調査を 1 件済ませる（Retry で消えることを確かめるため）。
            yield return Teleport(r, StandNear(LeftPoint));
            yield return WaitUntil(() => r.Coordinator.Peek(out IInvestigationPoint p) == InvestigationRejectReason.None && ReferenceEquals(p, point),
                3f, "左の地点が候補になる");
            Assert.IsTrue(r.Coordinator.TryRequest().Accepted);
            yield return WaitUntil(() => r.Record.Record.IsInvestigated(point.PointId), 8f, "調査完了");
            Assert.AreEqual(1, r.Record.Record.Count);

            // 明示開始 → 4 Wave（敵は湧いた直後に倒す。Wave 間の待ち時間は Data のまま）。
            Time.timeScale = 3f;
            Assert.IsTrue(r.Stage.RequestCombatStart());
            yield return WaitUntil(() =>
            {
                if (r.Session.State == CombatSessionState.Victory)
                {
                    return true;
                }

                KillAllEnemies();
                return false;
            }, 60f, "4 Wave を通して勝利");
            Time.timeScale = 1f;

            Assert.AreEqual(CombatSessionState.Victory, r.Session.State);
            Assert.AreEqual(4, r.Waves.CurrentWave, "4 Wave。");
            Assert.AreEqual(ExpectedTotalVirtue, r.Progress.Virtue, "徳の合計は 94（W1 近接1／W2 遠距離1／W3 近接2＋遠距離1／W4 強敵1）。");
            Assert.AreEqual(0, r.Actor.IllegalTransitionCount);

            // Retry → Scene 再読込 → 自由探索から。
            yield return WaitUntil(() => r.Outcome.RetryArmed, 10f, "Retry が受け付け可能になる");
            r.Outcome.RequestRetry();
            yield return WaitUntil(() =>
            {
                List<TrialStageController> stages = All<TrialStageController>();
                return stages.Count == 1 && !ReferenceEquals(stages[0], firstStage);
            }, 10f, "Scene の再読込");
            yield return null;
            yield return null;

            Refs r2 = Collect();
            Assert.IsFalse(r2.Stage.EncounterRequested, "再読込後は自由探索から。");
            Assert.AreEqual(0, All<EnemyActor>().Count, "敵は居ない。");
            Assert.AreEqual(0, r2.Record.Record.Count, "調査済みは残らない（Scene 単位の記録）。");
            Assert.AreEqual(0, r2.Progress.Virtue, "徳は Retry でリセット。");
            Assert.IsFalse(r2.Proxy.ProxyShown, "表示代理の残留なし。");
            Assert.AreEqual(InvestigationTexts.CombatStartPrompt, r2.Hud.StartLine);
            Assert.AreEqual(4, InvestigationPointRegistry.Count, "地点の登録は新 Scene の 4 つだけ（旧 Scene の残留なし）。");
            Assert.AreEqual(InvestigationTexts.UnknownMark, MarkerOf(r2, PointAt(r2, LeftPoint)).LabelText, "調査済みだった地点も「？」に戻る。");
        }

        // ================================================================
        // P09／P11／R10：再読込 3 回で犬丸・駆動・購読・UI が各 1、代理・調査済み・徳の残留なし
        // ================================================================

        [UnityTest]
        public IEnumerator Reload3Times_KeepsSingletons_AndLeavesNoResidue()
        {
            for (int i = 0; i < 3; i++)
            {
                int pass = i + 1;
                yield return LoadTrial();
                Refs r = Collect();

                Assert.AreEqual(1, All<CompanionActor>().Count, pass + " 回目：犬丸は 1 体。");
                Assert.AreEqual(1, All<CompanionInvestigationController>().Count, pass + " 回目：探索の駆動は 1 つ。");
                Assert.AreEqual(1, All<CompanionInvestigationProxyPresenter>().Count, pass + " 回目：表示代理は 1 つ。");
                Assert.AreEqual(1, All<InvestigationInteractInput>().Count, pass + " 回目：入力仲介は 1 つ。");
                Assert.AreEqual(1, All<InvestigationPromptHud>().Count, pass + " 回目：短文 UI は 1 つ。");
                Assert.AreEqual(1, All<TrialCombatStartInput>().Count, pass + " 回目：開始入力は 1 つ。");
                Assert.AreEqual(r.Markers.Count + 1, r.Coordinator.Events.ListenerCount,
                    pass + " 回目：通知の購読はマーカー＋UI の分だけ（累積なし）。");
                Assert.AreEqual(4, InvestigationPointRegistry.Count, pass + " 回目：地点レジストリは 4。");
                Assert.AreEqual(0, r.Record.Record.Count, pass + " 回目：調査済みの残留なし。");
                Assert.AreEqual(0, r.Progress.Virtue, pass + " 回目：徳の残留なし。");
                Assert.IsFalse(r.Proxy.ProxyShown, pass + " 回目：代理の残留なし。");
                Assert.IsFalse(r.Stage.EncounterRequested, pass + " 回目：自由探索から。");
                Assert.AreEqual(1f, Time.timeScale, pass + " 回目：timeScale の残留なし。");

                // 次周で消えることを確かめるため、調査済みと徳を作っておく。
                CompanionInvestigationPoint point = PointAt(r, LeftPoint);
                Assert.IsTrue(r.Record.Record.TryMarkInvestigated(point.PointId));
                r.Progress.Grant(new RewardSnapshot(new Momotaro.Core.Identification.StableId("reward_reload_probe"), 7, default, false), out _);
                Assert.AreEqual(7, r.Progress.Virtue);
            }
        }
    }
}
