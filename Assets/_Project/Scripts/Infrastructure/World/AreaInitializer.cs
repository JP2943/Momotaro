using System.Collections;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Core.World;
using Momotaro.Data.World;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Encounter;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using Momotaro.Gameplay.Transfer;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// エリア Scene の初期化担当（P5-03b。仕様書 v1.1 §5.1）。<b>薄い Infrastructure 層</b>で、
    /// 起動確認・Session 取得・注入・復元・AreaReady の確定までを決まった順に行う。
    ///
    /// 順序（§5.1）：
    /// <list type="number">
    /// <item><description>Bootstrap のサービス初期化完了を確認する。</description></item>
    /// <item><description>Session 種別を決め、既存を再利用、なければ新規生成。</description></item>
    /// <item><description>Area 定義と必須参照を検証する。</description></item>
    /// <item><description>Progress／Area 記録／活動 Context を注入する。</description></item>
    /// <item><description>入口位置・向き、Actor 値、門、クリア済み Encounter を復元する。</description></item>
    /// <item><description>報酬・Feedback・HUD・入力の購読を接続する。</description></item>
    /// <item><description>カメラを即時配置する。</description></item>
    /// <item><description>AreaReady を確定して活動・入力を許可する。</description></item>
    /// </list>
    ///
    /// <b>「1 フレーム待てば大丈夫」に依存しない。</b> 各段を明示的に順番に呼び、
    /// 途中で失敗したら Ready を確定せずに理由を残す。初期化前の Actor 更新・報酬購読は
    /// <see cref="AreaContext.IsAreaReady"/> と Gameplay 時計のゲートが止める。
    ///
    /// <b>上の順序は「初回だけの構築」と「入場ごとの準備」が混ざっている</b>（P5.5 §6.2 手順 5。
    /// 工程 P55-10b）。入場ごとに Scene を読み直していたあいだは、それで区別する必要が無かった
    /// ——Scene が新品なら「初回」しか無い。旧 Area を<b>保持して再利用する</b>と
    /// （§6.2 手順 11）二度目の入場が起きるので、どちらなのかを言い分ける必要が出た。
    ///
    /// <list type="table">
    /// <item><term><see cref="EnsureSceneBuilt"/>（初回だけ）</term>
    /// <description>Session の取得、進行・調査記録の注入、カタログと接続の受け渡し、
    /// 死亡再開の実行役と Encounter の取り出し口。<b>この Scene が生きているあいだ変わらないもの。</b></description></item>
    /// <item><term><see cref="PrepareForEntry"/>（入場ごと）</term>
    /// <description>入口の解決と配置、Actor 値の復元、門・調査・Encounter の記録の反映、
    /// 出入口の跳ね返り止め、前の入場の途中動作の破棄、到着世代の報告。
    /// <b>留守のあいだに変わりうるもの。</b></description></item>
    /// </list>
    ///
    /// <b>どちらに置くかを間違えると、二重処理か未反映のどちらかになる。</b>
    /// 受入条件は「速いか」ではなく、<b>読み直していた従来とゲーム上の結果が変わらないか</b>
    /// （§11 の P17）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaInitializer : MonoBehaviour
    {
        [Header("このエリア")]
        [Tooltip("AreaId・入口の明示参照を持つ根。")]
        [SerializeField] private AreaRoot _areaRoot;

        [Tooltip("初期化状態（AreaReady）。")]
        [SerializeField] private AreaContext _context;

        [Tooltip("遷移の受付条件を読む窓口。")]
        [SerializeField] private AreaTransitionConditionsSource _conditions;

        [Header("注入先")]
        [Tooltip("徳の窓口。Session の PlayerProgressState を Bind する。")]
        [SerializeField] private PlayerProgressHolder _progress;

        [Tooltip("調査記録の窓口。Area 記録を Bind する。")]
        [SerializeField] private InvestigationRecordHolder _record;

        [Tooltip("Actor 値の採取・復元の窓口（§4.4〜§4.6）。")]
        [SerializeField] private AreaActorTransferPort _transferPort;

        [Tooltip("この区画の Encounter（§8。戦闘の無い区画は未割当でよい）。")]
        [SerializeField] private AreaEncounterRunner _encounter;

        [Tooltip("本編型死亡再開の実行役（P5-08。§9.1）。")]
        [SerializeField] private CampaignRespawnRunner _respawn;

        [Tooltip("普通敵の生成と撃破記録（P6A。普通敵の居ない区画は未割当でよい）。")]
        [SerializeField] private AreaFieldEnemyDirector _fieldEnemies;

        [Tooltip("1 エリアに複数の遭遇戦があるときのまとめ（P6A。無ければ未割当）。")]
        [SerializeField] private AreaEncounterGroup _encounterGroup;

        [Header("カタログ")]
        [Tooltip("P5 のエリアカタログ。遷移サービスへ渡す。")]
        [SerializeField] private AreaCatalogData _catalog;

        [Tooltip("エリア接続の一覧（P5.5 §3.1）。P5 の構成では未設定でよい。")]
        [SerializeField] private AreaConnectionData _connections;

        /// <summary>この Area が渡すカタログ（Validator・テスト用）。</summary>
        public AreaCatalogData Catalog => _catalog;

        /// <summary>この Area が渡す接続一覧（Validator・テスト用。無ければ null）。</summary>
        public AreaConnectionData Connections => _connections;

        /// <summary>普通敵の生成役（P6A。未割当なら null）。</summary>
        public AreaFieldEnemyDirector FieldEnemies => _fieldEnemies;

        /// <summary>配線する（Builder が呼ぶ。P6A）。</summary>
        public void BindFieldEnemies(AreaFieldEnemyDirector director)
        {
            _fieldEnemies = director;
        }

        /// <summary>複数遭遇戦のまとめを配線する（Builder が呼ぶ。P6A）。</summary>
        public void BindEncounterGroup(AreaEncounterGroup group)
        {
            _encounterGroup = group;
        }

        /// <summary>複数遭遇戦のまとめ（P6A。未割当なら null）。</summary>
        public AreaEncounterGroup EncounterGroup => _encounterGroup;

        /// <summary>初期化が成功したか（診断・テスト用）。</summary>
        public bool Initialized { get; private set; }

        /// <summary>
        /// この Scene インスタンスの構築が済んだか（診断・テスト用。工程 P55-10b）。
        /// </summary>
        public bool SceneBuilt { get; private set; }

        /// <summary>
        /// 構築を行った回数（診断・テスト用）。<b>1 を超えたら「一度だけ」が壊れている。</b>
        /// </summary>
        public int BuildCount { get; private set; }

        /// <summary>
        /// 入場準備を行った回数（診断・テスト用）。保持した Area へ戻るたびに増える。
        /// <b>ここが増えないまま再利用されるのが、裁定 2 で塞いだ穴である。</b>
        /// </summary>
        public int EntryCount { get; private set; }

        /// <summary>失敗の理由（診断・テスト用。成功なら空）。</summary>
        public string FailureReason { get; private set; } = string.Empty;

        /// <summary>このエリアの安定 ID。</summary>
        public StableId AreaId => _areaRoot != null ? _areaRoot.AreaId : default;

        /// <summary>起動待ちが終わったか（診断・テスト用）。</summary>
        public bool WaitFinished { get; private set; }

        /// <summary>
        /// 初期化を始めた時点で捕まえた到着トークン（診断・テスト用）。
        /// 0 は「このエリア宛ての生きた到着要求が無い」。直開きとは限らない。
        /// </summary>
        public int ArrivalToken { get; private set; }

        /// <summary>
        /// 直開きの自己許可を断ったか（診断・テスト用）。
        /// 終端失敗で放棄された遷移のあとに、遅れて読み終わった Scene がここに来る。
        /// </summary>
        public bool SelfActivationBlocked { get; private set; }

        // 構築のときに解決して持ち回るもの（入場ごとに引き直さない）。
        private GameSessionBootService _sessions;
        private GameSessionState _session;
        private AreaTransitionService _transitions;

        private void Start()
        {
            // すでに起動の成否が確定しているなら待たない。
            // <b>正常系に固定の待ちを足さない</b>（§6.3 末尾）。待つのは確定していないときだけ。
            if (BootstrapWait.IsReadyNow)
            {
                WaitFinished = true;
                Initialize();
                return;
            }

            StartCoroutine(InitializeWhenBootstrapReady());
        }

        /// <summary>
        /// 常駐の起動完了を待ってから初期化する（GPT レビュー R2 の指摘 2）。
        /// Bootstrap も Scene 側も <c>Start()</c> で動くので、順序に頼ると
        /// 「先に動いた側が諦めて、あとから常駐が立っても何も起きない」が起きる。
        /// </summary>
        private IEnumerator InitializeWhenBootstrapReady()
        {
            bool ok = false;
            yield return BootstrapWait.Wait(r => ok = r);
            WaitFinished = true;

            if (!ok)
            {
                Fail("常駐サービスが起動しなかったため初期化できません。");
                yield break;
            }

            Initialize();
        }

        /// <summary>
        /// 初期化を実行する（テストから明示的に呼べるよう分離）。
        ///
        /// 中身は <see cref="PrepareForEntry"/>——<b>初回の Scene 構築と、入場ごとの準備処理は
        /// 同じ手順の中に並んでいて、二度目からは構築の段だけを飛ばす</b>（§6.2 手順 5）。
        /// 名前を残しているのは、起動経路と既存テストがこれを初期化の入口として見ているため。
        /// </summary>
        public bool Initialize() => PrepareForEntry();

        /// <summary>
        /// <b>この Area へ入場するたび</b>に走る準備処理（P5.5 §6.2 手順 5。工程 P55-10b）。
        ///
        /// <b>以前はここが一度しか走らなかった。</b> 冒頭に <c>if (Initialized) return true;</c> があり、
        /// <c>Start()</c> も二度目は呼ばれない。入場ごとに Scene を読み直していたあいだは
        /// それで正しかった——Scene が新品なら「初回」しか無い。
        /// 旧 Area を<b>保持して再利用する</b>と（§6.2 手順 11）、二度目の入場で
        /// <b>入口配置・最新 Snapshot の復元・門／調査／Encounter の反映・到着世代の報告が
        /// まるごと落ちる</b>。それが GPT 裁定 2 の指摘した穴である。
        ///
        /// だから<b>初回だけの構築</b>（<see cref="EnsureSceneBuilt"/>：部品の配線と参照解決）と
        /// <b>入場ごとの準備</b>（ここ）を分けた。順序は §5.1 のままで、
        /// 二度目からは構築の段が何もしないだけである。
        ///
        /// <b>受入条件は「速いか」ではなく「同じ結果か」。</b> 保持して再利用しても、
        /// 読み直していた従来とゲーム上の結果が変わらないことを要求する（§11 の P17）。
        /// </summary>
        public bool PrepareForEntry()
        {
            // 1. 必須参照の検証（Scene に触る前に落とす）。
            if (_areaRoot == null || _areaRoot.Definition == null || _context == null)
            {
                return Fail("AreaRoot／AreaDefinition／AreaContext が未配線です。");
            }

            if (_catalog == null)
            {
                return Fail("Area カタログが未配線です。");
            }

            StableId areaId = _areaRoot.AreaId;

            // 入場ごとに言い直す値は、入場ごとに 0 へ戻す。
            // 前の入場の「遅れて着いた」判定が残ると、二度目の入場が不当に活動を断られる。
            SelfActivationBlocked = false;

            // 初期化を「始めた時点の」到着要求を捕まえる（GPT レビュー R2 の指摘 4）。
            // 完了時に共有領域から読み直すと自分自身との比較になり、照合の意味が無くなる。
            int arrivalToken = CaptureArrivalToken(areaId);
            ArrivalToken = arrivalToken;

            // 2. 到着先の入口を決める。遷移で来たならその入口、直開きなら既定入口（§5.2）。
            //    <b>入場ごとに決め直す。</b> 同じ Area へ別の入口から入ることがある。
            StableId entryId = ResolveEntryId(areaId);
            if (entryId.IsEmpty)
            {
                return Fail("到着する入口を解決できません（area=" + areaId.Value + "）。");
            }

            if (!_areaRoot.TryGetEntryPoint(entryId, out AreaEntryPoint entryPoint))
            {
                return Fail("入口 '" + entryId.Value + "' が Scene にありません。");
            }

            _context.BeginInitialize(areaId, entryId);

            // 3. 初回だけの構築（Session・注入・カタログ・接続・死亡再開の配線）。
            if (!EnsureSceneBuilt())
            {
                return false;
            }

            GameSessionState session = _session;
            AreaRuntimeState area = session.GetOrCreateArea(areaId);

            // <b>ここで訪問済みにしない</b>（P5.5 §4.1／§11 の E06。工程 P55-04b）。
            // 記録するのは活動を許可した所有者（<see cref="AreaTransitionService.NoteArrival"/>）で、
            // 所有者が居ない直開きだけ下で自分が記録する。

            // 4. <b>前の入場の残りを捨てる。</b> 保持した Area へ戻ると同じ Actor へ帰ってくるので、
            //    出て行ったときの攻撃モーション・構え・先行入力・向きのロックがそのまま残る。
            //    値には触らない（下の復元が正本）。順は 中立化 → 配置 → 復元 で固定する。
            _transferPort?.ResetForAreaEntry();

            //    成長の効果を<b>基礎値と取得 ID から</b>置き直す（P6A。加算を繰り返さない）。値の復元より前——
            //    最大値が古いままだと、運ばれてきた HP が上限超過で拒否される。
            CampaignCatalog campaign = _transitions.Catalog != null ? _transitions.Catalog.Campaign : null;
            if (campaign != null)
            {
                // テスト専用の調整（P6 の検証 campaign だけが 1 以外を持つ）：基礎最大 HP の倍率 → 成長の加算の順。
                _transferPort?.SetPlayerMaxHpScale(campaign.TestPlayerMaxHpScale);
                _transferPort?.ApplyMaxHpBonus(campaign.MaxHpBonusOf(session.Progress));
                _fieldEnemies?.SetEnemyAttackPowerScale(campaign.TestEnemyAttackScale);
                _encounterGroup?.SetEnemyAttackPowerScale(campaign.TestEnemyAttackScale);
                _encounter?.SetEnemyAttackPowerScale(campaign.TestEnemyAttackScale);
            }

            // 5. 入口へ配置し、運ばれてきた Actor 値を復元する（§4.4〜§4.6）。
            //    値の復元は AreaReady より前。1 つでも失敗したら Ready を確定しない。
            PlaceArrivals(entryPoint, definitionFacing: ResolveFacing(entryId));

            // 死亡再開で着いたなら、運ばれてきた値ではなく<b>全回復</b>を適用する（§9.1 手順 6）。
            bool respawnArrival = session.Respawn.Phase == CampaignRespawnPhase.Requested;
            if (respawnArrival)
            {
                if (_transferPort == null)
                {
                    return Fail("死亡再開で到着しましたが、Actor を復帰させる窓口が未配線です。");
                }

                _transitions.ClearPendingTransfer();
                _transferPort.RestoreForCampaignRespawn();

                // <b>ここで完了扱いにしない</b>（GPT レビュー R6 の指摘 1）。
                // 完了の確定は AreaTransitionService が活動を許可したあとに行う（§9.1 手順 7）。
            }
            else if (_transitions.TryPeekPendingLoad(out Momotaro.Gameplay.Save.PartySaveValues saved))
            {
                // 保存からの再開（P6A-02）。保存値を<b>遷移の復元と同じ検証付きの窓口</b>で適用する。
                // 失敗したら Ready を確定しない——候補 Session は採用されず、ファイルも変わらない。
                if (_transferPort == null)
                {
                    return Fail("保存から再開しましたが、Actor を復元する窓口が未配線です。");
                }

                if (!_transferPort.TryApplySaveValues(saved))
                {
                    return Fail("保存値を復元できませんでした: " + _transferPort.LastApplyFailure);
                }
            }
            else if (_transitions.TryPeekPendingTransfer(out AreaTransferSnapshot transfer))
            {
                if (_transferPort == null)
                {
                    return Fail("Actor 値が運ばれてきましたが、復元する窓口が未配線です。");
                }

                if (!_transferPort.TryApply(transfer))
                {
                    return Fail("Actor 値を復元できませんでした: " + _transferPort.LastApplyFailure);
                }
            }

            // 6. 門の開通を記録から復元する（§4.3／§7.3）。
            //    復元は「すでに開いていた」のであって、いま開通したのではないので通知は出さない。
            //    ここで失敗したら Ready を確定しない：見た目だけ開いて通れない状態を受入にしない。
            //    <b>入場ごとに押し直す。</b> 保持していた側の Area でも、留守のあいだに
            //    記録が変わっていることがある（同じ Flag を別の Area のレバーが開ける構成）。
            //    適用済みなら回数は増えない（<see cref="Momotaro.Gameplay.Interaction.AreaFlagDoor.TryApplyOpened"/>）。
            foreach (Momotaro.Gameplay.Interaction.AreaFlagDoor door in _areaRoot.Doors)
            {
                if (door == null || !area.IsOpen(door.FlagId))
                {
                    continue;
                }

                if (!door.TryApplyOpened(out string doorError))
                {
                    return Fail("開通済みの門を復元できませんでした: " + doorError);
                }
            }

            // 7. Encounter のクリア済みを復元する（§4.3／§8.4 末尾）。
            //    取り出し口の配線は構築側（一度だけ）、記録の反映は<b>入場ごと</b>。
            //    撤退の確定（P6A-04）：挑戦途中のまま出て行った遭遇戦は、戻ってきたら最初の Wave から。
            //    <b>退出が成功したからここへ来ている</b>——移動に失敗した場合は出発側の挑戦がそのまま続く。
            if (_encounter != null && _encounter.AllowRetreat)
            {
                _encounter.AbandonChallenge();
            }

            _encounterGroup?.AbandonUnfinished();
            _encounter?.RestoreFromRecord();
            _encounterGroup?.RestoreAllFromRecord();

            //    普通敵（P6A）：現在周期で撃破済みでない配置だけを、<b>起こさずに</b>用意する。
            //    留守のあいだに周期が進んでいれば（休息）ここで作り直す。起こすのは活動許可のとき。
            _fieldEnemies?.PrepareForEntry();

            //    <b>遭遇 Trigger の「範囲内」も測り直す</b>（工程 P55-15b。付録 C.41）。
            //
            //    出入口と同じ理由である——<c>OnTriggerEnter</c>／<c>Exit</c> は
            //    その MonoBehaviour が有効な間しか届かず、活動ゲートが止めている間の退出は届かない。
            //    こちらの残留は逆向きに出る：true で固まると <c>OnTriggerEnter</c> が即 return するので、
            //    <b>未クリアの遭遇戦が再入場後に二度と始まらない</b>。
            //
            //    <b>工程 P55-15a では繋ぎ忘れていた。</b> メソッドは足したのに、ここから呼んでいなかった
            //    （記録 038 §1「API があることと、繋がっていることは別」）。
            foreach (Momotaro.Gameplay.Encounter.AreaEncounterTrigger trigger
                     in _areaRoot.EncounterTriggers)
            {
                trigger?.ResyncOccupancy();
            }

            // 8. 到着直後の跳ね返りを止める（§6.1 末尾）。
            //    入口 Trigger の中に立った状態で到着するのが普通なので、
            //    一度出るまで出入口は要求を出さない。押しっぱなしを新しい押下と解釈しない。
            //    <b>入場ごとに必要。</b> 落とすと、保持した Area へ戻った瞬間に来た道へ跳ね返る。
            foreach (AreaExitGate gate in _areaRoot.ExitGates)
            {
                if (gate != null)
                {
                    // <b>順は「測り直し → 解除待ち」。</b> 測り直しは範囲内を事実へ合わせ、
                    // 解除待ちは持ち込んだ入力を無効にする——別のことなので両方呼ぶ
                    // （工程 P55-15a。試遊報告①）。
                    gate.ResyncOccupancy();
                    gate.DisarmOnArrival();
                }
            }

            // 9. 準備できたことを報告する。<b>活動の許可はここで出さない</b>（GPT レビュー R2 の指摘 1）。
            //    許可は世代・対象・タイムアウトを確認した所有者＝遷移サービスが出す。
            _context.MarkPrepared();

            if (arrivalToken != 0)
            {
                // 遷移で来た。開始時に捕まえたトークンで報告し、所有者の許可を待つ。
                AreaPendingArrival.TryMarkPrepared(arrivalToken, areaId, entryId);
            }
            else if (AreaPendingArrival.SelfActivationAllowed)
            {
                // 直開き（遷移が 1 つも走っていない起動）。所有者が居ないので自分で許可する。
                // 訪問済みの記録も、許可を出す者＝自分が行う。
                session.MarkVisited(areaId);
                _context.Activate();
                GameModeProvider.Current?.ChangeMode(GameMode.Exploration);
            }
            else
            {
                // <b>トークンが無い＝直開き、ではない</b>（GPT レビュー R3 の指摘 1）。
                // 遷移が終端失敗して放棄されたあと、キャンセルできないロードが遅れて
                // この Scene を読み終えた場合もここに来る。許可を出す所有者はもう居ないので、
                // 準備だけ済ませて<b>活動させない</b>。プレイヤーは Error 表示から Launcher へ戻る。
                SelfActivationBlocked = true;
                AreaPendingArrival.NoteBlockedSelfActivation();
                GameLog.Warning(LogCategory.Scene,
                    "Arrived after the transition was abandoned; not activating this area: " + areaId.Value);
            }

            EntryCount++;
            Initialized = true;
            FailureReason = string.Empty;
            GameLog.Info(LogCategory.Scene,
                "Area ready: " + areaId.Value + " / " + entryId.Value + " (entry " + EntryCount + ")");
            return true;
        }

        /// <summary>
        /// <b>この Scene インスタンスにつき一度だけ</b>の構築（P5.5 §6.2 手順 5。工程 P55-10b）。
        ///
        /// 入れるのは<b>部品の配線と参照解決</b>だけである。Session の取得、進行・調査記録の注入、
        /// カタログと接続の受け渡し、死亡再開の実行役と Encounter の取り出し口。
        /// どれも「この Scene が生きているあいだ変わらないもの」で、入場ごとにやり直す意味が無い。
        ///
        /// <b>記録から State を反映する処理はここに入れない。</b> 門・調査・Encounter・入口配置は
        /// 留守のあいだに変わりうるので、入場ごとに <see cref="PrepareForEntry"/> が反映する。
        /// 「一度だけ」と「毎回」をここで間違えると、二重処理か未反映のどちらかになる。
        /// </summary>
        private bool EnsureSceneBuilt()
        {
            if (SceneBuilt)
            {
                // Session が入れ替わっていたら、注入済みの参照はもう正本ではない。
                // New Game は Single 読込を伴うのでこの Scene ごと消えるため、通常は起こらない。
                // 起こったときに<b>静かに古い State を使い続けない</b>ために見ておく。
                GameSessionState current = _sessions != null ? _sessions.EnsureSession(_catalog.EncounterClearPolicy) : null;
                if (current == null || !ReferenceEquals(current, _session))
                {
                    return Fail("Session が入れ替わっています（この Scene の注入はもう正本ではありません）。");
                }

                return true;
            }

            StableId areaId = _areaRoot.AreaId;

            // Session を用意する（既存があれば再利用。§5.2）。
            GameSessionBootService sessions = ResolveSessionService();
            if (sessions == null)
            {
                return Fail("Session サービスが見つかりません（Bootstrap 未起動）。");
            }

            GameSessionState session = sessions.EnsureSession(_catalog.EncounterClearPolicy);

            // 注入（Actor の活動開始より前。§4.2／§4.3）。
            AreaRuntimeState area = session.GetOrCreateArea(areaId);

            if (_progress != null && !_progress.Bind(session.Progress))
            {
                return Fail("進行データを注入できませんでした（使用後の差し替えの可能性）。");
            }

            if (_record != null && !_record.Bind(area.Investigation))
            {
                return Fail("調査記録を注入できませんでした。");
            }

            // 遷移サービスへカタログと受付条件を渡す。
            AreaTransitionService transitions = ResolveTransitionService();
            if (transitions == null)
            {
                return Fail("遷移サービスが見つかりません（Bootstrap 未起動）。");
            }

            if (!transitions.Bind(_catalog, _conditions))
            {
                return Fail("Area カタログを構築できませんでした（Data の不整合）。");
            }

            // 接続一覧（P5.5 §3.1）。<b>未設定は失敗ではない</b>——P5 の Area は接続を持たず、
            // 出入口が行き先を直接指す。設定されているのに壊れている場合だけ失敗にする。
            if (!transitions.BindConnections(_connections))
            {
                return Fail("エリア接続を構築できませんでした（Data の不整合）。");
            }

            // 死亡再開の実行役へ Session と遷移役を渡す（§9.1）。
            //
            // <b>この下で失敗しうる段より前に配線する</b>（GPT レビュー R7 の指摘 1）。
            // 以前は門の復元より後に置いていたので、門の復元で落ちると実行役が未配線のまま残り、
            // <b>再開画面は出るが押しても何も起きない</b>状態になっていた。
            // 判断（一度限り・段階）は Session 側が持ち、ここは配線だけを行う。
            if (_respawn != null)
            {
                _respawn.BindSession(() => session);
                _respawn.BindTravel(transitions);
            }

            // Encounter へ Session の世界状態の<b>取り出し口</b>を渡す（§4.3／§8.4 末尾）。
            // 常駐 Session は Scene へ serialize できないので、参照ではなく取り出し口を渡す。
            // 記録の<b>反映</b>（RestoreFromRecord）は入場ごとなので、ここには置かない。
            _encounter?.BindSession(() => area, () => session.RespawnCycle, () => session);
            _encounter?.BindAreaRoot(_areaRoot);
            if (_encounterGroup != null)
            {
                foreach (AreaEncounterRunner runner in _encounterGroup.Runners)
                {
                    runner?.BindSession(() => area, () => session.RespawnCycle, () => session);
                    runner?.BindAreaRoot(_areaRoot);
                }
            }
            _fieldEnemies?.BindSession(() => session);

            _sessions = sessions;
            _session = session;
            _transitions = transitions;
            SceneBuilt = true;
            BuildCount++;
            return true;
        }

        /// <summary>
        /// 初期化の<b>開始時点</b>の到着トークンを捕まえる（§6.2 末尾。GPT レビュー R2 の指摘 4）。
        ///
        /// 報告のときに <see cref="AreaPendingArrival.TransitionId"/> を読み直すと、
        /// 渡す値と比べる値が同じものになり、照合が常に成立してしまう。
        /// 初期化の途中で新しい遷移が始まっていた場合、<b>古い Scene の初期化担当が
        /// 新しい世代を「準備できた」ことにしてしまう</b>。だから開始時に捕まえて持ち回る。
        ///
        /// 自分宛てでない（別エリア宛ての）要求は 0 を返す＝直開き扱い。
        /// </summary>
        public static int CaptureArrivalToken(StableId areaId) =>
            AreaPendingArrival.HasPending && AreaPendingArrival.AreaId.Equals(areaId)
                ? AreaPendingArrival.TransitionId
                : 0;

        /// <summary>
        /// 到着する入口を決める。遷移で来たならその入口、直開きなら Data の既定入口（§5.2）。
        /// 遷移の要求が<b>別のエリア宛て</b>なら自分のものではないので、既定入口を使う。
        /// </summary>
        private StableId ResolveEntryId(StableId areaId)
        {
            if (AreaPendingArrival.HasPending && AreaPendingArrival.AreaId.Equals(areaId))
            {
                return AreaPendingArrival.EntryId;
            }

            return _areaRoot.Definition.DefaultEntryId;
        }

        /// <summary>
        /// 到着位置へ置く（§3.1／§4.4）。<b>位置は運ばず、目的地の入口へ置換する。</b>
        /// 向きは Data の入口定義が正本。犬丸は入口の手前（真上に重ねない。§3.2）。
        ///
        /// 実際に動かすのは <see cref="AreaActorTransferPort"/>。明示参照で根を持っているので
        /// ここから <c>Find*</c> を使わずに済む。
        /// </summary>
        private void PlaceArrivals(AreaEntryPoint entryPoint, Vector3 definitionFacing)
        {
            if (_transferPort == null)
            {
                return;
            }

            _transferPort.PlaceAt(
                entryPoint.ArrivalPosition,
                entryPoint.ArrivalPosition - definitionFacing * 1.2f,
                definitionFacing);
        }

        /// <summary>
        /// 同じ Area の中の旅立ち（レビュー 720161d 指摘 1）で、<b>動かす前に</b>到着できるかを確かめる。
        /// Scene を読み直さないので、入口が Scene にあること・Actor を置く窓口があることだけを見る。何も変えない。
        /// </summary>
        public bool CanPlaceWithinArea(StableId entryId, out string failure)
        {
            if (!Initialized || _areaRoot == null || _transferPort == null)
            {
                failure = "Area が準備できていないか、Actor を置く窓口が未配線です。";
                return false;
            }

            if (entryId.IsEmpty || !_areaRoot.TryGetEntryPoint(entryId, out _))
            {
                failure = "入口 '" + entryId.Value + "' が Scene にありません。";
                return false;
            }

            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// 同じ Area の中の旅立ちで主人公と犬丸を入口へ置き直す。<b>Actor の値（HP 等）には触らない</b>——回復は
        /// 呼び出し側の休息が行う。進行中の行動は中立化し、入口・遭遇 Trigger の「範囲内」を測り直す（入口の跳ね返り防止も入場と同じ）。
        /// </summary>
        public bool TryPlaceWithinArea(StableId entryId, out string failure)
        {
            if (!CanPlaceWithinArea(entryId, out failure))
            {
                return false;
            }

            _areaRoot.TryGetEntryPoint(entryId, out AreaEntryPoint entryPoint);
            _transferPort.ResetForAreaEntry();
            PlaceArrivals(entryPoint, ResolveFacing(entryId));
            Physics.SyncTransforms();
            foreach (Momotaro.Gameplay.Encounter.AreaEncounterTrigger trigger in _areaRoot.EncounterTriggers)
            {
                trigger?.ResyncOccupancy();
            }

            foreach (AreaExitGate gate in _areaRoot.ExitGates)
            {
                if (gate != null)
                {
                    gate.ResyncOccupancy();
                    gate.DisarmOnArrival();
                }
            }

            failure = string.Empty;
            return true;
        }

        /// <summary>入口定義の 4 方向を XZ のベクトルへ直す。</summary>
        private Vector3 ResolveFacing(StableId entryId)
        {
            foreach (AreaEntryDefinition e in _areaRoot.Definition.Entries)
            {
                if (e == null || !e.EntryId.Equals(entryId))
                {
                    continue;
                }

                switch (e.Facing)
                {
                    case CardinalDirection.North: return Vector3.forward;
                    case CardinalDirection.East: return Vector3.right;
                    case CardinalDirection.South: return Vector3.back;
                    case CardinalDirection.West: return Vector3.left;
                }
            }

            return Vector3.forward;
        }

        private bool Fail(string reason)
        {
            FailureReason = reason;
            Initialized = false;
            GameLog.Error(LogCategory.Scene, "Area initialization failed: " + reason);
            return false;
        }

        private static GameSessionBootService ResolveSessionService() =>
            BootstrapServices.Get<GameSessionBootService>();

        private static AreaTransitionService ResolveTransitionService() =>
            BootstrapServices.Get<AreaTransitionService>();
    }
}
