using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-01 受入修正：Player Animator Controller（AC_Player）の Base Layer に 4 方向の Hurt State が登録され、
    /// 同名 Clip が割り当てられ、ループ無効であることを検出する回帰テスト。State 未登録だと Hurt へ遷移しても被弾
    /// アニメーションが再生されず、<see cref="PlayerVisualAdapter"/> が State 不足警告を出すため、接続漏れを自動検出する。
    /// </summary>
    public sealed class PlayerHurtAnimatorStateTests
    {
        private const string ControllerPath =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/AC_Player.controller";

        private static readonly string[] HurtStateNames =
        {
            "AN_Player_Hurt_Down", "AN_Player_Hurt_Left", "AN_Player_Hurt_Right", "AN_Player_Hurt_Up",
        };

        private static AnimatorController LoadController()
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(c, "AC_Player が見つかりません（" + ControllerPath + "）。");
            return c;
        }

        private static AnimatorStateMachine BaseLayer(AnimatorController c)
        {
            Assert.Greater(c.layers.Length, 0, "レイヤーが 1 つ以上存在する。");
            AnimatorControllerLayer layer = null;
            foreach (AnimatorControllerLayer l in c.layers)
            {
                if (l.name == "Base Layer")
                {
                    layer = l;
                    break;
                }
            }

            Assert.IsNotNull(layer, "Base Layer が存在する。");
            Assert.IsNotNull(layer.stateMachine, "Base Layer に StateMachine が存在する。");
            return layer.stateMachine;
        }

        private static int CountStates(AnimatorStateMachine sm, string name)
        {
            int n = 0;
            foreach (ChildAnimatorState cs in sm.states)
            {
                if (cs.state != null && cs.state.name == name)
                {
                    n++;
                }
            }

            return n;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string name)
        {
            foreach (ChildAnimatorState cs in sm.states)
            {
                if (cs.state != null && cs.state.name == name)
                {
                    return cs.state;
                }
            }

            return null;
        }

        [Test]
        public void Controller_Loads_WithBaseLayer()
        {
            Assert.IsNotNull(BaseLayer(LoadController()));
        }

        [Test]
        public void BaseLayer_ContainsFourHurtStates_OnceEach()
        {
            AnimatorStateMachine sm = BaseLayer(LoadController());
            foreach (string name in HurtStateNames)
            {
                Assert.AreEqual(1, CountStates(sm, name), "Base Layer 直下に State が 1 つ: " + name);
            }
        }

        [Test]
        public void EachHurtState_HasNonNullMotion_WithMatchingClipName()
        {
            AnimatorStateMachine sm = BaseLayer(LoadController());
            foreach (string name in HurtStateNames)
            {
                AnimatorState st = FindState(sm, name);
                Assert.IsNotNull(st, "State 不在: " + name);
                Assert.IsNotNull(st.motion, "Motion が null: " + name);
                Assert.AreEqual(name, st.motion.name, "同名 Clip が割り当てられている: " + name);
            }
        }

        [Test]
        public void EachHurtClip_IsNotLooping()
        {
            AnimatorStateMachine sm = BaseLayer(LoadController());
            foreach (string name in HurtStateNames)
            {
                AnimatorState st = FindState(sm, name);
                Assert.IsNotNull(st, "State 不在: " + name);
                var clip = st.motion as AnimationClip;
                Assert.IsNotNull(clip, "Motion が AnimationClip: " + name);
                AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
                Assert.IsFalse(settings.loopTime, "Hurt Clip はループ無効: " + name);
            }
        }

        [Test]
        public void StateNames_MatchPlayerVisualNamesOutput()
        {
            // PlayerVisualNames.ClipName(Hurt, dir) が返す名前と、実在 State 名が一致する（表示接続の正本一致を保証）。
            var dirs = new[] { FacingDirection.Down, FacingDirection.Left, FacingDirection.Right, FacingDirection.Up };
            AnimatorStateMachine sm = BaseLayer(LoadController());
            foreach (FacingDirection d in dirs)
            {
                string clipName = PlayerVisualNames.ClipName(PlayerState.Hurt, d);
                Assert.IsNotNull(FindState(sm, clipName), "VisualNames が返す State が存在する: " + clipName);
            }
        }
    }
}
