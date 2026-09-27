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

        /// <summary>Scene・Data・接続一覧を生成する。</summary>
        public static BuildResult BuildAll()
        {
            Phase5BuildTargets targets = Phase55WorldLayout.Targets();

            Phase5ExplorationBuilder.BuildResult scenes = Phase5ExplorationBuilder.Build(targets);
            var outputs = new List<string>(scenes.Outputs);
            if (!scenes.Success)
            {
                return new BuildResult(false, scenes.Message, outputs);
            }

            AreaConnectionData connections = EnsureConnections();
            outputs.Add(Phase55WorldIds.ConnectionDataPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new BuildResult(true,
                scenes.Message
                + "\n接続 " + connections.Connections.Count + " 件（東西の往復）。"
                + "境界 x=" + Phase55WorldLayout.SeamX
                + "／通路 z=" + Phase55WorldLayout.SeamZ
                + " 幅 " + Phase55WorldLayout.PassageWidth
                + "／B 原点 x=" + Phase55WorldLayout.AreaBOrigin.x + "。",
                outputs);
        }

        /// <summary>
        /// 東西の接続を 2 レコードで作る（§3.1）。
        ///
        /// <b>往復は 1 レコードを使い回さない。</b> 方向・演出時間・到着入口を片側だけ
        /// 変えたいときに必ず破綻する。逆方向は <c>ReverseConnectionId</c> で結び、
        /// 向きが反転していることを Data 検証が見る（§10.1）。
        /// </summary>
        private static AreaConnectionData EnsureConnections()
        {
            Phase5Placeholder.EnsureFolder(Phase55WorldIds.DataFolder);

            var asset = AssetDatabase.LoadAssetAtPath<AreaConnectionData>(
                Phase55WorldIds.ConnectionDataPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AreaConnectionData>();
                AssetDatabase.CreateAsset(asset, Phase55WorldIds.ConnectionDataPath);
            }

            Phase5ExplorationBuilder.SetIdentity(
                asset, Phase55WorldIds.Connections, "P5.5 エリア接続（東西）");

            var east = new AreaConnectionDefinition();
            east.Configure(
                Phase55WorldIds.ConnectionAToB,
                Phase55WorldIds.AreaA, Phase55WorldIds.ExitAEast,
                Phase55WorldIds.AreaB, Phase5AreaIds.AreaBFromA,
                AreaTransitionStyle.Slide, AreaConnectionDirection.East,
                Phase55WorldIds.ConnectionBToA);

            var west = new AreaConnectionDefinition();
            west.Configure(
                Phase55WorldIds.ConnectionBToA,
                Phase55WorldIds.AreaB, Phase55WorldIds.ExitBWest,
                Phase55WorldIds.AreaA, Phase5AreaIds.AreaAFromB,
                AreaTransitionStyle.Slide, AreaConnectionDirection.West,
                Phase55WorldIds.ConnectionAToB);

            asset.SetConnections(new List<AreaConnectionDefinition> { east, west });
            EditorUtility.SetDirty(asset);
            return asset;
        }
    }
}
