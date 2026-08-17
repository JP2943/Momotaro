using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>剣閃観測（<see cref="IAttackSwingSource.SwingStage"/>）で用いる特別な段の値（Phase3.5 P3.5-05）。</summary>
    public static class AttackSwing
    {
        /// <summary>必殺技の振りを表す SwingStage 値。通常コンボ段（1..N）と区別する。</summary>
        public const int SpecialStage = 100;
    }

    /// <summary>
    /// 近接攻撃の「振り（swing）」を Presentation（剣閃VFX）が観測するための読み取り専用契約（Phase3.5 P3.5-05。仕様書 §6/§7.2）。
    /// 具象駆動（<see cref="Momotaro.Gameplay.Player.PlayerStateController"/> 等）へ Presentation が依存せず、判定（Active）区間・段・
    /// Hitbox 中心／範囲・前方を参照して、Active 開始〜終了に同期した剣閃を「空振りでも」描ける。実装側は Gameplay 状態を一切変更しない。
    /// </summary>
    public interface IAttackSwingSource
    {
        /// <summary>判定（Active）区間中か。Hitbox が有効な区間のみ true。</summary>
        bool IsSwingHitboxActive { get; }

        /// <summary>現在段（通常コンボは 1..N、必殺技は <see cref="AttackSwing.SpecialStage"/>、非攻撃時 0）。剣閃素材の段別選択に用いる。</summary>
        int SwingStage { get; }

        /// <summary>Hitbox 中心（world）。剣閃の表示位置。</summary>
        Vector3 SwingCenter { get; }

        /// <summary>Hitbox 半径（各軸の half extent, m）。おおよその範囲対応の参照。</summary>
        Vector3 SwingHalfExtents { get; }

        /// <summary>攻撃前方（XZ）。剣閃の 4 方向選択に用いる。</summary>
        Vector3 SwingForward { get; }
    }
}
