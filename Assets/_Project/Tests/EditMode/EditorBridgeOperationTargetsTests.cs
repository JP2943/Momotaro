using System.Reflection;
using Momotaro.Data;
using Momotaro.Editor.Phase4;
using Momotaro.Editor.Validation;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// Editor 常駐ブリッジが名前で呼ぶ操作の「呼び先」が実在することを検査する。
    ///
    /// ブリッジ本体は <c>Momotaro.Editor</c> をコンパイル時に参照していない（参照するとそちらが壊れたときに
    /// ブリッジごと動かなくなり、<b>コンパイルエラーを報告する手段が失われる</b>）。代わりにリフレクションで
    /// 型名・メソッド名を解決している。その綴りが実装の変更で腐っていないことを、依存を持てる<b>テスト側</b>から確かめる。
    ///
    /// これが赤になったら、ブリッジ側の名前（<c>EditorBridgeOperations</c>）を実装に合わせて直す。
    /// 実行時にしか分からない失敗を、テスト時に前倒しするための一本。
    /// </summary>
    public sealed class EditorBridgeOperationTargetsTests
    {
        [Test]
        public void BuildInumaru_TargetExists()
        {
            System.Type type = typeof(Phase4CompanionBuilder);

            Assert.AreEqual("Momotaro.Editor.Phase4.Phase4CompanionBuilder", type.FullName,
                "ブリッジは完全修飾名で型を探す。名前空間・型名を変えたらブリッジ側も直すこと。");

            MethodInfo build = type.GetMethod(
                "Build", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string), typeof(string) }, null);
            Assert.IsNotNull(build, "Build(string, string, string) をブリッジが呼ぶ。");

            foreach (string field in new[] { "InumaruPrefabPath", "InumaruDataPath", "InumaruAttackDataPath" })
            {
                FieldInfo f = type.GetField(field, BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(f, "出力先の定数 " + field + " をブリッジが読む。");
                Assert.IsTrue(f.IsLiteral, field + " は const であること（ブリッジは定数として読む）。");
            }
        }

        [Test]
        public void ValidateProjectData_TargetExists()
        {
            System.Type type = typeof(ProjectDataValidator);

            Assert.AreEqual("Momotaro.Editor.Validation.ProjectDataValidator", type.FullName);

            MethodInfo runAll = type.GetMethod(
                "RunAll", BindingFlags.Public | BindingFlags.Static, null, System.Type.EmptyTypes, null);
            Assert.IsNotNull(runAll, "RunAll() をブリッジが呼ぶ。");

            System.Type reportType = typeof(DataValidationReport);
            Assert.IsNotNull(reportType.GetProperty("HasErrors", BindingFlags.Public | BindingFlags.Instance),
                "検証結果の HasErrors をブリッジが読む。");
            Assert.IsNotNull(reportType.GetProperty("Errors", BindingFlags.Public | BindingFlags.Instance),
                "検証結果の Errors をブリッジが読む。");
        }

        [Test]
        public void BuildCompanionField_TargetExists()
        {
            System.Type builder = typeof(Phase4CompanionFieldBuilder);

            Assert.AreEqual("Momotaro.Editor.Phase4.Phase4CompanionFieldBuilder", builder.FullName,
                "ブリッジは完全修飾名で型を探す。名前空間・型名を変えたらブリッジ側も直すこと。");

            FieldInfo scenePath = builder.GetField("DefaultScenePath", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(scenePath, "出力先の定数 DefaultScenePath をブリッジが読む。");
            Assert.IsTrue(scenePath.IsLiteral, "DefaultScenePath は const であること。");

            MethodInfo build = builder.GetMethod(
                "Build", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            Assert.IsNotNull(build, "Build(string) をブリッジが呼ぶ。");

            System.Type resultType = typeof(Phase4CompanionFieldBuilder.BuildResult);
            Assert.IsNotNull(resultType.GetProperty("Success", BindingFlags.Public | BindingFlags.Instance));
            Assert.IsNotNull(resultType.GetProperty("Message", BindingFlags.Public | BindingFlags.Instance));
        }

        [Test]
        public void ValidateCompanionField_TargetExists()
        {
            System.Type validator = typeof(Phase4CompanionFieldValidator);

            Assert.AreEqual("Momotaro.Editor.Phase4.Phase4CompanionFieldValidator", validator.FullName);

            MethodInfo validate = validator.GetMethod(
                "Validate", BindingFlags.Public | BindingFlags.Static, null,
                new[]
                {
                    typeof(UnityEngine.SceneManagement.Scene),
                    typeof(System.Collections.Generic.List<string>),
                    typeof(System.Collections.Generic.List<string>),
                },
                null);

            Assert.IsNotNull(validate,
                "Validate(Scene, List<string>, List<string>) をブリッジが呼ぶ。引数の形を変えたらブリッジ側も直すこと。");
        }

        [Test]
        public void BuildResult_ExposesSuccessAndMessage()
        {
            System.Type resultType = typeof(Phase4CompanionBuilder.BuildResult);

            Assert.IsNotNull(resultType.GetProperty("Success", BindingFlags.Public | BindingFlags.Instance),
                "生成結果の Success をブリッジが読む。");
            Assert.IsNotNull(resultType.GetProperty("Message", BindingFlags.Public | BindingFlags.Instance),
                "生成結果の Message をブリッジが読む。");
        }
    }
}
