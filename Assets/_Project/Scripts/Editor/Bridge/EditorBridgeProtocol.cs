using System;
using System.IO;
using UnityEngine;

namespace Momotaro.EditorBridge
{
    /// <summary>
    /// Editor ブリッジの受け渡し形式（開発補助。出荷物には含まれない Editor 専用アセンブリ）。
    ///
    /// 目的は「Unity を開いている人にテスト結果とコンパイルエラーを目視で転記してもらう」往復を無くすこと。
    /// プロジェクト直下の <c>_bridge/</c> フォルダをポストにして、外部（コード生成側）が <c>command.json</c> を置き、
    /// 開いたままの Editor がそれを実行して <c>result.json</c> に結果を書き戻す。
    ///
    /// <b>Assets の外に置く</b>のは、Unity に資産として Import させないため（Import されると毎回の書き込みが
    /// AssetDatabase の更新を誘発し、実行中のテストを壊す）。
    ///
    /// 実行できるのは列挙で限定した操作だけで、任意コードの実行・ファイル削除・シェル起動はできない。
    /// </summary>
    [Serializable]
    public sealed class BridgeCommand
    {
        /// <summary>コマンドの同一性。同じ id は二度実行しない（Editor の再読み込みをまたいでも再実行しない）。</summary>
        public string id;

        /// <summary>操作名（<see cref="EditorBridgeCommands"/> のいずれか）。</summary>
        public string command;

        /// <summary>run-tests：<c>EditMode</c> または <c>PlayMode</c>。既定は EditMode。</summary>
        public string mode;

        /// <summary>run-tests：テスト名の絞り込み（正規表現。空で全件）。</summary>
        public string filter;

        /// <summary>compile-status：変更が無くても再コンパイルを強制するか。</summary>
        public bool force;

        /// <summary>run-op：実行する操作名（許可された操作のみ）。</summary>
        public string op;

        /// <summary>
        /// run-tests：成功件数の下限（0 は指定なし）。ここを下回ったら <c>failed</c> で返す。
        ///
        /// 「1380 件通っていたはずの実行が、フィルタの綴り違いで 3 件だけ通って ok になる」事故を外から止めるための栓。
        /// 件数が分かっている実行では、直前の実績を少し下回る値を入れておく。
        /// </summary>
        public int minPassed;

        /// <summary>
        /// run-op（<c>verify-required-tests</c>）：照合する工程名（<c>P4-FIX-02A</c> 等）。
        /// 空なら必須一覧に載っている全工程を対象にする。
        /// </summary>
        public string stage;

        /// <summary>
        /// run-op（<c>verify-required-tests</c>）：照合する実行記録の id（<c>runs/&lt;id&gt;.json</c>）。
        /// カンマ区切りで複数指定できる（EditMode と PlayMode を 1 回の判定にまとめるため）。
        /// </summary>
        public string runIds;

        /// <summary>
        /// run-op（<c>verify-required-tests</c>）：照合に使う必須テスト一覧（P5-09。仕様書 §16.1）。
        ///
        /// <b>許可リストの鍵だけ</b>を受け付ける（"P4" / "P5"）。任意のパスは読まない。
        /// 空なら既定の P4 で、既存の照合はそのまま動く。
        /// </summary>
        public string manifest;
    }

    /// <summary>コマンドの実行結果。</summary>
    [Serializable]
    public sealed class BridgeResult
    {
        /// <summary>対応するコマンドの id。</summary>
        public string id;

        /// <summary>操作名。</summary>
        public string command;

        /// <summary>running（実行中）／ok（成功）／failed（実行できたが結果が不合格）／error（実行できなかった）。</summary>
        public string status;

        /// <summary>1 行の要約。</summary>
        public string message;

        /// <summary>開始時刻（UTC, ISO 8601）。</summary>
        public string startedAt;

        /// <summary>終了時刻（UTC, ISO 8601。実行中は空）。</summary>
        public string finishedAt;

