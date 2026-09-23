using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// Unity の非同期ロードを、調停役が観測できる形に包む（P5-03b。仕様書 v1.1 §6.3）。
    ///
    /// <b>調停役に <c>AsyncOperation</c> を直接持たせない。</b> Gameplay は UnityEngine の Scene API を
    /// 触らない層なのが 1 つ、もう 1 つは<b>タイムアウト後も同じ操作を観測し続ける</b>必要があるため
    /// （Unity の非同期ロードはキャンセルできない）。包んでおけばテストで Fake に差し替えられる。
    /// </summary>
    public sealed class AreaSceneLoadOperation : IAreaLoadOperation
    {
        private readonly AsyncOperation _operation;

        /// <summary>開始できなかった場合は <paramref name="operation"/> に null を渡す（即 Error 扱い）。</summary>
        public AreaSceneLoadOperation(AsyncOperation operation)
        {
            _operation = operation;
        }

        /// <inheritdoc />
        public bool IsDone => _operation == null || _operation.isDone;

        /// <inheritdoc />
        public bool HasError => _operation == null;
    }
}
