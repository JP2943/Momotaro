namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// Bootstrap が起動時に初期化する常駐サービスの共通インターフェース。
    /// GameManager／SceneFlow／Save／Input／Audio 等はこれを実装する（Phase 0 では枠のみ）。
    /// Awake 順への暗黙依存を避けるため、初期化は <see cref="Initialize"/> を通じて明示的に行う。
    /// </summary>
    public interface IGameService
    {
        /// <summary>ログ・診断用の識別名。</summary>
        string ServiceName { get; }

        /// <summary>
        /// サービスを初期化し、結果を返す。副作用は最小限に保ち、失敗は例外でなく
        /// <see cref="ServiceInitResult"/> で表す。
        /// </summary>
        ServiceInitResult Initialize();
    }
}
