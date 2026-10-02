using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 表示役が配信元・主人公を探すときの<b>同じ Scene 優先</b>の探索（P6A 後続課題 F06。P5.5 の 2 Area 在留）。
    ///
    /// <c>FindFirstObjectByType</c> は載っている Scene すべてから<b>どれか 1 つ</b>を返す。2 Area が同時に載っていると、
    /// 到着 Area の表示役が出発 Area の配信元を掴みうる（掴んだ相手が閉じれば、到着側の JG 閃光・手応えが出ない）。
    /// 活動ゲートの開閉順で今は避けられているが、順序に頼らず<b>自分と同じ Scene のもの</b>を選ぶ。
    /// 同じ Scene に無いときだけ従来どおり最初のもの（P3.5 の単一 Scene・常駐側に置いた構成）。
    /// </summary>
    public static class SceneLocalLookup
    {
        /// <summary>有効な <typeparamref name="T"/> のうち、<paramref name="requester"/> と同じ Scene のものを優先して返す。</summary>
        public static T FindPreferSameScene<T>(Component requester) where T : Component
        {
            T[] all = Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            if (all.Length == 0)
            {
                return null;
            }

            if (requester != null)
            {
                UnityEngine.SceneManagement.Scene scene = requester.gameObject.scene;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].gameObject.scene == scene)
                    {
                        return all[i];
                    }
                }
            }

            return all[0];
        }
    }
}
