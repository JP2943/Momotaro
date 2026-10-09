using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// P6A の<b>実ビルド</b>確認の自動操作（P6A 23／25。工程 P6A-06）。コマンドライン引数があるときだけタイトルが起動する。
    ///
    /// <list type="bullet">
    /// <item><c>-p6a-smoke new</c>：New Game → 到着・保存 → きびだんごを 1 つ使う（保存契機ではない変化）→ 正常終了の保存 → 終了。</item>
    /// <item><c>-p6a-smoke close</c>：New Game → 到着・保存 → <b>版を進めない変化だけ</b>（HP・犬丸の Down と残時間）→
    /// 期待値を書き出して<b>通常の終了要求</b>（<c>Application.Quit</c>）。終了前の保存は wantsToQuit の経路が行う（レビュー R1）。</item>
    /// <item><c>-p6a-smoke continue</c>：別プロセスで Continue → 採用 → 状態を書き出して終了（Editor の static に依らない）。</item>
    /// <item><c>-p6a-smoke play</c>：New Game のあと<b>実入力で歩き・実攻撃で普通敵を倒し・お地蔵様で休息する</b>を繰り返し、
    /// その間のフレーム時間と採取時間を測る（P6A 25 の「連続撃破・通常移動中」）。</item>
    /// <item><c>-p6a-smoke perf</c>：New Game のあと保存契機を 150 回以上出し（連続・書込中の追加要求を含む）、
    /// 採取のメインスレッド時間・要求から完了までの時間・フレーム時間を測って書き出す。<c>-p6a-slow-io &lt;ms&gt;</c> で遅い I/O。</item>
    /// </list>
    /// 結果は <c>-p6a-out &lt;path&gt;</c> へ JSON で書く。保存先は <c>-p6a-save-dir &lt;dir&gt;</c>（試遊者の保存へ触れない）。
    /// <b>試遊の通常起動では何もしない。</b>
    /// </summary>
    public static class Phase6SmokeArgs
    {
        /// <summary>モード（空なら無効）。</summary>
        public static string Mode { get; private set; } = string.Empty;

        /// <summary>結果の書き出し先。</summary>
        public static string OutPath { get; private set; } = string.Empty;

        /// <summary>遅い I/O の遅延（ミリ秒。0 で無し）。</summary>
        public static int SlowIoMilliseconds { get; private set; }

        /// <summary>保存の採取を止める（性能の切り分け用。<c>-p6a-no-save</c>）。</summary>
        public static bool NoSave { get; private set; }

        /// <summary>P6B：きびだんご使用の何秒目で通常の終了要求を出すか（<c>-p6b-use-quit-at</c>）。</summary>
        public static float UseQuitAt { get; private set; } = 0.6f;

        /// <summary>計測の前に切り分けの下準備を段階ごとに行う（<c>-p6a-play-probe</c>）。</summary>
        public static bool Probe { get; private set; }

        /// <summary>引数を読み、保存先の差し替えを<b>保存サービスが調停役を作る前に</b>当てる。有効なら true。</summary>
        public static bool TryApply()
        {
            string[] args = Environment.GetCommandLineArgs();
            string saveDir = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "-p6a-smoke":
                        Mode = args[i + 1];
                        break;
                    case "-p6a-out":
                        OutPath = args[i + 1];
                        break;
                    case "-p6a-save-dir":
                        saveDir = args[i + 1];
                        break;
                    case "-p6a-play-probe":
                        Probe = args[i + 1] == "1";
                        break;
                    case "-p6b-use-quit-at":
                        float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float at);
                        UseQuitAt = at;
                        break;
                    case "-p6a-no-save":
                        NoSave = args[i + 1] == "1";
                        break;
                    case "-p6a-slow-io":
                        int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int ms);
                        SlowIoMilliseconds = ms;
                        break;
                }
            }

            if (string.IsNullOrEmpty(Mode) || string.IsNullOrEmpty(saveDir))
            {
                Mode = string.Empty;
                return false;
            }

            // 窓が前面に無くても止めない（自動確認の窓は Editor の後ろで開くことがある。止まると計測が終わらない）。
            Application.runInBackground = true;

            // 窓が前面に無いと、Input System は既定で（背景の扱いが「リセットして無効」なら）キーボードを止める。自動操作の仮想キーボードが
            // 効かず、実攻撃・歩行が起きないまま計測が進んでいた（2026-10-02、前面の窓によって撃破 0 になる回があった）。自動確認のときだけ前面を問わない。
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            CampaignSaveService.TestDirectoryOverride = saveDir;
            CampaignSaveService saves = BootstrapServices.Get<CampaignSaveService>();
            if (saves != null && SlowIoMilliseconds > 0)
            {
                saves.FileSystemOverride = new SlowSaveFileSystem(SlowIoMilliseconds);
            }

            return true;
        }
    }

    /// <summary>書込・置換を遅らせるファイル操作（遅い I/O の計測用）。</summary>
    public sealed class SlowSaveFileSystem : ISaveFileSystem
    {
        private readonly RealSaveFileSystem _real = new RealSaveFileSystem();
        private readonly int _delay;

        public SlowSaveFileSystem(int delayMilliseconds) => _delay = delayMilliseconds;

        public bool Exists(string path) => _real.Exists(path);
        public string ReadAllText(string path) => _real.ReadAllText(path);

        public void WriteAllTextDurable(string path, string text)
        {
            Thread.Sleep(_delay);
            _real.WriteAllTextDurable(path, text);
        }

        public void Replace(string source, string destination) => _real.Replace(source, destination);
        public void Move(string source, string destination) => _real.Move(source, destination);
        public void Delete(string path) => _real.Delete(path);
        public void CreateDirectory(string path) => _real.CreateDirectory(path);
        public IDisposable TryLock(string path) => _real.TryLock(path);
    }

    /// <summary>自動操作の本体（タイトルの <see cref="Phase6CampaignLauncher"/> が起動する）。</summary>
    [DisallowMultipleComponent]
    public sealed partial class Phase6SmokeDriver : MonoBehaviour
    {
        private Phase6CampaignLauncher _launcher;
        private readonly Dictionary<string, string> _result = new Dictionary<string, string>();

        /// <summary>起動する（常駐へ置く。タイトル Scene が入れ替わっても続く）。</summary>
        public static void Begin(Phase6CampaignLauncher launcher)
        {
            var go = new GameObject("Phase6SmokeDriver");
            DontDestroyOnLoad(go);
            var driver = go.AddComponent<Phase6SmokeDriver>();
            driver._launcher = launcher;
            driver.StartCoroutine(driver.Run());
        }

        private IEnumerator Run()
        {
            _result["mode"] = Phase6SmokeArgs.Mode;
            _result["platform"] = Application.platform.ToString();
            _result["unity"] = Application.unityVersion;
            _result["isEditor"] = Application.isEditor ? "true" : "false";
            _result["processId"] = Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
            _result["device"] = SystemInfo.deviceModel + " / " + SystemInfo.processorType + " / " + SystemInfo.graphicsDeviceName;
            _result["slowIoMs"] = Phase6SmokeArgs.SlowIoMilliseconds.ToString(CultureInfo.InvariantCulture);

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            switch (Phase6SmokeArgs.Mode)
            {
                case "new":
                    yield return NewGameThenExit();
                    break;
                case "continue":
                    yield return ContinueThenExit();
                    break;
                case "close":
                    yield return NewGameThenWindowClose();
                    break;
                case "play":
                    yield return PerfPlay();
                    break;
                case "perf":
                    yield return Perf();
                    break;
                case "growth":
                    yield return GrowthThenExit();
                    break;
                case "useclose":
                    yield return UseThenWindowClose();
                    break;
                case "justevade":
                    yield return JustEvadeThenExit();
                    break;
                case "story":
                    yield return StoryThenExit();
                    break;
                default:
                    _result["error"] = "unknown mode";
                    Finish();
                    break;
            }
        }

        private static CampaignSaveService Saves => BootstrapServices.Get<CampaignSaveService>();

        private IEnumerator StartNewGame()
        {
            // 人がタイトルを見ている間に済む文字の先描きを待つ（画面の無い batchmode では描かれないので上限つき）。
            float prewarmDeadline = Time.realtimeSinceStartup + 3f;
            while (!_launcher.PrewarmDone && Time.realtimeSinceStartup < prewarmDeadline)
            {
                yield return null;
            }

            _result["titlePrewarmDone"] = _launcher.PrewarmDone ? "true" : "false";
            if (!_launcher.PressNewGame(confirmed: true))
            {
                _result["error"] = "new game refused: " + _launcher.Status;
                yield break;
            }

            yield return WaitArrived();
            yield return WaitSaved();
        }

        private static IEnumerator WaitArrived()
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
                AreaTransitionService t = BootstrapServices.Get<AreaTransitionService>();
                if (bundle != null && bundle.Context != null && bundle.Context.IsAreaReady && t != null && !t.IsTransitionUnsettled)
                {
                    yield break;
                }

                yield return null;
            }
        }

        private static IEnumerator WaitSaved()
        {
            SaveCoordinator c = Saves.Coordinator;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (c != null && (c.IsDirty || c.IsWriting) && c.Status != SaveStatus.Failed && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        private IEnumerator NewGameThenExit()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            GameSessionState session = GameSessionProvider.Current;
            session.TryConsumeKibidango(1); // 保存契機ではない変化（正常終了の保存が拾うこと）。
            _result["adventureId"] = session.AdventureId;
            _result["kibidango"] = session.Kibidango.ToString(CultureInfo.InvariantCulture);
            _result["area"] = CurrentAreaProvider.Current.AreaId.Value;
            _result["dirtyBeforeExit"] = Saves.Coordinator.IsDirty ? "true" : "false";

            SaveExitOutcome outcome = SaveExitOutcome.TimedOut;
            yield return Saves.SaveBeforeExit(o => outcome = o);
            _result["exitOutcome"] = outcome.ToString();
            _result["savedRevision"] = Saves.Coordinator.SavedRevision.ToString(CultureInfo.InvariantCulture);
            _result["revision"] = session.Changes.Revision.ToString(CultureInfo.InvariantCulture);
            Finish();
        }

        private IEnumerator ContinueThenExit()
        {
            CampaignAdventureFlow flow = Saves.Flow;
            bool done = false;
            string failure = null;
            flow.Continued += () => done = true;
            flow.ContinueFailed += reason => failure = reason;
            if (!_launcher.PressContinue())
            {
                _result["error"] = "continue refused: " + _launcher.Status;
                Finish();
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 60f;
            while (!done && failure == null && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!done)
            {
                _result["error"] = "continue failed: " + (failure ?? "timeout");
                Finish();
                yield break;
            }

            yield return WaitArrived();
            GameSessionState session = GameSessionProvider.Current;
            _result["adventureId"] = session.AdventureId;
            _result["kibidango"] = session.Kibidango.ToString(CultureInfo.InvariantCulture);
            _result["area"] = CurrentAreaProvider.Current != null ? CurrentAreaProvider.Current.AreaId.Value : string.Empty;
            _result["checkpoint"] = session.Checkpoint.Value;
            _result["resume"] = session.Resume.ToString();
            _result["savedRevision"] = Saves.Coordinator.SavedRevision.ToString(CultureInfo.InvariantCulture);
            _result["dirtyAfterContinue"] = Saves.Coordinator.IsDirty ? "true" : "false";
            WriteParty("after");
            WriteProgress("after");
            WriteStory("after");
            if (TryPort(out AreaActorTransferPort after) && after.PlayerState != null)
            {
                _result["afterUsingItem"] = after.PlayerState.IsUsingItem ? "true" : "false";
                _result["afterHasCounter"] = after.PlayerState.HasJustEvadeCounter ? "true" : "false";
            }

            Finish();
        }

        // ---------------------------------------------------------------- P6B 19：成長・権利・使用後の残数と HP

        /// <summary>
        /// P6B の実ビルド確認（<c>-p6a-smoke growth</c>）：New Game → 初期お地蔵様のメニューで取得・払い戻し →
        /// HP を下げて<b>仮想キーボードの F</b>できびだんごを使い、確定まで待つ → 値を書き出して正常終了の保存 → 終了。
        /// 別プロセスの <c>continue</c> が同じ値を書き出すかを Editor 側で突き合わせる。
        /// </summary>
        private IEnumerator GrowthThenExit()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            GameSessionState session = GameSessionProvider.Current;
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            CampaignCatalog campaign = BootstrapServices.Get<AreaTransitionService>().Catalog.Campaign;
            Momotaro.Gameplay.Interaction.AreaInteractionOutcome opened = shrines.OnShrineInteracted(campaign.InitialShrineId);
            if (!shrines.IsMenuOpen)
            {
                _result["error"] = "shrine menu did not open: " + opened.Message;
                Finish();
                yield break;
            }

            string[] buy = { "growth_vit_01", "growth_atk_01", "growth_atk_02", "growth_atk_stock_01", "growth_sta_01" };
            foreach (string id in buy)
            {
                ShrineMenuResult r = shrines.PurchaseGrowth(new Momotaro.Core.Identification.StableId(id));
                if (r != ShrineMenuResult.GrowthPurchased)
                {
                    _result["error"] = "purchase " + id + " failed: " + shrines.Message;
                    Finish();
                    yield break;
                }

                yield return null;
            }

            if (shrines.RefundGrowth(new Momotaro.Core.Identification.StableId("growth_sta_01")) != ShrineMenuResult.GrowthRefunded)
            {
                _result["error"] = "refund failed: " + shrines.Message;
                Finish();
                yield break;
            }

            shrines.Close();
            yield return null;
            yield return null;

            if (!TryPort(out AreaActorTransferPort port) || port.PlayerVitals == null || port.PlayerState == null)
            {
                _result["error"] = "no player";
                Finish();
                yield break;
            }

            port.PlayerVitals.Vitals.Health.SetCurrent(40);
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("P6BSmokeKeyboard");
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float deadline = Time.realtimeSinceStartup + 8f;
            while (port.PlayerState.ItemUseCompleteCount == 0 && port.PlayerState.ItemUseInterruptCount == 0
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _result["useStarted"] = port.PlayerState.ItemUseStartCount.ToString(CultureInfo.InvariantCulture);
            _result["useCommitted"] = port.PlayerState.ItemUseCommitCount.ToString(CultureInfo.InvariantCulture);
            _result["useRejection"] = port.PlayerState.LastItemUseRejection.ToString();
            _result["adventureId"] = session.AdventureId;
            _result["kibidango"] = session.Kibidango.ToString(CultureInfo.InvariantCulture);
            _result["area"] = CurrentAreaProvider.Current.AreaId.Value;
            WriteParty("before");
            WriteProgress("before");

            SaveExitOutcome outcome = SaveExitOutcome.TimedOut;
            yield return Saves.SaveBeforeExit(o => outcome = o);
            _result["exitOutcome"] = outcome.ToString();
            Finish();
        }

        /// <summary>
        /// P6B 14（レビュー ddb2d19 D1）：New Game → HP を下げて仮想キーボードの F で使い始め、<c>-p6b-use-quit-at</c> 秒で
        /// <b>通常の終了要求</b>（<c>Application.Quit</c> → wantsToQuit → 最新を採って保存 → 終了）。
        /// 別プロセスの continue が、その時点の HP・残数（確定前なら未回復・未消費、確定後なら回復済み・1 個減）を書き出すかを突き合わせる。
        /// </summary>
        private IEnumerator UseThenWindowClose()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            if (!TryPort(out AreaActorTransferPort port) || port.PlayerVitals == null || port.PlayerState == null)
            {
                _result["error"] = "no player";
                Finish();
                yield break;
            }

            GameSessionState session = GameSessionProvider.Current;
            port.PlayerVitals.Vitals.Health.SetCurrent(40);
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>("P6BSmokeKeyboard");
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F));
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            float deadline = Time.realtimeSinceStartup + 6f;
            while (port.PlayerState.IsUsingItem && port.PlayerState.ItemUseElapsed < Phase6SmokeArgs.UseQuitAt
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!port.PlayerState.IsUsingItem)
            {
                _result["error"] = "the use did not start or ended before the quit point (rejection="
                    + port.PlayerState.LastItemUseRejection + ")";
                Finish();
                yield break;
            }

            _result["quitAtElapsed"] = port.PlayerState.ItemUseElapsed.ToString("0.###", CultureInfo.InvariantCulture);
            _result["quitCommitted"] = port.PlayerState.ItemUseCommitted ? "true" : "false";
            _result["adventureId"] = session.AdventureId;
            _result["kibidango"] = session.Kibidango.ToString(CultureInfo.InvariantCulture);
            _result["area"] = CurrentAreaProvider.Current.AreaId.Value;
            WriteParty("before");
            _result["closeRequested"] = "true";
            WriteResult();

            // 通常の終了要求（使用中のまま）。wantsToQuit がいったん断り、最新を採って保存してから自分で終了する。
            Application.Quit();
            float quitDeadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < quitDeadline)
            {
                yield return null;
            }

            _result["error"] = "the normal quit did not finish (outcome=" + Saves.LastExitOutcome
                + " awaitingChoice=" + Saves.AwaitingExitChoice + ")";
            Finish();
        }

        // ---------------------------------------------------------------- P6C 15：実入力のジャスト回避と反撃・別プロセス Continue

        /// <summary>
        /// P6C の実ビルド確認（<c>-p6a-smoke justevade</c>）：New Game → A の普通敵（近接）の通常の攻撃に、<b>仮想キーボードの Space＋方向キー</b>で
        /// 合わせてジャスト回避 → 方向キー＋J で反撃（強化された段の HP）→ 早い通常回避の後の反撃（通常の HP）と比べる →
        /// もう一度ジャスト回避して強化を持ったまま正常終了の保存 → 終了。別プロセスの <c>continue</c> で強化が無いこと・徳が戻ることを確かめる。
        /// 時機を細かく測るため、試行の間だけ timeScale を 0.5 にする（自動確認だけの措置）。
        /// </summary>
        private IEnumerator JustEvadeThenExit()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            if (!TryPort(out AreaActorTransferPort port) || port.PlayerVitals == null || port.PlayerState == null)
            {
                _result["error"] = "no player";
                Finish();
                yield break;
            }

            _keyboard = InputSystem.AddDevice<Keyboard>("P6CSmokeKeyboard");
            yield return null;
            port.PlayerVitals.SetGuardianResolver(null); // 犬丸の「かばう」は対象外（主人公の回避を見る）

            int justDamage = -1;
            int normalDamage = -1;
            int justAttempts = 0;
            int normalAttempts = 0;
            var lines = new List<string>();
            for (int i = 0; i < 16 && (justDamage < 0 || normalDamage < 0); i++)
            {
                bool wantJust = justDamage < 0 && (normalDamage >= 0 || i % 2 == 0);
                if (wantJust)
                {
                    justAttempts++;
                }
                else
                {
                    normalAttempts++;
                }

                string outcome = null;
                int damage = -1;
                bool boosted = false;
                yield return EvadeAndCounter(port, wantJust ? 0.085f : 0.17f, true, (o, d, b) => { outcome = o; damage = d; boosted = b; });
                lines.Add((wantJust ? "just" : "early") + ":" + outcome + ":" + damage + ":" + (boosted ? "boosted" : "normal"));
                if (wantJust && outcome == "JustEvade" && boosted && damage > 0)
                {
                    justDamage = damage;
                }
                else if (!wantJust && outcome != "JustEvade" && outcome != "Damage" && !boosted && damage > 0)
                {
                    normalDamage = damage;
                }
            }

            _result["attempts"] = string.Join(" ", lines);
            _result["justEvadeSuccess"] = port.PlayerState.JustEvadeSuccessCount.ToString(CultureInfo.InvariantCulture);
            _result["counterConsumed"] = port.PlayerState.JustEvadeCounterConsumeCount.ToString(CultureInfo.InvariantCulture);
            _result["boostedHits"] = port.PlayerState.CounterBoostedHitCount.ToString(CultureInfo.InvariantCulture);
            _result["justCounterDamage"] = justDamage.ToString(CultureInfo.InvariantCulture);
            _result["normalCounterDamage"] = normalDamage.ToString(CultureInfo.InvariantCulture);
            _result["counterHarder"] = justDamage > normalDamage && normalDamage > 0 ? "true" : "false";

            // 強化を持ったまま正常終了（保存しない一時状態であることを別プロセスで確かめる）。
            for (int i = 0; i < 8 && !port.PlayerState.HasJustEvadeCounter; i++)
            {
                yield return EvadeAndCounter(port, 0.085f, false, (o, d, b) => { });
            }

            _result["holdingCounterAtExit"] = port.PlayerState.HasJustEvadeCounter ? "true" : "false";
            Time.timeScale = 1f;
            GameSessionState session = GameSessionProvider.Current;
            _result["adventureId"] = session.AdventureId;
            _result["area"] = CurrentAreaProvider.Current.AreaId.Value;
            WriteParty("before");
            WriteProgress("before");
            SaveExitOutcome exit = SaveExitOutcome.TimedOut;
            yield return Saves.SaveBeforeExit(o => exit = o);
            _result["exitOutcome"] = exit.ToString();
            Finish();
        }

        /// <summary>
        /// 近接の 1 試行：敵の東西南北いずれかの 1.9m に立ち、背を向けて J（音）で気付かせ、予兆の残りが <paramref name="lead"/> 秒で
        /// Space＋敵の方向キー。<paramref name="counter"/> なら回避の後に方向キー＋J で反撃し、敵へ与えた HP を返す。
        /// </summary>
        private IEnumerator EvadeAndCounter(AreaActorTransferPort port, float lead, bool counter, Action<string, int, bool> done)
        {
            Momotaro.Gameplay.Player.PlayerStateController player = port.PlayerState;
            Momotaro.Gameplay.Player.PlayerVitalsHolder vitals = port.PlayerVitals;
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (bundle == null || !bundle.TryResolve(out Momotaro.Gameplay.Player.PlayerRoot root))
            {
                done("no-root", -1, false);
                yield break;
            }

            var facing = root.GetComponentInChildren<Momotaro.Gameplay.Player.PlayerFacing>();
            Momotaro.Gameplay.Enemy.EnemyActor enemy = null;
            float best = float.MaxValue;
            foreach (Momotaro.Gameplay.Enemy.EnemyActor e in FindObjectsByType<Momotaro.Gameplay.Enemy.EnemyActor>(FindObjectsSortMode.None))
            {
                if (e == null || e.IsDefeated || !e.gameObject.activeInHierarchy || e.gameObject.scene != bundle.gameObject.scene
                    || !e.name.Contains("Melee"))
                {
                    continue;
                }

                float d = Vector3.Distance(e.transform.position, root.transform.position);
                if (d < best)
                {
                    best = d;
                    enemy = e;
                }
            }

            if (enemy == null)
            {
                done("no-enemy", -1, false);
                yield break;
            }

            enemy.ResetState();
            enemy.SetAttackPowerScale(0.3f);
            var attack = enemy.GetComponentInChildren<Momotaro.Gameplay.Enemy.Combat.EnemyAttackController>();
            Time.timeScale = 1f;
            float deadline = Time.realtimeSinceStartup + 12f;
            float nextTap = 0f;
            while (Time.realtimeSinceStartup < deadline
                   && !(attack.IsAttacking && attack.Phase == Momotaro.Gameplay.Enemy.Combat.EnemyAttackMachine.Phase.Prepare))
            {
                vitals.RestoreForWaveRecovery();
                Vector3 toPlayer = root.transform.position - enemy.transform.position;
                toPlayer.y = 0f;
                Vector3 axis = AxisOf(toPlayer);
                if (toPlayer.magnitude > 2.35f || toPlayer.magnitude < 1.55f || Vector3.Angle(toPlayer, axis) > 10f)
                {
                    Vector3 stand = enemy.transform.position + axis * 1.9f;
                    root.transform.position = new Vector3(stand.x, root.transform.position.y, stand.z);
                    if (root.Body != null)
                    {
                        root.Body.position = root.transform.position;
                        root.Body.linearVelocity = Vector3.zero;
                    }
                }

                facing?.ConfirmFromInput(new Vector2(axis.x, axis.z));
                if (Time.realtimeSinceStartup >= nextTap)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.J));
                    nextTap = Time.realtimeSinceStartup + 2.5f;
                }
                else
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                }

                yield return null;
            }

            if (!(attack.IsAttacking && attack.Phase == Momotaro.Gameplay.Enemy.Combat.EnemyAttackMachine.Phase.Prepare))
            {
                done("no-attack", -1, false);
                yield break;
            }

            Time.timeScale = 0.5f;
            Vector3 off = root.transform.position - enemy.transform.position;
            off.y = 0f;
            Key stepKey = KeyOf(-AxisOf(off));
            var rec = new FirstResult();
            vitals.Results.AddListener(rec);
            bool pressed = false;
            while (attack.IsAttacking && attack.Phase == Momotaro.Gameplay.Enemy.Combat.EnemyAttackMachine.Phase.Prepare)
            {
                float remaining = attack.CurrentPrepareSeconds - attack.AttackElapsed;
                if (!pressed && remaining <= lead + Time.deltaTime)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Space, stepKey));
                    pressed = true;
                }
                else if (pressed)
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            float activeDeadline = Time.realtimeSinceStartup + 2f;
            while (attack.Phase == Momotaro.Gameplay.Enemy.Combat.EnemyAttackMachine.Phase.Active && Time.realtimeSinceStartup < activeDeadline)
            {
                yield return null;
            }

            yield return null;
            vitals.Results.RemoveListener(rec);
            string outcome = rec.Kind.HasValue ? rec.Kind.Value.ToString() : "None";
            if (!counter)
            {
                Time.timeScale = 1f;
                done(outcome, -1, false);
                yield break;
            }

            float stepDeadline = Time.realtimeSinceStartup + 2f;
            while ((player.IsStepping || player.Current == Momotaro.Gameplay.Player.PlayerState.Hurt) && Time.realtimeSinceStartup < stepDeadline)
            {
                yield return null;
            }

            int boostedBefore = player.CounterBoostedHitCount;
            var enemyRec = new EnemyDamage(player);
            enemy.Results.AddListener(enemyRec);
            Vector3 now = root.transform.position - enemy.transform.position;
            now.y = 0f;
            Key toward = KeyOf(-AxisOf(now));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(toward, Key.J));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(toward));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            float hitDeadline = Time.realtimeSinceStartup + 2f;
            while (enemyRec.Damage < 0 && Time.realtimeSinceStartup < hitDeadline)
            {
                yield return null;
            }

            enemy.Results.RemoveListener(enemyRec);
            Time.timeScale = 1f;
            bool boosted = player.CounterBoostedHitCount > boostedBefore;
            float settle = Time.realtimeSinceStartup + 3f;
            while ((attack.IsAttacking || player.Current == Momotaro.Gameplay.Player.PlayerState.Attack) && Time.realtimeSinceStartup < settle)
            {
                yield return null;
            }

            done(outcome, enemyRec.Damage, boosted);
        }

        private static Vector3 AxisOf(Vector3 v)
        {
            if (v.sqrMagnitude < 1e-6f)
            {
                return Vector3.back;
            }

            return Mathf.Abs(v.x) > Mathf.Abs(v.z) ? new Vector3(Mathf.Sign(v.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(v.z));
        }

        private static Key KeyOf(Vector3 axis) =>
            Mathf.Abs(axis.x) > Mathf.Abs(axis.z) ? (axis.x > 0f ? Key.D : Key.A) : (axis.z > 0f ? Key.W : Key.S);

        private sealed class FirstResult : Momotaro.Gameplay.Combat.IHitResultListener
        {
            public Momotaro.Gameplay.Combat.HitResultKind? Kind;

            public void OnHitResult(in Momotaro.Gameplay.Combat.HitResult result)
            {
                if (Kind == null)
                {
                    Kind = result.Kind;
                }
            }
        }

        private sealed class EnemyDamage : Momotaro.Gameplay.Combat.IHitResultListener
        {
            private readonly Momotaro.Gameplay.Player.PlayerStateController _player;

            public EnemyDamage(Momotaro.Gameplay.Player.PlayerStateController player) => _player = player;

            public int Damage = -1;

            public void OnHitResult(in Momotaro.Gameplay.Combat.HitResult result)
            {
                if (Damage < 0 && ReferenceEquals(result.Attacker, _player)
                    && result.Kind == Momotaro.Gameplay.Combat.HitResultKind.Damage)
                {
                    Damage = Mathf.RoundToInt(result.AppliedDamage.Hp);
                }
            }
        }

        /// <summary>成長・権利・徳・上限・能力（P6B 19）を書き出す。</summary>
        private void WriteProgress(string prefix)
        {
            GameSessionState session = GameSessionProvider.Current;
            AreaTransitionService t = BootstrapServices.Get<AreaTransitionService>();
            CampaignCatalog campaign = t != null && t.Catalog != null ? t.Catalog.Campaign : null;
            if (session == null || campaign == null)
            {
                return;
            }

            var ids = new List<string>();
            foreach (GrowthInfo g in campaign.GrowthNodes)
            {
                if (session.Progress.HasGrowth(g.GrowthId))
                {
                    ids.Add(g.GrowthId.Value);
                }
            }

            _result[prefix + "Growth"] = string.Join("+", ids);
            _result[prefix + "RefundRights"] = session.Progress.RefundRights.ToString(CultureInfo.InvariantCulture);
            _result[prefix + "Virtue"] = session.Progress.AvailableVirtue.ToString(CultureInfo.InvariantCulture);
            _result[prefix + "Capacity"] = campaign.KibidangoCapacityOf(session.Progress).ToString(CultureInfo.InvariantCulture);
            if (TryPort(out AreaActorTransferPort port) && port.PlayerVitals != null && port.PlayerState != null)
            {
                _result[prefix + "MaxHp"] = port.PlayerVitals.MaxHp.ToString(CultureInfo.InvariantCulture);
                _result[prefix + "AttackMultiplier"] =
                    port.PlayerState.GrowthAttackHpMultiplier.ToString("0.00", CultureInfo.InvariantCulture);
            }
        }

        // ---------------------------------------------------------------- 通常の終了要求（P6A 22・23。レビュー R1）

        private static bool TryPort(out AreaActorTransferPort port)
        {
            port = null;
            AreaTransitionService t = BootstrapServices.Get<AreaTransitionService>();
            return t != null && t.TryGetActiveTransferPort(out port);
        }

        private void WriteParty(string prefix)
        {
            if (!TryPort(out AreaActorTransferPort port))
            {
                _result[prefix + "PartyError"] = "no transfer port";
                return;
            }

            Momotaro.Gameplay.Save.PartySaveValues party = port.ExportForSave();
            _result[prefix + "PlayerHp"] = party.Player.Hp.ToString(CultureInfo.InvariantCulture);
            _result[prefix + "CompanionDown"] = party.HasCompanion && party.Companion.IsDown ? "true" : "false";
            _result[prefix + "CompanionRecovery"] = party.Companion.RecoveryRemaining.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private IEnumerator NewGameThenWindowClose()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            if (!TryPort(out AreaActorTransferPort port))
            {
                _result["error"] = "no transfer port";
                Finish();
                yield break;
            }

            // 版を進めない変化だけを作る：主人公の HP を減らし、犬丸を Down（復帰まで長め）にする。
            Momotaro.Gameplay.Save.PartySaveValues before = port.ExportForSave();
            var player = new Momotaro.Gameplay.Save.PlayerSaveValues(Math.Max(1, before.Player.Hp - 7),
                before.Player.Stamina, before.Player.StaminaRegenDelay, 0f);
            Momotaro.Gameplay.Save.CompanionSaveValues c = before.Companion;
            long revision = GameSessionProvider.Current.Changes.Revision;

            // 復帰待ちは Data の上限を超えられないので、受け付けられる最も長い値を使う（終了までに自然復帰しないように）。
            bool applied = false;
            foreach (float recovery in new[] { 60f, 30f, 20f, 15f, 10f, 8f, 6f, 5f })
            {
                var companion = new Momotaro.Gameplay.Save.CompanionSaveValues(c.CompanionId, 0, true, recovery, 0f,
                    c.AttackCooldown, c.GuardCooldown, c.EvadeCooldown, c.GuardianCooldown);
                if (port.TryApplySaveValues(new Momotaro.Gameplay.Save.PartySaveValues(player, before.HasCompanion, companion)))
                {
                    applied = true;
                    _result["appliedRecovery"] = recovery.ToString("0.###", CultureInfo.InvariantCulture);
                    break;
                }
            }

            if (!applied)
            {
                _result["error"] = "could not apply the change: " + port.LastApplyFailure;
                Finish();
                yield break;
            }

            yield return null;
            GameSessionState session = GameSessionProvider.Current;
            _result["adventureId"] = session.AdventureId;
            _result["area"] = CurrentAreaProvider.Current.AreaId.Value;
            _result["revisionUnchanged"] = session.Changes.Revision == revision ? "true" : "false";
            _result["dirtyBeforeClose"] = Saves.Coordinator.IsDirty ? "true" : "false";
            WriteParty("before");
            _result["closeRequested"] = "true";
            WriteResult();

            // 通常の終了要求。冒険中なので wantsToQuit がいったん断り、最新を採って保存してから自分で終了する。
            Application.Quit();

            // 終わらなければ（素通しされず、保存も終わらない）、理由を残して閉じる。
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            _result["error"] = "the normal quit did not finish (outcome=" + Saves.LastExitOutcome
                + " awaitingChoice=" + Saves.AwaitingExitChoice + " wantsToQuit=" + Saves.WantsToQuitCount + ")";
            Finish();
        }

        // ---------------------------------------------------------------- 実プレイ中の保存性能（P6A 25）

        private Keyboard _keyboard;
        private readonly List<double> _restMs = new List<double>();
        private readonly List<double> _menuMs = new List<double>();

        private IEnumerator PerfPlay()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            _keyboard = InputSystem.AddDevice<Keyboard>("P6ASmokeKeyboard");
            yield return null;
            SaveCoordinator c = Saves.Coordinator;
            if (Phase6SmokeArgs.NoSave)
            {
                // 切り分け：保存の採取（と書込）を一切起こさない。ほかの処理（休息・敵の作り直し・UI）は同じ。
                c.CanCapture = () => false;
            }

            _result["noSave"] = Phase6SmokeArgs.NoSave ? "true" : "false";
            CampaignShrineService shrines = BootstrapServices.Get<CampaignShrineService>();
            int captureStart = c.CaptureMilliseconds.Count;
            int successStart = c.SuccessCount;
            var frames = new List<double>();
            var walkFrames = new List<double>();
            int kills = 0;
            int rests = 0;
            int failedKills = 0;
            int failedRests = 0;
            bool measuring = true;
            bool walking = false;
            string phase = "start";
            var spikes = new List<string>();
            int lastCaptures = c.CaptureMilliseconds.Count;
            int lastGc = GC.CollectionCount(0);

            // 33ms を超えたフレームは、そのとき何をしていたか（歩行・撃破・休息）と、そのフレームに保存の採取があったかを残す。
            IEnumerator FrameSampler()
            {
                while (measuring)
                {
                    yield return null;
                    double ms = Time.unscaledDeltaTime * 1000.0;
                    frames.Add(ms);
                    if (walking)
                    {
                        walkFrames.Add(ms);
                    }

                    int captures = c.CaptureMilliseconds.Count;
                    int gc = GC.CollectionCount(0);
                    if (ms > 33.4)
                    {
                        spikes.Add(phase + "=" + ms.ToString("0.0", CultureInfo.InvariantCulture) + "ms(capture="
                            + (captures > lastCaptures ? "yes" : "no") + ",writing=" + (c.IsWriting ? "yes" : "no")
                            + ",gc=" + (gc - lastGc) + ")");
                    }

                    lastCaptures = captures;
                    lastGc = gc;
                }
            }

            Coroutine sampler = StartCoroutine(FrameSampler());
            if (Phase6SmokeArgs.Probe)
            {
                // 切り分けの下準備を段階に分けて行い、どの段階で止まるかを見る。
                // 1. GC：明示の全回収にかかる時間。2. 一時停止（入力の切り替え）だけ。3. メニューと同じ文字の IMGUI 描画だけ。
                phase = "probe/gc";
                Stopwatch gcWatch = Stopwatch.StartNew();
                GC.Collect();
                gcWatch.Stop();
                _result["probeGcMs"] = gcWatch.Elapsed.TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture);
                for (int i = 0; i < 15; i++)
                {
                    yield return null;
                }

                phase = "probe/pause";
                Momotaro.Gameplay.Modes.IGameModeService modes = Momotaro.Gameplay.Modes.GameModeProvider.Current;
                modes?.ChangeMode(Momotaro.Gameplay.Modes.GameMode.Paused);
                for (int i = 0; i < 15; i++)
                {
                    yield return null;
                }

                modes?.ChangeMode(Momotaro.Gameplay.Modes.GameMode.Exploration);
                for (int i = 0; i < 15; i++)
                {
                    yield return null;
                }

                phase = "probe/gui";
                var probeGo = new GameObject("Phase6SmokeGuiProbe");
                Phase6SmokeGuiProbe probe = probeGo.AddComponent<Phase6SmokeGuiProbe>();
                for (int i = 0; i < 15; i++)
                {
                    yield return null;
                }

                _result["probeGuiFrames"] = probe.DrawnFrames.ToString(CultureInfo.InvariantCulture);
                Destroy(probeGo);
                for (int i = 0; i < 15; i++)
                {
                    yield return null;
                }
            }

            _result["probe"] = Phase6SmokeArgs.Probe ? "true" : "false";
            float started = Time.realtimeSinceStartup;
            for (int cycle = 0; cycle < 8 && Time.realtimeSinceStartup - started < 100f; cycle++)
            {
                // 歩く（実キー W・D・S・A を順に押し続ける）。保存の書込はこの間にも裏で走る。
                phase = "c" + cycle + "/walk";
                walking = true;
                foreach (Key key in new[] { Key.W, Key.D, Key.S, Key.A })
                {
                    InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
                    float until = Time.realtimeSinceStartup + 0.5f;
                    while (Time.realtimeSinceStartup < until)
                    {
                        yield return null;
                    }
                }

                InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
                walking = false;

                // 普通敵を実攻撃（J）で倒す。撃破のたびに報酬と保存要求が実経路で立つ。
                phase = "c" + cycle + "/kill";
                AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
                if (bundle != null && bundle.TryResolve(out AreaFieldEnemyDirector director))
                {
                    var targets = new List<GameObject>(director.Spawned);
                    foreach (GameObject go in targets)
                    {
                        Momotaro.Gameplay.Enemy.EnemyActor enemy = go != null
                            ? go.GetComponentInChildren<Momotaro.Gameplay.Enemy.EnemyActor>() : null;
                        if (enemy == null || enemy.IsDefeated || !go.activeInHierarchy)
                        {
                            continue;
                        }

                        bool killed = false;
                        yield return KillWithAttacks(enemy, ok => killed = ok);
                        if (killed)
                        {
                            kills++;
                        }
                        else
                        {
                            failedKills++;
                        }
                    }
                }

                // お地蔵様で休息する（実キー E で調べ、休息。普通敵が戻り、保存要求が立つ）。
                bool rested = false;
                int restCycle = cycle;
                yield return RestAtShrine(shrines, ok => rested = ok, p => phase = "c" + restCycle + "/" + p);
                if (rested)
                {
                    rests++;
                }
                else
                {
                    failedRests++;
                }
            }

            float elapsed = Time.realtimeSinceStartup - started;
            yield return WaitSaved();
            measuring = false;
            StopCoroutine(sampler);
            InputSystem.RemoveDevice(_keyboard);

            var captures = new List<double>();
            for (int i = captureStart; i < c.CaptureMilliseconds.Count; i++)
            {
                captures.Add(c.CaptureMilliseconds[i]);
            }

            _result["seconds"] = elapsed.ToString("0.0", CultureInfo.InvariantCulture);
            _result["kills"] = kills.ToString(CultureInfo.InvariantCulture);
            _result["failedKills"] = failedKills.ToString(CultureInfo.InvariantCulture);
            _result["rests"] = rests.ToString(CultureInfo.InvariantCulture);
            _result["failedRests"] = failedRests.ToString(CultureInfo.InvariantCulture);
            _result["saves"] = (c.SuccessCount - successStart).ToString(CultureInfo.InvariantCulture);
            _result["failures"] = c.FailureCount.ToString(CultureInfo.InvariantCulture);
            _result["captures"] = captures.Count.ToString(CultureInfo.InvariantCulture);
            _result["captureP95Ms"] = Percentile(captures, 0.95);
            _result["captureMaxMs"] = Percentile(captures, 1.0);
            _result["frames"] = frames.Count.ToString(CultureInfo.InvariantCulture);
            _result["frameMedianMs"] = Percentile(frames, 0.5);
            _result["frameP95Ms"] = Percentile(frames, 0.95);
            _result["frameP99Ms"] = Percentile(frames, 0.99);
            _result["frameMaxMs"] = Percentile(frames, 1.0);
            _result["walkFrames"] = walkFrames.Count.ToString(CultureInfo.InvariantCulture);
            _result["walkFrameP95Ms"] = Percentile(walkFrames, 0.95);
            _result["walkFrameMaxMs"] = Percentile(walkFrames, 1.0);
            _result["over33ms"] = CountOver(frames, 33.4).ToString(CultureInfo.InvariantCulture);
            _result["spikes"] = string.Join(" ", spikes);
            _result["menuCallMs"] = string.Join(" ", _menuMs.ConvertAll(v => v.ToString("0.0", CultureInfo.InvariantCulture)));
            _result["restCallMs"] = string.Join(" ", _restMs.ConvertAll(v => v.ToString("0.0", CultureInfo.InvariantCulture)));
            if (kills == 0 || failedKills > 0 || rests == 0 || c.Status == SaveStatus.Failed || (!Phase6SmokeArgs.NoSave && c.SuccessCount == successStart))
            {
                _result["error"] = "play did not exercise saves (kills=" + kills + " rests=" + rests + " status=" + c.Status + ")";
            }

            Finish();
        }

        private static int CountOver(List<double> values, double limit)
        {
            int n = 0;
            foreach (double v in values)
            {
                if (v > limit)
                {
                    n++;
                }
            }

            return n;
        }

        private IEnumerator KillWithAttacks(Momotaro.Gameplay.Enemy.EnemyActor enemy, Action<bool> done, float seconds = 8f)
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (bundle == null || !bundle.TryResolve(out Momotaro.Gameplay.Player.PlayerRoot root))
            {
                done(false);
                yield break;
            }

            var facing = root.GetComponentInChildren<Momotaro.Gameplay.Player.PlayerFacing>();
            var vitals = root.GetComponentInChildren<Momotaro.Gameplay.Player.PlayerVitalsHolder>();
            float deadline = Time.realtimeSinceStartup + seconds;
            float nextPress = 0f;
            bool pressed = false;
            while (Time.realtimeSinceStartup < deadline && enemy != null && !enemy.IsDefeated)
            {
                // 計測を死亡の解決で止めないよう HP は保つ（測る対象は撃破と保存）。
                if (vitals != null && vitals.Vitals.Health.Current < vitals.Vitals.Health.Max / 2)
                {
                    vitals.Vitals.Health.SetCurrent(vitals.Vitals.Health.Max);
                }

                Vector3 stand = enemy.transform.position + new Vector3(0f, 0f, -1.0f);
                if (root.Body != null)
                {
                    root.Body.position = stand;
                    root.Body.linearVelocity = Vector3.zero;
                }

                root.transform.position = stand;
                facing?.ConfirmFromInput(Vector2.up);
                if (Time.realtimeSinceStartup >= nextPress)
                {
                    pressed = !pressed;
                    InputSystem.QueueStateEvent(_keyboard, pressed ? new KeyboardState(Key.J) : new KeyboardState());
                    nextPress = Time.realtimeSinceStartup + 0.12f;
                }

                yield return null;
            }

            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            bool killed = enemy == null || enemy.IsDefeated;
            if (!killed && !_result.ContainsKey("firstKillFailure"))
            {
                var state = root.GetComponentInChildren<Momotaro.Gameplay.Player.PlayerStateController>();
                _result["firstKillFailure"] = "enemyHp=" + enemy.CurrentHp + " player=" + (state != null ? state.Current.ToString() : "?")
                    + " mode=" + (Momotaro.Gameplay.Modes.GameModeProvider.Current != null
                        ? Momotaro.Gameplay.Modes.GameModeProvider.Current.Current.ToString() : "null")
                    + " timeScale=" + Time.timeScale.ToString("0.###", CultureInfo.InvariantCulture)
                    + " clockFrozen=" + Momotaro.Gameplay.Session.GameplayClockProvider.IsFrozen
                    + " enemyActive=" + enemy.isActiveAndEnabled;
            }

            done(killed);
        }

        private IEnumerator RestAtShrine(CampaignShrineService shrines, Action<bool> done, Action<string> setPhase)
        {
            AreaRuntimeBundle bundle = CurrentAreaProvider.Current;
            if (shrines == null || bundle == null
                || !bundle.TryResolve(out Momotaro.Gameplay.Interaction.ShrinePoint point)
                || !bundle.TryResolve(out Momotaro.Gameplay.Player.PlayerRoot root))
            {
                done(false);
                yield break;
            }

            Vector3 stand = point.InteractionAnchor + new Vector3(0f, 0f, 1.0f);
            stand.y = root.transform.position.y;
            if (root.Body != null)
            {
                root.Body.position = stand;
                root.Body.linearVelocity = Vector3.zero;
            }

            root.transform.position = stand;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;

            // 調べる（お地蔵様の Interact が呼ぶのと同じ窓口）。計測の対象は保存なので、選択の入力は経由しない。
            // 段階を分けて数フレーム空ける：メニューを開く（登録・保存要求・メニューの初回描画）／休息（回復・敵の作り直し・保存要求）。
            setPhase("menu");
            Stopwatch menuWatch = Stopwatch.StartNew();
            shrines.OnShrineInteracted(point.ShrineId);
            menuWatch.Stop();
            _menuMs.Add(menuWatch.Elapsed.TotalMilliseconds);
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            setPhase("rest");
            if (!shrines.IsMenuOpen)
            {
                done(false);
                yield break;
            }

            yield return null;

            // 休息そのもの（全回復・補充・普通敵の作り直し・保存要求）にかかったメインスレッド時間。保存の採取は LateUpdate で別。
            Stopwatch restWatch = Stopwatch.StartNew();
            bool ok = shrines.Rest() == ShrineMenuResult.Rested;
            restWatch.Stop();
            _restMs.Add(restWatch.Elapsed.TotalMilliseconds);
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            setPhase("close");
            shrines.Close();
            yield return null;
            done(ok);
        }

        // ---------------------------------------------------------------- 性能（P6A 25）

        private IEnumerator Perf()
        {
            yield return StartNewGame();
            if (_result.ContainsKey("error"))
            {
                Finish();
                yield break;
            }

            GameSessionState session = GameSessionProvider.Current;
            SaveCoordinator c = Saves.Coordinator;
            int captureStart = c.CaptureMilliseconds.Count;
            var latencies = new List<double>();
            var frames = new List<double>();
            int requests = 0;
            int burstExtra = 0;
            var random = new System.Random(6);

            // 計測中のフレーム時間（ms）。
            bool measuring = true;
            IEnumerator FrameSampler()
            {
                while (measuring)
                {
                    yield return null;
                    frames.Add(Time.unscaledDeltaTime * 1000.0);
                }
            }

            Coroutine sampler = StartCoroutine(FrameSampler());
            for (int i = 0; i < 150; i++)
            {
                // 状態を変える（連続撃破の代わりに版を進める変化）→ 保存要求。
                session.TryConsumeKibidango(1);
                session.RefillKibidango(3);
                double t0 = Time.realtimeSinceStartupAsDouble;
                session.Changes.RequestAutosave("perf");
                requests++;

                // 10 回に 1 回は、書込中に追加要求を重ねる（最新要求の取りこぼしが無いこと）。
                if (i % 10 == 9)
                {
                    Debug.Log("P6A smoke perf " + i + ": burst");
                    float wait = Time.realtimeSinceStartup + 1f;
                    while (!c.IsWriting && Time.realtimeSinceStartup < wait)
                    {
                        yield return null;
                    }

                    session.TryConsumeKibidango(1);
                    session.Changes.RequestAutosave("perf_burst");
                    requests++;
                    burstExtra++;
                }

                float deadline = Time.realtimeSinceStartup + 10f;
                while ((c.IsDirty || c.IsWriting) && c.Status != SaveStatus.Failed && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                latencies.Add((Time.realtimeSinceStartupAsDouble - t0) * 1000.0);
                Debug.Log("P6A smoke perf " + i + ": " + latencies[latencies.Count - 1].ToString("0.0", CultureInfo.InvariantCulture)
                    + " ms, status=" + c.Status + " rev=" + c.Session.Changes.Revision + " saved=" + c.SavedRevision
                    + " mode=" + (Momotaro.Gameplay.Modes.GameModeProvider.Current != null
                        ? Momotaro.Gameplay.Modes.GameModeProvider.Current.Current.ToString() : "null")
                    + " respawn=" + session.Respawn.Phase);
                if (c.Status == SaveStatus.Failed)
                {
                    _result["error"] = "save failed: " + c.LastError;
                    break;
                }

                if (c.Session.Changes.Revision != c.SavedRevision)
                {
                    _result["error"] = "latest revision not saved (" + c.Session.Changes.Revision + " vs " + c.SavedRevision + ")";
                    break;
                }

                int gap = random.Next(2, 10);
                for (int f = 0; f < gap; f++)
                {
                    yield return null;
                }
            }

            measuring = false;
            StopCoroutine(sampler);

            var captures = new List<double>();
            for (int i = captureStart; i < c.CaptureMilliseconds.Count; i++)
            {
                captures.Add(c.CaptureMilliseconds[i]);
            }

            _result["requests"] = requests.ToString(CultureInfo.InvariantCulture);
            _result["burstExtraRequests"] = burstExtra.ToString(CultureInfo.InvariantCulture);
            _result["captures"] = captures.Count.ToString(CultureInfo.InvariantCulture);
            _result["successes"] = c.SuccessCount.ToString(CultureInfo.InvariantCulture);
            _result["failures"] = c.FailureCount.ToString(CultureInfo.InvariantCulture);
            _result["bytes"] = c.LastBytes.ToString(CultureInfo.InvariantCulture);
            _result["captureP95Ms"] = Percentile(captures, 0.95);
            _result["captureMaxMs"] = Percentile(captures, 1.0);
            _result["captureMedianMs"] = Percentile(captures, 0.5);
            _result["latencyP95Ms"] = Percentile(latencies, 0.95);
            _result["latencyMaxMs"] = Percentile(latencies, 1.0);
            _result["latencyMedianMs"] = Percentile(latencies, 0.5);
            _result["frameP95Ms"] = Percentile(frames, 0.95);
            _result["frameMaxMs"] = Percentile(frames, 1.0);
            _result["frameMedianMs"] = Percentile(frames, 0.5);
            _result["frames"] = frames.Count.ToString(CultureInfo.InvariantCulture);
            Finish();
        }

        private static string Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
            {
                return "NaN";
            }

            var sorted = new List<double>(values);
            sorted.Sort();
            int index = (int)Math.Ceiling(p * sorted.Count) - 1;
            index = Mathf.Clamp(index, 0, sorted.Count - 1);
            return sorted[index].ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void Finish()
        {
            WriteResult();

            // 結果は書いた。終了前の保存（と失敗時の選択待ち）は自動確認では行わない——New Game の確認は
            // 自分で SaveBeforeExit を通し終えている。選択待ちで止まるとプロセスが終わらない（実際に踏んだ）。
            CampaignSaveService saves = Saves;
            if (saves != null)
            {
                saves.ChooseQuitWithoutSaving();
            }
            else
            {
                Application.Quit();
            }
        }

        private void WriteResult()
        {
            try
            {
                var sb = new StringBuilder("{");
                bool first = true;
                foreach (KeyValuePair<string, string> kv in _result)
                {
                    sb.Append(first ? "\n  " : ",\n  ");
                    first = false;
                    sb.Append('"').Append(Escape(kv.Key)).Append("\": \"").Append(Escape(kv.Value)).Append('"');
                }

                sb.Append("\n}\n");
                if (!string.IsNullOrEmpty(Phase6SmokeArgs.OutPath))
                {
                    File.WriteAllText(Phase6SmokeArgs.OutPath, sb.ToString(), new UTF8Encoding(false));
                }
            }
            catch (Exception e)
            {
                Debug.LogError("P6A smoke: could not write the result: " + e.Message);
            }
        }

        private static string Escape(string s) =>
            (s ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
    }
}
