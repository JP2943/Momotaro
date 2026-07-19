namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// リバインド（キー再割り当て）の上書きJSONを永続化する抽象。
    /// 既定は <see cref="PlayerPrefsRebindStore"/>。テストではメモリ実装に差し替える。
    /// </summary>
    public interface IRebindStore
    {
        /// <summary>キーに対して JSON を保存する。</summary>
        void Save(string key, string json);

        /// <summary>キーの JSON を読み込む。無ければ null。</summary>
        string Load(string key);

        /// <summary>キーが存在するか。</summary>
        bool Has(string key);
    }
}
