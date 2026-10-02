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

            _save = flow.PeekSave();
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

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !_ready || _started)
            {
                return;
            }

            if (_confirmingNewGame)
            {
                if (keyboard.yKey.wasPressedThisFrame)
                {
                    PressNewGame(confirmed: true);
                }
                else if (keyboard.nKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
                {
                    _confirmingNewGame = false;
                }

                return;
            }

            if (keyboard.nKey.wasPressedThisFrame)
            {
                PressNewGame(confirmed: false);
            }
            else if (keyboard.cKey.wasPressedThisFrame)
            {
                PressContinue();
            }
        }

        private void OnGUI()
        {
            if (_started)
            {
                return;
            }

            const float width = 520f;
            var area = new Rect((Screen.width - width) * 0.5f, 120f, width, 260f);
            GUI.Box(area, "桃太郎 P6A 進行・保存試遊");
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 32f, area.width - 32f, area.height - 48f));
            if (!_ready)
            {
                GUILayout.Label("起動中…");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(DescribeSave());
            if (_confirmingNewGame)
            {
                GUILayout.Label("いまの冒険は退避され、はじめからになります。よろしいですか？（Y／N）");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("はい（Y）", GUILayout.Height(28f)))
                {
                    PressNewGame(confirmed: true);
                }

                if (GUILayout.Button("いいえ（N）", GUILayout.Height(28f)))
                {
                    _confirmingNewGame = false;
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                if (GUILayout.Button("はじめから（N）", GUILayout.Height(30f)))
                {
                    PressNewGame(confirmed: false);
                }

                GUI.enabled = _save.CanLoad;
                if (GUILayout.Button("つづきから（C）", GUILayout.Height(30f)))
                {
                    PressContinue();
                }

                GUI.enabled = true;
            }

            GUILayout.Label(_status);
            GUILayout.EndArea();
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
