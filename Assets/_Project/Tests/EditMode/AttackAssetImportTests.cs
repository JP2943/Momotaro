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
    /// P2-03A 受入修正：攻撃素材（64 PNG）・12 Animation Clip・Animator Controller・Player Prefab の
    /// Import 設定と整合性を UnityEditor API で検証する EditMode テスト。
    /// Hitbox・段送り・数値処理は対象外（本テストは素材受入のみを検査する）。
    /// </summary>
    public sealed class AttackAssetImportTests
    {
        private const string AttackDir =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/Attack";

        private const string SpritesDir =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites";

        private const string ControllerPath =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/AC_Player.controller";

        private const string PrefabPath =
            "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";

        private static readonly string[] Directions = { "down", "left", "right", "up" };
        private static readonly string[] Caps = { "Down", "Left", "Right", "Up" };

        // stage -> (frames, fps, size)
        private static readonly Dictionary<int, (int frames, int fps, int size)> StageInfo =
            new Dictionary<int, (int, int, int)>
            {
                { 1, (5, 12, 128) },
                { 2, (5, 12, 128) },
                { 3, (6, 10, 192) },
            };

        private static List<string> AttackPngPaths()
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { AttackDir })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".png"))
                .OrderBy(p => p)
                .ToList();
        }

        private static int StageOf(string path)
        {
            Match m = Regex.Match(path, @"momotaro_attack(\d)_");
            return int.Parse(m.Groups[1].Value);
        }

        [Test]
        public void Pngs_CountIsExactly64_WithNamingAndPerStageCounts()
        {
            List<string> paths = AttackPngPaths();
            Assert.AreEqual(64, paths.Count, "攻撃 PNG は正確に 64 枚。");

            var perStage = new Dictionary<int, int> { { 1, 0 }, { 2, 0 }, { 3, 0 } };
            var namePattern = new Regex(@"^momotaro_attack[123]_(down|left|right|up)_\d{2}$");
            foreach (string p in paths)
            {
                string file = System.IO.Path.GetFileNameWithoutExtension(p);
                Assert.IsTrue(namePattern.IsMatch(file), "命名規則不一致: " + file);
                perStage[StageOf(p)]++;
            }

            Assert.AreEqual(20, perStage[1], "1 段目 = 5 フレーム × 4 方向 = 20。");
            Assert.AreEqual(20, perStage[2], "2 段目 = 20。");
            Assert.AreEqual(24, perStage[3], "3 段目 = 6 フレーム × 4 方向 = 24。");
        }

        [Test]
        public void Pngs_HaveExpectedDimensions()
        {
            foreach (string p in AttackPngPaths())
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                Assert.IsNotNull(tex, "テクスチャ読込失敗: " + p);
                int expected = StageInfo[StageOf(p)].size;
                Assert.AreEqual(expected, tex.width, "幅不一致: " + p);
                Assert.AreEqual(expected, tex.height, "高さ不一致: " + p);
            }
        }

        [Test]
        public void Pngs_ImporterSettingsMatchSpec()
        {
            foreach (string p in AttackPngPaths())
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                Assert.IsNotNull(ti, "TextureImporter 取得失敗: " + p);

                Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, "Texture Type: " + p);
                Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, "Sprite Mode: " + p);
                Assert.AreEqual(100f, ti.spritePixelsPerUnit, "PPU: " + p);
                Assert.AreEqual(FilterMode.Bilinear, ti.filterMode, "Filter Mode: " + p);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "Compression: " + p);
                Assert.IsTrue(ti.alphaIsTransparency, "Alpha Is Transparency: " + p);

                var settings = new TextureImporterSettings();
                ti.ReadTextureSettings(settings);
                Assert.AreEqual((int)SpriteAlignment.BottomCenter, settings.spriteAlignment, "Pivot alignment (Bottom Center): " + p);
                Assert.AreEqual(SpriteMeshType.FullRect, settings.spriteMeshType, "Sprite Mesh Type (Full Rect): " + p);
                Assert.AreEqual(new Vector2(0.5f, 0f), settings.spritePivot, "Pivot (0.5, 0): " + p);
            }
        }

        [Test]
        public void Clips_ExactlyTwelve_WithFramesSampleRateLoopOffAndNoMissingSprites()
        {
            int found = 0;
            for (int stage = 1; stage <= 3; stage++)
            {
                (int frames, int fps, int _) = StageInfo[stage];
                foreach (string cap in Caps)
                {
                    string path = $"{SpritesDir}/AN_Player_Attack{stage}_{cap}.anim";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    Assert.IsNotNull(clip, "Clip が見つからない: " + path);
                    found++;

                    Assert.AreEqual(fps, clip.frameRate, "Sample Rate: " + path);

                    AnimationClipSettings s = AnimationUtility.GetAnimationClipSettings(clip);
                    Assert.IsFalse(s.loopTime, "Loop Time は無効: " + path);

                    EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    Assert.AreEqual(1, bindings.Length, "Sprite 参照カーブは 1 本: " + path);

                    ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, bindings[0]);
                    Assert.AreEqual(frames, keys.Length, "フレーム数: " + path);
                    foreach (ObjectReferenceKeyframe k in keys)
                    {
                        Assert.IsNotNull(k.value, "Missing Sprite 参照: " + path);
                    }
                }
            }

            Assert.AreEqual(12, found, "Animation Clip は正確に 12 本。");
        }

        [Test]
        public void AnimatorController_HasTwelveAttackStates_WithMotion()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(ac, "AnimatorController 読込失敗: " + ControllerPath);

            int attackStates = 0;
            foreach (AnimatorControllerLayer layer in ac.layers)
            {
                foreach (ChildAnimatorState cs in layer.stateMachine.states)
                {
                    if (cs.state.name.StartsWith("AN_Player_Attack"))
                    {
                        attackStates++;
                        Assert.IsNotNull(cs.state.motion, "状態に Motion 未割当: " + cs.state.name);
                    }
                }
            }

            Assert.AreEqual(12, attackStates, "Attack 状態は 12 個。");
        }

        [Test]
        public void PlayerPrefab_AnimatorControllerReferenceIsNotMissing()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Prefab 読込失敗: " + PrefabPath);

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "Prefab に Animator が無い。");
            Assert.IsNotNull(animator.runtimeAnimatorController, "Animator Controller 参照が Missing。");
        }
    }
}