        /// <summary>run-tests：成功・失敗・スキップ件数。</summary>
        public int passed;

        /// <summary>失敗件数。</summary>
        public int failed;

        /// <summary>スキップ件数。</summary>
        public int skipped;

        /// <summary>run-tests：結果が確定しなかった件数（Inconclusive）。従来は集計から落ちていた。</summary>
        public int inconclusive;

        /// <summary>
        /// run-tests：開始時に予定されていた件数（0 は不明）。実行件数がこれを下回るなら完走していない。
        /// 「途中で止まったのに失敗 0 件だから緑」を外から見分けるために残す。
        /// </summary>
        public int expected;

        /// <summary>run-tests：全体の終わり方（<c>completed</c> / <c>cancelled</c> / <c>missing</c> / <c>unknown</c>）。</summary>
        public string termination;

        /// <summary>
        /// run-tests：この実行の葉テスト全件を書き出したファイル（プロジェクト直下からの相対パス）。
        /// <c>result.json</c> は次の実行で上書きされるが、こちらは実行ごとに別ファイルとして残る。
        /// </summary>
        public string runLogPath;

        /// <summary>失敗したテストの詳細・コンパイルエラーの本文など。</summary>
        public string[] details = Array.Empty<string>();
    }

    /// <summary>
    /// 1 件のテスト結果（葉）。件数だけでは「どのテストが Skip したのか」が分からず、
    /// 「以前と同数だから非必須」といった当てにならない判断しかできなくなるため、名前ごと残す。
    /// </summary>
    [Serializable]
    public sealed class BridgeLeafResult
    {
        /// <summary>実行結果に現れる完全名（必須テスト一覧との照合キー）。</summary>
        public string fullName;

        /// <summary>Passed / Failed / Skipped / Inconclusive。</summary>
        public string status;

        /// <summary>NUnit の詳細な結果状態（<c>Skipped:Ignored</c>、<c>Failed:Error</c> 等）。</summary>
        public string resultState;

        /// <summary>失敗・スキップの理由（成功時は空）。</summary>
        public string message;
    }

    /// <summary>
    /// 1 回のテスト実行の完全な記録。<c>_bridge/runs/&lt;id&gt;.json</c> へ書き、次の実行で上書きしない。
    /// 受入記録がここを指せば、「どのテストが何件どうだったか」を後から追える。
    /// </summary>
    [Serializable]
    public sealed class BridgeRunLog
    {
        /// <summary>コマンド id。</summary>
        public string id;

        /// <summary>EditMode / PlayMode。</summary>
        public string mode;

        /// <summary>適用したフィルタ（空なら全件）。</summary>
        public string filter;

        /// <summary>開始・終了時刻（UTC, ISO 8601）。</summary>
        public string startedAt;

        /// <summary>終了時刻（UTC, ISO 8601）。</summary>
        public string finishedAt;

        /// <summary>Unity のバージョン。</summary>
        public string unityVersion;

        /// <summary>判定結果（ok / failed / error）。</summary>
        public string status;

        /// <summary>全体の終わり方。</summary>
        public string termination;

        /// <summary>全体結果の ResultState（中断・異常の判別に使った値そのもの）。</summary>
        public string rootResultState;

        /// <summary>件数。</summary>
        public int expected;

        /// <summary>成功件数。</summary>
        public int passed;

        /// <summary>失敗件数。</summary>
        public int failed;

        /// <summary>スキップ件数。</summary>
        public int skipped;

        /// <summary>結果が確定しなかった件数。</summary>
        public int inconclusive;

        /// <summary>葉テストの全結果。</summary>
        public BridgeLeafResult[] leaves = Array.Empty<BridgeLeafResult>();
    }

    /// <summary>ブリッジの生存確認。Editor が開いているか・コンパイル中か・再生中かを外から見るための窓。</summary>
    [Serializable]
    public sealed class BridgeStatus
    {
        /// <summary>この状態を書いた時刻（UTC, ISO 8601）。古ければ Editor が閉じている。</summary>
        public string aliveAt;

