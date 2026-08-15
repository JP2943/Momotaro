using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 強敵スプライト受入：侍骸骨 155 枚（Stun 16 枚を含む）の Import 設定・寸法・命名・Pivot／接地を検証する（P3-09 前の資産受入）。基本モーションは
    /// 192×192、Heavy Overhead／Unguardable Thrust の修正版は 320×320。共通 PPU（＝桃太郎・剣士と同一）を維持し、320 素材は
    /// 単純な Bottom Center 放置でなく、192 の足元（下端 8px）と一致する共通接地アンカーを Custom Pivot として持つことを検証する。
    /// 192 素材足元＝8px、320 素材足元＝16px（全フレーム一定）より、320 Pivot.y=(16-8)/320=0.025 で接地一致（縦の跳ね無し）。
    /// </summary>
    public sealed class SamuraiSkeletonImportTests
    {
        private const string SpritesDir =
            "Assets/_Project/Art/Characters/Enemies/SamuraiSkeleton/Prototype/Sprites";

        private const string PpuReferenceSprite =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/Attack/Attack1/momotaro_attack1_down_01.png";

        private static readonly string[] Quad = { "down", "left", "right", "up" };

        private struct Motion { public string Folder; public string Key; public bool Directional; public int Frames; public int Size; }

        private static readonly Motion[] Motions =
        {
            new Motion { Folder = "Idle", Key = "idle", Directional = true, Frames = 4, Size = 192 },
            new Motion { Folder = "Move", Key = "move", Directional = true, Frames = 6, Size = 192 },
            new Motion { Folder = "Hurt", Key = "hurt", Directional = true, Frames = 3, Size = 192 },
            new Motion { Folder = "Stun", Key = "stun", Directional = true, Frames = 4, Size = 192 },
            new Motion { Folder = "NormalAttack", Key = "normal_attack", Directional = true, Frames = 5, Size = 192 },
            new Motion { Folder = "HeavyOverhead", Key = "heavy_overhead", Directional = true, Frames = 7, Size = 320 },
            new Motion { Folder = "UnguardableThrust", Key = "unguardable_thrust", Directional = true, Frames = 8, Size = 320 },
            new Motion { Folder = "Down", Key = "down", Directional = false, Frames = 7, Size = 192 },
        };

        private static IEnumerable<string> PathsOf(Motion m)
        {
            if (m.Directional)
            {
                foreach (string d in Quad)
                {
                    for (int i = 1; i <= m.Frames; i++)
                    {
                        yield return $"{SpritesDir}/{m.Folder}/samurai_skeleton_{m.Key}_{d}_{i:00}.png";
                    }
                }
            }
            else
            {
                for (int i = 1; i <= m.Frames; i++)
                {
                    yield return $"{SpritesDir}/{m.Folder}/samurai_skeleton_{m.Key}_{i:00}.png";
                }
            }
        }

        private static IEnumerable<string> AllPaths() => Motions.SelectMany(PathsOf);

        private static float ExpectedPpu()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(PpuReferenceSprite);
            Assert.IsNotNull(ti, "PPU 参照（桃太郎）が見つからない。");
            return ti.spritePixelsPerUnit;
        }

        [Test]
        public void Sprites_Total155_PerMotionCounts()
        {
            var counts = new Dictionary<string, int>();
            foreach (Motion m in Motions)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{SpritesDir}/{m.Folder}" });
                counts[m.Folder] = guids.Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
            }

            Assert.AreEqual(16, counts["Idle"]);
            Assert.AreEqual(24, counts["Move"]);
            Assert.AreEqual(12, counts["Hurt"]);
            Assert.AreEqual(16, counts["Stun"]);
            Assert.AreEqual(20, counts["NormalAttack"]);
            Assert.AreEqual(28, counts["HeavyOverhead"]);
            Assert.AreEqual(32, counts["UnguardableThrust"]);
            Assert.AreEqual(7, counts["Down"]);
            Assert.AreEqual(155, counts.Values.Sum(), "合計 155 枚（Stun 16 枚を含む）。");
        }

        [Test]
        public void AllSprites_Dimensions_AndSpecImportSettings()
        {
            float expectedPpu = ExpectedPpu();

            foreach (Motion m in Motions)
            {
                foreach (string path in PathsOf(m))
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    Assert.IsNotNull(tex, "PNG が見つからない（命名不一致の可能性）: " + path);
                    Assert.AreEqual(m.Size, tex.width, "幅: " + path);
                    Assert.AreEqual(m.Size, tex.height, "高さ: " + path);

                    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                    Assert.IsNotNull(ti, "TextureImporter: " + path);
                    Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, "Sprite: " + path);
                    Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, "Single: " + path);
                    Assert.AreEqual(expectedPpu, ti.spritePixelsPerUnit, "PPU 共通: " + path);
                    Assert.AreEqual(FilterMode.Bilinear, ti.filterMode, "Bilinear: " + path);
                    Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "None: " + path);
                    Assert.IsTrue(ti.alphaIsTransparency, "Alpha Is Transparency: " + path);
                    Assert.IsFalse(ti.mipmapEnabled, "Mip 無効: " + path);
                    Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, "Clamp: " + path);
                    Assert.IsTrue(ti.sRGBTexture, "sRGB: " + path);
                    Assert.AreEqual(TextureImporterNPOTScale.None, ti.npotScale, "NPOT None: " + path);

                    var s = new TextureImporterSettings();
                    ti.ReadTextureSettings(s);
                    Assert.AreEqual(SpriteMeshType.FullRect, s.spriteMeshType, "Full Rect: " + path);
                    Assert.IsFalse(s.spriteGenerateFallbackPhysicsShape, "物理シェイプ無効: " + path);
                }
            }
        }

        [Test]
        public void Pivot_192BottomCenter_320CustomGroundAnchor()
        {
            foreach (Motion m in Motions)
            {
                foreach (string path in PathsOf(m))
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                    var s = new TextureImporterSettings();
                    ti.ReadTextureSettings(s);

                    if (m.Size == 192)
                    {
                        Assert.AreEqual((int)SpriteAlignment.BottomCenter, s.spriteAlignment, "192 は Bottom Center: " + path);
                    }
                    else
                    {
                        // 320 は Bottom Center 放置でなく、192 足元(8px)と一致する共通接地アンカー(y=0.025)。
                        Assert.AreEqual((int)SpriteAlignment.Custom, s.spriteAlignment, "320 は Custom Pivot（Bottom Center 放置でない）: " + path);
                        Assert.AreEqual(0.5f, s.spritePivot.x, 1e-4f, "320 Pivot X（中央接地）: " + path);
                        Assert.AreEqual(0.025f, s.spritePivot.y, 1e-4f, "320 Pivot Y（足元 16px を 8px 上へ＝192 と接地一致）: " + path);
                    }
                }
            }
        }

        [Test]
        public void AllSprites_LoadWithoutMissing()
        {
            foreach (string path in AllPaths())
            {
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(path), "Single Sprite が生成されていない（Missing）: " + path);
            }
        }

        [Test]
        public void ExistingSkeletonSwordsman_NotOverwritten()
        {
            // 侍骸骨受入で既存の剣士 77 枚 Sprites を壊していないこと。
            string sw = "Assets/_Project/Art/Characters/Enemies/SkeletonSwordsman/Prototype/Sprites";
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { sw });
            int n = guids.Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
            Assert.AreEqual(77, n, "剣士 Sprites は 77 枚のまま。");
        }
    }
}
