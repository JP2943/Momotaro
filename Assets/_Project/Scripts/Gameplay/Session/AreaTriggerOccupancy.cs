using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// Trigger の「範囲内」を<b>記憶ではなく、いまの重なりから測る</b>（工程 P55-15a。試遊報告①）。
    ///
    /// <b>なぜ測り直しが要るのか。</b> <c>OnTriggerEnter</c>／<c>OnTriggerExit</c> は
    /// <b>その MonoBehaviour が有効なあいだ</b>しか届かない。
    /// <see cref="AreaActivityGate"/> は非活動 Area の Collider と Behaviour を止めるので、
    /// 止まっている間に主人公が範囲から出ても<b>退出が一度も届かない</b>。
    ///
    /// さらに悪いことに、出入口の Trigger と<b>到着入口はずれている</b>——
    /// 東西配置では出入口が x=−13.4（奥行 1.6 なので −14.2〜−12.6）、到着入口が x=−11.5 で、
    /// <b>到着位置は Trigger の外</b>である。だから再入場しても
    /// <c>OnTriggerEnter</c> も <c>OnTriggerExit</c> も起きず、
    /// 「範囲内」は<b>去ったときの true のまま固まる</b>。
    ///
    /// その状態で出口方向へ 0.15 秒入力すると、主人公がどこに居ても遷移が要求される——
    /// 試遊で「エリア B の戦闘区域を歩き回っていると、突然エリア A の『B へ』の位置まで
    /// 強制的に移動させられる」として報告された現象である。
    ///
    /// <b>測り方は AABB の重なりで足りる。</b> 入場の瞬間に「だいたい合っている」ところへ
    /// 直せば、そのあとは Trigger の出入りが真偽を引き継ぐ。
    /// 厳密な形で測る必要があるのは<b>境目に立っている場合だけ</b>で、
    /// そこは次のフレームの移動で <c>OnTriggerEnter</c>／<c>Exit</c> が直す。
    /// </summary>
    public static class AreaTriggerOccupancy
    {
        /// <summary>
        /// その Trigger が<b>いま</b>主人公と重なっているか。
        ///
        /// <paramref name="trigger"/> か <paramref name="player"/> が無ければ false
        /// （測れないときに true を返すと、固まった true を別の固まった true へ置き換えるだけになる）。
        /// </summary>
        public static bool IsOverlappingPlayer(Collider trigger, PlayerRoot player)
        {
            if (trigger == null || player == null)
            {
                return false;
            }

            Bounds box = trigger.bounds;
            Collider[] colliders = player.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || c.isTrigger || !c.enabled)
                {
                    continue;
                }

                if (box.Intersects(c.bounds))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
