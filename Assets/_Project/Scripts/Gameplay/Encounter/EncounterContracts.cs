using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Scenes;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>Encounter を開始できなかった理由（P5-07。仕様書 v1.1 §8.2）。</summary>
    public enum EncounterStartRejection
    {
        /// <summary>拒否していない。</summary>
        None = 0,

        /// <summary>配線が足りない（構成・生成・境界のいずれか）。</summary>
        NotWired = 1,

        /// <summary>エリアの初期化が終わっていない。</summary>
        AreaNotReady = 2,

        /// <summary>探索中ではない（戦闘・Pause・会話・Loading）。</summary>
        WrongMode = 3,

        /// <summary>主人公が生存していない。</summary>
        PlayerNotAlive = 4,

        /// <summary>エリア遷移が走っている。</summary>
        Transitioning = 5,

        /// <summary>すでに開始済み（Starting／Playing／Resolving）。</summary>
        AlreadyRunning = 6,

        /// <summary>この再出現周期ではクリア済み（§8.4 末尾）。</summary>
        AlreadyCleared = 7,

        /// <summary>境界の内側へ安全に配置できない（§8.2 末尾）。</summary>
        UnsafePlacement = 8,

        /// <summary>敵を全数生成できなかった（§8.2）。</summary>
        SpawnFailed = 9,
    }

    /// <summary>開始要求の結果（P5-07）。</summary>
    public readonly struct EncounterStartDecision
    {
        private EncounterStartDecision(bool started, int runId, EncounterStartRejection rejection, string detail)
        {
            Started = started;
            RunId = runId;
            Rejection = rejection;
            Detail = detail ?? string.Empty;
        }

        /// <summary>戦闘が始まったか。</summary>
        public bool Started { get; }

        /// <summary>この実行の世代（始まらなかったときは 0、開始途中で失敗したときは失敗した世代）。</summary>
        public int RunId { get; }

        /// <summary>始まらなかった理由。</summary>
        public EncounterStartRejection Rejection { get; }

        /// <summary>理由の補足（表示・診断用）。</summary>
        public string Detail { get; }

        public static EncounterStartDecision Accept(int runId) =>
            new EncounterStartDecision(true, runId, EncounterStartRejection.None, null);

        public static EncounterStartDecision Reject(EncounterStartRejection rejection, string detail = null) =>
            new EncounterStartDecision(false, 0, rejection, detail);

        public static EncounterStartDecision Fail(int runId, EncounterStartRejection rejection, string detail) =>
            new EncounterStartDecision(false, runId, rejection, detail);
    }

    /// <summary>
    /// 開始の受付条件（§8.2 手順 2）。<b>判定の正本はここ 1 つ</b>で、Trigger 側に別の条件を書かない。
    /// </summary>
    public interface IAreaEncounterConditions
    {
        /// <summary>エリアの初期化が終わって活動が許可されているか（§5.1 手順 8）。</summary>
        bool IsAreaReady { get; }

        /// <summary>探索中か。</summary>
        bool IsExploration { get; }

        /// <summary>主人公が生存しているか。</summary>
        bool IsPlayerAlive { get; }

        /// <summary>エリア遷移が走っているか。</summary>
        bool IsTransitioning { get; }
    }

    /// <summary>
    /// 開始の直前に、進行中の探索を<b>同期的に</b>撤収する窓口（§8.2 手順 4）。
    ///
    /// 非同期にすると、撤収が終わる前に敵が生成されて「調査しながら殴られる」状態が作れてしまう。
    /// §8.3 の「新規 Actor 行動の停止は、少なくとも敵生成・命中解決・次の物理移動より前に反映する」は
    /// このことを言っている。
    /// </summary>
    public interface IEncounterInterruptSink
    {
        /// <summary>調査・代理表示を撤収し、探索の行動・移動所有権を解放する。</summary>
        void InterruptForEncounter();
    }

    /// <summary>
    /// アリーナ境界（§8.2 手順 5、§8.4 手順 5）。<b>Transform は Scene 側が持つ</b>
    /// （共有 Data や常駐 Session へ保存しない。§8.1）。
    /// </summary>
    public interface IArenaBoundary
    {
        /// <summary>
        /// 境界を有効化する。主人公が封鎖 Collider に重ならず内部に居ること、
        /// 犬丸も内部へ安全に配置できることを確かめ、できなければ<b>有効化せずに</b> false を返す（§8.2 末尾）。
        /// </summary>
        bool TryEnable(out string error);

        /// <summary>境界を解除する（勝利・敗北・開始失敗）。二重呼び出し安全。</summary>
        void Disable();

        /// <summary>境界が有効か（診断・テスト用）。</summary>
        bool IsEnabled { get; }
    }

    /// <summary>
    /// 開始時に固めた敵構成（§8.1 末尾「Encounter 開始時に敵構成・参照・使用値を Snapshot 化する」）。
    ///
    /// <b>Data を実行中に読み直さない。</b> 読み直すと、途中で Data が編集されたときに
    /// 「予定生成数」と「実際に出た数」がずれ、勝利条件（全予定生成完了）が意味を失う。
    /// </summary>
    public readonly struct EncounterPlan
    {
        private readonly StableId[] _enemyIds;

        public EncounterPlan(StableId encounterId, IReadOnlyList<StableId> enemyIds)
        {
            EncounterId = encounterId;

            int count = enemyIds != null ? enemyIds.Count : 0;
            _enemyIds = new StableId[count];
            for (int i = 0; i < count; i++)
            {
                _enemyIds[i] = enemyIds[i];
            }
        }

        /// <summary>この Encounter の安定 ID（クリア記録の鍵）。</summary>
        public StableId EncounterId { get; }

        /// <summary>予定している敵の数。</summary>
        public int PlannedCount => _enemyIds != null ? _enemyIds.Length : 0;

        /// <summary>予定している敵の ID（出現点は<b>この順</b>に対応させる。§8.1）。</summary>
        public IReadOnlyList<StableId> EnemyIds => _enemyIds ?? System.Array.Empty<StableId>();

        /// <summary>構成として成立しているか（空の敵構成は通さない。§13.3）。</summary>
        public bool IsValid => !EncounterId.IsEmpty && PlannedCount > 0;
    }

    /// <summary>
    /// 敵の生成（§8.2 手順 7・8）。<b>生成と活動許可を分ける</b>のが契約の要点で、
    /// 「Runtime に Spawn 失敗の可能性がある以上、Spawn 中の敵が先に活動・死亡しないことを保証する」
    /// （§8.2 末尾）を型で守る。
    /// </summary>
    public interface IEncounterSpawner
    {
        /// <summary>
        /// 予定した敵を<b>全数、非活動のまま</b>生成して登録する。
        /// 1 体でも作れなければ false を返し、途中まで作ったものは自分で片付ける。
        /// </summary>
        bool TrySpawnAll(in EncounterPlan plan, out string error);

        /// <summary>生成した敵の活動（AI・攻撃）を許可する（§8.2 手順 8）。</summary>
        void ActivateSpawned();

        /// <summary>
        /// 生成物・登録・購読・Projectile・攻撃スロット・ヘイトを解放する
        /// （§8.2 の失敗時、§8.4 手順 5）。二重呼び出し安全。
        /// </summary>
        void ReleaseAll();

        /// <summary>いま生成している敵の数（診断・テスト用）。</summary>
        int SpawnedCount { get; }

        /// <summary>生成した敵が活動を許可されているか（診断・テスト用）。</summary>
        bool SpawnedActive { get; }

        /// <summary>登録した敵（勝利判定の突き合わせ用）。</summary>
        IReadOnlyList<IEnemyDefeatSource> Spawned { get; }
    }

    /// <summary>
    /// 仲間の活動 Context へ「いまこの区画の戦闘がどの段階か」を供給する（§8.4 末尾）。
    ///
    /// <b>既存 Session の状態をそのまま渡さない。</b> 渡すと、勝利のあと
    /// <c>CombatSessionController</c> が Victory のまま残るため、仲間が
    /// 「戦闘の終端＝入力不可」と読んで永久に停止する（§8.4 が名指しで禁じている形）。
    /// 開始前・解放後は<b>活動中 Session なし</b>（null）を返す。
    /// </summary>
    public interface IAreaEncounterActivitySource
    {
        /// <summary>
        /// 仲間へ供給する戦闘セッション状態。<c>null</c> は「活動中の Encounter なし」。
        /// Starting は <see cref="CombatSessionState.Preparing"/>、Playing／Resolving は
        /// <see cref="CombatSessionState.Playing"/> として供給する。
        /// </summary>
        CombatSessionState? ActivitySession { get; }
    }
}
