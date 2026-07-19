using System.Collections.Generic;
using Momotaro.Infrastructure.Bootstrap;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// テスト用スタブサービス。初期化結果を指定でき、初期化された順序を共有リストへ記録する。
    /// </summary>
    internal sealed class StubGameService : IGameService
    {
        private readonly ServiceInitResult _result;
        private readonly List<string> _order;
        private readonly bool _throws;

        public StubGameService(string name, ServiceInitResult result, List<string> order, bool throws = false)
        {
            ServiceName = name;
            _result = result;
            _order = order;
            _throws = throws;
        }

        public string ServiceName { get; }

        public ServiceInitResult Initialize()
        {
            _order.Add(ServiceName);
            if (_throws)
            {
                throw new System.InvalidOperationException("stub failure");
            }

            return _result;
        }
    }
}
