using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 敵スプライト受入：骸骨剣士 77 枚の Import 設定・寸法・命名を検証する（Sprite/Single/Full Rect/Bottom Center/Bilinear/
    /// Compression None/Alpha/No Mip/Clamp/PPU＝桃太郎基準/物理シェイプ無効）。素材内容は pixel 固定せず設定のみ検査する。
    /// </summary>
    public sealed class SkeletonSwordsmanImportTests
    {
        private const string SpritesDir =
            "Assets/_Project/Art/Characters/Enemies/SkeletonSwordsman/Prototype/Sprites";

        private const string PpuReferenceSprite =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/Attack/Attack1/momotaro_attack1_down_01.png";

        private struct Motion { public string Folder; public string Key; public string[] Dirs; public int Frames; }

        private static readonly string[] Quad = { "down", "left", "right", "up" };

        private static readonly Motion[] Motions =
        {
            new Motion { Folder = "Idle", Key = "idle", Dirs = Quad, Frames = 4 },
            new Motion { Folder = "Walk", Key = "walk", Dirs = Quad, Frames = 4 },
            new Motion { Folder = "Attack", Key = "attack", Dirs = Quad, Frames = 4 },
            new Motion { Folder = "Hurt", Key = "hurt", Dirs = Quad, Frames = 3 },
            new Motion { Folder = "Stun", Key = "stun", Dirs = Quad, Frames = 3 },
            new Motion { Folder = "Down", Key = "down", Dirs = new[] { "down" }, Frames = 5 },
        };

        private static IEnumerable<string> AllPaths()
        {
            foreach (Motion m in Motions)
            {
                foreach (string d in m.Dirs)
                {
                    for (int i = 1; i <= m.Frames; i++)
                    {
                        yield return $"{SpritesDir}/{m.Folder}/skeleton_swordsman_{m.Key}_{d}_{i:00}.png";
                    }
                }
            }
        }

        private static float ExpectedPpu()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(PpuReferenceSprite);
            Assert.IsNotNull(ti, "PPU 参照（桃太郎）が見つからない。");
            return ti.spritePixelsPerUnit;
        }

        [Test]
        public void Pngs_Total77_PerMotionCounts()
        {
            var counts = new Dictionary<string, int>();
            foreach (Motion m in Motions)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{SpritesDir}/{m.Folder}" });
                counts[m.Folder] = guids.Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
            }

            Assert.AreEqual(16, counts["Idle"]);
            Assert.AreEqual(16, counts["Walk"]);
            Assert.AreEqual(16, counts["Attack"]);
            Assert.AreEqual(12, counts["Hurt"]);
            Assert.AreEqual(12, counts["Stun"]);
            Assert.AreEqual(5, counts["Down"]);
            Assert.AreEqual(77, counts.Values.Sum(), "合計 77 枚。");
        }

        [Test]
        public void Pngs_192Square_WithSpecImporterSettings()
        {
            float expectedPpu = ExpectedPpu();

            foreach (string path in AllPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(tex, "PNG が見つからない（命名不一致の可能性）: " + path);
                Assert.AreEqual(192, tex.width, "幅 192: " + path);
                Assert.AreEqual(192, tex.height, "高さ 192: " + path);

                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(ti, "TextureImporter 取得失敗: " + path);
                Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, "Sprite: " + path);
                Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, "Single: " + path);
                Assert.AreEqual(expectedPpu, ti.spritePixelsPerUnit, "PPU 一致: " + path);
                Assert.AreEqual(FilterMode.Bilinear, ti.filterMode, "Bilinear: " + path);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "Compression None: " + path);
                Assert.IsTrue(ti.alphaIsTransparency, "Alpha Is Transparency: " + path);
                Assert.IsFalse(ti.mipmapEnabled, "Mip Maps 無効: " + path);
                Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, "Wrap Clamp: " + path);

                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                Assert.AreEqual((int)SpriteAlignment.BottomCenter, settings.spriteAlignment, "Bottom Center: " + path);
                Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, "Full Rect: " + path);
                Assert.IsFalse(settings.spriteGenerateFallbackPhysicsShape, "物理シェイプ生成 無効: " + path);
            }
        }

        [Test]
        public void AllSprites_LoadWithoutMissing()
        {
            foreach (string path in AllPaths())
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                Assert.IsNotNull(sprite, "Single Sprite が生成されていない（Missing）: " + path);
            }
        }
    }
}
