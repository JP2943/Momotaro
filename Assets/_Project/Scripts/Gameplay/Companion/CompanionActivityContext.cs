using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Scenes;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 活動許可の供給元（P4-FIX F05）。Scene に 1 つ置き、既存の正本から現在の許可を組み立てる。
    ///
    /// 読む先は 2 つだけ。
    /// <list type="bullet">
    /// <item><description><see cref="GameModeProvider"/>：Pause・会話・イベントの区別。</description></item>
    /// <item><description><see cref="CombatSessionController"/>：Encounter が開始待ち・継続中か
    /// （<b>Wave の幕間も戦闘中</b>）。</description></item>
    /// </list>
    ///
    /// 戦闘中かどうかを敵の数から数え直したりはしない。判定を作り直すと必ず食い違うので、既存の正本をそのまま使う。
    ///
    /// <b>未配線なら停止を返す。</b>「分からないから通す」にすると配線漏れが最後まで表に出ず、
    /// Pause 中に仲間が動くといった形で実機でだけ露見する。停止していればすぐ気付く。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionActivityContext : MonoBehaviour, ICompanionActivitySource
    {
        [Tooltip("戦闘セッションの正本（未設定なら同じ Scene の探索はせず、Encounter 無しとして扱う）。")]
        [SerializeField] private CombatSessionController _session;

        [Tooltip("戦闘セッションを持たない区画（自由探索など）であることを明示する。")]
        [SerializeField] private bool _noEncounterInThisArea;

        [Tooltip("明示開始の試遊段階（P4-08R。未設定なら P3.5 と同じく自動開始の Scene として扱う）。")]
        [SerializeField] private TrialStageController _stage;

        [Tooltip("P5 の区画 Encounter（§8.4 末尾。開始前・解放後は「活動中 Session なし」を供給する）。")]
        [SerializeField] private AreaEncounterRunner _areaEncounter;

        private IEncounterStartGate _startGateOverride;

        private IEncounterStartGate StartGateSource => _startGateOverride ?? (_stage != null ? _stage : null);

        /// <inheritdoc />
        public CompanionActivity Current
        {
            get
            {
                IGameModeService modes = GameModeProvider.Current;
                if (modes == null)
                {
                    // モードの正本が居ない＝まだ何も初期化されていない。安全側に止める。
                    return CompanionActivity.Stopped;
                }

                if (_areaEncounter == null && _session == null && !_noEncounterInThisArea)
                {
                    // セッションを繋ぎ忘れたのか、本当に無い区画なのかを区別できない。黙って通さない。
                    return CompanionActivity.Stopped;
                }

                // P5 の区画 Encounter が配線されていれば<b>それが正本</b>（§8.4 末尾）。
                //
                // 既存 Session の状態をそのまま読むと、勝利のあと Victory が残り続けるため、
                // 仲間が「戦闘の終端＝入力不可」と読んで永久に停止する。
                // 区画 Encounter は解放後に「活動中 Session なし」を返すので、探索へちゃんと戻れる。
                if (_areaEncounter != null)
                {
                    return CompanionActivityResolver.Resolve(modes.Current, _areaEncounter.ActivitySession);
                }

                CombatSessionState? session = _session != null ? _session.State : (CombatSessionState?)null;

                // 明示開始の試遊段階（P4-08R）：戦闘開始が要求されるまでの Preparing は「開始待ちの Encounter」ではなく
                // 自由探索として供給する（v1.0 §7.1「専用 Scene の自由探索は Encounter 開始前の別の試遊段階として供給」）。
                // 要求されたあとの Preparing は従来どおり戦闘中（開始待ち）。
                IEncounterStartGate gate = StartGateSource;
                if (session == CombatSessionState.Preparing && gate != null && !gate.EncounterRequested)
                {
                    session = null;
                }

                return CompanionActivityResolver.Resolve(modes.Current, session);
            }
        }

        /// <summary>
        /// 配線されている戦闘セッション（未配線なら null。Scene 検査・診断用）。
        /// Scene を保存したあとに「繋がっているか」を外から読めないと、Validator が配線漏れを検出できない。
        /// </summary>
        public CombatSessionController Session => _session;

        /// <summary>戦闘セッションを持たない区画だと宣言されているか（Scene 検査・診断用）。</summary>
        public bool AreaWithoutEncounter => _noEncounterInThisArea;

        /// <summary>
        /// 供給元として成立しているか（セッションが繋がっている、または「無い区画」と宣言されている）。
        /// どちらでもない場合、この Context は常に停止を返す＝仲間が一切動かない。
        /// </summary>
        public bool IsWired => _areaEncounter != null || _session != null || _noEncounterInThisArea;

        /// <summary>戦闘セッションを注入する（Scene 構築・テスト）。</summary>
        public void Bind(CombatSessionController session)
        {
            if (session != null)
            {
                _session = session;
            }
        }

        /// <summary>
        /// 明示開始の門を注入する（P4-08R。null で解除）。門が「まだ要求されていない」と言う間、Preparing を自由探索として扱う。
        /// 門は Scene の明示参照（試遊段階の制御役）から注入する。P3.5 の自動開始 Scene には門が無く、従来どおり動く。
        /// </summary>
        public void SetEncounterStartGate(IEncounterStartGate gate)
        {
            _startGateOverride = gate;
        }

        /// <summary>Scene の明示開始の門（試遊段階の制御役）を配線する（Scene 構築）。</summary>
        public void BindStage(TrialStageController stage)
        {
            _stage = stage;
        }

        /// <summary>注入されている明示開始の門（Scene 検査・診断用）。</summary>
        public IEncounterStartGate StartGate => StartGateSource;

        /// <summary>配線された試遊段階（Scene 検査用）。</summary>
        public TrialStageController Stage => _stage;

        /// <summary>この区画には戦闘セッションが無いことを明示する（自由探索区画）。</summary>
        public void MarkAreaWithoutEncounter()
        {
            _noEncounterInThisArea = true;
        }

        /// <summary>
        /// P5 の区画 Encounter を注入する（§8.4 末尾）。配線されるとこちらが正本になり、
        /// 既存 Session の Victory 残りに引きずられなくなる。
        /// </summary>
        public void BindAreaEncounter(AreaEncounterRunner runner)
        {
            if (runner != null)
            {
                _areaEncounter = runner;
            }
        }

        /// <summary>配線された区画 Encounter（Scene 検査・診断用）。</summary>
        public AreaEncounterRunner AreaEncounter => _areaEncounter;

        private void OnEnable()
        {
            CompanionActivityProvider.Current = this;
        }

        private void OnDisable()
        {
            // 自分が差さっている場合だけ外す（別の Context に差し替わっていたら触らない）。
            if (ReferenceEquals(CompanionActivityProvider.Current, this))
            {
                CompanionActivityProvider.Current = null;
            }
        }
    }
}
