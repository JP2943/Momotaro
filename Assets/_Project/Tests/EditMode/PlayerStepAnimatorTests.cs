using System.Collections.Generic;
using System.Linq;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-09 受入修正：ステップの Animator 接続を検証する。PlayerVisualNames が Step 各方向の期待 State 名を返し、実際の
    /// AC_Player.controller の Base Layer に Step 4 State が存在し、各 State に Loop 無効の Motion（Step Clip）が割り当てられ、
    /// PF_Player_Momotaro の Animator が当該 Controller を参照し、既存 Idle/Move/Guard/Attack State が消えていないことを確認する。
    /// 文字列解決だけでなく実 Asset を読む。
    /// </summary>
    public sealed class PlayerStepAnimatorTests
    {
        private const string ControllerPath =
            "Assets/_Project/Art/Characters/Player/Momotaro/Prototype/Sprites/AC_Player.controller";
        private const string PrefabPath =
            "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";

        private static readonly (FacingDirection facing, string cap)[] Dirs =
        {
            (FacingDirection.Down, "Down"),
            (FacingDirection.Left, "Left"),
            (FacingDirection.Right, "Right"),
            (FacingDirection.Up, "Up"),
        };

        private static AnimatorController LoadController()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(ac, "Animator Controller が見つからない: " + ControllerPath);
            return ac;
        }

        private static ChildAnimatorState[] BaseLayerStates(AnimatorController ac)
        {
            Assert.GreaterOrEqual(ac.layers.Length, 1, "Base Layer が存在する。");
            Assert.AreEqual("Base Layer", ac.layers[0].name, "Layer 0 は Base Layer。");
            return ac.layers[0].stateMachine.states;
        }

        [Test]
        public void VisualNames_Step_ResolveExpectedStateNames()
        {
            foreach (var (facing, cap) in Dirs)
            {
                Assert.AreEqual($"AN_Player_Step_{cap}", PlayerVisualNames.ClipName(PlayerState.Step, facing),
                    "Step の期待 State 名。");
            }
        }

        [Test]
        public void Controller_HasStepFourStates_WithLoopOffMotion()
        {
            var ac = LoadController();
            ChildAnimatorState[] states = BaseLayerStates(ac);

            foreach (var (_, cap) in Dirs)
            {
                string name = $"AN_Player_Step_{cap}";
                AnimatorState st = states.FirstOrDefault(s => s.state != null && s.state.name == name).state;
                Assert.IsNotNull(st, "Step State が存在: " + name);
                Assert.IsNotNull(st.motion, "Step State に Motion 割り当て: " + name);

                var clip = st.motion as AnimationClip;
                Assert.IsNotNull(clip, "Motion は AnimationClip: " + name);
                Assert.IsFalse(clip.isLooping, "Step Clip は Loop 無効: " + name);
            }
        }

        [Test]
        public void Controller_ExistingCoreStates_NotRemovedOrRenamed()
        {
            var ac = LoadController();
            HashSet<string> names = BaseLayerStates(ac).Where(s => s.state != null).Select(s => s.state.name).ToHashSet();

            foreach (string core in new[]
            {
                "AN_Player_Idle_Down", "AN_Player_Move_Down", "AN_Player_Guard_Down",
                "AN_Player_Attack1_Down", "AN_Player_Attack2_Down", "AN_Player_Attack3_Down",
            })
            {
                Assert.IsTrue(names.Contains(core), "既存 State が保持されている: " + core);
            }
        }

        [Test]
        public void Prefab_Animator_ReferencesTheController()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "Player Prefab が見つからない: " + PrefabPath);

            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "Prefab に Animator が存在。");

            var ac = LoadController();
            Assert.AreEqual(ac, animator.runtimeAnimatorController, "Prefab Animator は AC_Player を参照している。");
        }
    }
}
