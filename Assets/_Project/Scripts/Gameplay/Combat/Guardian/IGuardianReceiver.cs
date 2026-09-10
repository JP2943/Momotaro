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

        /// <summary>
        /// 転送された命中を受け取り、<b>実際に処理したか</b>を返す。
        ///
        /// <see cref="CanTakeOver"/> を通っても、受け口が命中を捨てることがある。最も起きやすいのは
        /// <b>同一命中の二重受理を弾く場合</b>で、敵の 1 振りが主人公と仲間の両方に重なったときに起きる
        /// （<see cref="HitId"/> は攻撃側で決まるため、両者に届くのは同じ id）。仲間が判定から直接受けた後に
        /// 主人公からの転送が届くと、それは正しく捨てられる。
        ///
        /// このとき転送を「成立した」と扱うと、<b>主人公は自分に当たった命中を無傷でやり過ごす</b>。
        /// 肩代わりは誰かが痛みを引き受ける仕組みなので、引き受け手が居なかった転送は成立していない。
        /// false を返して呼び出し側を通常 Damage へ戻すため、可否を <see cref="ReceiveHit"/> と分けて公開する。
        /// </summary>
        /// <returns>受理した（ダメージ・ガード・回避のいずれかとして処理した）なら true。捨てたなら false。</returns>
        bool TryReceiveTransferredHit(in HitInfo hit);
    }
}
