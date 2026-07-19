namespace Momotaro.Data
{
    /// <summary>
    /// 自己検証が可能なデータの共通インターフェース。
    /// Validation ツール（P0-12）が全 Data Asset を走査して呼び出す。
    /// </summary>
    public interface IValidatableData
    {
        /// <summary>
        /// 自身の内容を検証し、問題を <paramref name="report"/> へ追加する。
        /// 派生型は基底の実装を呼んだ上で固有の検証を追加すること。
        /// </summary>
        void Validate(DataValidationReport report);
    }
}
