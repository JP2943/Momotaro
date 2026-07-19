using System.Collections.Generic;
using Momotaro.Infrastructure.Input;

namespace Momotaro.Tests.EditMode
{
    /// <summary>テスト用のメモリ内リバインドストア。</summary>
    internal sealed class InMemoryRebindStore : IRebindStore
    {
        private readonly Dictionary<string, string> _map = new Dictionary<string, string>();

        public void Save(string key, string json)
        {
            _map[key] = json;
        }

        public string Load(string key)
        {
            return _map.TryGetValue(key, out string v) ? v : null;
        }

        public bool Has(string key)
        {
            return _map.ContainsKey(key);
        }
    }
}
