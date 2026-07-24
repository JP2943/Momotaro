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

        /// <summary>
        /// 入力ゲートが開いているか（GameMode が Gameplay のとき true）。非 Gameplay 中は false。
        /// Gameplay 層は移動・攻撃の実行可否判定にこれを用いる（Phase2 P2-02）。
        /// </summary>
        bool Active { get; }

        /// <summary>
        /// 攻撃ボタンの押下エッジを 1 回だけ取り出す（Phase2 P2-02）。押下の瞬間だけ true を返し、
        /// 取り出すとクリアされるため、ボタン保持では連続実行されない（本書 §4.2「Hold 継続なし」）。
        /// 非 Gameplay 中の押下は蓄積しない。
        /// </summary>
        /// <returns>未消費の押下エッジがあれば true。</returns>
        bool ConsumeAttackPressed();

        /// <summary>
        /// ステップボタンの押下エッジを 1 回だけ取り出す（Phase2 P2-09）。攻撃と同様に押下の瞬間だけ true を返し、
        /// 取り出すとクリアされる。非 Gameplay 中の押下は蓄積しない。
        /// </summary>
        /// <returns>未消費の押下エッジがあれば true。</returns>
        bool ConsumeStepPressed();

        /// <summary>
        /// 必殺技ボタンを保持中か（Phase2 P2-10）。長押しでチャージするため、押下エッジではなく保持状態で供給する。
        /// 非 Gameplay 時は解除される。
        /// </summary>
        bool SpecialAttackHeld { get; }
    }
}
