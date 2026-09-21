using System.Collections.Generic;
using System.Reflection;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Scenes;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 仲間の活動許可（P4-FIX F05）を固定する。
    ///
    /// 押さえたい間違いは 2 つある。
    ///
    /// 1 つは <b><c>GameMode == Exploration</c> を「非戦闘」と読むこと</b>。Wave の幕間も、開始待ちの Encounter も
    /// 戦闘中であり、そこで探索を許すと戦闘の最中に犬丸が調べに行ってしまう（N16）。
    ///
    /// もう 1 つは <b>未配線を黙って通すこと</b>。「分からないから許可」にすると、配線漏れが実機でしか露見しない。
    /// 停止していればすぐ気付く（N17）。
    /// </summary>
    public sealed class CompanionActivityContextTests : CompanionActivityFixture
    {
        private readonly List<Object> _spawned = new List<Object>();
        private IGameModeService _originalModes;
        private ICompanionActivitySource _originalSource;

        [SetUp]
        public void SetUp()
        {
            _originalModes = GameModeProvider.Current;
            _originalSource = CompanionActivityProvider.Current;
        }

        [TearDown]
        public void TearDown()
        {
            GameModeProvider.Current = _originalModes;
            CompanionActivityProvider.Current = _originalSource;

            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        /// <summary>モードの正本役（このテストが読むのは <see cref="Current"/> だけ）。</summary>
        private sealed class FakeModes : IGameModeService
        {
            public GameMode Current { get; set; } = GameMode.Exploration;
            public bool CanPause => true;

#pragma warning disable 67 // このテストでは発火しない。
            public event System.Action<GameModeChanged> ModeChanged;
#pragma warning restore 67

            public bool ChangeMode(GameMode next)
            {
                Current = next;
                return true;
            }

            public void AddListener(IGameModeListener listener)
            {
            }

            public void RemoveListener(IGameModeListener listener)
            {
            }
        }

        // ---- 純粋ロジック（GameMode × セッション状態） ----

        [Test]
        public void Exploration_WithoutEncounter_IsFreeRoam()
        {
            CompanionActivity activity = CompanionActivityResolver.Resolve(GameMode.Exploration, null);

            Assert.IsTrue(activity.CanAct);
            Assert.IsTrue(activity.ClocksRun);
            Assert.IsFalse(activity.EncounterActive);
            Assert.IsTrue(activity.CanInvestigate, "自由探索区画では探索できる。");
        }

        /// <summary>
        /// N16：Wave の幕間も、開始待ちの Encounter も戦闘中。<c>GameMode</c> が Exploration でも探索は不可。
        /// </summary>
        [Test]
        public void IntermissionAndPendingEncounter_DisallowInvestigation()
        {
            CompanionActivity intermission =
                CompanionActivityResolver.Resolve(GameMode.Exploration, CombatSessionState.Intermission);
            CompanionActivity preparing =
                CompanionActivityResolver.Resolve(GameMode.Exploration, CombatSessionState.Preparing);
            CompanionActivity playing =
                CompanionActivityResolver.Resolve(GameMode.Exploration, CombatSessionState.Playing);

            Assert.IsFalse(intermission.CanInvestigate, "Wave 幕間は戦闘中。探索できない。");
            Assert.IsTrue(intermission.EncounterActive);

            Assert.IsFalse(preparing.CanInvestigate, "開始待ちの Encounter も戦闘中。");
            Assert.IsTrue(preparing.EncounterActive);

            Assert.IsFalse(playing.CanInvestigate);
            Assert.IsTrue(playing.EncounterActive);

            // 戦闘そのものは止めない。止めるのは探索だけ。
            Assert.IsTrue(intermission.CanAct);
            Assert.IsTrue(intermission.ClocksRun);
        }

        [Test]
        public void FinishedEncounter_ReturnsToFreeRoam()
        {
            foreach (CombatSessionState state in new[]
                     {
                         CombatSessionState.Victory, CombatSessionState.Defeat, CombatSessionState.Reloading,
                     })
            {
                CompanionActivity activity = CompanionActivityResolver.Resolve(GameMode.Exploration, state);
                Assert.IsFalse(activity.EncounterActive, state + " は戦闘継続中ではない。");
                Assert.IsFalse(activity.CanInvestigate, state + " は結果画面（入力不可）なので探索も受け付けない（v1.0 §7.1）。");
                Assert.IsTrue(activity.ClocksRun, state + " でも時計は止めない（Pause ではない）。");
            }
        }

        /// <summary>
        /// 明示開始の門（P4-08R）：要求前の Preparing は「開始待ちの Encounter」ではなく自由探索として供給する。
        /// 要求後の Preparing は従来どおり戦闘中。門が無い Scene（3.5 の自動開始）は従来どおり。
        /// </summary>
        [Test]
        public void StartGate_MakesPreparingFreeRoam_UntilCombatIsRequested()
        {
            GameModeProvider.Current = new FakeModes { Current = GameMode.Exploration };

            var sessionGo = new GameObject("Session");
            _spawned.Add(sessionGo);
            var session = sessionGo.AddComponent<CombatSessionController>();
            Assert.AreEqual(CombatSessionState.Preparing, session.State, "前提：Session は Preparing から始まる。");

            var go = new GameObject("ActivityContext");
            _spawned.Add(go);
            var context = go.AddComponent<CompanionActivityContext>();
            context.Bind(session);

            Assert.IsFalse(context.Current.CanInvestigate, "門が無ければ Preparing は開始待ちの Encounter＝戦闘中。");

            var gate = new FakeGate { EncounterRequested = false };
            context.SetEncounterStartGate(gate);
            Assert.IsTrue(context.Current.CanInvestigate, "要求前は自由探索。");
            Assert.IsFalse(context.Current.EncounterActive);

            gate.EncounterRequested = true;
            Assert.IsFalse(context.Current.CanInvestigate, "要求後の Preparing は戦闘中（開始待ち）。");
            Assert.IsTrue(context.Current.EncounterActive);
        }

        private sealed class FakeGate : IEncounterStartGate
        {
            public bool EncounterRequested { get; set; }
        }

        [Test]
        public void Paused_FreezesButKeepsOngoingAction()
        {
            CompanionActivity activity =
                CompanionActivityResolver.Resolve(GameMode.Paused, CombatSessionState.Playing);

            Assert.IsFalse(activity.ClocksRun, "時計は止まる。");
            Assert.IsFalse(activity.CanAct, "新しい行動は始めない。");
            Assert.IsFalse(activity.DiscardOngoing,
                "Pause は凍結であって破棄ではない（復帰時に同じ段から続ける）。");
            Assert.IsTrue(activity.EncounterActive, "Pause しても戦闘は終わっていない。");
        }

        [Test]
        public void DialogueAndEvent_DiscardOngoingAction()
        {
            foreach (GameMode mode in new[] { GameMode.Dialogue, GameMode.Event, GameMode.GameOver })
            {
                CompanionActivity activity = CompanionActivityResolver.Resolve(mode, CombatSessionState.Playing);
                Assert.IsFalse(activity.ClocksRun, mode + " では時計を止める。");
                Assert.IsTrue(activity.DiscardOngoing, mode + " では古い行動を捨てる。");
            }
        }

        [Test]
        public void Combat_IsFightingRegardlessOfSession()
        {
            CompanionActivity activity = CompanionActivityResolver.Resolve(GameMode.Combat, null);

            Assert.IsTrue(activity.CanAct);
            Assert.IsTrue(activity.EncounterActive);
            Assert.IsFalse(activity.CanInvestigate);
        }

        // ---- 未配線（N17） ----

        [Test]
        public void MissingContext_DisallowsActions()
        {
            var go = new GameObject("ActivityContext");
            _spawned.Add(go);
            var context = go.AddComponent<CompanionActivityContext>();

            // (a) モードの正本が居ない。
            GameModeProvider.Current = null;
            CompanionActivity noModes = context.Current;
            Assert.IsFalse(noModes.CanAct, "モードの正本が未初期化なら通さない。");
            Assert.IsFalse(noModes.ClocksRun);

            // (b) モードは居るが、戦闘セッションを繋ぎ忘れている。
            //     「セッションが無い区画」なのか「繋ぎ忘れ」なのかを区別できないので通さない。
            GameModeProvider.Current = new FakeModes { Current = GameMode.Exploration };
            CompanionActivity noSession = context.Current;
            Assert.IsFalse(noSession.CanAct, "セッション未配線を黙って許可しない。");
            Assert.IsFalse(noSession.CanInvestigate);

            // (c) 「この区画に Encounter は無い」と明示すれば通る。
            context.MarkAreaWithoutEncounter();
            CompanionActivity declared = context.Current;
            Assert.IsTrue(declared.CanAct, "無いことを明示したなら動いてよい。");
            Assert.IsTrue(declared.CanInvestigate);
            Assert.IsFalse(declared.EncounterActive);
        }

        [Test]
        public void UnknownMode_IsStopped()
        {
            CompanionActivity activity = CompanionActivityResolver.Resolve((GameMode)999, null);

            Assert.IsFalse(activity.CanAct, "知らないモードを黙って戦闘扱いにしない。");
            Assert.IsFalse(activity.ClocksRun);
        }

        // ---- 供給元の差し替え ----

        [Test]
        public void EnabledContext_BecomesTheProviderSource()
        {
            CompanionActivityProvider.Current = null;

            var go = new GameObject("ActivityContext");
            _spawned.Add(go);
            var context = go.AddComponent<CompanionActivityContext>();
            InvokePrivate(context, "OnEnable"); // EditMode では自動で走らない。

            Assert.AreSame(context, CompanionActivityProvider.Current);

            InvokePrivate(context, "OnDisable");

            Assert.IsNull(CompanionActivityProvider.Current, "無効化で供給元を残さない。");
        }

        /// <summary>
        /// 供給元が居なければ<b>停止</b>（P4-FIX-R2。v1.0 §7.2）。以前はここに GameMode だけを見る許可側の
        /// フォールバックがあり、Context の欠落・無効化・Scene 切替の瞬間に仲間が動き出す穴だった（レビュー R2-08）。
        /// GameMode が何であっても、供給元が無い＝分からない＝何も許さない。
        /// </summary>
        [Test]
        public void NoProviderSource_IsStopped_RegardlessOfGameMode()
        {
            CompanionActivityProvider.Current = null;

            GameModeProvider.Current = null;
            AssertStopped(CompanionActivityProvider.Activity, "モードの正本も無い");

            GameModeProvider.Current = new FakeModes { Current = GameMode.Exploration };
            AssertStopped(CompanionActivityProvider.Activity, "Exploration でも供給元が無ければ");

            GameModeProvider.Current = new FakeModes { Current = GameMode.Combat };
            AssertStopped(CompanionActivityProvider.Activity, "Combat でも供給元が無ければ");

            Assert.IsFalse(CompanionActivityProvider.HasSource);
        }

        /// <summary>
        /// 実 Context を無効化・破棄すると供給元が外れ、その瞬間から停止になる（許可側へ戻らない）。
        /// 別の Context へ差し替わっていた場合は、古い Context の無効化で新しい供給元を外さない（再配線）。
        /// </summary>
        [Test]
        public void ContextDisableAndDestroy_LeaveTheRuntimeStopped()
        {
            GameModeProvider.Current = new FakeModes { Current = GameMode.Exploration };

            var go = new GameObject("ActivityContext");
            _spawned.Add(go);
            var context = go.AddComponent<CompanionActivityContext>();
            context.MarkAreaWithoutEncounter();
            InvokePrivate(context, "OnEnable");
            Assert.IsTrue(CompanionActivityProvider.Activity.CanAct, "前提：配線済みの Context は許可を返す。");

            InvokePrivate(context, "OnDisable");
            AssertStopped(CompanionActivityProvider.Activity, "Context を無効化した直後");

            InvokePrivate(context, "OnEnable");
            Assert.IsTrue(CompanionActivityProvider.Activity.CanAct, "再有効化で戻る。");

            // 再配線：新しい Context へ差し替わったあと、古い Context の無効化は新しい供給元に触らない。
            var go2 = new GameObject("ActivityContext2");
            _spawned.Add(go2);
            var context2 = go2.AddComponent<CompanionActivityContext>();
            context2.MarkAreaWithoutEncounter();
            InvokePrivate(context2, "OnEnable");
            Assert.AreSame(context2, CompanionActivityProvider.Current);

            InvokePrivate(context, "OnDisable");
            Assert.AreSame(context2, CompanionActivityProvider.Current, "古い Context の無効化で新しい供給元を外さない。");

            InvokePrivate(context2, "OnDisable"); // 破棄は OnDisable を伴う（EditMode では手で呼ぶ）。
            Object.DestroyImmediate(go2);
            AssertStopped(CompanionActivityProvider.Activity, "唯一の Context を破棄した直後");
        }

        private static void AssertStopped(CompanionActivity activity, string when)
        {
            Assert.IsFalse(activity.CanAct, when + "：行動を許さない。");
            Assert.IsFalse(activity.ClocksRun, when + "：時計を進めない。");
            Assert.IsFalse(activity.CanInvestigate, when + "：探索を受け付けない。");
            Assert.IsFalse(activity.DiscardOngoing, when + "：破棄でもない（何も分からないので凍結）。");
        }
    }
}
