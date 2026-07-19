using Momotaro.Core.Logging;
using Momotaro.Data;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Momotaro.Editor.Validation
{
    /// <summary>
    /// ビルド前に Data 検証を実行し、エラーがあればビルドを停止する Build Hook 候補（仕様書 11.11）。
    /// 「エラー時は本番ビルドを停止する」方針を満たす。警告は停止させない。
    /// </summary>
    public sealed class ValidateOnBuild : IPreprocessBuildWithReport
    {
        /// <inheritdoc />
        public int callbackOrder => 0;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            DataValidationReport result = ProjectDataValidator.RunAll();

            if (!result.HasErrors)
            {
                GameLog.Info(LogCategory.Validation,
                    "Data validation passed (" + result.Warnings.Count + " warning(s)).");
                return;
            }

            GameLog.Error(LogCategory.Validation,
                "Data validation failed with " + result.Errors.Count + " error(s). Build aborted.");

            throw new BuildFailedException(
                "Data validation failed with " + result.Errors.Count +
                " error(s). Run 'Momotaro/Validation/Validate Project Data' for details.");
        }
    }
}
