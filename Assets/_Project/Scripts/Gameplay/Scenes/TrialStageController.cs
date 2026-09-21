using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using UnityEngine;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// P4 専用試遊 Scene の段階制御（P4-08R。v1.0 §7.1・§13.1、レビュー R2-09）。
    ///
    /// 起動直後は<b>自由探索</b>（主人公＋犬丸＋探索地点。敵はまだ生成しない）。P4 限定の試遊開始操作で
    /// <see cref="RequestCombatStart"/> が呼ばれると、<b>探索を解放してから</b>既存の 4 Wave を起動する
    /// （「戦闘開始要求は、探索中断 → 表示代理の解放 → 敵生成／攻撃許可の順」）。
    ///
    /// P3.5 の自動開始（<see cref="WaveRunner"/> の autoStart）は一律には変えない。P4 の Builder が autoStart を切り、
    /// 代わりにこの制御役が開始する。Retry は Scene 再読込なので、新しい Scene は再び自由探索から始まる（§13.1）。
    /// 活動 Context（<see cref="CompanionActivityContext"/>）は本コンポーネントを <see cref="IEncounterStartGate"/> として読み、
    /// 要求前の Preparing を自由探索として供給する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrialStageController : MonoBehaviour, IEncounterStartGate
    {
        [Tooltip("起動する Wave（autoStart は切ってあること）。")]
        [SerializeField] private WaveRunner _waves;

        [Tooltip("探索の調停役（戦闘開始前に依頼を中断・解放する）。")]
        [SerializeField] private InvestigationCoordinator _investigation;

        /// <inheritdoc />
        public bool EncounterRequested { get; private set; }

        /// <summary>配線された Wave（Scene 検査用）。</summary>
        public WaveRunner Waves => _waves;

        /// <summary>配線された探索の調停役（Scene 検査用）。</summary>
        public InvestigationCoordinator Investigation => _investigation;

        /// <summary>配線する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(WaveRunner waves, InvestigationCoordinator investigation)
        {
            if (waves != null)
            {
                _waves = waves;
            }

            if (investigation != null)
            {
                _investigation = investigation;
            }
        }

        /// <summary>
        /// 試遊開始操作。探索中断 → 表示代理の解放（中断に含まれる）→ Wave 起動の順。二重要求は無視して false。
        /// </summary>
        public bool RequestCombatStart()
        {
            if (EncounterRequested)
            {
                return false;
            }

            // 1. 探索を同期的に中断・解放する（v1.0 §13.1「Wave 開始時には探索を解放してから戦闘を起動する」）。
            _investigation?.InterruptAllForCombat();

            // 2. 以後の Preparing は「開始待ちの Encounter」＝戦闘中として供給される。
            EncounterRequested = true;

            // 3. 敵生成／攻撃許可（既存の 4 Wave）。
            _waves?.RequestStartWave();
            return true;
        }

        [ContextMenu("Request Combat Start")]
        private void RequestCombatStartFromMenu()
        {
            RequestCombatStart();
        }
    }
}
