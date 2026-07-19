using System;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// <see cref="InputService"/> を常駐サービスとして Bootstrap に組み込むアダプタ（仕様書 11.7）。
    /// 起動時に InputActionAsset から InputService を生成し、GameMode を購読して Action Map を
    /// 自動切替させる。使用するアセットは、明示指定 → プロジェクト全体アクション（<see cref="InputSystem.actions"/>）
    /// の順で解決する。どちらも無い場合は入力を無効のまま起動する（致命的ではない）。
    /// </summary>
    public sealed class InputBootService : IGameService, IDisposable
    {
        private readonly InputActionAsset _explicitAsset;
        private readonly IGameModeService _modes;

        /// <summary>生成された InputService。アセットが無ければ null。</summary>
        public InputService Input { get; private set; }

        /// <param name="explicitAsset">使用する InputActionAsset。null なら project-wide を試す。</param>
        /// <param name="modes">購読する GameMode サービス。</param>
        public InputBootService(InputActionAsset explicitAsset, IGameModeService modes)
        {
            _explicitAsset = explicitAsset;
            _modes = modes;
        }

        /// <inheritdoc />
        public string ServiceName => "Input";

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            InputActionAsset asset = _explicitAsset != null ? _explicitAsset : InputSystem.actions;
            if (asset == null)
            {
                GameLog.Warning(LogCategory.Input,
                    "No InputActionAsset assigned and no project-wide actions set; input remains disabled.");
                return ServiceInitResult.Ok("Input idle (no action asset).");
            }

            Input = new InputService(asset, new PlayerPrefsRebindStore());

            if (_modes != null)
            {
                _modes.AddListener(Input);
                // 現在モードに対応する Map を初期適用する。
                Input.OnModeChanged(new GameModeChanged(_modes.Current, _modes.Current));
            }

            return ServiceInitResult.Ok("Input ready (asset: " + asset.name + ").");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_modes != null && Input != null)
            {
                _modes.RemoveListener(Input);
            }

            Input?.Dispose();
            Input = null;
        }
    }
}
