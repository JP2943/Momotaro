using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 「調べる」を出す側（主人公）の読み取り契約（P4-07A。v1.0 §7.1「主人公が生存し、新しい Interact を実行できるか」）。
    /// 探索の調停役はこれだけを読み、主人公の状態機の中身を知らない。テストは Fake を注入する。
    /// </summary>
    public interface IInteractActor
    {
        /// <summary>位置（地点との距離の基準）。</summary>
        Vector3 Position { get; }

        /// <summary>前方（同距離の地点を「前方に近いほう」で選ぶ基準）。</summary>
        Vector3 Forward { get; }

        /// <summary>いま新しい Interact を実行できるか（通常の移動／待機だけ。攻撃・Step・Hurt・ガード・死亡中は不可）。</summary>
        bool CanInteract { get; }
    }
}
