using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

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
    /// Scene を作り直す操作は、<b>未保存の変更があるとき自分で断る</b>ものだけ載せる
    /// （<c>build-companion-field</c> / <c>build-companion-trial</c>）。断らない操作は手で加えた変更を消してしまう。</description></item>
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

        /// <summary>実行記録を必須テスト一覧と照合する（工程の受入判定）。</summary>
        public const string VerifyRequiredTests = "verify-required-tests";

        /// <summary>仲間の検証 Scene を再生成し、そのまま検査する（P4-FIX F01）。</summary>
        public const string BuildCompanionField = "build-companion-field";

        /// <summary>仲間試遊 Scene を再生成し、そのまま検査する（P4-08R）。</summary>
        public const string BuildCompanionTrial = "build-companion-trial";

        /// <summary>実行できる操作の一覧（エラーメッセージにそのまま出す）。</summary>
        public static readonly string[] All =
            { BuildInumaru, ValidateProjectData, VerifyRequiredTests, BuildCompanionField, BuildCompanionTrial };

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
            return op == BuildInumaru || op == ValidateProjectData || op == VerifyRequiredTests
                || op == BuildCompanionField || op == BuildCompanionTrial;
        }

        /// <summary>操作を実行する。未知の操作・呼び出し失敗は <see cref="OperationResult.Success"/> false で返す。</summary>
        public static OperationResult Run(BridgeCommand command)
        {
            string op = command?.op;

            try
            {
                switch (op)
                {
                    case BuildInumaru:
                        return RunBuildInumaru();

                    case ValidateProjectData:
                        return RunValidateProjectData();

                    case VerifyRequiredTests:
                        return RunVerifyRequiredTests(command);

                    case BuildCompanionField:
                        return RunBuildCompanionField();

                    case BuildCompanionTrial:
                        return RunBuildCompanionTrial();

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

        /// <summary>
        /// 実行記録（<c>_bridge/runs/&lt;id&gt;.json</c>）を必須テスト一覧と照合する。
        ///
        /// テストの実行そのものと、工程の受入判定を分けている。判定を実行中のテストの中でやると、
        /// 自分自身の結果を見ることになって成立しない。ここは実行が終わったあとに走る別の口。
        /// </summary>
        private static OperationResult RunVerifyRequiredTests(BridgeCommand command)
        {
            Phase4RequiredTests.Manifest manifest = Phase4RequiredTests.Load(out string loadError);
            if (manifest == null)
            {
                return new OperationResult(false, loadError);
            }

            string[] runIds = SplitIds(command?.runIds);
            if (runIds.Length == 0)
            {
                return new OperationResult(false,
                    "照合する実行記録が指定されていません（runIds にコマンド id をカンマ区切りで指定してください）。");
            }

            var leaves = new List<Phase4RequiredTestGate.LeafOutcome>();
            var sources = new List<string>();
            var problems = new List<string>();

            foreach (string runId in runIds)
            {
                string path = EditorBridgePaths.RunLog(runId);
                if (!File.Exists(path))
                {
                    problems.Add("実行記録が見つかりません: " + runId);
                    continue;
                }

                BridgeRunLog log;
                try
                {
                    log = JsonUtility.FromJson<BridgeRunLog>(File.ReadAllText(path));
                }
                catch (Exception e)
                {
                    problems.Add("実行記録を読めません（" + runId + "）: " + e.Message);
                    continue;
                }

                if (log == null)
                {
                    problems.Add("実行記録が空です: " + runId);
                    continue;
                }

                // 実行自体が成立していない記録を、照合の材料にしない（欠落した結果は「Passed でない」ではなく「不明」）。
                if (!string.Equals(log.termination, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add("実行が完走していない記録です（" + runId + "、termination=" + log.termination + "）。");
                }

                sources.Add(runId + "（" + log.mode + "、"
                    + (string.IsNullOrEmpty(log.filter) ? "全件" : "filter=" + log.filter) + "、"
                    + log.leaves.Length + " 件）");

                foreach (BridgeLeafResult leaf in log.leaves)
                {
                    leaves.Add(new Phase4RequiredTestGate.LeafOutcome(leaf.fullName, leaf.status));
                }
            }

            string stage = command?.stage;
            List<string> required = manifest.RequiredFullNames(stage);
            List<string> allowedSkips = manifest.AllowedSkipFullNames();

            Phase4RequiredTestGate.GateReport report =
                Phase4RequiredTestGate.Check(required, allowedSkips, leaves);

            var details = new List<string>();
            details.Add("工程: " + (string.IsNullOrEmpty(stage) ? "（全工程）" : stage));
            foreach (string source in sources)
            {
                details.Add("実行記録: " + source);
            }

            foreach (string problem in problems)
            {
                details.Add("[問題] " + problem);
            }

            foreach (string missing in report.Missing)
            {
                details.Add("[欠落] " + missing);
            }

            foreach (string notPassed in report.NotPassed)
            {
                details.Add("[非 Passed] " + notPassed);
            }

            foreach (string skip in report.UnlistedSkips)
            {
                details.Add("[未説明の Skip] " + skip);
            }

            // 合意済み要求（E／P／R）に対応するテストが一覧に無ければ、テストが全部 Passed でも受入ではない（レビュー R2-10）。
            // 「テストが緑」と「要求が押さえられている」を別々に検査する。
            List<Phase4RequiredTests.RequirementEntry> unmet = manifest.UnmetRequirements(stage);
            foreach (Phase4RequiredTests.RequirementEntry requirement in unmet)
            {
                details.Add("[未対応要求] " + requirement.id + ": " + requirement.summary);
            }

            bool success = report.Passed && problems.Count == 0 && unmet.Count == 0;
            string summary = report.Summarize();
            if (unmet.Count > 0)
            {
                summary += " 未対応の要求 " + unmet.Count + " 件。";
            }

            return new OperationResult(success, summary, details);
        }

        /// <summary>カンマ区切りの id を分解する（空白は落とす）。</summary>
        private static string[] SplitIds(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return Array.Empty<string>();
            }

            string[] parts = raw.Split(',');
            var ids = new List<string>();
            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                {
                    ids.Add(trimmed);
                }
            }

            return ids.ToArray();
        }

        private const string CompanionBuilderType = "Momotaro.Editor.Phase4.Phase4CompanionBuilder";
        private const string ProjectValidatorType = "Momotaro.Editor.Validation.ProjectDataValidator";
        private const string CompanionFieldBuilderType = "Momotaro.Editor.Phase4.Phase4CompanionFieldBuilder";
        private const string CompanionFieldValidatorType = "Momotaro.Editor.Phase4.Phase4CompanionFieldValidator";
        private const string CompanionTrialBuilderType = "Momotaro.Editor.Phase4.Phase4CompanionTrialBuilder";
        private const string CompanionTrialValidatorType = "Momotaro.Editor.Phase4.Phase4CompanionTrialValidator";

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

        /// <summary>
        /// 仲間の検証 Scene を再生成し、続けて Validator にかける（P4-FIX F01）。
        ///
        /// <b>Scene を作り直す操作をブリッジへ載せるのは、これが初めて。</b>これまで載せなかったのは、
        /// 手で加えた変更を黙って消してしまうため。ここでは<b>未保存の変更があるときは実行せず断る</b>ことで
        /// その危険を無くしてある（オーナーが席を外していても、保存していない作業は壊れない）。
        /// 生成は決定的なので、保存済みの状態から作り直すのは元へ戻すのと同じ意味しか持たない。
        /// </summary>
        private static OperationResult RunBuildCompanionField() =>
            RunSceneBuild(CompanionFieldBuilderType, CompanionFieldValidatorType, "仲間の検証 Scene");

        /// <summary>仲間試遊 Scene を再生成し、続けて Validator にかける（P4-08R）。条件は検証 Scene と同じ。</summary>
        private static OperationResult RunBuildCompanionTrial() =>
            RunSceneBuild(CompanionTrialBuilderType, CompanionTrialValidatorType, "仲間試遊 Scene");

        private static OperationResult RunSceneBuild(string builderType, string validatorType, string label)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene open = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    return new OperationResult(false,
                        "未保存の変更がある Scene が開いているため実行しません（"
                        + (string.IsNullOrEmpty(open.path) ? "無題Scene" : open.path)
                        + "）。保存してから再実行してください。");
                }
            }

            Type builder = FindType(builderType);
            if (builder == null)
            {
                return new OperationResult(false, "型が見つかりません: " + builderType);
            }

            string scenePath = ReadConstString(builder, "DefaultScenePath");
            if (scenePath == null)
            {
                return new OperationResult(false, builderType + ".DefaultScenePath が見つかりません。");
            }

            MethodInfo build = builder.GetMethod(
                "Build", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (build == null)
            {
                return new OperationResult(false, builderType + ".Build(string) が見つかりません。");
            }

            object result = build.Invoke(null, new object[] { scenePath });
            if (result == null)
            {
                return new OperationResult(false, "Build の戻り値が空でした。");
            }

            Type resultType = result.GetType();
            bool success = ReadProperty(resultType, result, "Success") is bool b && b;
            string message = ReadProperty(resultType, result, "Message") as string ?? string.Empty;

            var details = new List<string> { scenePath, message };
            if (!success)
            {
                return new OperationResult(false, label + "の生成に失敗しました。", details);
            }

            // 生成しただけで終わらせない。出荷される Scene そのものを検査して、
            // 「作った」と「正しい」を分けて報告する。
            return ValidateBuiltScene(validatorType, label, details);
        }

        /// <summary>生成直後の Scene を対応する Validator にかける。</summary>
        private static OperationResult ValidateBuiltScene(string validatorType, string label, List<string> details)
        {
            Type validator = FindType(validatorType);
            if (validator == null)
            {
                return new OperationResult(false, "型が見つかりません: " + validatorType, details);
            }

            MethodInfo validate = validator.GetMethod(
                "Validate", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(UnityEngine.SceneManagement.Scene), typeof(List<string>), typeof(List<string>) }, null);
            if (validate == null)
            {
                return new OperationResult(false,
                    validatorType + ".Validate(Scene, List<string>, List<string>) が見つかりません。", details);
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            validate.Invoke(null, new object[]
            {
                UnityEngine.SceneManagement.SceneManager.GetActiveScene(), errors, warnings,
            });

            foreach (string w in warnings)
            {
                details.Add("[警告] " + w);
            }

            foreach (string e in errors)
            {
                details.Add("[エラー] " + e);
            }

            return new OperationResult(
                errors.Count == 0,
                errors.Count == 0
                    ? label + "を生成し、検査も通りました（警告 " + warnings.Count + " 件）。"
                    : label + "は生成しましたが検査でエラー " + errors.Count + " 件。",
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
