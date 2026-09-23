using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data.World;
using Momotaro.Gameplay.Session;
using System.Collections;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// 統合起動 Scene の起動役（P5-03b 修正。仕様書 v1.1 §3.1／§5.2）。
    ///
    /// 「統合起動 Scene から Play → 新規 P5 Session → A の開始点」を実際に行う。
    /// 以前は目印だけで、統合起動と直開きの初期化順が検証できていなかった（GPT レビュー R1 の中位指摘）。
    ///
    /// <b>薄いことが要点。</b> Session の生成も Area の初期化も既存の経路へ任せ、
    /// ここは「Bootstrap が立ったら A の開始点へ行く」とだけ言う。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Phase5TrialLauncher : MonoBehaviour
    {
        [Tooltip("P5 のエリアカタログ。死亡再開点＝最初の行き先。")]
        [SerializeField] private AreaCatalogData _catalog;

        [Tooltip("起動時に自動で A へ移動するか。テストが手動で進めたい場合は false。")]
        [SerializeField] private bool _travelOnStart = true;

        /// <summary>移動を要求したか（診断・テスト用）。</summary>
        public bool Requested { get; private set; }

        /// <summary>要求が拒否された理由（診断・テスト用）。</summary>
        public AreaTransitionRejection LastRejection { get; private set; }

        /// <summary>起動待ちが終わったか（診断・テスト用）。</summary>
        public bool WaitFinished { get; private set; }

        private void Start()
        {
            if (!_travelOnStart)
            {
                return;
            }

            // すでに起動の成否が確定しているなら待たない（正常系に固定の待ちを足さない）。
            if (BootstrapWait.IsReadyNow)
            {
                WaitFinished = true;
                TryEnterFirstArea();
                return;
            }

            StartCoroutine(EnterWhenBootstrapReady());
        }

        /// <summary>
        /// 常駐の起動完了を待ってから進む。<b>どちらが先に Start しても成立する</b>
        /// （GPT レビュー R2 の指摘 2）。固定フレーム待ちではなく成否の確定を待つ。
        /// </summary>
        private IEnumerator EnterWhenBootstrapReady()
        {
            bool ok = false;
            yield return BootstrapWait.Wait(r => ok = r);
            WaitFinished = true;

            if (!ok)
            {
                GameLog.WarningOnce(LogCategory.Boot, "p5_trial_no_bootstrap",
                    "Phase5TrialLauncher: 常駐サービスが起動しなかったため、エリアへは移動しません。");
                yield break;
            }

            TryEnterFirstArea();
        }

        /// <summary>
        /// 最初のエリアへ入る。カタログの死亡再開点（＝ A の開始点。§3.1）を行き先にする。
        /// </summary>
        public bool TryEnterFirstArea()
        {
            if (_catalog == null)
            {
                GameLog.Error(LogCategory.Boot, "Phase5TrialLauncher: catalog is not wired.");
                return false;
            }

            // Bootstrap が居ないのは「この Scene を単体で開いた」という正常な状態で、異常ではない。
            if (!BootstrapWait.IsReadyNow)
            {
                GameLog.WarningOnce(LogCategory.Boot, "p5_trial_no_bootstrap",
                    "Phase5TrialLauncher: 常駐サービスが起動していないため、エリアへは移動しません"
                    + "（Bootstrap Scene から起動してください）。");
                return false;
            }

            AreaTransitionService transitions = BootstrapServices.Get<AreaTransitionService>();
            if (transitions == null)
            {
                GameLog.WarningOnce(LogCategory.Boot, "p5_trial_no_transition",
                    "Phase5TrialLauncher: 遷移サービスが登録されていないため、エリアへは移動しません。");
                return false;
            }

            // 終端失敗したときの戻り先は「この Scene」。パスを外から差すのではなく、
            // 起動役が自分の居場所を名乗る（§6.3「既存 Launcher へ戻る操作を提示」）。
            if (!string.IsNullOrEmpty(gameObject.scene.path))
            {
                transitions.LauncherScenePath = gameObject.scene.path;
            }

            // 統合起動 Scene には Area が無いので、受付条件は「起動直後の素通し」を使う。
            transitions.Bind(_catalog, new LaunchConditions());

            StableId areaId = _catalog.RespawnAreaId;
            StableId entryId = _catalog.RespawnEntryId;
            AreaTransitionDecision decision = transitions.TryTravel(areaId, entryId);

            Requested = decision.Accepted;
            LastRejection = decision.Rejection;
            return decision.Accepted;
        }

        /// <summary>
        /// 起動時だけの受付条件。Area がまだ無いので §6.1 の Area 条件は評価できない。
        ///
        /// <b>素通しにしてよいのは「まだどのエリアにも居ない」ときだけ。</b>
        /// 到着後は各エリアの <c>AreaTransitionConditionsSource</c> が差し替わり、以降は通常の条件で判定される。
        /// </summary>
        private sealed class LaunchConditions : IAreaTransitionConditions
        {
            public bool IsAreaReady => true;
            public Momotaro.Gameplay.Modes.GameMode Mode => Momotaro.Gameplay.Modes.GameMode.Exploration;
            public bool IsPlayerAlive => true;
            public bool IsPlayerBusy => false;
            public bool IsEncounterActive => false;
        }
    }
}
