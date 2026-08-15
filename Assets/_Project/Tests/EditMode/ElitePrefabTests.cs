using System.Reflection;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Presentation.Enemy;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-09：強敵 Prefab（PF_Enemy_Elite_Prototype）の健全性。必要 Component、Elite Data（4 攻撃）、侍骸骨 Controller、Visual Adapter の
    /// Elite 命名スタイル、Missing Script／負スケール無しを検証する。戦闘 AI の完成接続は P3-09 の別作業だが、資産の結線は本テストで担保する。
    /// </summary>
    public sealed class ElitePrefabTests
    {
        private const string Elite = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        private static GameObject Load()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(Elite);
            Assert.IsNotNull(go, "Elite Prefab が無い: " + Elite);
            return go;
        }

        [Test]
        public void ElitePrefab_HasRequiredComponents_And4Attacks()
        {
            GameObject go = Load();
            var actor = go.GetComponentInChildren<EnemyActor>(true);
            Assert.IsNotNull(actor, "EnemyActor");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyMotor>(true), "EnemyMotor");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyBrain>(true), "EnemyBrain");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyAttackController>(true), "EnemyAttackController");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyPerception>(true), "EnemyPerception");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyThreatTracker>(true), "EnemyThreatTracker");
            Assert.IsNotNull(go.GetComponentInChildren<Rigidbody>(true), "Rigidbody");
            Assert.IsNotNull(go.GetComponentInChildren<BoxCollider>(true), "BoxCollider");

            Assert.IsNotNull(actor.Archetype, "Elite Archetype 割当。");
            Assert.AreEqual(4, actor.Archetype.AttackCount, "強敵は 4 攻撃（通常/強/ガード不能/突進）。");
        }

        [Test]
        public void EliteVisualAdapter_UsesEliteNamingStyle()
        {
            var adapter = Load().GetComponentInChildren<EnemyVisualAdapter>(true);
            Assert.IsNotNull(adapter, "EnemyVisualAdapter");
            var style = (EnemyVisualNamingStyle)GetField(adapter, "_namingStyle");
            Assert.AreEqual(EnemyVisualNamingStyle.Elite, style, "強敵は Elite 命名（Move/分類別攻撃）。");
        }

        [Test]
        public void ElitePrefab_NoMissingScripts_NoNegativeScale()
        {
            GameObject go = Load();
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                    "Missing Script: " + t.name);
                Vector3 s = t.localScale;
                Assert.IsTrue(s.x >= 0f && s.y >= 0f && s.z >= 0f, "負スケール不使用: " + t.name);
            }
        }

        private static object GetField(object t, string n)
        {
            FieldInfo f = t.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + n);
            return f.GetValue(t);
        }
    }
}
