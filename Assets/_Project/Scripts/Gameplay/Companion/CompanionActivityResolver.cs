using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Scenes;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// <see cref="GameMode"/> と戦闘セッションの状態から <see cref="CompanionActivity"/> を組む純粋ロジック（P4-FIX F05）。
    ///
    /// <b><c>GameMode == Exploration</c> を「非戦闘」と読まない。</b>Wave の幕間も、開始待ちの Encounter も戦闘中である。
    /// 戦闘かどうかの判定を各所で作り直すと必ず食い違うので、正本はここ 1 つにする。
    /// </summary>
    public static class CompanionActivityResolver
    {
        /// <summary>戦闘セッションが継続中とみなす状態か（開始待ち・Wave 幕間を含む）。</summary>
        public static bool IsEncounterActive(CombatSessionState state)
        {
            return state == CombatSessionState.Preparing
                || state == CombatSessionState.Playing
                || state == CombatSessionState.Intermission;
        }

        /// <summary>戦闘の終端（結果画面・再読込待ち）か。入力不可なので探索も受け付けない。</summary>
        public static bool IsConcluded(CombatSessionState state)
        {
            return state == CombatSessionState.Victory
                || state == CombatSessionState.Defeat
                || state == CombatSessionState.Reloading;
        }

        /// <summary>
        /// 現在の許可を決める。
        /// </summary>
        /// <param name="mode">ゲーム全体のモード。</param>
        /// <param name="session">戦闘セッションの状態。セッションが無い区画・Encounter 開始前の自由探索では null。</param>
        public static CompanionActivity Resolve(GameMode mode, CombatSessionState? session)
        {
            bool encounterActive = session.HasValue && IsEncounterActive(session.Value);
            bool concluded = session.HasValue && IsConcluded(session.Value);

            switch (mode)
            {
                // 凍結：進行中の攻撃の段・HitId・既命中集合を保ったまま、時計だけ止める。
                case GameMode.Paused:
                case GameMode.Loading:
                    return CompanionActivity.Paused(encounterActive);

                // 破棄：古い攻撃を捨てる。復帰時に途中の Active から再開させない。
                case GameMode.Dialogue:
                case GameMode.Event:
                case GameMode.GameOver:
                    return CompanionActivity.Interrupted(encounterActive);

                case GameMode.Combat:
                    return CompanionActivity.Fighting;

                case GameMode.Exploration:
                    // ここが F05 の要点。Exploration でも Encounter が動いていれば戦闘中として扱う。
                    // 勝敗の結果画面は戦闘ではないが入力不可なので探索も不可（§7.1）。
                    if (encounterActive)
                    {
                        return CompanionActivity.Fighting;
                    }

                    return concluded ? CompanionActivity.Concluded : CompanionActivity.FreeRoam;

                default:
                    // 知らないモードは通さない（新しいモードが増えたとき、黙って戦闘扱いにしない）。
                    return CompanionActivity.Stopped;
            }
        }
    }
}
