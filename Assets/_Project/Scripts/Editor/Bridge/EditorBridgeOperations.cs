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

        /// <summary>P5 の探索試遊（3 Scene・Data カタログ・仮地形）を再生成する（P5-02。仕様書 §16.1）。</summary>
        public const string BuildExplorationTrial = "build-exploration-trial";

        /// <summary>P5 の探索試遊を Scene・Asset の両面から検査する（P5-09。仕様書 §16.1）。</summary>
        public const string ValidateExplorationTrial = "validate-exploration-trial";

        /// <summary>
        /// P5.5 の実ワールド配置（A の東に B・接続 Data）を再生成する（P55-03c。仕様書 §3.2）。
        /// P5 の受入用 Scene は作り替えない（別フォルダへ出す）。
        /// </summary>
        public const string BuildPhase55World = "build-phase55-world";

        /// <summary>
        /// P5.5 の実ワールド配置を検査する（P55-03c。仕様書 §3.2／§7.1／§7.3／§10）。
        /// 2 つの Scene の<b>関係</b>（境界・通路・並び・カメラ軸・覆い・接続 Data）を見る。
        /// </summary>
        public const string ValidatePhase55World = "validate-phase55-world";

        /// <summary>P6A の検証 campaign（A–B–C・タイトル・Data）を再生成する（P6A-06。P6 仕様 §11）。</summary>
        public const string BuildPhase6World = "build-phase6-world";

        /// <summary>P6A の検証 campaign を検査する（配置は P5.5 と同じ基準、加えて campaign の整合）。</summary>
        public const string ValidatePhase6World = "validate-phase6-world";

        /// <summary>
        /// P6A の Windows 実ビルドを作り、別プロセスで New Game／正常終了／Continue と保存性能の計測を走らせる（P6A 23／25）。
        /// Scene は開かない（ビルドは保存済みの Scene から作る）。数分かかる。
        /// </summary>
        public const string P6APlayerSmoke = "p6a-player-smoke";

        /// <summary>P6B の検証 campaign（P6A と同じ 3 エリア構成を設定で再利用）を再生成する（P6B 04）。</summary>
        public const string BuildPhase6BWorld = "build-phase6b-world";

        /// <summary>P6B の検証 campaign を検査する（P6A と同じ検査＋9 ノード・使用・払い戻し・入力。P6B 17）。</summary>
        public const string ValidatePhase6BWorld = "validate-phase6b-world";

        /// <summary>P6B の Windows 実ビルドで成長・権利・残数・HP の別プロセス復元を確かめる（P6B 19）。</summary>
        public const string P6BPlayerSmoke = "p6b-player-smoke";

        /// <summary>P6C の検証 campaign（P6B と同じ構成を設定で再利用）を再生成する（P6C 15）。</summary>
        public const string BuildPhase6CWorld = "build-phase6c-world";

        /// <summary>P6C の検証 campaign を検査する（P6B と同じ検査＋ジャスト回避の Data・ガード不能の可否・表示と音の配線。P6C 15）。</summary>
        public const string ValidatePhase6CWorld = "validate-phase6c-world";

        /// <summary>P6C の Windows 実ビルドで実入力のジャスト回避・反撃・別プロセス Continue を確かめる（P6C 15）。</summary>
        public const string P6CPlayerSmoke = "p6c-player-smoke";

        /// <summary>実行できる操作の一覧（エラーメッセージにそのまま出す）。</summary>
        public static readonly string[] All =
        {
            BuildInumaru, ValidateProjectData, VerifyRequiredTests,
            BuildCompanionField, BuildCompanionTrial, BuildExplorationTrial,
            ValidateExplorationTrial, BuildPhase55World, ValidatePhase55World,
            BuildPhase6World, ValidatePhase6World, P6APlayerSmoke,
            BuildPhase6BWorld, ValidatePhase6BWorld, P6BPlayerSmoke,
            BuildPhase6CWorld, ValidatePhase6CWorld, P6CPlayerSmoke,
        };

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
                || op == BuildCompanionField || op == BuildCompanionTrial || op == BuildExplorationTrial
                || op == ValidateExplorationTrial || op == BuildPhase55World
                || op == ValidatePhase55World || op == BuildPhase6World || op == ValidatePhase6World
                || op == P6APlayerSmoke || op == BuildPhase6BWorld || op == ValidatePhase6BWorld
                || op == P6BPlayerSmoke || op == BuildPhase6CWorld || op == ValidatePhase6CWorld
                || op == P6CPlayerSmoke;
        }

        /// <summary>操作を実行する。未知の操作・呼び出し失敗は <see cref="OperationResult.Success"/> false で返す。</summary>
        public static OperationResult Run(BridgeCommand command)
        {
            string op = command?.op;

            try
            {
                // Test Runner が残した未保存の一時 Scene（InitTestScene）は捨てる。これが開いたままだと
                // 「未保存の変更がある Scene」として Scene 操作が全部断られる（中断した PlayMode のあとに起きる）。
                // それ以外の未保存 Scene は従来どおり各操作が断る。
                // 戻り値（他の未保存 Scene があるという理由）はここでは使わない——その場合は何も捨てておらず、
                // Scene を触る各操作が自分で断る。
                EditorBridgeTestRun.PrepareScenesForPlayMode();
                switch (op)
                {
                    case BuildInumaru:
                        return RunBuildInumaru();

                    case ValidateProjectData:
                        return RunValidateProjectData();

                    case VerifyRequiredTests:
                        return RunVerifyRequiredTests(command);

                    case BuildExplorationTrial:
                        return RunBuildExplorationTrial();

                    case ValidateExplorationTrial:
                        return RunValidateExplorationTrial();

                    case BuildPhase55World:
                        return RunBuilder(Phase55WorldBuilderType);

                    case ValidatePhase55World:
                        return RunPhase55WorldValidation();

                    case BuildPhase6World:
                        return RunBuilder(Phase6WorldBuilderType);

                    case ValidatePhase6World:
                        return RunWorldValidation(Phase6WorldValidatorType, "P6A 検証ワールド");

                    case P6APlayerSmoke:
                        return RunBuilder(Phase6PlayerSmokeType);

                    case BuildPhase6BWorld:
                        return RunBuilder(Phase6WorldBuilderType, "BuildP6B");

                    case ValidatePhase6BWorld:
                        return RunWorldValidation(Phase6WorldValidatorType, "P6B 検証ワールド", "ValidateP6B");

                    case P6BPlayerSmoke:
                        return RunBuilder(Phase6PlayerSmokeType, "BuildAllP6B");

                    case BuildPhase6CWorld:
                        return RunBuilder(Phase6WorldBuilderType, "BuildP6C");

                    case ValidatePhase6CWorld:
                        return RunWorldValidation(Phase6WorldValidatorType, "P6C 検証ワールド", "ValidateP6C");

                    case P6CPlayerSmoke:
                        return RunBuilder(Phase6PlayerSmokeType, "BuildAllP6C");

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
            // 一覧は許可リストから選ぶ（§16.1）。空なら既定の P4 で、既存の照合は変わらない。
            string manifestKey = command?.manifest;
            Phase4RequiredTests.Manifest manifest = Phase4RequiredTests.Load(manifestKey, out string loadError);
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
            Phase4RequiredTests.TryResolveFileName(manifestKey, out string manifestFile, out _);
            details.Add("必須一覧: " + manifestFile);
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
        private const string ExplorationBuilderType = "Momotaro.Editor.Phase5.Phase5ExplorationBuilder";
        private const string ExplorationValidatorType = "Momotaro.Editor.Phase5.Phase5ExplorationValidator";
        private const string ExplorationAssetValidatorType = "Momotaro.Editor.Phase5.Phase5AssetValidator";
        private const string Phase55WorldBuilderType = "Momotaro.Editor.Phase55.Phase55WorldBuilder";
        private const string Phase55WorldValidatorType = "Momotaro.Editor.Phase55.Phase55WorldValidator";
        private const string Phase6WorldBuilderType = "Momotaro.Editor.Phase6.Phase6WorldBuilder";
        private const string Phase6WorldValidatorType = "Momotaro.Editor.Phase6.Phase6WorldValidator";
        private const string Phase6PlayerSmokeType = "Momotaro.Editor.Phase6.Phase6PlayerSmoke";

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

        /// <summary>
        /// P5 の探索試遊（3 Scene・Data カタログ・仮地形）を再生成する（P5-02。仕様書 §16.1）。
        ///
        /// <b>未保存の Scene 変更があれば Builder 自身が断る</b>ため、手で加えた変更を黙って消さない
        /// （`CLAUDE.md`「Scene を作り直す操作を載せてよいのは、未保存の変更があるとき自分で断る場合だけ」）。
        /// 本アセンブリは Momotaro.Editor を参照しないので、既存の Scene 生成と同じく反射で呼ぶ。
        /// </summary>
        private static OperationResult RunBuildExplorationTrial() => RunBuilder(ExplorationBuilderType);

        /// <summary>
        /// P5.5 の実ワールド配置を検査する（P55-03c）。
        ///
        /// <b>Scene を開く操作なので、未保存の変更があれば断る</b>（生成と同じ扱い）。
        /// </summary>
        private static OperationResult RunPhase55WorldValidation() =>
            RunWorldValidation(Phase55WorldValidatorType, "P5.5 実ワールド配置");

        /// <summary>
        /// <c>Validate(List&lt;string&gt;, List&lt;string&gt;)</c> を持つワールド検査を呼ぶ（P5.5／P6A で同じ形）。
        /// <b>Scene を開く操作なので、未保存の変更があれば断る</b>（生成と同じ扱い）。
        /// </summary>
        private static OperationResult RunWorldValidation(string validatorTypeName, string label,
            string methodName = "Validate")
        {
            for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene open =
                    UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    return new OperationResult(false,
                        "未保存の変更がある Scene が開いているため検査しません（"
                        + (string.IsNullOrEmpty(open.path) ? "(無題 Scene)" : open.path)
                        + "）。保存するか破棄してから再実行してください。");
                }
            }

            Type validator = FindType(validatorTypeName);
            if (validator == null)
            {
                return new OperationResult(false, "型が見つかりません: " + validatorTypeName);
            }

            MethodInfo validate = validator.GetMethod(
                methodName, BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(List<string>), typeof(List<string>) }, null);
            if (validate == null)
            {
                return new OperationResult(false,
                    validatorTypeName + "." + methodName + "(List<string>, List<string>) が見つかりません。");
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            validate.Invoke(null, new object[] { errors, warnings });

            var details = new List<string>();
            for (int i = 0; i < warnings.Count; i++)
            {
                details.Add("[警告] " + warnings[i]);
            }

            for (int i = 0; i < errors.Count; i++)
            {
                details.Add("[エラー] " + errors[i]);
            }

            if (errors.Count > 0)
            {
                return new OperationResult(false,
                    label + "の検査でエラー " + errors.Count + " 件（警告 "
                    + warnings.Count + " 件）。", details);
            }

            return new OperationResult(true,
                label + "の検査を通りました（警告 " + warnings.Count + " 件）。", details);
        }

        /// <summary>
        /// 引数なしの <c>BuildAll()</c> を持つ生成器を反射で呼ぶ（P5／P5.5 で同じ形）。
        /// 戻り値は <c>Success</c>／<c>Message</c>／<c>Outputs</c> を持つ構造体であることを期待する。
        /// </summary>
        private static OperationResult RunBuilder(string builderTypeName, string methodName = "BuildAll")
        {
            Type builder = FindType(builderTypeName);
            if (builder == null)
            {
                return new OperationResult(false, "型が見つかりません: " + builderTypeName);
            }

            MethodInfo build = builder.GetMethod(
                methodName, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (build == null)
            {
                return new OperationResult(false, builderTypeName + "." + methodName + "() が見つかりません。");
            }

            object result = build.Invoke(null, null);
            if (result == null)
            {
                return new OperationResult(false, "BuildAll の戻り値が空でした。");
            }

            Type resultType = result.GetType();
            bool success = ReadProperty(resultType, result, "Success") is bool b && b;
            string message = ReadProperty(resultType, result, "Message") as string ?? string.Empty;

            var details = new List<string>();
            if (ReadProperty(resultType, result, "Outputs") is IEnumerable<string> outputs)
            {
                details.AddRange(outputs);
            }

            return new OperationResult(success, message, details);
        }

        /// <summary>
        /// P5 の探索試遊を検査する（P5-09。仕様書 §13.2／§16.1）。
        ///
        /// <b>Scene を開く操作なので、未保存の変更があれば断る</b>。生成と同じ扱いにする
        /// （`CLAUDE.md`「手で加えた変更を黙って消さない」）。
        ///
        /// 検査は 2 本立て（§13.2）。Asset／Build は Scene を開かずに済むので先に走らせ、
        /// そのあと Area Scene を 1 つずつ開いて Scene Validator にかける。
        /// 片方だけ通っても合格にしない。
        /// </summary>
        private static OperationResult RunValidateExplorationTrial()
        {
            for (int i = 0; i < UnityEditor.SceneManagement.EditorSceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene open =
                    UnityEditor.SceneManagement.EditorSceneManager.GetSceneAt(i);
                if (open.isDirty)
                {
                    return new OperationResult(false,
                        "未保存の変更がある Scene が開いているため検査しません（"
                        + (string.IsNullOrEmpty(open.path) ? "(無題 Scene)" : open.path)
                        + "）。保存するか破棄してから再実行してください。");
                }
            }

            Type assetValidator = FindType(ExplorationAssetValidatorType);
            if (assetValidator == null)
            {
                return new OperationResult(false, "型が見つかりません: " + ExplorationAssetValidatorType);
            }

            MethodInfo assetValidate = assetValidator.GetMethod(
                "Validate", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(List<string>), typeof(List<string>) }, null);
            if (assetValidate == null)
            {
                return new OperationResult(false,
                    ExplorationAssetValidatorType + ".Validate(List<string>, List<string>) が見つかりません。");
            }

            Type sceneValidator = FindType(ExplorationValidatorType);
            if (sceneValidator == null)
            {
                return new OperationResult(false, "型が見つかりません: " + ExplorationValidatorType);
            }

            MethodInfo sceneValidate = sceneValidator.GetMethod(
                "Validate", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(UnityEngine.SceneManagement.Scene), typeof(List<string>), typeof(List<string>) }, null);
            if (sceneValidate == null)
            {
                return new OperationResult(false,
                    ExplorationValidatorType + ".Validate(Scene, List<string>, List<string>) が見つかりません。");
            }

            if (!(sceneValidator.GetField("AreaScenePaths", BindingFlags.Public | BindingFlags.Static)
                    ?.GetValue(null) is string[] scenePaths) || scenePaths.Length == 0)
            {
                return new OperationResult(false, ExplorationValidatorType + ".AreaScenePaths が読めません。");
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            var details = new List<string>();

            var assetErrors = new List<string>();
            var assetWarnings = new List<string>();
            assetValidate.Invoke(null, new object[] { assetErrors, assetWarnings });
            Collect("Asset/Build", assetErrors, assetWarnings, errors, warnings, details);

            foreach (string scenePath in scenePaths)
            {
                UnityEngine.SceneManagement.Scene scene =
                    UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                        scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

                var sceneErrors = new List<string>();
                var sceneWarnings = new List<string>();
                sceneValidate.Invoke(null, new object[] { scene, sceneErrors, sceneWarnings });
                Collect(scenePath, sceneErrors, sceneWarnings, errors, warnings, details);
            }

            // 検査のあとに P5 Scene を開いたままにしない（次の操作が「開いている Scene」を前提にしないため）。
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);

            return new OperationResult(
                errors.Count == 0,
                errors.Count == 0
                    ? "P5 探索試遊の検査を通りました（警告 " + warnings.Count + " 件）。"
                    : "P5 探索試遊の検査でエラー " + errors.Count + " 件（警告 " + warnings.Count + " 件）。",
                details);
        }

        /// <summary>1 つの検査の結果を、出どころ付きでまとめる。</summary>
        private static void Collect(
            string source, List<string> sourceErrors, List<string> sourceWarnings,
            List<string> errors, List<string> warnings, List<string> details)
        {
            foreach (string w in sourceWarnings)
            {
                warnings.Add(w);
                details.Add("[警告] " + source + ": " + w);
            }

            foreach (string e in sourceErrors)
            {
                errors.Add(e);
                details.Add("[エラー] " + source + ": " + e);
            }
        }
    }
}
