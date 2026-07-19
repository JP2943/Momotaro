using System;
using Momotaro.Gameplay.Modes;

namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// GameMode サービスを常駐サービスとして Bootstrap に組み込むアダプタ。
    /// <see cref="GameModeService"/>（Gameplay 層）は Infrastructure を参照できない（循環回避）ため、
    /// Infrastructure 側の本アダプタが生成・保持し、<see cref="IGameService"/> として初期化枠に載せる。
    /// 初期化時に <see cref="GameModeProvider"/> へ注入し、Gameplay 側からモード変更を要求できるようにする。
    /// </summary>
    public sealed class GameModeBootService : IGameService, IDisposable
    {
        /// <summary>保持するモードサービス。Input／HUD はこれを購読する。</summary>
        public GameModeService Modes { get; }

        public GameModeBootService()
        {
            // 起動直後は Loading 相当から始める。
            Modes = new GameModeService(GameMode.Loading);
        }

        /// <inheritdoc />
        public string ServiceName => "GameMode";

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            // Gameplay 側からモード変更を要求できるよう、常駐サービスを提供点へ注入する。
            GameModeProvider.Current = Modes;
            return ServiceInitResult.Ok("GameMode ready (initial: " + Modes.Current + ").");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (ReferenceEquals(GameModeProvider.Current, Modes))
            {
                GameModeProvider.Current = null;
            }
        }
    }
}
