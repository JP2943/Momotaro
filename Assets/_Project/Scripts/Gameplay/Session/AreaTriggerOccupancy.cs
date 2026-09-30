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
            if (trigger == null || player == null || !TryWorldBounds(trigger, out Bounds box))
            {
                return false;
            }

            Collider[] colliders = player.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider c = colliders[i];
                if (c == null || c.isTrigger)
                {
                    continue;
                }

                if (!TryWorldBounds(c, out Bounds other))
                {
                    continue;
                }

                if (box.Intersects(other))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// その Collider の世界 AABB を、<b>有効・無効に関わらず</b>求める（工程 P55-15b）。
        ///
        /// <b><c>Collider.bounds</c> に頼れない。</b> 測り直しが走るのは入場準備の最中で、
        /// そのとき<b>活動ゲートはまだ開いていない</b>——Collider は無効である。
        /// 無効な Collider の <c>bounds</c> は信頼できないので、
        /// そこを鵜呑みにすると「いつも範囲外」になり、
        /// <b>Trigger の中へ到着する配置で出られなくなる</b>（GPT 指摘 2）。
        ///
        /// <see cref="BoxCollider"/> は<b>形から自分で組む</b>（Trigger も主人公の当たりも Box）。
        /// それ以外は <c>bounds</c> へ落とすが、無効なら測れないものとして扱う——
        /// <b>測れないときに true を返さない</b>のがこの道具の約束である
        /// （固まった true を別の固まった true へ置き換えるだけになる）。
        /// </summary>
        private static bool TryWorldBounds(Collider collider, out Bounds bounds)
        {
            bounds = default;
            if (collider == null)
            {
                return false;
            }

            // <b>Capsule と Sphere も形から組む</b>（工程 P55-15b）。
            //
            // 主人公の当たりは <see cref="CapsuleCollider"/> で、入場準備の最中は
            // <b>活動ゲートが主人公の根ごと止めている</b>。Trigger 側だけ形から測っても、
            // 相手が測れなければ「重なっていない」になる——
            // <b>Trigger の中へ到着する配置で出られない</b>のが直らない。
            if (collider is CapsuleCollider capsule)
            {
                Transform ct = capsule.transform;
                float r = capsule.radius;
                float half = Mathf.Max(capsule.height * 0.5f, r);
                Vector3 axis = capsule.direction == 0 ? Vector3.right
                    : capsule.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 a = ct.TransformPoint(capsule.center + (axis * (half - r)));
                Vector3 b = ct.TransformPoint(capsule.center - (axis * (half - r)));

                // 半径は最大の拡大率で見る（等倍でない Transform でも足りる側へ寄せる）。
                Vector3 sc = ct.lossyScale;
                float radius = r * Mathf.Max(Mathf.Abs(sc.x),
                    Mathf.Max(Mathf.Abs(sc.y), Mathf.Abs(sc.z)));

                bounds = new Bounds(a, Vector3.zero);
                bounds.Encapsulate(b);
                bounds.Expand(radius * 2f);
                return true;
            }

            if (collider is SphereCollider sphere)
            {
                Transform st = sphere.transform;
                Vector3 ss = st.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(ss.x),
                    Mathf.Max(Mathf.Abs(ss.y), Mathf.Abs(ss.z)));
                bounds = new Bounds(st.TransformPoint(sphere.center), Vector3.one * (radius * 2f));
                return true;
            }

            if (collider is BoxCollider box)
            {
                Transform t = box.transform;
                Vector3 half = box.size * 0.5f;
                bool first = true;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? -half.x : half.x,
                        (i & 2) == 0 ? -half.y : half.y,
                        (i & 4) == 0 ? -half.z : half.z);
                    Vector3 world = t.TransformPoint(box.center + corner);
                    if (first)
                    {
                        bounds = new Bounds(world, Vector3.zero);
                        first = false;
                    }
                    else
                    {
                        bounds.Encapsulate(world);
                    }
                }

                return true;
            }

            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
            {
                return false;
            }

            bounds = collider.bounds;
            return true;
        }
    }
}
