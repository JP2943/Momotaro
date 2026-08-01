using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 敵スプライト受入：21 本の Animation Clip の fps／Loop／フレーム数／昇順／Sprite 単独バインドを検証する。
    /// Idle/Walk/Attack 各4方向×4F、Hurt/Stun 各4方向×3F、Down 5F。Animation Event は使わない（Hitbox は Gameplay 正本）。
    /// </summary>
    public sealed class SkeletonSwordsmanClipTests
    {
        private const string AniDir =
            "Assets/_Project/Art/Characters/Enemies/SkeletonSwordsman/Prototype/Animations";

        private struct Spec { public string File; public int Frames; public int Fps; public bool Loop; }

        private static readonly string[] Caps = { "Down", "Left", "Right", "Up" };

        private static Spec[] BuildSpecs()
        {
            var list = new System.Collections.Generic.List<Spec>();
            foreach (string c in Caps)
            {
                list.Add(new Spec { File = $"AN_Skeleton_Idle_{c}", Frames = 4, Fps = 4, Loop = true });
                list.Add(new Spec { File = $"AN_Skeleton_Walk_{c}", Frames = 4, Fps = 8, Loop = true });
                list.Add(new Spec { File = $"AN_Skeleton_Attack_{c}", Frames = 4, Fps = 8, Loop = false });
                list.Add(new Spec { File = $"AN_Skeleton_Hurt_{c}", Frames = 3, Fps = 12, Loop = false });
                list.Add(new Spec { File = $"AN_Skeleton_Stun_{c}", Frames = 3, Fps = 10, Loop = false });
            }

            list.Add(new Spec { File = "AN_Skeleton_Down", Frames = 5, Fps = 8, Loop = false });
            return list.ToArray();
        }

        [Test]
        public void Clips_21_WithFramesFpsLoop_SpriteOnly_NoMissing()
        {
            Spec[] specs = BuildSpecs();
            Assert.AreEqual(21, specs.Length);

            foreach (Spec s in specs)
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
        public void Events_AreEmpty_NoAnimationEventDrivesGameplay()
        {
            foreach (Spec s in BuildSpecs())
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AniDir}/{s.File}.anim");
                Assert.IsNotNull(clip);
                Assert.AreEqual(0, clip.events.Length, "Animation Event を持たない（Gameplay 正本ではない）: " + s.File);
            }
        }
    }
}
