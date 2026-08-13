using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-10：検証用 Variant（Prototype の Toggle）。ガード／回避能力を持つ Prefab が <see cref="EnemyDefenseController"/> と、能力 Data が
    /// 有効な Archetype を持ち、Missing Script／負スケールが無いことを検証する（手動受入の「防御 Variant／回避 Variant」の資産結線を担保）。
    /// </summary>
    public sealed class DefenseVariantPrefabTests
    {
        private const string Guard = "Assets/_Project/Prefabs/Enemies/PF_Enemy_GuardVariant.prefab";
        private const string Evade = "Assets/_Project/Prefabs/Enemies/PF_Enemy_EvadeVariant.prefab";

        private static GameObject Load(string p)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            Assert.IsNotNull(go, "Prefab が無い: " + p);
            return go;
        }

        [Test]
        public void GuardVariant_HasController_AndGuardAbilityData()
        {
            GameObject go = Load(Guard);
            Assert.IsNotNull(go.GetComponentInChildren<EnemyDefenseController>(true), "EnemyDefenseController");
            var actor = go.GetComponentInChildren<EnemyActor>(true);
            Assert.IsNotNull(actor.Archetype, "Archetype 割当。");
            Assert.IsTrue(actor.Archetype.CanGuard, "ガード能力有効。");
            Assert.IsFalse(actor.Archetype.CanEvade, "回避は無効（ガード Variant）。");
            AssertClean(go);
        }

        [Test]
        public void EvadeVariant_HasController_AndEvadeAbilityData()
        {
            GameObject go = Load(Evade);
            Assert.IsNotNull(go.GetComponentInChildren<EnemyDefenseController>(true), "EnemyDefenseController");
            var actor = go.GetComponentInChildren<EnemyActor>(true);
            Assert.IsNotNull(actor.Archetype, "Archetype 割当。");
            Assert.IsTrue(actor.Archetype.CanEvade, "回避能力有効。");
            Assert.IsFalse(actor.Archetype.CanGuard, "ガードは無効（回避 Variant）。");
            AssertClean(go);
        }

        private static void AssertClean(GameObject go)
        {
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                    "Missing Script: " + t.name);
                Vector3 s = t.localScale;
                Assert.IsTrue(s.x >= 0f && s.y >= 0f && s.z >= 0f, "負スケール不使用: " + t.name);
            }
        }
    }
}
