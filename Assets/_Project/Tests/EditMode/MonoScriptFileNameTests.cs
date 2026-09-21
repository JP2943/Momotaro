using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 出荷コードの MonoBehaviour／ScriptableObject は、<b>クラス名と同じ名前のファイル</b>に置く。
    ///
    /// Unity はファイル名と一致するクラスにしか MonoScript を割り当てない。別名ファイルの中の MonoBehaviour は
    /// AddComponent はできるが Scene／Prefab へ<b>保存すると Missing Script になる</b>。実際に起きた：
    /// <c>InvestigationRecordHolder</c>（InvestigationRecord.cs）と <c>CompanionRosterContext</c>（ICompanionAvailability.cs）が
    /// Builder 直後の在庫では動くのに、保存した Scene を読み直すと消えていた（PlayMode の実 Scene 検証で発覚。P4-08R）。
    /// メモリ上の Scene を検査する Validator では捕まえられないので、ここで静的に固定する。
    /// </summary>
    public sealed class MonoScriptFileNameTests
    {
        private static readonly Regex UnityObjectClass = new Regex(
            @"\bclass\s+(\w+)\s*(?:<[^>]*>)?\s*:\s*[^{;]*?\b(MonoBehaviour|ScriptableObject)\b",
            RegexOptions.Compiled);

        [Test]
        public void ProductionUnityObjects_LiveInFilesNamedAfterTheClass()
        {
            var offenders = new List<string>();
            string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/_Project/Scripts" });
            Assert.Greater(guids.Length, 0, "出荷スクリプトが見つかる。");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs"))
                {
                    continue;
                }

                string fileName = Path.GetFileNameWithoutExtension(path);
                string text = File.ReadAllText(path);
                foreach (Match m in UnityObjectClass.Matches(text))
                {
                    string cls = m.Groups[1].Value;
                    if (cls != fileName)
                    {
                        offenders.Add(path + " : " + cls + "（" + m.Groups[2].Value + "）");
                    }
                }
            }

            Assert.IsEmpty(offenders,
                "ファイル名と一致しない Unity オブジェクト派生クラスがあります（Scene／Prefab へ保存すると Missing Script になる）:\n- "
                + string.Join("\n- ", offenders));
        }
    }
}
