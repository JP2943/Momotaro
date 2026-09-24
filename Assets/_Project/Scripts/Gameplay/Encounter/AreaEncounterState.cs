namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// エリア内 Encounter の状態（P5-07。仕様書 v1.1 §8.2）。
    ///
    /// <b>これは探索との外側の引渡し</b>であって、<c>CombatSessionController</c> の戦闘内状態
    /// （Preparing／Playing／Victory…）を置き換えるものではない（§8.2 冒頭）。
    /// 戦闘の中身は既存 Session が持ち、こちらは「この区画の戦闘が始まったか・終わったか」だけを持つ。
    /// </summary>
    public enum AreaEncounterState
    {
        /// <summary>まだ始まっていない。Trigger は受け付ける。</summary>
        Dormant = 0,

        /// <summary>開始手順の最中（§8.2 手順 3〜8）。再入・別遷移・追加 Interact はここで閉じる。</summary>
        Starting = 1,

        /// <summary>戦闘中。</summary>
        Playing = 2,

        /// <summary>勝敗が確定して後始末の最中。</summary>
        Resolving = 3,

        /// <summary>クリア済み。この周期では再開しない（§8.4 末尾）。</summary>
        Cleared = 4,

        /// <summary>主人公が死亡した。再開は §9 の死亡再開が扱う。</summary>
        Defeated = 5,

        /// <summary>開始に失敗した。Trigger 退出 → 再進入で再試行できる（§8.2）。</summary>
        Failed = 6,
    }

    /// <summary>
    /// Encounter の状態と<b>実行世代（RunId）</b>を持つ純粋な状態機（P5-07。仕様書 v1.1 §8.2／§8.3）。
    ///
    /// <b>RunId を先に確定するのが要点。</b> §8.2 手順 3 は「Starting と RunId を先に確定して、
    /// 再入・別遷移・追加 Interact を閉じる」と決めている。手順 4 で調査を撤収すると、
    /// 撤収の通知を受けた購読者が<b>その場で開始を要求し直す</b>ことがある（実際に P4 で踏んだ形）。
    /// 先に閉じておかないと、その再要求が新しい Starting を作り、前の実行の後始末が
    /// 新しい実行を壊す。
    ///
    /// <b>排他の解除は所有者の一致で確認する</b>（§8.3 末尾）。だから状態を進める操作はすべて
    /// RunId を要求し、一致しない要求は黙って無視する（例外にしない：古い通知は「来る」ものだから）。
    /// </summary>
    public sealed class AreaEncounterMachine
    {
        /// <summary>いまの状態。</summary>
        public AreaEncounterState State { get; private set; } = AreaEncounterState.Dormant;

        /// <summary>いまの実行世代。0 は「実行していない」。</summary>
        public int RunId { get; private set; }

        /// <summary>世代が合わずに捨てた要求の数（診断・テスト用）。</summary>
        public int StaleRequestCount { get; private set; }

        /// <summary>開始を受け付けられる状態か（Dormant か、失敗後の再試行）。</summary>
        public bool CanBeginStart =>
            State == AreaEncounterState.Dormant || State == AreaEncounterState.Failed;

        /// <summary>戦闘として扱う状態か（開始待ち・戦闘中・後始末中。§8.4 末尾）。</summary>
        public bool IsEngaged =>
            State == AreaEncounterState.Starting
            || State == AreaEncounterState.Playing
            || State == AreaEncounterState.Resolving;

        /// <summary>
        /// 開始を確定する。受け付けられなければ 0 を返す（<b>RunId は進めない</b>）。
        /// </summary>
        public int BeginStart()
        {
            if (!CanBeginStart)
            {
                StaleRequestCount++;
                return 0;
            }

            RunId++;
            State = AreaEncounterState.Starting;
            return RunId;
        }

        /// <summary>生成まで済んで戦闘へ入る（§8.2 手順 8）。</summary>
        public bool MarkPlaying(int runId) => Advance(runId, AreaEncounterState.Starting, AreaEncounterState.Playing);

        /// <summary>勝敗が出て後始末へ入る（§8.4 手順 3）。</summary>
        public bool BeginResolve(int runId) => Advance(runId, AreaEncounterState.Playing, AreaEncounterState.Resolving);

        /// <summary>クリアとして終える。</summary>
        public bool MarkCleared(int runId) => Advance(runId, AreaEncounterState.Resolving, AreaEncounterState.Cleared);

        /// <summary>敗北として終える。</summary>
        public bool MarkDefeated(int runId) => Advance(runId, AreaEncounterState.Resolving, AreaEncounterState.Defeated);

        /// <summary>
        /// 開始に失敗した（§8.2）。Starting からのみ入る。再試行できるよう RunId は戻さない
        /// （戻すと、後から届いた旧世代の通知が新しい実行に一致してしまう）。
        /// </summary>
        public bool MarkFailed(int runId) => Advance(runId, AreaEncounterState.Starting, AreaEncounterState.Failed);

        /// <summary>
        /// クリア済みとして始める（記録からの復元。§4.3）。実行はしていないので RunId は進めない。
        /// </summary>
        public void RestoreCleared()
        {
            State = AreaEncounterState.Cleared;
        }

        private bool Advance(int runId, AreaEncounterState from, AreaEncounterState to)
        {
            if (runId == 0 || runId != RunId || State != from)
            {
                StaleRequestCount++;
                return false;
            }

            State = to;
            return true;
        }
    }
}
