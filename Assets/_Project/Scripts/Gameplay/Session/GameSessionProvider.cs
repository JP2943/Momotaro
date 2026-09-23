namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 本編型 Session の供給点（P5-03b。仕様書 v1.1 §4.2／§5.1）。
    /// 既存の <c>GameModeProvider</c>／<c>CompanionActivityProvider</c> と同じ形。
    ///
    /// <b>Gameplay は Infrastructure を参照できない</b>ので、Session を所有する Bootstrap 側が
    /// ここへ差し、Area の初期化担当が取り出す。Session そのものは純粋 State
    /// （<see cref="GameSessionState"/>）で、Scene 由来の Actor や Presenter は持たない（§4.1 末尾）。
    ///
    /// <b>未設定は「本編型 Session ではない」。</b> P3.5／P4 の試遊 Scene はこれが null のまま動き、
    /// Holder は従来どおりローカル State を使う（§5.2「試遊 Scene は本編 Session を自動注入しない」）。
    /// </summary>
    public static class GameSessionProvider
    {
        /// <summary>現在の Session（未設定なら null）。</summary>
        public static GameSessionState Current { get; set; }

        /// <summary>Session が差さっているか（診断・テスト用）。</summary>
        public static bool HasSession => Current != null;
    }
}