        /// <summary>ブリッジが有効か（メニュー <c>Momotaro/Bridge/Enabled</c>）。</summary>
        public bool enabled;

        /// <summary>Unity のバージョン。</summary>
        public string unityVersion;

        /// <summary>コンパイル中か（true の間はコマンドを受け付けない）。</summary>
        public bool isCompiling;

        /// <summary>再生中か。</summary>
        public bool isPlaying;

        /// <summary>最後に受け取ったコマンドの id。</summary>
        public string lastCommandId;

        /// <summary>実行中のコマンドの id（空なら待機中）。</summary>
        public string busyCommandId;

        /// <summary>状態を書き出す間隔（秒）。</summary>
        public float pollSeconds;
    }

    /// <summary>受け渡しに使うファイルの場所。プロジェクト直下（<c>Assets</c> の外）に置く。</summary>
    public static class EditorBridgePaths
    {
        /// <summary>受け渡しフォルダ名。</summary>
        public const string FolderName = "_bridge";

        /// <summary>プロジェクト直下の受け渡しフォルダ。</summary>
        public static string Root
        {
            get
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                return Path.Combine(projectRoot, FolderName);
            }
        }

        /// <summary>外部が置くコマンド。</summary>
        public static string Command => Path.Combine(Root, "command.json");

        /// <summary>Editor が書く結果。</summary>
        public static string Result => Path.Combine(Root, "result.json");

        /// <summary>Editor が書く生存状態。</summary>
        public static string Status => Path.Combine(Root, "status.json");

        /// <summary>実行ごとの完全な記録を置くフォルダ（<c>result.json</c> と違い上書きしない）。</summary>
        public static string RunsRoot => Path.Combine(Root, "runs");

        /// <summary>この実行の記録ファイル。</summary>
        public static string RunLog(string id)
        {
            return Path.Combine(RunsRoot, Sanitize(id) + ".json");
        }

        /// <summary>ファイル名に使えない文字を落とす（id は外部が決めるため、そのままパスにしない）。</summary>
        public static string Sanitize(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return "unnamed";
            }

            char[] chars = id.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool safe = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || c == '-' || c == '_' || c == '.';
                if (!safe)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        /// <summary>使い方の説明（有効化時に生成する）。</summary>
        public static string Readme => Path.Combine(Root, "README.md");

        /// <summary>フォルダが無ければ作る。</summary>
        public static void EnsureFolder()
        {
            if (!Directory.Exists(Root))
            {
                Directory.CreateDirectory(Root);
            }
        }

        /// <summary>実行記録フォルダが無ければ作る。</summary>
        public static void EnsureRunsFolder()
        {
            EnsureFolder();
            if (!Directory.Exists(RunsRoot))
            {
                Directory.CreateDirectory(RunsRoot);
            }
        }
    }

    /// <summary>実行できる操作（これ以外は受け付けない）。</summary>
    public static class EditorBridgeCommands
    {
        /// <summary>生存確認。Unity のバージョンを返すだけ。</summary>
        public const string Ping = "ping";

        /// <summary>AssetDatabase を更新する（Import のみ。結果を待たない）。</summary>
        public const string Refresh = "refresh";

        /// <summary>スクリプトを再コンパイルし、コンパイルエラー・警告を返す。</summary>
        public const string CompileStatus = "compile-status";

        /// <summary>テストを実行し、件数と失敗内容を返す。</summary>
        public const string RunTests = "run-tests";

        /// <summary>
        /// 許可された編集操作（Prefab の再生成・Data 検証）を実行する。ダイアログを出さない操作だけを載せているため、
        /// 無人でも Editor が止まらない。操作名は <c>EditorBridgeOperations</c> の一覧を参照。
        /// </summary>
        public const string RunOp = "run-op";

        /// <summary>既知の操作か。</summary>
        public static bool IsKnown(string command)
        {
            return command == Ping || command == Refresh || command == CompileStatus
                || command == RunTests || command == RunOp;
        }
    }
}
