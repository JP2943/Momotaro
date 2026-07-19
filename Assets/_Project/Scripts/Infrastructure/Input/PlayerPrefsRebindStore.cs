using UnityEngine;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// PlayerPrefs を用いた <see cref="IRebindStore"/> の既定実装。
    /// Phase 0 の保存枠であり、必要なら後続 Phase でセーブ本体へ統合する。
    /// </summary>
    public sealed class PlayerPrefsRebindStore : IRebindStore
    {
        /// <inheritdoc />
        public void Save(string key, string json)
        {
            PlayerPrefs.SetString(key, json);
            PlayerPrefs.Save();
        }

        /// <inheritdoc />
        public string Load(string key)
        {
            return PlayerPrefs.GetString(key, null);
        }

        /// <inheritdoc />
        public bool Has(string key)
        {
            return PlayerPrefs.HasKey(key);
        }
    }
}
