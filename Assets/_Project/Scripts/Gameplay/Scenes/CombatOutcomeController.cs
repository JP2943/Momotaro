using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 勝利・敗北・リトライの統合（Phase3.5 P3.5-08。仕様書 §4.3 / §9）。戦闘開始から終了・再挑戦までを切れ目なく接続する。
    /// 最終 Wave 完了（<see cref="WaveRunner.AllWavesCleared"/>）を Session の Victory へ、Player 死亡由来の Defeat（Session が
    /// 既に遷移）を受けて、結果状態に入った瞬間から残留 Cleanup・結果パネル表示遅延（<see cref="CombatOutcomeTimer"/>）・
    /// Retry 受付遅延を制御する。Retry は <see cref="CombatSessionController.RequestReload"/> 経由で現在 Scene を再読込し、
    /// 二重要求は Session 状態機が拒否する（<see cref="CombatRetryInput"/> が本 Controller の <see cref="RequestRetry"/> を呼ぶ）。
    ///
    /// 結果表示中は Victory・Defeat 双方で入力を停止する（§5.1 Table4 / §9.1）。GameMode を GameOver へ切り替えて Gameplay Action Map を
    /// 閉じ、Player 入力を無効化する（Retry 入力は <see cref="CombatRetryInput"/> が Action Map に依らず受け付ける）。結果表示・Retry 受付は
    /// timeScale に依存しない（<see cref="Time.unscaledDeltaTime"/> 駆動）。Scene API・入力読取・UI 描画には直接触れない（Scene 再読込は
    /// <see cref="ICombatSceneReloader"/>、入力は <see cref="CombatRetryInput"/>、パネルは HUD が本 Controller の
    /// <see cref="ResultVisible"/> を読んで描く）。状態検出はイベントではなくポーリングで行い、EditMode で決定的に駆動できる
    /// （<see cref="Tick"/>）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatOutcomeController : MonoBehaviour
    {
        [SerializeField] private CombatSessionController _session;
        [SerializeField] private WaveRunner _waves;

        [Tooltip("誤入力防止時間（Retry 有効まで。§4.3 初期 0.50s）。")]
        [SerializeField] private float _retryArmDelay = 0.50f;

        [Tooltip("結果パネル表示までの遅延（§9.1 初期 0.75s）。")]
        [SerializeField] private float _panelDelay = 0.75f;

        private CombatOutcomeTimer _timer;
        private CombatSessionState _lastState = CombatSessionState.Preparing;
        private bool _wavesSubscribed;
        private bool _pendingVictory;
        private bool _lastStateInitialized;

        private CombatOutcomeTimer Timer => _timer ??= new CombatOutcomeTimer(_retryArmDelay, _panelDelay);

        /// <summary>結果パネルを表示してよいか（Victory／Defeat 突入から <see cref="_panelDelay"/> 経過）。HUD が読む。</summary>
        public bool ResultVisible => IsResultState(CurrentState) && Timer.ResultVisible;

        /// <summary>Retry 入力を受け付けてよいか（結果突入から <see cref="_retryArmDelay"/> 経過）。入力読取が読む。</summary>
        public bool RetryArmed => IsResultState(CurrentState) && Timer.RetryArmed;

        /// <summary>現在が結果状態（Victory／Defeat）か。</summary>
        public bool IsResult => IsResultState(CurrentState);

        private CombatSessionState CurrentState => _session != null ? _session.State : CombatSessionState.Preparing;

        /// <summary>Session／WaveRunner を注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(CombatSessionController session, WaveRunner waves)
        {
            if (session != null)
            {
                _session = session;
            }

            if (waves != null)
            {
                _waves = waves;
            }

            ResubscribeWaves();
        }

        private void OnEnable()
        {
            ResubscribeWaves();
        }

        private void OnDisable()
        {
            if (_wavesSubscribed && _waves != null)
            {
                _waves.AllWavesCleared -= OnAllWavesCleared;
            }

            _wavesSubscribed = false;
        }

        private void Update()
        {
            // 結果表示・Retry 受付は timeScale に依存させない（万一 HitStop 等で timeScale が 0 でもパネルと受付が進む）。
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>1 フレーム進める（Update から、またはテストが決定的に呼ぶ）。状態のポーリング・結果計時を行う。</summary>
        public void Tick(float deltaTime)
        {
            if (_session == null)
            {
                return;
            }

            // 最終 Wave 完了 → Victory（Playing のときだけ。新規 Enemy 予定なし＝WaveRunner が保証。§9.1）。
            if (_pendingVictory && _session.State == CombatSessionState.Playing)
            {
                _session.ToVictory();
                _pendingVictory = false;
            }

            CombatSessionState s = _session.State;
            if (!_lastStateInitialized || s != _lastState)
            {
                OnStateEntered(s);
                _lastState = s;
                _lastStateInitialized = true;
            }

            if (IsResultState(s))
            {
                Timer.Tick(deltaTime);
            }
        }

        /// <summary>Retry を要求する（受付有効時のみ）。二重要求は Session 状態機が拒否するため再読込は一度だけ発火する。</summary>
        public void RequestRetry()
        {
            if (!RetryArmed)
            {
                return;
            }

            _session.RequestReload();
        }

        private void OnStateEntered(CombatSessionState s)
        {
            switch (s)
            {
                case CombatSessionState.Victory:
                case CombatSessionState.Defeat:
                    Timer.Enter();
                    LockInput();        // 結果表示中は操作不能（§5.1 Table4 / §9.1）。Victory・Defeat 双方で明示的に入力停止。
                    CleanupResiduals(); // 残留 Projectile を掃除（§9.1。Telegraph/Slot は撃破 Cleanup 済み）。
                    break;
                default:
                    Timer.Reset(); // Preparing／Playing／Intermission／Reloading（Loading への切替は Reloader が担う）。
                    break;
            }
        }

        private void OnAllWavesCleared()
        {
            _pendingVictory = true; // 次 Tick で Playing を確認して Victory へ（イベント中に状態機を直接叩かない）。
        }

        private void ResubscribeWaves()
        {
            if (_wavesSubscribed || _waves == null || !isActiveAndEnabled)
            {
                return;
            }

            _waves.AllWavesCleared += OnAllWavesCleared;
            _wavesSubscribed = true;
        }

        private static void LockInput()
        {
            // Gameplay Action Map を閉じて Player 入力を停止する（結果表示中は操作不能。§5.1/§9.1）。GameMode は Bootstrap が
            // 常駐生成するため試遊では常に有効。未起動でも null 安全（no-op）。Retry 入力は Action Map に依らず低レベル Device を直接
            // 読む CombatRetryInput が担うため、GameOver でも受け付けられる。読込完了後は新 Scene の GameplaySceneMode が Exploration
            // を要求して操作可能へ戻る。GameMode 切替は timeScale を変えないため、結果タイマ（unscaled 駆動）とも干渉しない。
            GameModeProvider.Current?.ChangeMode(GameMode.GameOver);
        }

        private static void CleanupResiduals()
        {
            EnemyProjectileRegistry.DespawnAll(); // 冪等・空集合安全。
        }

        private static bool IsResultState(CombatSessionState s)
        {
            return s == CombatSessionState.Victory || s == CombatSessionState.Defeat;
        }
    }
}
