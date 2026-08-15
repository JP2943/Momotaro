using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 強敵スプライト受入：侍骸骨の 29 Animation Clip（Idle/Move/Hurt/Stun/NormalAttack/HeavyOverhead/UnguardableThrust 各 4 方向＋Down）の
    /// フレーム数・fps・Loop・昇順・Sprite 単独バインド・Animation Event 不使用・Transform カーブ不使用と、Animator Controller の 29 State を
    /// 検証する。HeavyOverhead=7 枚（修正版）、UnguardableThrust=8 枚（修正版）、Down=7 枚。攻撃タイミングの正本は Gameplay（P3-09）。
    /// </summary>
    public sealed class SamuraiSkeletonClipTests
    {
        private const string AniDir =
            "Assets/_Project/Art/Characters/Enemies/SamuraiSkeleton/Prototype/Animations";
        private const string ControllerPath =
            "Assets/_Project/Art/Characters/Enemies/SamuraiSkeleton/Prototype/Controllers/AC_SamuraiSkeleton.controller";

        private static readonly string[] Caps = { "Down", "Left", "Right", "Up" };

        private struct Spec { public string File; public int Frames; public int Fps; public bool Loop; }

        private static Spec[] BuildSpecs()
        {
            var list = new List<Spec>();
            foreach (string c in Caps)
            {
                list.Add(new Spec { File = $"AN_SamuraiSkeleton_Idle_{c}", Frames = 4, Fps = 4, Loop = true });
                list.Add(new Spec { File = $"AN_SamuraiSkeleton_Move_{c}", Frames = 6, Fps = 8, Loop = true });
                list.Add(new Spec { File = $"AN_SamuraiSkeleton_Hurt_{c}", Frames = 3, Fps = 12, Loop = false });
                list.Add(new Spec { File = $"AN_SamuraiSkeleton_Stun_{c}", Frames = 4, Fps = 10, Loop = false });
                list.Add(new Spec { File = $"AN_SamuraiSkeleton_NormalAttack_{c}", Frames = 5, Fps = 8, Loop = false });
                list.Add(new Spec { File = $"AN_SamuraiSkeleton_HeavyOverhead_{c}", Frames = 7, Fps = 8, Loop = false });
                list.Add(new Spec { File = $"AN_SamuraiSkeleton_UnguardableThrust_{c}", Frames = 8, Fps = 8, Loop = false });
            }

            list.Add(new Spec { File = "AN_SamuraiSkeleton_Down", Frames = 7, Fps = 8, Loop = false });
            return list.ToArray();
        }

        [Test]
        public void Clips_29_FramesFpsLoop_SpriteOnly_NoMissing()
        {
            Spec[] specs = BuildSpecs();
            Assert.AreEqual(29, specs.Length, "Stun 4 本を含め 29 本。");

            foreach (Spec s in specs)
            {
                string path = $"{AniDir}/{s.File}.anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                Assert.IsNotNull(clip, "Clip が見つからない: " + path);
                Assert.AreEqual(s.Fps, clip.frameRate, "fps: " + path);

                AnimationClipSettings cs = AnimationUtility.GetAnimationClipSettings(clip);
                Assert.AreEqual(s.Loop, cs.loopTime, "Loop: " + path);

                Assert.AreEqual(0, AnimationUtility.GetCurveBindings(clip).Length, "Transform 等 float カーブ無し: " + path);
                EditorCurveBinding[] obj = AnimationUtility.GetObjectReferenceCurveBindings(clip);
                Assert.AreEqual(1, obj.Length, "参照カーブは Sprite 1 本: " + path);
                Assert.AreEqual(typeof(SpriteRenderer), obj[0].type, "SpriteRenderer: " + path);
                Assert.AreEqual("m_Sprite", obj[0].propertyName, "m_Sprite: " + path);
                Assert.AreEqual(string.Empty, obj[0].path, "同一 GameObject: " + path);

                ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, obj[0]);
                Assert.AreEqual(s.Frames, keys.Length, "フレーム数: " + path);
                for (int i = 0; i < keys.Length; i++)
                {
                    Assert.IsNotNull(keys[i].value, "Missing Sprite: " + path + " frame " + i);
                    if (i > 0)
                    {
                        Assert.Greater(keys[i].time, keys[i - 1].time, "昇順(01→…): " + path);
                    }
                }
            }
        }

        [Test]
        public void Clips_NoAnimationEvents()
        {
            foreach (Spec s in BuildSpecs())
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{AniDir}/{s.File}.anim");
                Assert.IsNotNull(clip);
                Assert.AreEqual(0, clip.events.Length, "Animation Event を持たない（当たり判定は Gameplay 正本）: " + s.File);
            }
        }

        [Test]
        public void Controller_Has29States_NoMissingClips_DefaultIdleDown()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(controller, "AC_SamuraiSkeleton が無い: " + ControllerPath);
            Assert.AreEqual(1, controller.layers.Length);

            ChildAnimatorState[] states = controller.layers[0].stateMachine.states;
            var names = states.Select(s => s.state.name).ToList();

            var expected = new List<string>();
            foreach (string c in Caps)
            {
                expected.Add($"Idle_{c}");
                expected.Add($"Move_{c}");
                expected.Add($"Hurt_{c}");
                expected.Add($"Stun_{c}");
                expected.Add($"NormalAttack_{c}");
                expected.Add($"HeavyOverhead_{c}");
                expected.Add($"UnguardableThrust_{c}");
            }

            expected.Add("Down");

            Assert.AreEqual(29, states.Length, "29 State（Stun 4 を含む）。");
            foreach (string e in expected)
            {
                Assert.Contains(e, names, "必要な State が無い: " + e);
            }

            foreach (ChildAnimatorState st in states)
            {
                Assert.IsNotNull(st.state.motion, "State に Clip 参照が無い（Missing）: " + st.state.name);
                Assert.IsInstanceOf<AnimationClip>(st.state.motion, "Motion は AnimationClip: " + st.state.name);
            }

            Assert.IsNotNull(controller.layers[0].stateMachine.defaultState, "Default State 未設定。");
            Assert.AreEqual("Idle_Down", controller.layers[0].stateMachine.defaultState.name, "Default は Idle_Down。");
        }
    }
}
