using System;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
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

        private PlayerInputAdapter _playerInputAdapter;

        /// <summary>生成された InputService。アセットが無ければ null。</summary>
        public InputService Input { get; private set; }

        /// <summary>Player Gameplay 向けの入力（P1-02）。アセットが無ければ null。</summary>
        public IPlayerInput PlayerInput => _playerInputAdapter?.Input;

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

            // Player 向け入力アダプタ（Gameplay/Move・Guard）。必須アクションが無い場合は入力のみ有効。
            try
            {
                _playerInputAdapter = new PlayerInputAdapter(asset);
                // Gameplay 層（Player コンポーネント）が参照する提供点へ注入する。
                PlayerInputProvider.Current = _playerInputAdapter.Input;
            }
            catch (Exception ex)
            {
                GameLog.Warning(LogCategory.Input,
                    "Player input adapter not created: " + ex.Message);
            }

            if (_modes != null)
            {
                _modes.AddListener(Input);
                // 現在モードに対応する Map を初期適用する。
                Input.OnModeChanged(new GameModeChanged(_modes.Current, _modes.Current));

                if (_playerInputAdapter != null)
                {
                    _modes.AddListener(_playerInputAdapter);
                    _playerInputAdapter.OnModeChanged(new GameModeChanged(_modes.Current, _modes.Current));
                }
            }

            return ServiceInitResult.Ok("Input ready (asset: " + asset.name + ").");
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_modes != null)
            {
                if (Input != null)
                {
                    _modes.RemoveListener(Input);
                }

                if (_playerInputAdapter != null)
                {
                    _modes.RemoveListener(_playerInputAdapter);
                }
            }

            if (_playerInputAdapter != null && ReferenceEquals(PlayerInputProvider.Current, _playerInputAdapter.Input))
            {
                PlayerInputProvider.Current = null;
            }

            _playerInputAdapter?.Dispose();
            _playerInputAdapter = null;
            Input?.Dispose();
            Input = null;
        }
    }
}
