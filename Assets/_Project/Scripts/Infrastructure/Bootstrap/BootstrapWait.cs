using System.Collections;
using Momotaro.Core.Logging;
using UnityEngine;

namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// 常駐サービスの起動完了を待つ（P5-03b 修正。GPT レビュー R2 の指摘 2）。
    ///
    /// <b>Script Execution Order に頼らない。</b> <see cref="BootstrapRoot"/> も Scene 側の初期化担当も
    /// <c>Start()</c> で動くため、どちらが先かは決まっていない。先に動いた側が「まだ居ない」で諦めると、
    /// あとから常駐が立っても何も起きない。
    ///
    /// <b>固定フレーム待ちにもしない。</b> 「1 フレーム待てば大丈夫」は環境で崩れるし、
    /// 失敗したのか遅いのかも区別できない。<b>成否が確定するまで待ち</b>、上限を超えたら失敗として返す。
    /// </summary>
    public static class BootstrapWait
    {
        /// <summary>既定の待ち上限（unscaled 秒）。これを超えたら「起動していない」と判断する。</summary>
        public const float DefaultTimeoutSeconds = 10f;

        /// <summary>起動の成否が確定していて、かつ成功しているか。</summary>
        public static bool IsReadyNow =>
            BootstrapRoot.Instance != null
            && BootstrapRoot.Instance.BootstrapFinished
            && BootstrapRoot.Instance.BootstrapSucceeded;

        /// <summary>
        /// 起動の成否が確定するまで待つ。
        /// </summary>
        /// <param name="onResult">成功したかを受け取る。上限を超えた場合は false。</param>
        /// <param name="timeoutSeconds">待ち上限（unscaled 秒）。</param>
        public static IEnumerator Wait(System.Action<bool> onResult, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            float waited = 0f;
            while (waited < timeoutSeconds)
            {
                BootstrapRoot root = BootstrapRoot.Instance;
                if (root != null && root.BootstrapFinished)
                {
                    onResult?.Invoke(root.BootstrapSucceeded);
                    yield break;
                }

                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            GameLog.Warning(LogCategory.Boot,
                "常駐サービスの起動完了を " + timeoutSeconds + " 秒待ちましたが確定しませんでした。");
            onResult?.Invoke(false);
        }
    }
}
