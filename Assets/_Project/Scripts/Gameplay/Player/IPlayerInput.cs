using System;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の Gameplay コードが参照する入力の抽象（Phase1 P1-02）。
    /// Gameplay 側は InputActionAsset や Scene を直接探索せず、この Interface だけに依存する。
    /// 実際の入力読取は Infrastructure 層のアダプタが供給する。
    /// </summary>
    public interface IPlayerInput
    {
        /// <summary>移動入力（Deadzone 適用後の生値）。非 Gameplay 時はゼロ。</summary>
        Vector2 Move { get; }

        /// <summary>ガード保持中か。非 Gameplay 時は解除される。</summary>
        bool GuardHeld { get; }

        /// <summary>ガードが押下開始された瞬間に発火。</summary>
        event Action GuardStarted;

        /// <summary>ガードが解除された瞬間に発火（入力解除・ゲート解除の双方）。</summary>
        event Action GuardCanceled;
    }
}
