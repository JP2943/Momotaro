using System;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Modes;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// Input System をまとめて扱うサービス（仕様書 11.7）。GameMode に応じて Action Map を
    /// 切り替え、直近に使われた入力デバイスから Control Scheme を追跡し、リバインドの保存・読込枠を提供する。
    ///
    /// <see cref="IGameModeListener"/> として GameMode 変更を購読する。使用終了時は <see cref="Dispose"/> で
    /// イベント購読を必ず解除する。
    /// </summary>
    public sealed class InputService : IGameModeListener, IDisposable
    {
        /// <summary>リバインド上書きの保存キー。</summary>
        public const string RebindStoreKey = "momotaro.input.rebinds";

        private const string SchemeKeyboardMouse = "Keyboard&Mouse";
        private const string SchemeGamepad = "Gamepad";

        private readonly InputActionAsset _asset;
        private readonly IRebindStore _store;
        private bool _disposed;

        /// <summary>現在有効な Action Map 名。未設定なら null。</summary>
        public string ActiveMapName { get; private set; }

        /// <summary>直近に使われたデバイスに基づく Control Scheme 名。</summary>
        public string CurrentScheme { get; private set; } = SchemeKeyboardMouse;

        /// <summary>Control Scheme が変化したときに発火する。</summary>
        public event Action<string> SchemeChanged;

        public InputService(InputActionAsset asset, IRebindStore store)
        {
            _asset = asset ?? throw new ArgumentNullException(nameof(asset));
            _store = store ?? throw new ArgumentNullException(nameof(store));

            LoadRebinds();
            InputSystem.onEvent += OnInputEvent;
        }

        /// <summary>
        /// 指定名の Action Map だけを有効化し、他を無効化する。
        /// </summary>
        /// <returns>対象 Map が見つかり有効化した場合 true。</returns>
        public bool SwitchToActionMap(string mapName)
        {
            InputActionMap target = _asset.FindActionMap(mapName, throwIfNotFound: false);
            if (target == null)
            {
                GameLog.WarningOnce(LogCategory.Input, "map_missing_" + mapName,
                    "Action map not found: " + mapName);
                return false;
            }

            foreach (InputActionMap map in _asset.actionMaps)
            {
                if (map != target)
                {
                    map.Disable();
                }
            }

            target.Enable();
            ActiveMapName = mapName;
            GameLog.Info(LogCategory.Input, "Active action map: " + mapName);
            return true;
        }

        /// <inheritdoc />
        public void OnModeChanged(GameModeChanged change)
        {
            GameModeProfile profile = GameModeCatalog.GetProfile(change.Current);
            SwitchToActionMap(profile.ActionMap);
        }

        /// <summary>現在のバインド上書きを保存する。</summary>
        public void SaveRebinds()
        {
            _store.Save(RebindStoreKey, _asset.SaveBindingOverridesAsJson());
            GameLog.Info(LogCategory.Input, "Saved input rebinds.");
        }

        /// <summary>保存済みのバインド上書きを読み込む。無ければ何もしない。</summary>
        public void LoadRebinds()
        {
            if (!_store.Has(RebindStoreKey))
            {
                return;
            }

            _asset.LoadBindingOverridesFromJson(_store.Load(RebindStoreKey));
            GameLog.Info(LogCategory.Input, "Loaded input rebinds.");
        }

        /// <summary>すべてのバインド上書きを消去して既定へ戻す。</summary>
        public void ResetRebinds()
        {
            _asset.RemoveAllBindingOverrides();
        }

        private void OnInputEvent(InputEventPtr eventPtr, InputDevice device)
        {
            // 状態変化イベントのみを見て、直近に操作されたデバイスを判定する。
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
            {
                return;
            }

            string scheme = device is Gamepad ? SchemeGamepad : SchemeKeyboardMouse;
            if (scheme == CurrentScheme)
            {
                return;
            }

            CurrentScheme = scheme;
            GameLog.Info(LogCategory.Input, "Input scheme: " + scheme);
            SchemeChanged?.Invoke(scheme);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            InputSystem.onEvent -= OnInputEvent;
            _disposed = true;
        }
    }
}
