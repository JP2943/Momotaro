using System;
using System.Collections.Generic;
using Momotaro.Core.Logging;

namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// <see cref="IGameModeService"/> の実装。モードを保持し、変更時にイベントと接続枠リスナーへ
    /// 通知する。同一モードへの変更は抑制する。
    /// </summary>
    public sealed class GameModeService : IGameModeService
    {
        private readonly List<IGameModeListener> _listeners = new List<IGameModeListener>();

        public GameModeService(GameMode initial = GameMode.Loading)
        {
            Current = initial;
        }

        /// <inheritdoc />
        public GameMode Current { get; private set; }

        /// <inheritdoc />
        public bool CanPause => GameModeCatalog.GetProfile(Current).CanPause;

        /// <inheritdoc />
        public event Action<GameModeChanged> ModeChanged;

        /// <inheritdoc />
        public bool ChangeMode(GameMode next)
        {
            if (next == Current)
            {
                GameLog.Info(LogCategory.Core, "GameMode unchanged: " + next);
                return false;
            }

            var change = new GameModeChanged(Current, next);
            Current = next;
            GameLog.Info(LogCategory.Core, "GameMode: " + change.Previous + " -> " + change.Current);

            ModeChanged?.Invoke(change);
            NotifyListeners(change);
            return true;
        }

        /// <inheritdoc />
        public void AddListener(IGameModeListener listener)
        {
            if (listener == null || _listeners.Contains(listener))
            {
                return;
            }

            _listeners.Add(listener);
        }

        /// <inheritdoc />
        public void RemoveListener(IGameModeListener listener)
        {
            _listeners.Remove(listener);
        }

        private void NotifyListeners(GameModeChanged change)
        {
            // コールバック内での購読解除（自己/他リスナーの Remove）で後続が飛ばされないよう、
            // 走査前にスナップショットを取る。
            IGameModeListener[] snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnModeChanged(change);
            }
        }
    }
}
