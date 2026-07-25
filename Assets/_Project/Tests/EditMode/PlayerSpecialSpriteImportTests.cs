using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 必殺技仮スプライト受入：チャージ（4 方向×4＝16 枚）・必殺技本体（4 方向×7＝28 枚）の個別 PNG 計 44 枚と、
    /// 8 本の Animation Clip（Charge 4 本 Loop 有／Special Attack 4 本 Loop 無）の Import 設定・整合性を検証する。
    /// フレームごとに PNG サイズが異なるため、Sprite Sheet 化せず Single mode・Custom Pivot・Full Rect で受け入れる。
    /// 素材内容は pixel hash で固定せず、枚数・命名・設定・Clip 構成のみを検査する（Pivot の見た目は Editor で目視）。
    /// </summary>
    public sealed class PlayerSpecialSpriteImportTests
    {
        private const string SpritesDir =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites";
        private const string SpecialDir = SpritesDir + "/Special";

        // 既存キャラクター素材（通常攻撃 1 段目・128px・PPU100）を身体サイズ基準の参照にする。
        private const string PpuReferenceSprite = SpritesDir + "/Attack/Attack1/momotaro_attack1_down_01.png";

        private static readonly string[] Dirs = { "down", "left", "right", "up" };
        private static readonly string[] Caps = { "Down", "Left", "Right", "Up" };

        // フレームごとに絵のピクセル寸法が異なるため、身体身長を既存 Idle に合わせて Charge/Attack で PPU を分ける。
        private const float ChargePpu = 95f;
        private const float AttackPpu = 253f;

        private static IEnumerable<string> ChargePngs()
        {
            foreach (string d in Dirs)
                for (int i = 1; i <= 4; i++)
                    yield return $"{SpecialDir}/Charge/momotaro_special_charge_{d}_{i:00}.png";
        }

        private static IEnumerable<string> AttackPngs()
        {
            foreach (string d in Dirs)
                for (int i = 1; i <= 7; i++)
                    yield return $"{SpecialDir}/Attack/momotaro_special_attack_{d}_{i:00}.png";
        }

        [Test]
        public void Pngs_Counts_16Charge_28Attack_44Total_WithNaming()
        {
            int charge = AssetDatabase.FindAssets("t:Texture2D", new[] { SpecialDir + "/Charge" })
                .Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
            int attack = AssetDatabase.FindAssets("t:Texture2D", new[] { SpecialDir + "/Attack" })
                .Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));

            Assert.AreEqual(16, charge, "Charge = 4 フレーム × 4 方向 = 16。");
            Assert.AreEqual(28, attack, "Special Attack = 7 フレーム × 4 方向 = 28。");
            Assert.AreEqual(44, charge + attack, "合計 44 枚。");

            var pat = new Regex(@"^momotaro_special_(charge|attack)_(down|left|right|up)_\d{2}$");
            foreach (string path in ChargePngs().Concat(AttackPngs()))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(tex, "PNG が見つからない（命名不一致の可能性）: " + path);
                Assert.IsTrue(pat.IsMatch(System.IO.Path.GetFileNameWithoutExtension(path)), "命名規則不一致: " + path);
            }
        }

        private static void AssertCommonImport(string path, float expectedPpu)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsNotNull(ti, "TextureImporter 取得失敗: " + path);
            Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, "Texture Type Sprite: " + path);
            Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, "Sprite Mode Single（Sheet 化しない）: " + path);
            Assert.AreEqual(expectedPpu, ti.spritePixelsPerUnit, "PPU: " + path);
            Assert.AreEqual(FilterMode.Bilinear, ti.filterMode, "Filter Mode Bilinear: " + path);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "Compression None: " + path);
            Assert.IsTrue(ti.alphaIsTransparency, "Alpha Is Transparency: " + path);
            Assert.IsFalse(ti.isReadable, "Read/Write 無効: " + path);
            Assert.IsFalse(ti.mipmapEnabled, "Mip Maps 無効: " + path);
            Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, "Wrap Mode Clamp: " + path);

            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            Assert.AreEqual((int)SpriteAlignment.Custom, s.spriteAlignment, "Custom Pivot（足元基準）: " + path);
            Assert.AreEqual(SpriteMeshType.FullRect, s.spriteMeshType, "Mesh Type Full Rect: " + path);
            Assert.IsFalse(s.spriteGenerateFallbackPhysicsShape, "Generate Physics Shape 無効: " + path);
        }

        [Test]
        public void ChargePngs_SingleMode_CustomPivot_ChargePpu()
        {
            foreach (string path in ChargePngs())
            {
                AssertCommonImport(path, ChargePpu);
            }
        }

        [Test]
        public void AttackPngs_SingleMode_CustomPivot_AttackPpu()
        {
            foreach (string path in AttackPngs())
            {
                AssertCommonImport(path, AttackPpu);
            }
        }

        [Test]
        public void Ppu_ChargeAndAttack_DifferAsDocumented_ReferenceUnchanged()
        {
            var refTi = (TextureImporter)AssetImporter.GetAtPath(PpuReferenceSprite);
            Assert.AreEqual(100f, refTi.spritePixelsPerUnit, "既存 128px 素材は PPU100 のまま。");
            Assert.AreNotEqual(ChargePpu, AttackPpu, "絵のピクセル寸法差により Charge/Attack で PPU を分ける。");
        }

        [Test]
        public void Charge_FourClips_FourFrames_LoopOn_SpriteOnly_NoMissing()
        {
            AssertClips("SpecialCharge", frames: 4, loop: true, fps: 5);
        }

        [Test]
        public void SpecialAttack_FourClips_SevenFrames_LoopOff_SpriteOnly_NoMissing()
        {
            AssertClips("SpecialAttack", frames: 7, loop: false, fps: 10);
        }

        private static void AssertClips(string state, int frames, bool loop, int fps)
        {
            foreach (string cap in Caps)
            {
                string path = $"{SpritesDir}/AN_Player_{state}_{cap}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Assert.IsNotNull(clip, "Clip が見つからない: " + path);
                Assert.AreEqual(fps, clip.frameRate, "Sample Rate: " + path);

                AnimationClipSettings cs = AnimationUtility.GetAnimationClipSettings(clip);
                Assert.AreEqual(loop, cs.loopTime, "Loop 設定: " + path);

                Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length, "float カーブを含まない: " + path);
                EditorCurveBinding[] obj = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                Assert.AreEqual(1, obj.Length, "参照カーブは Sprite の 1 本のみ: " + path);
                Assert.AreEqual(typeof(SpriteRenderer), obj[0].type, "対象は SpriteRenderer: " + path);
                Assert.AreEqual("m_Sprite", obj[0].propertyName, "プロパティは m_Sprite: " + path);
                Assert.AreEqual(string.Empty, obj[0].path, "同一 GameObject（path 空）: " + path);

                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, obj[0]);
                Assert.AreEqual(frames, keys.Length, "フレーム数: " + path);
                for (int i = 0; i < keys.Length; i++)
                {
                    Assert.IsNotNull(keys[i].value, "Missing Sprite 参照: " + path + " frame " + i);
                    if (i > 0)
                    {
                        Assert.Greater(keys[i].time, keys[i - 1].time, "フレーム時刻が 01→昇順: " + path);
                    }
                }
            }
        }

        [Test]
        public void ExistingClips_NotOverwritten()
        {
            // 既存の通常攻撃 Clip などが健在（新規 Clip は別名・別パスで追加、上書きしていない）。
            foreach (string existing in new[] { "AN_Player_Attack1_Down", "AN_Player_Idle_Down", "AN_Player_GuardBreak_Down" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{SpritesDir}/{existing}.anim");
                Assert.IsNotNull(clip, "既存 Clip が失われている: " + existing);
            }
        }
    }
}
