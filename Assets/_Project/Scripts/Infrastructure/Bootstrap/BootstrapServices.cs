namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// 常駐サービスへの参照口（P5-03b）。Scene 側の初期化担当が
    /// <c>FindObjectOfType</c> を使わずに常駐サービスへ辿るために置く。
    ///
    /// <b>万能マネージャにしない。</b> ここにあるのは「登録済みサービスを型で引く」だけで、
    /// 状態も生成も持たない。実体は <see cref="BootstrapRoot"/> の <see cref="ServiceRegistry"/>。
    /// Bootstrap が起動していなければ null を返し、呼び出し側が理由付きで失敗する。
    /// </summary>
    public static class BootstrapServices
    {
        /// <summary>登録済みサービスを型で引く。Bootstrap 未起動なら null。</summary>
        public static T Get<T>() where T : class, IGameService
        {
            return BootstrapRoot.Instance != null ? BootstrapRoot.Instance.GetService<T>() : null;
        }

        /// <summary>Bootstrap が起動しているか。</summary>
        public static bool IsReady => BootstrapRoot.Instance != null;
    }
}
