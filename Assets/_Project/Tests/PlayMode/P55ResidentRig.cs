using Momotaro.Gameplay.Session;
using Momotaro.Presentation.Cameras;
using Momotaro.Presentation.Combat;
using UnityEngine;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 常駐 CameraRig をテスト間で持ち越さないための後始末（P5.5 付録 A.2）。
    ///
    /// <b>常駐は <c>DontDestroyOnLoad</c> なので Scene を読み替えても消えない。</b>
    /// 残したまま次のテストが試遊 Scene（自前のカメラを持つ）を読むと
    /// <b>Camera が 2 台・AudioListener が 2 つ</b>になり、
    /// 「活動中は各 1 つ」を見る検査が<b>そのテストとは無関係な理由で</b>落ちる。
    /// 既存の <c>BootstrapRoot</c> の後始末と同じ理由・同じ形で消す。
    ///
    /// 提供点（常駐 Camera・画面揺れ）も一緒に外す。静的な参照が残ると、
    /// 破棄済みの個体を次のテストが引いてしまう。
    /// </summary>
    internal static class P55ResidentRig
    {
        internal static void Reset()
        {
            // Instance が差さっている個体だけでは足りない——2 つ目として自壊する途中の個体や、
            // Awake 前に止まった個体は Instance に入らない。型で掃く。
            AreaCameraRigHost[] hosts =
                Object.FindObjectsByType<AreaCameraRigHost>(FindObjectsSortMode.None);
            for (int i = 0; i < hosts.Length; i++)
            {
                if (hosts[i] != null)
                {
                    Object.DestroyImmediate(hosts[i].gameObject);
                }
            }

            if (AreaCameraRigHost.Instance != null)
            {
                Object.DestroyImmediate(AreaCameraRigHost.Instance.gameObject);
            }

            AreaCameraRigHost.ResetDiagnosticsForTests();
            AreaCameraRegionSetRegistry.ClearForTests();
            AreaCameraOwnerProvider.ClearForTests();
            CameraShakeProvider.ClearForTests();
        }
    }
}
