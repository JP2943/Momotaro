using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// Phase2 素材受入：Step / Hurt / Guard Break の個別 PNG（計 44 枚）と 12 Animation Clip の Import 設定・
    /// 整合性を検証する。素材内容そのものは pixel hash で固定せず、枚数・命名・設定・Clip 構成のみを検査する。
    /// </summary>
    public sealed class PlayerActionSpriteImportTests
    {
        private const string SpritesDir =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites";

        // 既存キャラクター素材の PPU 参照（通常攻撃 1 段目）。
        private const string PpuReferenceSprite =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/Attack/Attack1/momotaro_attack1_down_01.png";

        private static readonly string[] Directions = { "down", "left", "right", "up" };
        private static readonly string[] Caps = { "Down", "Left", "Right", "Up" };

        private struct State
        {
            public string Folder;
            public string Prefix;
            public string ClipState;
            public int Frames;
            public int Fps;
        }

        private static readonly State[] States =
        {
            new State { Folder = "Step", Prefix = "momotaro_step", ClipState = "Step", Frames = 4, Fps = 16 },
            new State { Folder = "Hurt", Prefix = "momotaro_hurt", ClipState = "Hurt", Frames = 3, Fps = 12 },
            new State { Folder = "GuardBreak", Prefix = "momotaro_guard_break", ClipState = "GuardBreak", Frames = 4, Fps = 12 },
        };

        private static float ExpectedPpu()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(PpuReferenceSprite);
            Assert.IsNotNull(ti, "PPU 参照スプライトが見つからない: " + PpuReferenceSprite);
            return ti.spritePixelsPerUnit;
        }

        private static IEnumerable<string> AllIndividualPngPaths()
        {
            foreach (State s in States)
            {
                foreach (string d in Directions)
                {
                    for (int i = 1; i <= s.Frames; i++)
                    {
                        yield return $"{SpritesDir}/{s.Folder}/{s.Prefix}_{d}_{i:00}.png";
                    }
                }
            }
        }

        [Test]
        public void Pngs_Count44_WithNamingAndPerStateCounts()
        {
            var perState = new Dictionary<string, int>();
            foreach (State s in States)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{SpritesDir}/{s.Folder}" });
                int count = guids.Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
                perState[s.ClipState] = count;
            }

            Assert.AreEqual(16, perState["Step"], "Step = 4 フレーム × 4 方向 = 16。");
            Assert.AreEqual(12, perState["Hurt"], "Hurt = 3 × 4 = 12。");
            Assert.AreEqual(16, perState["GuardBreak"], "GuardBreak = 4 × 4 = 16。");
            Assert.AreEqual(44, perState.Values.Sum(), "合計 44 枚。");

            var namePattern = new Regex(@"^momotaro_(step|hurt|guard_break)_(down|left|right|up)_\d{2}$");
            foreach (string path in AllIndividualPngPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(tex, "PNG が見つからない（命名不一致の可能性）: " + path);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                Assert.IsTrue(namePattern.IsMatch(file), "命名規則不一致: " + file);
            }
        }

        [Test]
        public void Pngs_Are128Square_WithSpecImporterSettings()
        {
            float expectedPpu = ExpectedPpu();

            foreach (string path in AllIndividualPngPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(tex, "テクスチャ読込失敗: " + path);
                Assert.AreEqual(128, tex.width, "幅 128: " + path);
                Assert.AreEqual(128, tex.height, "高さ 128: " + path);

                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.IsNotNull(ti, "TextureImporter 取得失敗: " + path);
                Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, "Texture Type: " + path);
                Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, "Sprite Mode Single: " + path);
                Assert.AreEqual(expectedPpu, ti.spritePixelsPerUnit, "PPU が既存素材と一致: " + path);
                Assert.AreEqual(FilterMode.Bilinear, ti.filterMode, "Filter Mode Bilinear: " + path);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "Compression None: " + path);
                Assert.IsTrue(ti.alphaIsTransparency, "Alpha Is Transparency: " + path);

                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                Assert.AreEqual((int)SpriteAlignment.BottomCenter, settings.spriteAlignment, "Pivot Bottom Center: " + path);
                Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, "Mesh Type Full Rect: " + path);
                Assert.IsFalse(settings.spriteGenerateFallbackPhysicsShape, "Generate Physics Shape 無効: " + path);
            }
        }

        [Test]
        public void Clips_Twelve_WithFramesSampleRateLoopOff_AndSpriteOnly_NoMissing()
        {
            int found = 0;
            foreach (State s in States)
            {
                foreach (string cap in Caps)
                {
                    string path = $"{SpritesDir}/AN_Player_{s.ClipState}_{cap}.anim";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    Assert.IsNotNull(clip, "Clip が見つからない: " + path);
                    found++;

                    Assert.AreEqual(s.Fps, clip.frameRate, "Sample Rate: " + path);

                    AnimationClipSettings cs = AnimationUtility.GetAnimationClipSettings(clip);
                    Assert.IsFalse(cs.loopTime, "Loop Time は無効: " + path);

                    // SpriteRenderer.sprite のみをアニメーションし、Transform/Collider/Rigidbody は変更しない。
                    EditorCurveBinding[] floatBindings = AnimationUtility.GetCurveBindings(clip);
                    Assert.AreEqual(0, floatBindings.Length, "Transform/Scale 等の float カーブを含まない: " + path);

                    EditorCurveBinding[] objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    Assert.AreEqual(1, objBindings.Length, "参照カーブは Sprite の 1 本のみ: " + path);
                    Assert.AreEqual(typeof(SpriteRenderer), objBindings[0].type, "対象は SpriteRenderer: " + path);
                    Assert.AreEqual("m_Sprite", objBindings[0].propertyName, "プロパティは m_Sprite: " + path);
                    Assert.AreEqual(string.Empty, objBindings[0].path, "同一 GameObject（path 空）: " + path);

                    ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, objBindings[0]);
                    Assert.AreEqual(s.Frames, keys.Length, "フレーム数: " + path);
                    for (int i = 0; i < keys.Length; i++)
                    {
                        Assert.IsNotNull(keys[i].value, "Missing Sprite 参照: " + path + " frame " + i);
                        // 01→02→… の昇順（時刻が単調増加）。
                        if (i > 0)
                        {
                            Assert.Greater(keys[i].time, keys[i - 1].time, "フレーム時刻が昇順: " + path);
                        }
                    }
                }
            }

            Assert.AreEqual(12, found, "Clip は 12 本。");
        }
    }
}
