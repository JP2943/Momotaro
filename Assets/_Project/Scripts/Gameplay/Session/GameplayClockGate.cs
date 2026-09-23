namespace Momotaro.Gameplay.Session
{
    /// <summary>Gameplay 時計が止まっているかの供給元。</summary>
    public interface IGameplayClockSource
    {
        /// <summary>Gameplay の時間・移動・攻撃を進めてはいけない状態か。</summary>
        bool IsFrozen { get; }
    }

    /// <summary>
    /// 遷移中に Gameplay 時計を止める窓口（P5-03b。仕様書 v1.1 §6.2 手順 3、受入 P5-E07）。
    ///
    /// <b>なぜ Tick の内側で見るのか。</b> 本プロジェクトの駆動系は
    /// 「<c>Update()</c> は <c>Tick(Time.deltaTime)</c> を呼ぶだけ」という形で、時間を外部注入する（`CLAUDE.md`）。
    /// そのため <c>Update()</c> 側だけで止めても、テストや他の駆動から <c>Tick</c> を<b>直接呼べば進んでしまう</b>。
    /// E07 が「直接 Tick でも進めない」を要求するのはこの穴のことなので、判定は各 <c>Tick</c> の入口に置く。
    ///
    /// <b>純粋クラスには入れない。</b> <c>StaminaState</c>・<c>HitReactionState</c> のような純粋 Runtime は
    /// 渡された deltaTime をそのまま進めるのが契約で、そこに暗黙の停止を混ぜると決定的な検証ができなくなる。
    /// 止めるのは「時間を配る側」＝ MonoBehaviour の駆動系に限る。
    ///
    /// <b>暗転・Error UI は止めない。</b> あれは unscaled 時間で動く Presentation の仕事で、
    /// Gameplay 時計とは別（§6.3）。
    /// </summary>
    public sealed class GameplayClockGate : IGameplayClockSource
    {
        /// <inheritdoc />
        public bool IsFrozen { get; private set; }

        /// <summary>凍結した回数（診断・テスト用）。</summary>
        public int FreezeCount { get; private set; }

        /// <summary>解除した回数（診断・テスト用）。</summary>
        public int ThawCount { get; private set; }

        /// <summary>Gameplay 時計を止める。二重呼び出しは安全（回数は数える）。</summary>
        public void Freeze()
        {
            FreezeCount++;
            IsFrozen = true;
        }

        /// <summary>Gameplay 時計を再開する。二重呼び出しは安全。</summary>
        public void Thaw()
        {
            ThawCount++;
            IsFrozen = false;
        }
    }

    /// <summary>
    /// Gameplay 時計の供給点（既存の <c>GameModeProvider</c>／<c>CompanionActivityProvider</c> と同じ形）。
    ///
    /// <b>未設定なら「凍結していない」。</b> <c>CompanionActivityProvider</c> が未設定で停止側へ倒すのとは
    /// 逆にしてある。理由は、こちらが<b>遷移という一時的な事象</b>を表すからで、既定を凍結にすると
    /// ゲートを知らない P3.5／P4 の試遊 Scene が丸ごと止まってしまう。
    /// 「供給元が無い＝遷移していない」が正しい既定。
    ///
    /// 遷移の所有者が <see cref="Current"/> を差し、終了時に必ず戻すこと。
    /// </summary>
    public static class GameplayClockProvider
    {
        /// <summary>現在の供給元（未設定なら null）。</summary>
        public static IGameplayClockSource Current { get; set; }

        /// <summary>いま Gameplay 時計が止まっているか。供給元が無ければ false。</summary>
        public static bool IsFrozen => Current != null && Current.IsFrozen;

        /// <summary>供給元が差さっているか（診断・テスト用）。</summary>
        public static bool HasSource => Current != null;
    }
}
