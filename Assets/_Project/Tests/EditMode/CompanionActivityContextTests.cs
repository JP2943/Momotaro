using System.Collections.Generic;
using System.Reflection;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Scenes;
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
    public sealed class CompanionActivityContextTests
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
            }
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
        /// 供給元が居ないあいだは、これまでどおり <see cref="GameModeProvider"/> だけで判断する（移行期の措置）。
        /// ここをいきなり停止側へ倒すと、まだ活動 Context を置いていない既存 Scene で仲間が固まる。
        /// P4-08R の Scene Validator が「Context が置かれていること」を必須にした時点でこの穴を閉じる。
        /// </summary>
        [Test]
        public void NoProviderSource_FallsBackToGameModeOnly()
        {
            CompanionActivityProvider.Current = null;

            GameModeProvider.Current = null;
            Assert.IsTrue(CompanionActivityProvider.Activity.ClocksRun,
                "未初期化（単体テスト等）は従来どおり許可する。");

            GameModeProvider.Current = new FakeModes { Current = GameMode.Paused };
            Assert.IsFalse(CompanionActivityProvider.Activity.ClocksRun, "Pause は従来どおり止める。");

            GameModeProvider.Current = new FakeModes { Current = GameMode.Exploration };
            Assert.IsTrue(CompanionActivityProvider.Activity.ClocksRun);
            Assert.IsFalse(CompanionActivityProvider.Activity.EncounterActive,
                "供給元が無いとセッションを知らないので、戦闘継続中とは主張しない。");
        }
    }
}
