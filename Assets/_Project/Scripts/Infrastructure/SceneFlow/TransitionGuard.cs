namespace Momotaro.Infrastructure.SceneFlow
{
    /// <summary>
    /// Scene 遷移の多重要求を防ぐ小さなガード。遷移中の追加要求は拒否する（仕様書 P0-08 受入条件）。
    /// </summary>
    public sealed class TransitionGuard
    {
        /// <summary>遷移が進行中か。</summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// 遷移を開始できるなら開始して true を返す。既に進行中なら false。
        /// </summary>
        public bool TryBegin()
        {
            if (IsActive)
            {
                return false;
            }

            IsActive = true;
            return true;
        }

        /// <summary>遷移を終了し、次の要求を受け付け可能にする。</summary>
        public void End()
        {
            IsActive = false;
        }
    }
}
