using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 敵スプライト受入：骸骨弓兵 85 枚＋矢 4 枚（計 89）の Import 設定・寸法・命名、21 本の Animation Clip の fps／Loop／
    /// フレーム数／昇順／Sprite 単独バインド／Animation Event 不使用、Animator Controller の 21 State と参照切れ無し、
    /// 矢 4 方向の存在を検証する。骸骨剣士受入テストと同基準。素材内容の美術品質は判定せず、欠落・方向混入・順番・Import 設定・
    /// 参照切れのみ検出する。Projectile／射撃 AI は P3-08 のため本テストでは扱わない。
    /// </summary>
    public sealed class SkeletonArcherImportTests
    {
        private const string Root = "Assets/_Project/Art/Characters/Enemies/SkeletonArcher/Prototype";
        private const string SpritesDir = Root + "/Sprites";
        private const string AniDir = Root + "/Animations";
        private const string ControllerPath = Root + "/Controllers/AC_SkeletonArcher.controller";

        private const string PpuReferenceSprite =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/Attack/Attack1/momotaro_attack1_down_01.png";

        private static readonly string[] Quad = { "down", "left", "right", "up" };
        private static readonly string[] Caps = { "Down", "Left", "Right", "Up" };

        private struct Motion { public string Folder; public string Key; public string[] Dirs; public int Frames; }

        private static readonly Motion[] Motions =
        {
            new Motion { Folder = "Idle", Key = "idle", Dirs = Quad, Frames = 4 },
            new Motion { Folder = "Walk", Key = "walk", Dirs = Quad, Frames = 4 },
            new Motion { Folder = "Shoot", Key = "shoot", Dirs = Quad, Frames = 6 },
            new Motion { Folder = "Hurt", Key = "hurt", Dirs = Quad, Frames = 3 },
            new Motion { Folder = "Stun", Key = "stun", Dirs = Quad, Frames = 3 },
            new Motion { Folder = "Down", Key = "down", Dirs = new[] { "down" }, Frames = 5 },
        };

        private static IEnumerable<string> CharacterPaths()
        {
            foreach (Motion m in Motions)
            {
                foreach (string d in m.Dirs)
                {
                    for (int i = 1; i <= m.Frames; i++)
                    {
                        yield return $"{SpritesDir}/{m.Folder}/skeleton_archer_{m.Key}_{d}_{i:00}.png";
                    }
                }
            }
        }

        private static IEnumerable<string> ArrowPaths()
        {
            foreach (string d in Quad)
            {
                yield return $"{SpritesDir}/Projectile/Arrow/skeleton_archer_arrow_{d}.png";
            }
        }

        private static float ExpectedPpu()
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(PpuReferenceSprite);
            Assert.IsNotNull(ti, "PPU 参照（桃太郎）が見つからない。");
            return ti.spritePixelsPerUnit;
        }

        [Test]
        public void CharacterSprites_Total85_PerMotionCounts()
        {
            var counts = new Dictionary<string, int>();
            foreach (Motion m in Motions)
            {
                string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{SpritesDir}/{m.Folder}" });
                counts[m.Folder] = guids.Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
            }

            Assert.AreEqual(16, counts["Idle"]);
            Assert.AreEqual(16, counts["Walk"]);
            Assert.AreEqual(24, counts["Shoot"]);
            Assert.AreEqual(12, counts["Hurt"]);
            Assert.AreEqual(12, counts["Stun"]);
            Assert.AreEqual(5, counts["Down"]);
            Assert.AreEqual(85, counts.Values.Sum(), "キャラ合計 85 枚。");
        }

        [Test]
        public void ArrowSprites_Total4_FourDirections()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { $"{SpritesDir}/Projectile/Arrow" });
            int n = guids.Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
            Assert.AreEqual(4, n, "矢は 4 枚。");
            foreach (string path in ArrowPaths())
            {
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(path), "矢 Sprite が無い（方向不一致の可能性）: " + path);
            }
        }

        // Import 規約（桃太郎・骸骨剣士と統一）を全 89 枚で検査する共通部。
        private void AssertSpecImportSettings(string path, float expectedPpu)
        {
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
            Assert.AreEqual((int)SpriteAlignment.BottomCenter, settings.spriteAlignment, "Bottom Center（方向別で不変）: " + path);
            Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, "Full Rect: " + path);
            Assert.IsFalse(settings.spriteGenerateFallbackPhysicsShape, "物理シェイプ生成 無効: " + path);
        }

        [Test]
        public void CharacterSprites_192Square_WithSpecImporterSettings()
        {
            float expectedPpu = ExpectedPpu();
            foreach (string path in CharacterPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(tex, "PNG が見つからない（命名不一致の可能性）: " + path);
                Assert.AreEqual(192, tex.width, "幅 192: " + path);
                Assert.AreEqual(192, tex.height, "高さ 192: " + path);
                AssertSpecImportSettings(path, expectedPpu);
            }
        }

        [Test]
        public void ArrowSprites_Square_WithSpecImporterSettings()
        {
            // 矢は 96×96（キャラの半寸）。方向別で寸法・Import 規約を揃える（Pivot 規則も 4 方向で不変）。
            float expectedPpu = ExpectedPpu();
            foreach (string path in ArrowPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(tex, "矢 PNG が見つからない: " + path);
                Assert.AreEqual(tex.height, tex.width, "矢は正方形: " + path);
                Assert.AreEqual(96, tex.width, "矢 96×96: " + path);
                AssertSpecImportSettings(path, expectedPpu);
            }
        }

        [Test]
        public void AllSprites_LoadWithoutMissing()
        {
            foreach (string path in CharacterPaths().Concat(ArrowPaths()))
            {
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<Sprite>(path), "Single Sprite が生成されていない（Missing）: " + path);
            }
        }

        private struct ClipSpec { public string File; public int Frames; public int Fps; public bool Loop; }

        private static ClipSpec[] BuildClipSpecs()
        {
            var list = new List<ClipSpec>();
            foreach (string c in Caps)
            {
                list.Add(new ClipSpec { File = $"AN_SkeletonArcher_Idle_{c}", Frames = 4, Fps = 4, Loop = true });
                list.Add(new ClipSpec { File = $"AN_SkeletonArcher_Walk_{c}", Frames = 4, Fps = 8, Loop = true });
                list.Add(new ClipSpec { File = $"AN_SkeletonArcher_Shoot_{c}", Frames = 6, Fps = 8, Loop = false });
                list.Add(new ClipSpec { File = $"AN_SkeletonArcher_Hurt_{c}", Frames = 3, Fps = 10, Loop = false });
                list.Add(new ClipSpec { File = $"AN_SkeletonArcher_Stun_{c}", Frames = 3, Fps = 8, Loop = false });
            }

            list.Add(new ClipSpec { File = "AN_SkeletonArcher_Down_Down", Frames = 5, Fps = 8, Loop = false });
            return list.ToArray();
        }

        [Test]
        public void Clips_21_WithFramesFpsLoop_SpriteOnly_NoMissing()
        {
            ClipSpec[] specs = BuildClipSpecs();
            Assert.AreEqual(21, specs.Length);

            foreach (ClipSpec s in specs)
            {
                string path = $"{AniDir}/{s.File}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Assert.IsNotNull(clip, "Clip が見つからない: " + path);
                Assert.AreEqual(s.Fps, clip.frameRate, "Sample Rate: " + path);

                AnimationClipSettings cs = AnimationUtility.GetAnimationClipSettings(clip);
                Assert.AreEqual(s.Loop, cs.loopTime, "Loop 設定: " + path);

                Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length, "float カーブ無し（Transform 不変）: " + path);
                EditorCurveBinding[] obj = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                Assert.AreEqual(1, obj.Length, "参照カーブは Sprite の 1 本: " + path);
                Assert.AreEqual(typeof(SpriteRenderer), obj[0].type, "SpriteRenderer 対象: " + path);
                Assert.AreEqual("m_Sprite", obj[0].propertyName, "m_Sprite: " + path);
                Assert.AreEqual(string.Empty, obj[0].path, "同一 GameObject（path 空）: " + path);

                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, obj[0]);
                Assert.AreEqual(s.Frames, keys.Length, "フレーム数: " + path);
                for (int i = 0; i < keys.Length; i++)
                {
                    Assert.IsNotNull(keys[i].value, "Missing Sprite: " + path + " frame " + i);
                    if (i > 0)
                    {
                        Assert.Greater(keys[i].time, keys[i - 1].time, "時刻が昇順（01→…）: " + path);
                    }
                }
            }
        }

        [Test]
        public void Clips_HaveNoAnimationEvents()
        {
            foreach (ClipSpec s in BuildClipSpecs())
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AniDir}/{s.File}.anim");
                Assert.IsNotNull(clip);
                Assert.AreEqual(0, clip.events.Length, "Animation Event を持たない（Projectile を発射しない。Gameplay 正本ではない）: " + s.File);
            }
        }

        [Test]
        public void Controller_Has21States_NoMissingClips()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, "Animator Controller が見つからない: " + ControllerPath);
            Assert.AreEqual(1, controller.layers.Length, "Base Layer 1 枚。");

            ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
            var names = states.Select(s => s.state.name).ToList();

            var expected = new List<string>();
            foreach (string c in Caps)
            {
                expected.Add($"Idle_{c}");
                expected.Add($"Walk_{c}");
                expected.Add($"Attack_{c}"); // 射撃 Clip を再生する攻撃 State（EnemyVisualNames 規則）。
                expected.Add($"Hurt_{c}");
                expected.Add($"Stun_{c}");
            }

            expected.Add("Down");

            Assert.AreEqual(21, states.Length, "21 State。");
            foreach (string e in expected)
            {
                Assert.Contains(e, names, "必要な State が無い: " + e);
            }

            foreach (ChildAnimatorState st in states)
            {
                Assert.IsNotNull(st.state.motion, "State に Clip 参照が無い（Missing）: " + st.state.name);
                Assert.IsInstanceOf<AnimationClip>(st.state.motion, "Motion は AnimationClip: " + st.state.name);
            }

            Assert.IsNotNull(controller.layers[0].stateMachine.defaultState, "Default State が未設定。");
            Assert.AreEqual("Idle_Down", controller.layers[0].stateMachine.defaultState.name, "Default は Idle_Down。");
        }
    }
}
