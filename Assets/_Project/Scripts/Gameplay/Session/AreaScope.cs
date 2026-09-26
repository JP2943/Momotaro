using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 登録簿の既定の絞り込み（P5.5 §4.3。棚卸し報告書 §2.2）。
    ///
    /// <b>P5.5 最大の危険箇所はここ。</b> static Registry 5 件はすべて Area Scene の部品が
    /// <c>OnEnable</c> で自己登録する。Area が 2 つ同時に在ると、
    /// 境界の向こうのレバー・扉が Interact 候補に挙がり、犬丸が隣 Area の地点へ調査に行き、
    /// <b>敵が境界越しに索敵する</b>——仕様書が対象外と明記した挙動がそのまま起きる。
    ///
    /// <b>構造ゲート（保存時から非 Active）だけでは足りない。</b> Prepared は
    /// 「到着先 Actor を復元・配線し、活動ゲートは閉じたまま」（§4.1）なので、
    /// 復元のために Actor を有効化した時点で索敵・演出の登録が走る。
    /// だから登録そのものは止めず、<b>利用者側が非活動 Area を除外できる</b>ようにする。
    ///
    /// 判定は 3 段。安全側（隠さない側）に倒してあるのは、絞り込みが効きすぎると
    /// 「主人公が誰にも見えない」「調べられるものが何も無い」という形で壊れるため。
    /// <list type="number">
    /// <item><description>現行の指定が無い → 絞らない（P3.5／P4／P5 の単一 Area 構成とテスト）。</description></item>
    /// <item><description>現行 Area の Scene の物 → 見せる。</description></item>
    /// <item><description><b>Area Scene ではない</b>物（常駐・起動 Scene・テストが作った実行時 Scene）→ 見せる。</description></item>
    /// </list>
    /// 隠すのは「束が載っている＝Area だと分かっていて、かつ現行ではない」Scene の物だけ。
    /// </summary>
    public static class AreaScope
    {
        /// <summary>絞り込みで実際に隠した回数（診断・テスト用）。</summary>
        public static int HiddenCount { get; private set; }

        /// <summary>いまゲームから見えてよい登録か。</summary>
        public static bool IsVisible(object item)
        {
            // 単一 Area 構成では 1 行目で抜ける（Unity API に触らない）。
            if (!CurrentAreaProvider.HasScope)
            {
                return true;
            }

            if (!(item is Component component) || component == null)
            {
                // Component でない登録（テストの Fake 等）は Scene に属さないので絞らない。
                return true;
            }

            int handle = component.gameObject.scene.handle;
            if (handle == CurrentAreaProvider.ActiveSceneHandle)
            {
                return true;
            }

            if (!AreaBundleDirectory.TryGetByScene(handle, out _))
            {
                return true;
            }

            HiddenCount++;
            return false;
        }

        /// <summary>その登録が指定した Scene の物か（撤去時の一括解除に使う）。</summary>
        public static bool BelongsTo(object item, int sceneHandle)
        {
            if (sceneHandle == 0 || !(item is Component component) || component == null)
            {
                return false;
            }

            return component.gameObject.scene.handle == sceneHandle;
        }

        /// <summary>診断値を戻す（テストの後始末）。</summary>
        public static void ResetDiagnostics()
        {
            HiddenCount = 0;
        }
    }
}
