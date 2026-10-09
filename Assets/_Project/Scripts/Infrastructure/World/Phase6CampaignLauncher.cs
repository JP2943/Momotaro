using System.Collections;
using Momotaro.Core.Logging;
using Momotaro.Data.World;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// P6A の試遊のタイトル（P6A-05／06。仕様 §10）。「はじめから」「つづきから」を出す仮 UI。
    ///
    /// <b>New Game には開始時の確認を置く</b>（保存があるとき）。旧案の「最初の手動 Save まで確認を遅らせる」は
    /// オートセーブと両立しない（仕様 §10）。Continue は保存を検証して候補 Session で再開し、
    /// 読めないときは理由を出す（黙って New Game にしない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Phase6CampaignLauncher : MonoBehaviour
    {
        [Tooltip("P6A の campaign カタログ。")]
        [SerializeField] private AreaCatalogData _catalog;

        private bool _ready;
        private bool _confirmingNewGame;
        private string _status = string.Empty;
        private SaveLoadDecision _save;
        private bool _started;

        /// <summary>自動確認は 1 プロセスで 1 回だけ（タイトルへ戻っても再起動しない）。</summary>
        private static bool _smokeStarted;

        /// <summary>カタログ（Builder が配線する）。</summary>
        public AreaCatalogData Catalog
        {
            get => _catalog;
            set => _catalog = value;
        }

        /// <summary>直近の表示文（診断・テスト用）。</summary>
        public string Status => _status;

        /// <summary>保存の状態（診断・テスト用）。</summary>
        public SaveLoadDecision Save => _save;

        private IEnumerator Start()
        {
            if (!BootstrapWait.IsReadyNow)
            {
                bool ok = false;
                yield return BootstrapWait.Wait(r => ok = r);
                if (!ok)
                {
                    _status = "常駐サービスが起動しませんでした。";
                    yield break;
                }
            }

            // 実ビルドの自動確認（P6A 23／25）。引数があるときだけ。保存先の差し替えは保存を覗く（Refresh）より先に当てる。
            bool smoke = !_smokeStarted && Phase6SmokeArgs.TryApply();

            // 「タイトルへ戻る」の行き先をこのタイトルにする（既定は汎用の Launcher）。
            AreaTransitionService transitions = BootstrapServices.Get<AreaTransitionService>();
            if (transitions != null && !string.IsNullOrEmpty(gameObject.scene.path))
            {
                transitions.LauncherScenePath = gameObject.scene.path;
            }

            _ready = true;
            Refresh();
            _menu.Reset(_save.CanLoad ? 1 : 0); // 保存があれば「つづきから」を選んだ状態で開く。

            if (smoke)
            {
                _smokeStarted = true;
                Phase6SmokeDriver.Begin(this);
            }
        }

        private CampaignAdventureFlow Flow => BootstrapServices.Get<CampaignSaveService>()?.Flow;

        /// <summary>保存の状態を読み直す。</summary>
        public void Refresh()
        {
            CampaignAdventureFlow flow = Flow;
            if (flow == null)
            {
                _status = "保存サービスがありません。";
                return;
            }

            _save = flow.PeekSave(_catalog);
        }

        /// <summary>はじめから（保存があれば確認を挟む）。</summary>
        public bool PressNewGame(bool confirmed)
        {
            if (!_ready || _started)
            {
                return false;
            }

            if (_save.Verdict != SaveLoadVerdict.NoSave && !confirmed)
            {
                _confirmingNewGame = true;
                return false;
            }

            _confirmingNewGame = false;
            if (!Flow.TryNewGame(_catalog, out string error))
            {
                _status = "はじめられません：" + error;
                GameLog.Warning(LogCategory.Boot, "New Game failed: " + error);
                return false;
            }

            _started = true;
            _status = "はじめから";
            return true;
        }

        /// <summary>つづきから。</summary>
        public bool PressContinue()
        {
            if (!_ready || _started)
            {
                return false;
            }

            CampaignAdventureFlow flow = Flow;
            if (!flow.TryContinue(_catalog, out string error))
            {
                _status = "つづきから始められません：" + error;
                return false;
            }

            _started = true;
            _status = string.IsNullOrEmpty(flow.RecoveryNotice)
                ? "つづきから"
                : "保存の片側が壊れていたため、もう一方から読みました。";
            return true;
        }

        private readonly PadMenuNavigator _menu = new PadMenuNavigator();

        private void Update()
        {
            if (!_ready || _started)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (_confirmingNewGame)
            {
                // 0：はい（退避してはじめる） 1：いいえ
                int chosen = _menu.Poll(2, null, out bool cancelled);
                bool yes = chosen == 0 || (keyboard != null && keyboard.yKey.wasPressedThisFrame);
                bool no = chosen == 1 || cancelled
                    || (keyboard != null && (keyboard.nKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame));
                if (yes)
                {
                    PressNewGame(confirmed: true);
                }
                else if (no)
                {
                    CancelConfirm();
                }

                return;
            }

            // 0：はじめから 1：つづきから（保存が読めるときだけ）
            int item = _menu.Poll(2, new[] { true, _save.CanLoad }, out _);
            if (item == 0 || (keyboard != null && keyboard.nKey.wasPressedThisFrame))
            {
                AskNewGame();
            }
            else if (item == 1 || (keyboard != null && keyboard.cKey.wasPressedThisFrame))
            {
                PressContinue();
            }
        }

        private void AskNewGame()
        {
            bool wasConfirming = _confirmingNewGame;
            PressNewGame(confirmed: false);
            if (_confirmingNewGame && !wasConfirming)
            {
                _menu.Reset(1); // 確認は「いいえ」から（うっかり上書きしない）。
            }
        }

        private void CancelConfirm()
        {
            _confirmingNewGame = false;
            _menu.Reset(0);
        }

        private int _prewarmFrames;

        /// <summary>仮 UI の文字の先描きが済んだか（実ビルド確認が待つ）。</summary>
        public bool PrewarmDone => _prewarmFrames >= 3;

        private string _storyCharacters;

        /// <summary>
        /// 会話 Data の文字も先に描く（P7。会話の初回表示で日本語の文字の準備に止まらないように。P6A F04 と同じ理由）。
        /// </summary>
        private void DrawStoryPrewarm()
        {
            Momotaro.Data.Story.CampaignStoryData story = _catalog != null ? _catalog.Story : null;
            if (story == null)
            {
                return;
            }

            if (_storyCharacters == null)
            {
                var seen = new System.Collections.Generic.HashSet<char>();
                var sb = new System.Text.StringBuilder();
                void Add(string text)
                {
                    if (string.IsNullOrEmpty(text))
                    {
                        return;
                    }

                    foreach (char c in text)
                    {
                        if (seen.Add(c))
                        {
                            sb.Append(c);
                        }
                    }
                }

                foreach (Momotaro.Data.Story.VillagerDefinition v in story.Villagers)
                {
                    Add(v?.DisplayName);
                }

                foreach (Momotaro.Data.Story.DialogueDefinition d in story.Dialogues)
                {
                    Add(d?.Speaker);
                    Add(d?.ConfirmLabel);
                    if (d != null)
                    {
                        foreach (string page in d.Pages)
                        {
                            Add(page);
                        }
                    }
                }

                foreach (Momotaro.Data.Story.QuestDefinition q in story.Quests)
                {
                    if (q == null)
                    {
                        continue;
                    }

                    Add(q.DisplayName);
                    Add(q.ObjectiveText);
                    Add(q.ProgressText);
                    Add(q.ReportText);
                    foreach (string page in q.OfferPages)
                    {
                        Add(page);
                    }

                    foreach (Momotaro.Data.Story.QuestObjective o in q.Objectives)
                    {
                        Add(o?.Label);
                    }
                }

                foreach (Momotaro.Data.Story.StoryEventDefinition e in story.Events)
                {
                    Add(e?.DisplayName);
                    Add(e?.LockedNotice);
                }

                foreach (Momotaro.Data.Story.ChapterDefinition c in story.Chapters)
                {
                    Add(c?.DisplayName);
                }

                Add("依頼の話を聞く受ける今は受けない報告して報酬を受け取る閉じる目的報酬徳次へ一覧進行中報告できる受領済み未受注章クリア済み未クリア標準困難な道記録なし払い戻し権利上限のため増えません");
                _storyCharacters = sb.ToString();
            }

            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0f);
            var rect = new Rect(0f, 0f, 4000f, 40f);
            GUI.Label(rect, _storyCharacters);
            GUI.Button(rect, _storyCharacters);
            GUI.Box(rect, _storyCharacters);
            GUI.color = previous;
        }

        private void OnGUI()
        {
            if (_started)
            {
                return;
            }

            _menu.BeginScaled();

            // 仮 UI の文字を先に描いておく（ゲーム中の初回メニューで止まらないように。PadMenuNavigator.PrewarmCharacters）。
            if (_prewarmFrames < 3)
            {
                PadMenuNavigator.DrawPrewarm();
                DrawStoryPrewarm();
                if (Event.current.type == EventType.Repaint)
                {
                    _prewarmFrames++;
                }
            }

            const float width = 520f;
            var area = new Rect((PadMenuNavigator.VirtualWidth - width) * 0.5f, 60f, width, 270f);
            GUI.Box(area, "桃太郎 P6A 進行・保存試遊");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 30f, area.width - 32f, area.height - 40f));
            if (!_ready)
            {
                GUILayout.Label("起動中…");
                GUILayout.EndArea();
                _menu.EndScaled();
                return;
            }

            GUILayout.Label(DescribeSave());
            if (_confirmingNewGame)
            {
                GUILayout.Label("いまの冒険は退避され、はじめからになります。よろしいですか？");
                if (_menu.DrawItem(0, "はい（Y）"))
                {
                    PressNewGame(confirmed: true);
                }

                if (_menu.DrawItem(1, "いいえ（N）"))
                {
                    CancelConfirm();
                }
            }
            else
            {
                if (_menu.DrawItem(0, "はじめから（N）"))
                {
                    AskNewGame();
                }

                if (_menu.DrawItem(1, "つづきから（C）", _save.CanLoad))
                {
                    PressContinue();
                }
            }

            GUILayout.Label(_status);
            GUILayout.FlexibleSpace();
            GUILayout.Label(PadMenuNavigator.Hint);
            GUILayout.EndArea();
            _menu.EndScaled();
        }

        private string DescribeSave()
        {
            switch (_save.Verdict)
            {
                case SaveLoadVerdict.NoSave:
                    return "保存はありません。";
                case SaveLoadVerdict.Ok:
                case SaveLoadVerdict.RecoveredFromOtherSide:
                    return "保存：世代 " + _save.Chosen.Info.Generation + "（" + _save.Chosen.Info.SavedAtUtc + "）";
                default:
                    return "保存を読めません（" + _save.Verdict + "）：" + _save.Detail;
            }
        }
    }
}
