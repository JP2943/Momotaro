using Momotaro.Core.Logging;
using Momotaro.Gameplay.Session;
using Momotaro.Infrastructure.Bootstrap;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// 本編型 Session を所有する常駐サービス（P5-03b。仕様書 v1.1 §4.2／§5.1 手順 2／§9.2）。
    ///
    /// <b>Session を 1 個だけ持つのがこのサービスの全部。</b> 徳・GrantOnce 記録・Area 記録・訪問・加入は
    /// すべてこの中の <see cref="GameSessionState"/> が正本で、Scene 側の Holder は Bind されて窓口になるだけ。
    ///
    /// <b>生成は遅延させる。</b> 起動しただけでは作らない。P3.5／P4 の試遊 Scene は本編 Session を
    /// 自動注入しないという規則（§5.2）があるので、本編型の Area が初期化されるときに
    /// <see cref="EnsureSession"/> が呼ばれて初めて作る。
    ///
    /// 破棄できるのは所有者だけ（§4.2）。明示的な New Game が <see cref="StartNewSession"/> を呼ぶ。
    /// Scene 側から共有 State をリセットする経路は作らない。
    /// </summary>
    public sealed class GameSessionBootService : IGameService
    {
        /// <inheritdoc />
        public string ServiceName => "GameSession";

        /// <summary>現在の Session（未生成なら null）。</summary>
        public GameSessionState Session { get; private set; }

        /// <summary>Session を作った回数（診断・テスト用。P01 が唯一性を見る）。</summary>
        public int CreatedCount { get; private set; }

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            // ここでは作らない。本編型 Area の初期化が要求したときだけ作る（§5.2）。
            //
            // <b>提供点は触らない。</b> 後から立った重複 Bootstrap の初期化が、
            // すでに走っている正本の Session を消してしまう（§5.2「後発破棄しても正本の Provider を解除しない」）。
            // 自分は Session を持っていないので、提供点に対して主張できることが何も無い。
            return ServiceInitResult.Ok("Game session service ready (no session yet).");
        }

        /// <summary>
        /// Session を用意する（§5.2「既存 P5 Session があれば再利用し、なければ新規作成」）。
        /// <b>二度目以降は同じ実体を返す。</b> 遷移で新しい Session を作らない。
        /// </summary>
        public GameSessionState EnsureSession()
        {
            if (Session != null)
            {
                GameSessionProvider.Current = Session;
                return Session;
            }

            Session = new GameSessionState();
            CreatedCount++;
            GameSessionProvider.Current = Session;
            GameLog.Info(LogCategory.Boot, "Created a new campaign session.");
            return Session;
        }

        /// <summary>
        /// 明示的な New Game（§9.2）。<b>これだけが新しい Session を作る。</b>
        /// 呼ぶ前に進行中のロード・Actor・購読を閉じるのは呼び出し側の責任。
        /// </summary>
        public GameSessionState StartNewSession()
        {
            Session = new GameSessionState();
            CreatedCount++;
            GameSessionProvider.Current = Session;
            GameLog.Info(LogCategory.Boot, "Started a new game session.");
            return Session;
        }

        /// <summary>
        /// Session を捨てる（Play 終了・アプリ終了）。保存はしない（§9.2）。
        ///
        /// <b>提供点の解除は所有者一致で行う</b>（§5.2 末尾）。重複 Bootstrap を後から壊したときに、
        /// 生きている正本の Session まで消さないため。「いま提供点に入っているのが自分の Session なら外す」。
        /// </summary>
        public void Dispose()
        {
            GameSessionState owned = Session;
            Session = null;

            if (owned != null && ReferenceEquals(GameSessionProvider.Current, owned))
            {
                GameSessionProvider.Current = null;
            }
        }
    }
}
