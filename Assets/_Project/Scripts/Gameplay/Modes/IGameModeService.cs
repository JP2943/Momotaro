using System;

namespace Momotaro.Gameplay.Modes
{
    /// <summary>
    /// 現在のゲームモードを保持し、変更を型付き通知するサービス（仕様書 13.6）。
    /// </summary>
    public interface IGameModeService
    {
        /// <summary>現在のモード。</summary>
        GameMode Current { get; }

        /// <summary>現在モードでポーズ操作が許可されるか。</summary>
        bool CanPause { get; }

        /// <summary>モード変更時に発火する型付きイベント。</summary>
        event Action<GameModeChanged> ModeChanged;

        /// <summary>
        /// モードを変更する。同一モードへの変更は何もしない。
        /// </summary>
        /// <returns>実際に変更された場合 true。</returns>
        bool ChangeMode(GameMode next);

        /// <summary>接続枠リスナーを登録する。</summary>
        void AddListener(IGameModeListener listener);

        /// <summary>接続枠リスナーを解除する。</summary>
        void RemoveListener(IGameModeListener listener);
    }
}
