using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>剣閃観測（<see cref="IAttackSwingSource.SwingStage"/>）で用いる特別な段の値（Phase3.5 P3.5-05）。</summary>
    public static class AttackSwing
    {
        /// <summary>必殺技の振りを表す SwingStage 値。通常コンボ段（1..N）と区別する。</summary>
        public const int SpecialStage = 100;

        /// <summary>敵近接（通常）の振りを表す SwingStage 値（§7.2 通常）。</summary>
        public const int EnemyMeleeNormal = 200;

        /// <summary>敵近接（強）の振りを表す SwingStage 値（§7.2 強）。</summary>
        public const int EnemyMeleeHeavy = 201;

        /// <summary>敵近接（ガード不能）の振りを表す SwingStage 値（§7.2 ガード不能）。</summary>
        public const int EnemyMeleeUnblockable = 202;
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

    /// <summary>
    /// 敵剣閃 VFX の素材選択に用いる、敵タイプ（見た目の大きさ・種別）の識別鍵（Phase3.5 P3.5-05）。§7.2 の敵タイプ
    /// （近接骸骨＝Small／侍骸骨＝Medium 等）ごとに専用の剣閃素材を割り当てるため、Presentation が観測元から鍵を取得する。
    /// <see cref="IAttackSwingSource"/> を実装する敵が併せて実装する（プレイヤーは実装しない）。
    /// </summary>
    public interface IEnemySlashVisual
    {
        /// <summary>敵タイプ鍵（例："Small"／"Medium"）。Presentation の剣閃素材テーブルの引き当てに用いる。</summary>
        string SlashVfxKey { get; }
    }

    /// <summary>
    /// ガード不能攻撃の「予告（予兆）」を Presentation が観測するための読み取り専用契約（Phase3.5 P3.5-05）。
    /// ガード不能攻撃は Guard／JG 不可のため、発生前（Prepare 区間）に敵頭上へ警告表示を出して回避（Step）を促す。
    /// EnemyAttackController が実装する。Gameplay は一切変更しない。
    /// </summary>
    public interface IEnemyUnblockableWarningSource
    {
        /// <summary>ガード不能攻撃の予兆（Prepare 区間）中か。</summary>
        bool IsUnblockableTelegraphing { get; }

        /// <summary>予告表示の基準位置（world。敵の中心）。表示側が頭上オフセットを加える。</summary>
        Vector3 WarningPosition { get; }
    }
}
