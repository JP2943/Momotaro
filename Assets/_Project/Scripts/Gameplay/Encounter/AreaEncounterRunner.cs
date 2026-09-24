using System;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data.Events;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// エリア内 Encounter の調停窓口（P5-07。仕様書 v1.1 §8.2〜§8.4）。
    ///
    /// <b>同じフレームの開始候補を 1 か所で処理する</b>（§8.3 冒頭）。Trigger・Interact・遷移が
    /// それぞれ勝手に判断すると、MonoBehaviour の Update 順で結果が変わる。
    /// 判定はここへ集約し、外からは <see cref="TryStart"/> と通知だけを受ける。
    ///
    /// <b>4 Wave の <c>WaveRunner</c> を書き換えて共用しない</b>（§8.1 末尾）。あれは P3.5／P4 の
    /// 4 Wave 用として維持し、P5 は小さなこの駆動が <see cref="CombatSessionController"/> の
    /// 敵登録・勝敗遷移を利用する。敵 AI・命中処理は複製しない。
    ///
    /// 時間は外部注入（<see cref="Tick"/>）。<c>Update</c> は <c>Tick(Time.deltaTime)</c> を呼ぶだけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaEncounterRunner : MonoBehaviour,
        IPlayerDefeatListener, IAreaEncounterActivitySource, IAreaEncounterState
    {
        [Header("構成（Data が正本。§8.1）")]
        [Tooltip("この区画の Encounter 定義。ID と EnemyIds の正本。")]
        [SerializeField] private EncounterData _encounter;

        [Header("Scene の配線")]
        [Tooltip("既存の戦闘セッション。敵登録・勝敗遷移を利用する（置き換えない）。")]
        [SerializeField] private CombatSessionController _session;

        [Tooltip("主人公の生存。死亡通知の供給元（§8.3 の死亡優先）。")]
        [SerializeField] private PlayerVitalsHolder _playerVitals;

        // Scene が持つ実装。<b>interface のフィールドは serialize されない</b>ので、
        // 保存されるのはこちらの具象参照。実行時は注入（Bind）があればそちらを優先する。
        [Tooltip("受付条件の供給元（§8.2 手順 2）。")]
        [SerializeField] private AreaEncounterConditionsSource _conditionsSource;

        [Tooltip("敵の生成（§8.2 手順 7・8）。")]
        [SerializeField] private AreaEncounterSpawner _spawnerSource;

        [Tooltip("アリーナ境界（§8.2 手順 5）。")]
        [SerializeField] private AreaArenaBoundary _arenaSource;

        [Tooltip("探索の同期撤収（§8.2 手順 4）。")]
        [SerializeField] private EncounterInterruptRelay _interruptSource;

        private IAreaEncounterConditions _injectedConditions;
        private IEncounterSpawner _injectedSpawner;
        private IArenaBoundary _injectedArena;
        private IEncounterInterruptSink _injectedInterrupts;
        private Func<AreaRuntimeState> _areaState;
        private Func<int> _respawnCycle;

        private IAreaEncounterConditions _conditions =>
            _injectedConditions ?? (_conditionsSource != null ? _conditionsSource : null);

        private IEncounterSpawner _spawner =>
            _injectedSpawner ?? (_spawnerSource != null ? _spawnerSource : null);

        private IArenaBoundary _arena =>
            _injectedArena ?? (_arenaSource != null ? _arenaSource : null);

        private IEncounterInterruptSink _interrupts =>
            _injectedInterrupts ?? (_interruptSource != null ? _interruptSource : null);

        private readonly AreaEncounterMachine _machine = new AreaEncounterMachine();
        private EncounterPlan _plan;
        private bool _victoryPending;
        private bool _defeatPending;
        private bool _subscribed;
        private PlayerDefeatChannel _playerDefeats;

        /// <summary>いまの状態。</summary>
        public AreaEncounterState State => _machine.State;

        /// <summary>いまの実行世代（0 は実行していない）。</summary>
        public int RunId => _machine.RunId;

        /// <summary>世代が合わずに捨てた要求の数（診断・テスト用）。</summary>
        public int StaleRequestCount => _machine.StaleRequestCount;

        /// <summary>直近の拒否理由（診断・テスト用）。</summary>
        public EncounterStartRejection LastRejection { get; private set; }

        /// <summary>直近の失敗の補足（表示・診断用）。</summary>
        public string LastFailureDetail { get; private set; } = string.Empty;

        /// <summary>開始に失敗した回数（診断・テスト用）。</summary>
        public int FailedStartCount { get; private set; }

        /// <summary>クリアした回数（診断・テスト用）。</summary>
        public int ClearedCount { get; private set; }

        /// <summary>この Encounter の安定 ID。</summary>
        public StableId EncounterId => _encounter != null ? _encounter.Id : default;

        /// <summary>開始時に固めた構成（診断・テスト用）。</summary>
        public EncounterPlan Plan => _plan;

        /// <summary>配線が揃っているか（Validator・テスト用）。</summary>
        public bool IsWired =>
            _encounter != null && _session != null && _conditions != null
            && _spawner != null && _arena != null;

        /// <summary>結果の短文（§8.4 手順 8。結果パネルで止めない）。</summary>
        public string ResultMessage { get; private set; } = string.Empty;

        /// <summary>
        /// 開始予約中・戦闘中・勝敗処理中か（§6.1 の遷移受付が読む）。
        /// 戦闘の最中にエリア遷移が通ると、敵と境界を残したまま次の Scene へ行ってしまう。
        /// </summary>
        public bool IsEncounterActive => _machine.IsEngaged;

        /// <inheritdoc />
        public CombatSessionState? ActivitySession
        {
            get
            {
                switch (_machine.State)
                {
                    case AreaEncounterState.Starting:
                        return CombatSessionState.Preparing;
                    case AreaEncounterState.Playing:
                    case AreaEncounterState.Resolving:
                        return CombatSessionState.Playing;
                    default:
                        // 開始前・解放後は<b>活動中 Session なし</b>（§8.4 末尾）。
                        // ここで既存 Session の Victory を返すと、仲間が永久停止する。
                        return null;
                }
            }
        }

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(
            EncounterData encounter,
            CombatSessionController session,
            IAreaEncounterConditions conditions,
            IEncounterSpawner spawner,
            IArenaBoundary arena,
            IEncounterInterruptSink interrupts = null,
            Func<AreaRuntimeState> areaState = null,
            Func<int> respawnCycle = null)
        {
            if (encounter != null)
            {
                _encounter = encounter;
            }

            if (session != null)
            {
                _session = session;
            }

            if (conditions != null)
            {
                _injectedConditions = conditions;
                if (conditions is AreaEncounterConditionsSource c)
                {
                    _conditionsSource = c;
                }
            }

            if (spawner != null)
            {
                _injectedSpawner = spawner;
                if (spawner is AreaEncounterSpawner sp)
                {
                    _spawnerSource = sp;
                }
            }

            if (arena != null)
            {
                _injectedArena = arena;
                if (arena is AreaArenaBoundary ab)
                {
                    _arenaSource = ab;
                }
            }

            if (interrupts != null)
            {
                _injectedInterrupts = interrupts;
                if (interrupts is EncounterInterruptRelay relay)
                {
                    _interruptSource = relay;
                }
            }

            _areaState = areaState ?? _areaState;
            _respawnCycle = respawnCycle ?? _respawnCycle;

            Subscribe();
        }

        /// <summary>
        /// Session の世界状態を注入する（P5-03b の初期化担当が呼ぶ）。
        /// 常駐 Session は Scene に serialize できないので、参照ではなく<b>取り出し口</b>を渡す。
        /// </summary>
        public void BindSession(Func<AreaRuntimeState> areaState, Func<int> respawnCycle)
        {
            _areaState = areaState ?? _areaState;
            _respawnCycle = respawnCycle ?? _respawnCycle;
        }

        /// <summary>主人公の生存の供給元を配線する（Scene 構築）。</summary>
        public void BindPlayerVitals(PlayerVitalsHolder vitals)
        {
            if (vitals != null)
            {
                _playerVitals = vitals;
            }

            ResolvePlayerDefeats();
        }

        /// <summary>主人公の死亡通知を購読する（§8.3 の死亡優先）。</summary>
        public void BindPlayerDefeat(PlayerDefeatChannel channel)
        {
            if (ReferenceEquals(_playerDefeats, channel))
            {
                return;
            }

            _playerDefeats?.RemoveListener(this);
            _playerDefeats = channel;
            _playerDefeats?.AddListener(this);
        }

        /// <summary>
        /// 記録からクリア済みを復元する（§4.3）。到着時に一度だけ呼ぶ。
        /// 実行はしていないので世代は進めない。
        /// </summary>
        public void RestoreFromRecord()
        {
            AreaRuntimeState area = _areaState?.Invoke();
            if (area == null || _encounter == null)
            {
                return;
            }

            if (area.IsEncounterCleared(_encounter.Id, CurrentRespawnCycle()))
            {
                _machine.RestoreCleared();
            }
        }

        /// <summary>
        /// 開始を要求する（§8.2）。Trigger・デバッグ操作の入口はこれ 1 つ。
        /// <b>主人公本人の進入かどうかは呼び出し側（Trigger）が判定する</b>（手順 1）。
        /// </summary>
        public EncounterStartDecision TryStart()
        {
            // ---- 手順 2：受付条件 ----
            if (!IsWired)
            {
                return Reject(EncounterStartRejection.NotWired);
            }

            if (!_plan.IsValid && !TryBuildPlan(out string planError))
            {
                return Reject(EncounterStartRejection.NotWired, planError);
            }

            if (!_machine.CanBeginStart)
            {
                return Reject(_machine.State == AreaEncounterState.Cleared
                    ? EncounterStartRejection.AlreadyCleared
                    : EncounterStartRejection.AlreadyRunning);
            }

            if (!_conditions.IsAreaReady)
            {
                return Reject(EncounterStartRejection.AreaNotReady);
            }

            if (!_conditions.IsExploration)
            {
                return Reject(EncounterStartRejection.WrongMode);
            }

            if (!_conditions.IsPlayerAlive)
            {
                return Reject(EncounterStartRejection.PlayerNotAlive);
            }

            if (_conditions.IsTransitioning)
            {
                return Reject(EncounterStartRejection.Transitioning);
            }

            if (IsClearedInRecord())
            {
                _machine.RestoreCleared();
                return Reject(EncounterStartRejection.AlreadyCleared);
            }

            // ---- 手順 3：Starting と RunId を先に確定する ----
            //
            // <b>手順 4 より前に閉じる。</b> 撤収の通知を受けた購読者がその場で開始を要求し直すことがあり、
            // 先に閉じていないと再要求が新しい Starting を作ってしまう（§8.2 手順 3 の狙い）。
            int runId = _machine.BeginStart();
            if (runId == 0)
            {
                return Reject(EncounterStartRejection.AlreadyRunning);
            }

            LastRejection = EncounterStartRejection.None;
            ResultMessage = string.Empty;
            _victoryPending = false;
            _defeatPending = false;

            // ---- 手順 4：調査と代理表示を同期撤収し、探索の所有権を解放する ----
            _interrupts?.InterruptForEncounter();

            // ---- 手順 5：アリーナ境界を有効化する ----
            if (!_arena.TryEnable(out string arenaError))
            {
                return FailStart(runId, EncounterStartRejection.UnsafePlacement, arenaError);
            }

            // ---- 手順 6：GameMode を Combat へ。活動 Context は State から供給される ----
            GameModeProvider.Current?.ChangeMode(GameMode.Combat);

            // ---- 手順 7：敵を非活動で全数生成・登録する ----
            if (!_spawner.TrySpawnAll(_plan, out string spawnError))
            {
                return FailStart(runId, EncounterStartRejection.SpawnFailed, spawnError);
            }

            // ---- 手順 8：全数を確認してから活動を許可する ----
            //
            // 「0 体だから全滅＝勝利」を作らないために、<b>予定数との一致</b>を見る（§8.2 末尾）。
            if (_spawner.SpawnedCount != _plan.PlannedCount)
            {
                return FailStart(runId, EncounterStartRejection.SpawnFailed,
                    "予定 " + _plan.PlannedCount + " 体に対して " + _spawner.SpawnedCount + " 体しか生成できませんでした。");
            }

            _session.StartWave();
            _spawner.ActivateSpawned();
            _machine.MarkPlaying(runId);
            return EncounterStartDecision.Accept(runId);
        }

        /// <summary>
        /// 1 フレーム進める。<b>勝敗の確定はここで行う</b>（§8.3 末尾）。
        ///
        /// 最後の敵の通知でその場で勝利を確定すると、同じ刻みに届いた主人公の死亡通知を
        /// 取りこぼして相打ちが勝利になる。候補を溜めて、刻みの終わりに死亡優先で決める。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!_victoryPending && !_defeatPending)
            {
                return;
            }

            bool defeat = _defeatPending || (_conditions != null && !_conditions.IsPlayerAlive);
            _victoryPending = false;
            _defeatPending = false;

            if (defeat)
            {
                ResolveDefeat();
                return;
            }

            ResolveVictory();
        }

        /// <summary>登録した敵が全滅した（<c>CombatSessionController.AllEnemiesDefeated</c> の購読）。</summary>
        private void OnAllEnemiesDefeated()
        {
            if (_machine.State != AreaEncounterState.Playing)
            {
                return;
            }

            // 予定生成完了を確認する（§8.4 手順 3。生成途中の 0 体を勝利にしない）。
            if (_spawner == null || _spawner.SpawnedCount != _plan.PlannedCount)
            {
                return;
            }

            _victoryPending = true;
        }

        /// <inheritdoc />
        public void OnPlayerDefeated(in PlayerDefeatedEvent defeated)
        {
            if (!_machine.IsEngaged)
            {
                return;
            }

            _defeatPending = true;
        }

        private void ResolveVictory()
        {
            int runId = _machine.RunId;
            if (!_machine.BeginResolve(runId))
            {
                return;
            }

            _session.ToVictory();

            // ---- 手順 4：この再出現周期にクリアを記録する ----
            AreaRuntimeState area = _areaState?.Invoke();
            if (area != null && area.TryMarkEncounterCleared(_plan.EncounterId, CurrentRespawnCycle()))
            {
                ClearedCount++;
            }

            // ---- 手順 5：AI・残留攻撃・Projectile・スロット・ヘイト・一時境界を解放する ----
            ReleaseRuntime();

            // ---- 手順 6：活動中 Encounter なしへ戻し、探索へ復帰する ----
            _machine.MarkCleared(runId);
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);

            // ---- 手順 8：短文。結果パネルや Enter 待ちで止めない ----
            ResultMessage = "戦闘終了";
        }

        private void ResolveDefeat()
        {
            int runId = _machine.RunId;
            if (!_machine.BeginResolve(runId))
            {
                return;
            }

            _session.ToDefeat();

            // 勝利記録は付けない（§8.3 の死亡優先）。撃破済みの徳は取り消さない（§8.5）。
            ReleaseRuntime();
            _machine.MarkDefeated(runId);
            ResultMessage = string.Empty;
        }

        private EncounterStartDecision FailStart(int runId, EncounterStartRejection rejection, string detail)
        {
            // 生成済みを破棄し、登録・境界・モードを元へ戻す（§8.2 末尾）。報酬・クリア記録は付けない。
            ReleaseRuntime();
            _machine.MarkFailed(runId);
            FailedStartCount++;
            LastRejection = rejection;
            LastFailureDetail = detail ?? string.Empty;
            GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            GameLog.Warning(LogCategory.Scene,
                "Encounter start failed: " + rejection + " / " + LastFailureDetail);
            return EncounterStartDecision.Fail(runId, rejection, detail);
        }

        private void ReleaseRuntime()
        {
            _spawner?.ReleaseAll();
            _arena?.Disable();
        }

        private EncounterStartDecision Reject(EncounterStartRejection rejection, string detail = null)
        {
            LastRejection = rejection;
            return EncounterStartDecision.Reject(rejection, detail);
        }

        private bool TryBuildPlan(out string error)
        {
            var plan = new EncounterPlan(_encounter.Id, _encounter.EnemyIds);
            if (!plan.IsValid)
            {
                error = "Encounter '" + _encounter.name + "' の敵構成が空です。";
                return false;
            }

            if (_encounter.IsBossEncounter)
            {
                error = "P5 は Boss 指定の Encounter を扱いません（§8.1）。";
                return false;
            }

            _plan = plan;
            error = null;
            return true;
        }

        private bool IsClearedInRecord()
        {
            AreaRuntimeState area = _areaState?.Invoke();
            return area != null && area.IsEncounterCleared(_plan.EncounterId, CurrentRespawnCycle());
        }

        private int CurrentRespawnCycle()
        {
            if (_respawnCycle != null)
            {
                return _respawnCycle();
            }

            GameSessionState session = GameSessionProvider.Current;
            return session != null ? session.RespawnCycle : 0;
        }

        private void Subscribe()
        {
            if (_subscribed || _session == null)
            {
                return;
            }

            _session.AllEnemiesDefeated += OnAllEnemiesDefeated;
            _subscribed = true;
            ResolvePlayerDefeats();
        }

        /// <summary>
        /// 主人公の死亡通知を、既存 Session と自分の両方へ繋ぐ（<c>WaveRunner</c> と同じ作法）。
        /// チャネルは実行時の実体なので Scene には serialize できない。供給元から取り出す。
        /// </summary>
        private void ResolvePlayerDefeats()
        {
            if (_playerVitals == null)
            {
                return;
            }

            PlayerDefeatChannel channel = _playerVitals.Defeats;
            BindPlayerDefeat(channel);
            _session?.BindPlayerDefeat(channel);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _session == null)
            {
                return;
            }

            _session.AllEnemiesDefeated -= OnAllEnemiesDefeated;
            _subscribed = false;
        }

        private void OnEnable()
        {
            Subscribe();
            _playerDefeats?.AddListener(this);
        }

        private void OnDisable()
        {
            Unsubscribe();
            _playerDefeats?.RemoveListener(this);

            // Scene 離脱で生成物と境界を残さない（§8.4 末尾「次 Scene へ死体を持ち越さない」）。
            ReleaseRuntime();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }
    }
}
