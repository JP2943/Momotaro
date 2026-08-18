using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05：剣閃／突き／ガード不能予告 VFX 素材（実アセット）の受入検査。方向別（Down/Left/Right/Up）の剣閃・突きと、
    /// 無方向の予告について、枚数・命名・Import 設定（Sprite / Single / PPU100 / AlphaIsTransparency / Mip Maps 無効 /
    /// Compression None / Center Pivot(0.5,0.5) / Full Rect / Physics Shape 無効 / Clamp / Read-Write 無効）を検証する。
    /// 素材内容は pixel hash で固定せず、枚数・命名・寸法（正方）・Import 設定のみを検査する（差し替えに強くする）。
    /// </summary>
    public sealed class VfxSlashAssetImportTests
    {
        private const string VfxRoot = "Assets/_Project/Art/VFX";
        private static readonly string[] Dirs = { "Down", "Left", "Right", "Up" };

        /// <summary>方向別 VFX セット（フォルダ名・ファイル接頭辞・1 方向あたりのコマ数）。</summary>
        // P3.5-06：素材はネスト構造（Slash/Player/<段>, Slash/Enemy/<鍵>/<分類>, Warning/Enemy/<鍵>/<分類>）へ再編。
        // ファイル接頭辞（prefix）は据え置き（フォルダのみ移動）のため、sub のみ新パスへ更新する。
        private static readonly (string sub, string prefix, int perDir)[] Directional =
        {
            ("Slash/Player/Combo1", "slash_small_a", 3),
            ("Slash/Player/Combo2", "slash_small_b", 3),
            ("Slash/Player/Combo3", "slash_small_c", 4),
            ("Slash/Player/Special", "slash_special_a", 5),
            ("Slash/Enemy/Small/Normal", "slash_enemy_small_a", 3),
            ("Slash/Enemy/Medium/Normal", "slash_enemy_medium_a", 3),
            ("Slash/Enemy/Medium/Heavy", "slash_enemy_heavy_a", 4),
            ("Slash/Enemy/Medium/Unblockable", "thrust_enemy_unguardable_a", 4),
        };

        private const string WarningSub = "Warning/Enemy/Medium/Unblockable";
        private const string WarningPrefix = "warning_enemy_unguardable_a";
        private const int WarningFrames = 4;

        private static IEnumerable<string> DirectionalPngs((string sub, string prefix, int perDir) set)
        {
            foreach (string d in Dirs)
            {
                for (int i = 1; i <= set.perDir; i++)
                {
                    yield return $"{VfxRoot}/{set.sub}/{d}/{set.prefix}_{d.ToLowerInvariant()}_{i:00}.png";
                }
            }
        }

        private static IEnumerable<string> WarningPngs()
        {
            for (int i = 1; i <= WarningFrames; i++)
            {
                yield return $"{VfxRoot}/{WarningSub}/{WarningPrefix}_{i:00}.png";
            }
        }

        private static void AssertVfxImport(string path)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(tex, "テクスチャ読込失敗（命名・パス不一致の可能性）: " + path);
            Assert.Greater(tex.width, 0, "幅が不正: " + path);
            Assert.AreEqual(tex.width, tex.height, "VFX は正方テクスチャ: " + path);

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsNotNull(ti, "TextureImporter 取得失敗: " + path);
            Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, "Texture Type Sprite: " + path);
            Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, "Sprite Mode Single（Sheet 化しない）: " + path);
            Assert.AreEqual(100f, ti.spritePixelsPerUnit, "PPU100: " + path);
            Assert.AreEqual(FilterMode.Bilinear, ti.filterMode, "Filter Mode Bilinear: " + path);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "Compression None: " + path);
            Assert.IsTrue(ti.alphaIsTransparency, "Alpha Is Transparency: " + path);
            Assert.IsFalse(ti.isReadable, "Read/Write 無効: " + path);
            Assert.IsFalse(ti.mipmapEnabled, "Mip Maps 無効: " + path);
            Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, "Wrap Mode Clamp: " + path);

            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            Assert.AreEqual((int)SpriteAlignment.Center, s.spriteAlignment, "Center Pivot（VFX は中心基準）: " + path);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), s.spritePivot, "Pivot (0.5, 0.5): " + path);
            Assert.AreEqual(SpriteMeshType.FullRect, s.spriteMeshType, "Mesh Type Full Rect: " + path);
            Assert.IsFalse(s.spriteGenerateFallbackPhysicsShape, "Generate Physics Shape 無効: " + path);
        }

        [Test]
        public void Directional_Sets_HaveExpectedCounts_PerDirection()
        {
            foreach ((string sub, string prefix, int perDir) set in Directional)
            {
                foreach (string d in Dirs)
                {
                    string dir = $"{VfxRoot}/{set.sub}/{d}";
                    int n = AssetDatabase.FindAssets("t:Texture2D", new[] { dir })
                        .Select(AssetDatabase.GUIDToAssetPath)
                        .Count(p => p.EndsWith(".png"));
                    Assert.AreEqual(set.perDir, n, $"{set.sub}/{d} のコマ数。");
                }
            }
        }

        [Test]
        public void Directional_Sets_ImportSettings_AreCorrect()
        {
            foreach ((string sub, string prefix, int perDir) set in Directional)
            {
                foreach (string path in DirectionalPngs(set))
                {
                    AssertVfxImport(path);
                }
            }
        }

        [Test]
        public void Warning_Has4Frames_Flat_NonDirectional()
        {
            int n = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{VfxRoot}/{WarningSub}" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Count(p => p.EndsWith(".png"));
            Assert.AreEqual(WarningFrames, n, "ガード不能予告は無方向の 4 コマ。");

            // 予告は方向別サブフォルダを持たない（フラット配置）。
            foreach (string d in Dirs)
            {
                Assert.IsFalse(AssetDatabase.IsValidFolder($"{VfxRoot}/{WarningSub}/{d}"),
                    "予告は方向別サブフォルダを持たない: " + d);
            }
        }

        [Test]
        public void Warning_ImportSettings_AreCorrect()
        {
            foreach (string path in WarningPngs())
            {
                AssertVfxImport(path);
            }
        }
    }
}
