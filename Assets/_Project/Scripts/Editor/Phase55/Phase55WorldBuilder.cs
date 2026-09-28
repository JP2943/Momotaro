using System.Collections.Generic;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase55
{
    /// <summary>
    /// P5.5 の実ワールド配置を生成する（仕様書 §3.1／§3.2。工程 P55-03c）。
    ///
    /// <b>地形の組み立ては P5 の Builder を使う。</b> 違うのは
    /// <list type="bullet">
    /// <item><description>出力先（<c>Phase55</c> フォルダ。P5 の受入用 Scene を作り替えない。§3.2）</description></item>
    /// <item><description>Authoring 原点（A の東に B を固定座標で置く。§3.2）</description></item>
    /// <item><description>外周壁の接続口（境界の区間を通路として開ける。§7.3）</description></item>
    /// <item><description>接続通路の Z（両 Area の Camera 領域中心＝0。§7.1）</description></item>
    /// <item><description>出入口 ID と接続 Data（§3.1）</description></item>
    /// </list>
    /// だけで、床・壁・仕掛け・システム・活動ゲートは同じ手順で組む。
    /// Builder を写すと必ず片方だけ直された状態になる。
    /// </summary>
    public static class Phase55WorldBuilder
    {
        /// <summary>生成結果。</summary>
        public readonly struct BuildResult
        {
            /// <summary>作る。</summary>
            public BuildResult(bool success, string message, IReadOnlyList<string> outputs)
            {
                Success = success;
                Message = message;
                Outputs = outputs ?? new List<string>();
            }

            /// <summary>成功したか。</summary>
            public bool Success { get; }

            /// <summary>人が読む要約。</summary>
            public string Message { get; }

            /// <summary>生成物のパス。</summary>
            public IReadOnlyList<string> Outputs { get; }
        }

        [MenuItem("Momotaro/Phase 5.5/Generate Connected World")]
        private static void GenerateInteractive()
        {
            BuildResult result = BuildAll();
            if (result.Success)
            {
                EditorUtility.DisplayDialog("P5.5 実ワールド配置", result.Message, "OK");
                return;
            }

            EditorUtility.DisplayDialog("P5.5 実ワールド配置（失敗）", result.Message, "OK");
        }

        /// <summary>
        /// <b>全配置</b>の Scene・Data・接続一覧を生成する（東西・南北）。
        ///
        /// 配置を足したときに生成だけ足して検査を足し忘れないよう、
        /// 生成も検査も <c>Phase55Arrangements.All()</c> をなめる形にしてある。
        /// </summary>
        public static BuildResult BuildAll()
        {
            var outputs = new List<string>();
            var lines = new List<string>();

            foreach (Phase55Arrangement arrangement in Phase55Arrangements.All())
            {
                BuildResult one = BuildOne(arrangement);
                outputs.AddRange(one.Outputs);
                lines.Add("【" + arrangement.Label + "】" + one.Message);
                if (!one.Success)
                {
                    return new BuildResult(false, string.Join("\n", lines), outputs);
                }
            }

            return new BuildResult(true, string.Join("\n", lines), outputs);
        }

        /// <summary>1 つの配置を生成する。</summary>
        public static BuildResult BuildOne(Phase55Arrangement arrangement)
        {
            // <b>接続を Scene より先に作る。</b> Area Scene の初期化担当がこの Asset を
            // 参照するので、あとから作ると参照が空のまま保存される。
            AreaConnectionData connections = EnsureConnections(arrangement);
            AssetDatabase.SaveAssets();

            Phase5BuildTargets targets = arrangement.Targets();

            Phase5ExplorationBuilder.BuildResult scenes = Phase5ExplorationBuilder.Build(targets);
            var outputs = new List<string>(scenes.Outputs);
            outputs.Add(arrangement.ConnectionDataPath);
            if (!scenes.Success)
            {
                return new BuildResult(false, scenes.Message, outputs);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new BuildResult(true,
                scenes.Message
                + "\n接続 " + connections.Connections.Count + " 件（往復）。"
                + "境界 " + arrangement.AlongName + "=" + arrangement.Along(targets.AreaBOrigin - targets.AreaAOrigin)
                + " の手前／通路 " + arrangement.AcrossName + "=" + arrangement.PassageCenter
                + " 幅 " + arrangement.PassageWidth
                + "／B 原点 " + targets.AreaBOrigin + "。",
                outputs);
        }

        /// <summary>
        /// 往復の接続を 2 レコードで作る（§3.1）。
        ///
        /// <b>往復は 1 レコードを使い回さない。</b> 方向・演出時間・到着入口を片側だけ
        /// 変えたいときに必ず破綻する。逆方向は <c>ReverseConnectionId</c> で結び、
        /// 向きが反転していることを Data 検証が見る（§10.1）。
        /// </summary>
        private static AreaConnectionData EnsureConnections(Phase55Arrangement arrangement)
        {
            Phase5Placeholder.EnsureFolder(
                System.IO.Path.GetDirectoryName(arrangement.ConnectionDataPath).Replace('\\', '/'));

            var asset = AssetDatabase.LoadAssetAtPath<AreaConnectionData>(
                arrangement.ConnectionDataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AreaConnectionData>();
                AssetDatabase.CreateAsset(asset, arrangement.ConnectionDataPath);
            }

            Phase5ExplorationBuilder.SetIdentity(
                asset, arrangement.ConnectionsId, arrangement.ConnectionsDisplayName);

            var forward = new AreaConnectionDefinition();
            forward.Configure(
                arrangement.ConnectionAToB,
                arrangement.AreaAId, arrangement.ExitFromA,
                arrangement.AreaBId, arrangement.EntryInB,
                AreaTransitionStyle.Slide, arrangement.ForwardDirection,
                arrangement.ConnectionBToA);

            var backward = new AreaConnectionDefinition();
            backward.Configure(
                arrangement.ConnectionBToA,
                arrangement.AreaBId, arrangement.ExitFromB,
                arrangement.AreaAId, arrangement.EntryInA,
                AreaTransitionStyle.Slide, arrangement.BackwardDirection,
                arrangement.ConnectionAToB);

            asset.SetConnections(new List<AreaConnectionDefinition> { forward, backward });
            EditorUtility.SetDirty(asset);
            return asset;
        }
    }
}
