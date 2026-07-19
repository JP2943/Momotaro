namespace Momotaro.Infrastructure.SceneFlow
{
    /// <summary>
    /// Scene 名の定数（仕様書 11.7 / 15.7）。Build Settings の登録名と一致させる。
    /// 文字列直書きを避け、遷移経路をここへ集約する。
    /// </summary>
    public static class SceneNames
    {
        /// <summary>常駐システムを起動する最初の Scene。</summary>
        public const string Bootstrap = "SCN_System_Bootstrap";

        /// <summary>タイトル前段の Launcher。</summary>
        public const string Launcher = "SCN_System_Launcher";

        /// <summary>非同期読込中に表示する Loading。</summary>
        public const string Loading = "SCN_System_Loading";

        /// <summary>垂直スライスのフィールド。</summary>
        public const string VsField = "SCN_VS_Field";
    }
}
