using System;
using System.Collections.Generic;
using System.Reflection;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// ブリッジから実行できる編集操作（P4 受入：作業中断を減らすための追加）。
    ///
    /// これまで Prefab の再生成や検証はメニューからの手作業しかなく、実装を届けても「メニューを 1 回押す」ために
    /// 作業が止まっていた。メニュー項目が確認ダイアログを出す（＝無人の PC で Editor が固まる）ため、
    /// ブリッジからメニューを叩くわけにはいかなかったのが理由。
    ///
    /// 実際にはダイアログを出しているのは <c>[MenuItem]</c> のラッパーだけで、<b>実処理はダイアログを出さない
    /// 公開メソッド</b>として分離されている。そこで本クラスは、そのメソッドだけを名前で列挙して呼ぶ。
    ///
    /// <b>安全側の作り</b>
    /// <list type="bullet">
    /// <item><description>呼べるのはここに書いた操作だけ。任意のメソッドは呼べない（引数も固定）。</description></item>
    /// <item><description>いずれもダイアログを出さず、何度実行しても同じ結果になる操作に限る。
    /// Scene を作り直す操作（試遊 Scene の再生成）は<b>載せない</b>。手で加えた変更を消してしまうため。</description></item>
    /// <item><description>参照は型名の文字列＋リフレクションで解決する。ブリッジが <c>Momotaro.Editor</c> を
    /// コンパイル時に参照すると、そちらが壊れたときブリッジごと動かなくなり、<b>コンパイルエラーを報告する手段が失われる</b>。
    /// 依存を持たないことでその事態を避ける。名前の綴りは EditMode テストが検査する。</description></item>
    /// </list>
    /// </summary>
    internal static class EditorBridgeOperations
    {
        /// <summary>犬丸の Data・攻撃 Data・Prefab を再生成する（既存があれば内容を保ちつつ配線だけ補う）。</summary>
        public const string BuildInumaru = "build-inumaru";

        /// <summary>プロジェクト内の全 Data アセットを検証する。</summary>
        public const string ValidateProjectData = "validate-project-data";

        /// <summary>実行できる操作の一覧（エラーメッセージにそのまま出す）。</summary>
        public static readonly string[] All = { BuildInumaru, ValidateProjectData };

        /// <summary>実行結果。</summary>
        public readonly struct OperationResult
        {
            /// <summary>操作として成立したか（検証で違反が見つかった場合は false）。</summary>
            public bool Success { get; }

            /// <summary>1 行の要約。</summary>
            public string Message { get; }

            /// <summary>詳細（生成物のパス・検証違反など）。</summary>
            public IReadOnlyList<string> Details { get; }

            public OperationResult(bool success, string message, IReadOnlyList<string> details = null)
            {
                Success = success;
                Message = message;
                Details = details ?? Array.Empty<string>();
            }
        }

        /// <summary>既知の操作か。</summary>
        public static bool IsKnown(string op)
        {
            return op == BuildInumaru || op == ValidateProjectData;
        }

        /// <summary>操作を実行する。未知の操作・呼び出し失敗は <see cref="OperationResult.Success"/> false で返す。</summary>
        public static OperationResult Run(string op)
        {
            try
            {
                switch (op)
                {
                    case BuildInumaru:
                        return RunBuildInumaru();

                    case ValidateProjectData:
                        return RunValidateProjectData();

                    default:
                        return new OperationResult(false,
                            "未知の操作: " + op + "（使えるのは " + string.Join(" / ", All) + "）");
                }
            }
            catch (TargetInvocationException e)
            {
                // 呼び出した側の例外は握りつぶさず、原因をそのまま返す。
                Exception inner = e.InnerException ?? e;
                return new OperationResult(false, op + " の実行で例外: " + inner.Message);
            }
            catch (Exception e)
            {
                return new OperationResult(false, op + " を実行できませんでした: " + e.Message);
            }
        }

        // ---- 個別の操作 ----

        private const string CompanionBuilderType = "Momotaro.Editor.Phase4.Phase4CompanionBuilder";
        private const string ProjectValidatorType = "Momotaro.Editor.Validation.ProjectDataValidator";

        private static OperationResult RunBuildInumaru()
        {
            Type builder = FindType(CompanionBuilderType);
            if (builder == null)
            {
                return new OperationResult(false, "型が見つかりません: " + CompanionBuilderType);
            }

            string prefabPath = ReadConstString(builder, "InumaruPrefabPath");
            string dataPath = ReadConstString(builder, "InumaruDataPath");
            string attackPath = ReadConstString(builder, "InumaruAttackDataPath");
            if (prefabPath == null || dataPath == null || attackPath == null)
            {
                return new OperationResult(false, CompanionBuilderType + " の出力先の定数が見つかりません。");
            }

            MethodInfo build = builder.GetMethod(
                "Build", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string), typeof(string), typeof(string) }, null);
            if (build == null)
            {
                return new OperationResult(false, CompanionBuilderType + ".Build(string, string, string) が見つかりません。");
            }

            object result = build.Invoke(null, new object[] { prefabPath, dataPath, attackPath });
            if (result == null)
            {
                return new OperationResult(false, "Build の戻り値が空でした。");
            }

            Type resultType = result.GetType();
            bool success = ReadProperty(resultType, result, "Success") is bool b && b;
            string message = ReadProperty(resultType, result, "Message") as string ?? string.Empty;

            var details = new List<string>();
            foreach (string line in message.Split('\n'))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    details.Add(line.Trim());
                }
            }

            return new OperationResult(
                success,
                success ? "犬丸の Prefab と Data を再生成しました。" : "犬丸の Prefab 生成に失敗しました。",
                details);
        }

        private static OperationResult RunValidateProjectData()
        {
            Type validator = FindType(ProjectValidatorType);
            if (validator == null)
            {
                return new OperationResult(false, "型が見つかりません: " + ProjectValidatorType);
            }

            MethodInfo runAll = validator.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (runAll == null)
            {
                return new OperationResult(false, ProjectValidatorType + ".RunAll() が見つかりません。");
            }

            object report = runAll.Invoke(null, null);
            if (report == null)
            {
                return new OperationResult(false, "RunAll の戻り値が空でした。");
            }

            Type reportType = report.GetType();
            bool hasErrors = ReadProperty(reportType, report, "HasErrors") is bool b && b;

            var details = new List<string>();
            if (ReadProperty(reportType, report, "Errors") is System.Collections.IEnumerable errors)
            {
                foreach (object e in errors)
                {
                    details.Add(e?.ToString() ?? string.Empty);
                }
            }

            return new OperationResult(
                !hasErrors,
                hasErrors ? "Data 検証でエラー " + details.Count + " 件。" : "Data 検証はすべて通りました。",
                details);
        }

        // ---- リフレクション補助 ----

        /// <summary>読み込み済みアセンブリから完全修飾名で型を探す（見つからなければ null）。</summary>
        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type t = assemblies[i].GetType(fullName, false);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static string ReadConstString(Type type, string fieldName)
        {
            FieldInfo f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            return f != null && f.IsLiteral ? f.GetRawConstantValue() as string : null;
        }

        private static object ReadProperty(Type type, object instance, string propertyName)
        {
            PropertyInfo p = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return p?.GetValue(instance);
        }
    }
}
