using System.Collections.Generic;
using Momotaro.Core.Logging;

namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// 常駐サービスを登録順に初期化する。初期化順を一元管理し、Awake 順への暗黙依存を排除する。
    /// Critical なサービスが失敗した時点で以降の初期化を止め、全体を失敗として返す。
    /// 非 Critical の失敗はログを残して続行する。
    /// </summary>
    public sealed class ServiceRegistry
    {
        private readonly List<IGameService> _services = new List<IGameService>();

        /// <summary>登録済みサービス数。</summary>
        public int Count => _services.Count;

        /// <summary>
        /// サービスを初期化順の末尾に登録する。
        /// </summary>
        public void Register(IGameService service)
        {
            if (service == null)
            {
                GameLog.Warning(LogCategory.Boot, "Attempted to register a null service; ignored.");
                return;
            }

            _services.Add(service);
        }

        /// <summary>
        /// 登録順に全サービスを初期化する。
        /// </summary>
        /// <returns>
        /// Critical な失敗が無く、Launcher へ進んでよい場合に true。
        /// Critical な失敗があった場合は false（起動を停止すべき）。
        /// </returns>
        public bool InitializeAll()
        {
            for (int i = 0; i < _services.Count; i++)
            {
                IGameService service = _services[i];
                ServiceInitResult result;

                try
                {
                    result = service.Initialize();
                }
                catch (System.Exception ex)
                {
                    // 例外は Critical 失敗として扱い、起動を停止する。
                    GameLog.Error(LogCategory.Boot,
                        "Service threw during Initialize: " + ex.Message, id: service.ServiceName);
                    return false;
                }

                if (result.Success)
                {
                    GameLog.Info(LogCategory.Boot,
                        "Initialized. " + (result.Message ?? string.Empty), id: service.ServiceName);
                    continue;
                }

                if (result.IsCritical)
                {
                    GameLog.Error(LogCategory.Boot,
                        "Critical service failed, aborting bootstrap: " + (result.Message ?? "(no message)"),
                        id: service.ServiceName);
                    return false;
                }

                GameLog.Warning(LogCategory.Boot,
                    "Non-critical service failed, continuing: " + (result.Message ?? "(no message)"),
                    id: service.ServiceName);
            }

            return true;
        }
    }
}
