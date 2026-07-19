using Momotaro.Core.Logging;
using UnityEngine;

namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// Scene 直開き補助（仕様書 15.7）。Bootstrap Scene を経由せずに任意の Scene を直接開いた場合でも、
    /// <see cref="BootstrapRoot"/> が存在しなければ自動生成し、常駐サービスを起動する。
    /// Bootstrap Scene 経由の通常起動時は既に実体があるため何もしない。
    /// </summary>
    public static class BootstrapInitializer
    {
        private const string RootObjectName = "[BootstrapRoot]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (BootstrapRoot.HasInstance)
            {
                return;
            }

            GameLog.Info(LogCategory.Boot, "No BootstrapRoot in the opened scene; spawning one (direct-open fallback).");
            var go = new GameObject(RootObjectName);
            go.AddComponent<BootstrapRoot>();
        }
    }
}
