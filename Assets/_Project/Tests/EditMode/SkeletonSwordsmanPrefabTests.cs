using System.Collections.Generic;
using Momotaro.Gameplay.Enemy;
using Momotaro.Presentation.Enemy;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 敵スプライト受入：近接プロトタイプ Prefab に表示基盤（SpriteRenderer/Animator/EnemyVisualAdapter）が接続され、
    /// Animator に全状態×方向の State が揃い、EnemyVisualNames の解決先がすべて存在する（Missing State/Script/Sprite 無し）ことを検証する。
    /// </summary>
    public sealed class SkeletonSwordsmanPrefabTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string ControllerPath =
            "Assets/_Project/Art/Characters/Enemies/SkeletonSwordsman/Prototype/Controllers/AC_SkeletonSwordsman.controller";

        private static GameObject LoadPrefab()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(go, "Prefab が見つからない: " + PrefabPath);
            return go;
        }

        private static HashSet<string> ControllerStateNames()
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(ac, "AnimatorController が見つからない: " + ControllerPath);
            var names = new HashSet<string>();
            foreach (ChildAnimatorState cs in ac.layers[0].stateMachine.states)
            {
                names.Add(cs.state.name);
            }

            return names;
        }

        [Test]
        public void Prefab_HasVisualComponents_NoMissingScripts()
        {
            GameObject prefab = LoadPrefab();

            Assert.IsNotNull(prefab.GetComponentInChildren<SpriteRenderer>(true), "SpriteRenderer が Visual Root 配下に必要。");
            var animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, "Animator が必要。");
            Assert.IsNotNull(animator.runtimeAnimatorController, "Animator Controller が割当済み。");
            Assert.IsNotNull(prefab.GetComponentInChildren<EnemyVisualAdapter>(true), "EnemyVisualAdapter が必要。");
            Assert.IsNotNull(prefab.GetComponent<EnemyActor>(), "Gameplay Root の EnemyActor は維持。");

            foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
            {
                Assert.IsNotNull(c, "Missing Script が含まれている（参照切れ）。");
            }
        }

        [Test]
        public void Controller_Has21States_IncludingFacingIndependentDown()
        {
            HashSet<string> names = ControllerStateNames();
            Assert.AreEqual(21, names.Count, "State は 21（4方向×5モーション＋Down）。");
            Assert.IsTrue(names.Contains("Down"), "共通正面 Down State が存在。");
        }

        [Test]
        public void EveryGameplayStateAndFacing_ResolvesToExistingState()
        {
            HashSet<string> names = ControllerStateNames();
            var facings = new[] { EnemyVisualFacing.Down, EnemyVisualFacing.Left, EnemyVisualFacing.Right, EnemyVisualFacing.Up };

            foreach (EnemyState state in System.Enum.GetValues(typeof(EnemyState)))
            {
                foreach (EnemyVisualFacing f in facings)
                {
                    string clip = EnemyVisualNames.StateName(state, f);
                    Assert.IsTrue(names.Contains(clip),
                        $"状態 {state}／向き {f} の解決先 State '{clip}' が Animator に無い（Missing State）。");
                }
            }
        }

        [Test]
        public void DownState_ResolvesToDown_ForAllFacings()
        {
            HashSet<string> names = ControllerStateNames();
            var facings = new[] { EnemyVisualFacing.Down, EnemyVisualFacing.Left, EnemyVisualFacing.Right, EnemyVisualFacing.Up };
            foreach (EnemyVisualFacing f in facings)
            {
                Assert.AreEqual("Down", EnemyVisualNames.StateName(EnemyState.Down, f));
            }

            Assert.IsTrue(names.Contains("Down"));
        }
    }
}
