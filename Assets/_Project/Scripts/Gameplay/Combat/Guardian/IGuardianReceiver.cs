using UnityEngine;

namespace Momotaro.Gameplay.Combat.Guardian
{
    /// <summary>
    /// 被弾を肩代わりできる守護者の契約（P4-01。守護／「かばう」）。仲間（犬丸ほか）が実装する想定だが、
    /// 仲間型には依存せず <see cref="IDamageable"/> であることだけを要求する（将来の護衛 NPC 等も同じ経路に載せられる）。
    ///
    /// 肩代わりの成立可否は 2 段で判定する：本人の状態（Down・退場・無効化など）は <see cref="CanTakeOver"/> が、
    /// 距離・クールダウン・対象選択といった状況判断は <see cref="IGuardianResolver"/> が担う。本契約は前者だけを表す。
    /// </summary>
    public interface IGuardianReceiver : IDamageable
    {
        /// <summary>守護者の現在位置（World）。転送する命中の接触点・進行方向の再計算に用いる。</summary>
        Vector3 WorldPosition { get; }

        /// <summary>
        /// 今この瞬間に肩代わりを引き受けられるか。Down・復帰待ち・退場・無効化中は false を返し、
        /// 呼び出し側は主人公への通常 Damage へフォールバックする。
        /// </summary>
        bool CanTakeOver { get; }
    }
}
