namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中結果（<see cref="HitResult"/>）を購読する側の契約（P2-01 受入修正）。
    /// Presentation（HUD・VFX・SE・ヒットストップ要求など）が後続 Task でこれを実装し、
    /// <see cref="HitResultChannel"/> を通じて型付き結果を受け取る。
    ///
    /// P2-01 では契約定義のみで、具象 Presentation は実装しない。
    /// </summary>
    public interface IHitResultListener
    {
        /// <summary>命中結果を受け取る。</summary>
        /// <param name="result">解決済みの型付き結果。</param>
        void OnHitResult(in HitResult result);
    }
}
