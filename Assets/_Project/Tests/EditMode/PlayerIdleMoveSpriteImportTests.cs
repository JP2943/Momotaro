using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// Idle／Move 仮スプライト手描き差し替え受入：Idle（4 方向×6＝24 枚）・Move（4 方向×6＝24 枚）の個別 PNG 計 48 枚と、
    /// 8 本の Animation Clip（いずれも Loop 有）の Import 設定・整合性を検証する。従来の 4dir Sheet（Multiple mode）から
    /// 他モーション（Attack／Step／Hurt／Special）と同じ個別 PNG・Single mode・BottomCenter Pivot(0.5,0)・Full Rect・
    /// PPU100 へ統一した。素材内容は pixel hash で固定せず、枚数・命名・寸法・設定・Clip 構成のみを検査する。
    /// </summary>
    public sealed class PlayerIdleMoveSpriteImportTests
    {
        private const string SpritesDir =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites";
        private const string IdleDir = SpritesDir + "/Idle";
        private const string MoveDir = SpritesDir + "/Move";
        private const string PlayerPrefab = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";

        // 既存キャラクター素材（通常攻撃 1 段目・PPU100・192px）を身体サイズ基準の参照にする。
        private const string PpuReferenceSprite = SpritesDir + "/Attack/Attack1/momotaro_attack1_down_01.png";

        private static readonly string[] Dirs = { "down", "left", "right", "up" };
        private static readonly string[] Caps = { "Down", "Left", "Right", "Up" };

        private const float Ppu = 100f;
        private const int FrameSize = 192;
        private const int FrameCount = 6;

        // 旧 Sheet（Multiple mode）の GUID。差し替え後はどこからも参照されていないこと。
        private const string LegacyIdleSheetGuid = "27038a246e0eb5445a8025d67cc14566";
        private const string LegacyMoveSheetGuid = "645734ea6658a1945b9fe15593c32519";

        private static string Png(string state, string dir, int index) =>
            $"{SpritesDir}/{state}/momotaro_{state.ToLowerInvariant()}_{dir}_{index:00}.png";

        private static IEnumerable<string> Pngs(string state)
        {
            foreach (string d in Dirs)
                for (int i = 1; i <= FrameCount; i++)
                    yield return Png(state, d, i);
        }

        [Test]
        public void Pngs_Counts_24Idle_24Move_48Total_WithNaming()
        {
            int idle = AssetDatabase.FindAssets("t:Texture2D", new[] { IdleDir })
                .Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));
            int move = AssetDatabase.FindAssets("t:Texture2D", new[] { MoveDir })
                .Select(AssetDatabase.GUIDToAssetPath).Count(p => p.EndsWith(".png"));

            Assert.AreEqual(24, idle, "Idle = 6 フレーム × 4 方向 = 24。");
            Assert.AreEqual(24, move, "Move = 6 フレーム × 4 方向 = 24。");
            Assert.AreEqual(48, idle + move, "合計 48 枚。");

            var pat = new Regex(@"^momotaro_(idle|move)_(down|left|right|up)_\d{2}$");
            foreach (string path in Pngs("Idle").Concat(Pngs("Move")))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(tex, "PNG が見つからない（命名不一致の可能性）: " + path);
                Assert.IsTrue(pat.IsMatch(System.IO.Path.GetFileNameWithoutExtension(path)), "命名規則不一致: " + path);
            }
        }

        private static void AssertCommonImport(string path)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(tex, "テクスチャ読込失敗: " + path);
            Assert.AreEqual(FrameSize, tex.width, "幅: " + path);
            Assert.AreEqual(FrameSize, tex.height, "高さ: " + path);

            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.IsNotNull(ti, "TextureImporter 取得失敗: " + path);
            Assert.AreEqual(TextureImporterType.Sprite, ti.textureType, "Texture Type Sprite: " + path);
            Assert.AreEqual(SpriteImportMode.Single, ti.spriteImportMode, "Sprite Mode Single（Sheet 化しない）: " + path);
            Assert.AreEqual(Ppu, ti.spritePixelsPerUnit, "PPU100（他モーションと統一）: " + path);
            Assert.AreEqual(FilterMode.Bilinear, ti.filterMode, "Filter Mode Bilinear: " + path);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, ti.textureCompression, "Compression None: " + path);
            Assert.IsTrue(ti.alphaIsTransparency, "Alpha Is Transparency: " + path);
            Assert.IsFalse(ti.isReadable, "Read/Write 無効: " + path);
            Assert.IsFalse(ti.mipmapEnabled, "Mip Maps 無効: " + path);
            Assert.AreEqual(TextureWrapMode.Clamp, ti.wrapMode, "Wrap Mode Clamp: " + path);

            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            Assert.AreEqual((int)SpriteAlignment.BottomCenter, s.spriteAlignment, "BottomCenter Pivot（足元基準・他モーション統一）: " + path);
            Assert.AreEqual(new Vector2(0.5f, 0f), s.spritePivot, "Pivot (0.5, 0): " + path);
            Assert.AreEqual(SpriteMeshType.FullRect, s.spriteMeshType, "Mesh Type Full Rect: " + path);
            Assert.IsFalse(s.spriteGenerateFallbackPhysicsShape, "Generate Physics Shape 無効: " + path);
        }

        [Test]
        public void IdlePngs_SingleMode_BottomCenter_192_Ppu100()
        {
            foreach (string path in Pngs("Idle")) AssertCommonImport(path);
        }

        [Test]
        public void MovePngs_SingleMode_BottomCenter_192_Ppu100()
        {
            foreach (string path in Pngs("Move")) AssertCommonImport(path);
        }

        [Test]
        public void Ppu_UnifiedTo100_MatchingReference()
        {
            var refTi = (TextureImporter)AssetImporter.GetAtPath(PpuReferenceSprite);
            Assert.AreEqual(100f, refTi.spritePixelsPerUnit, "参照素材は PPU100。");
            Assert.AreEqual(100f, Ppu, "Idle／Move も PPU100 へ統一。");
        }

        [Test]
        public void Idle_FourClips_SixFrames_LoopOn_5Fps_1_2Seconds()
        {
            // 4 コマ／1.0 秒から 6 コマへ増量。1 コマ 0.2 秒・全長 1.2 秒の呼吸ループとする。
            AssertClips("Idle", fps: 5, length: 1.2f);
        }

        [Test]
        public void Move_FourClips_SixFrames_LoopOn_12Fps_0_5Seconds()
        {
            // コマ数は 6 のまま（手描き差し替えのみ）。移動速度と整合済みの 12fps／0.5 秒を維持する。
            AssertClips("Move", fps: 12, length: 0.5f);
        }

        private static void AssertClips(string state, int fps, float length)
        {
            for (int di = 0; di < Dirs.Length; di++)
            {
                string dir = Dirs[di];
                string path = $"{SpritesDir}/AN_Player_{state}_{Caps[di]}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Assert.IsNotNull(clip, "Clip が見つからない: " + path);
                Assert.AreEqual(fps, clip.frameRate, "Sample Rate: " + path);
                Assert.AreEqual(length, clip.length, 0.001f, "再生時間: " + path);

                AnimationClipSettings cs = AnimationUtility.GetAnimationClipSettings(clip);
                Assert.IsTrue(cs.loopTime, "Loop 設定: " + path);

                Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length, "float カーブを含まない: " + path);
                EditorCurveBinding[] obj = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                Assert.AreEqual(1, obj.Length, "参照カーブは Sprite の 1 本のみ: " + path);
                Assert.AreEqual(typeof(SpriteRenderer), obj[0].type, "対象は SpriteRenderer: " + path);
                Assert.AreEqual("m_Sprite", obj[0].propertyName, "プロパティは m_Sprite: " + path);
                Assert.AreEqual(string.Empty, obj[0].path, "同一 GameObject（path 空）: " + path);

                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, obj[0]);
                Assert.AreEqual(FrameCount, keys.Length, "フレーム数: " + path);
                for (int i = 0; i < keys.Length; i++)
                {
                    var sprite = keys[i].value as Sprite;
                    Assert.IsNotNull(sprite, "Missing Sprite 参照: " + path + " frame " + i);
                    Assert.AreEqual(1f / fps * i, keys[i].time, 0.001f, "フレーム時刻が等間隔: " + path + " frame " + i);

                    string expected = Png(state, dir, i + 1);
                    Assert.AreEqual(expected, AssetDatabase.GetAssetPath(sprite),
                        "01→06 の順で新しい個別 PNG を参照: " + path + " frame " + i);
                }
            }
        }

        [Test]
        public void LegacySheets_NoLongerReferenced_ByClipsOrPrefab()
        {
            string[] targets = Caps.Select(c => $"{SpritesDir}/AN_Player_Idle_{c}.anim")
                .Concat(Caps.Select(c => $"{SpritesDir}/AN_Player_Move_{c}.anim"))
                .Concat(new[] { PlayerPrefab })
                .ToArray();

            foreach (string path in targets)
            {
                Assert.IsTrue(System.IO.File.Exists(path), "対象アセットが存在しない: " + path);
                string text = System.IO.File.ReadAllText(path);
                Assert.IsFalse(text.Contains(LegacyIdleSheetGuid), "旧 Idle Sheet を参照したまま: " + path);
                Assert.IsFalse(text.Contains(LegacyMoveSheetGuid), "旧 Move Sheet を参照したまま: " + path);
            }
        }

        [Test]
        public void PlayerPrefab_DefaultSprite_IsNewIdleDownFirstFrame()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, "Player プレハブが見つからない: " + PlayerPrefab);

            var sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            Assert.IsNotNull(sr, "SpriteRenderer が見つからない: " + PlayerPrefab);
            Assert.IsNotNull(sr.sprite, "既定スプライトが Missing: " + PlayerPrefab);
            Assert.AreEqual(Png("Idle", "down", 1), AssetDatabase.GetAssetPath(sr.sprite),
                "既定スプライトは新 Idle Down 01: " + PlayerPrefab);
        }

        [Test]
        public void ExistingClips_NotOverwritten()
        {
            // 他モーションの Clip が健在（Idle／Move 以外は変更していない）。
            foreach (string existing in new[]
                     { "AN_Player_Attack1_Down", "AN_Player_Step_Down", "AN_Player_GuardBreak_Down", "AN_Player_SpecialAttack_Down" })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{SpritesDir}/{existing}.anim");
                Assert.IsNotNull(clip, "既存 Clip が失われている: " + existing);
            }
        }
    }
}
