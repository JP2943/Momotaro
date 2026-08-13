using System.Collections.Generic;
using Momotaro.Data;
using Momotaro.Editor.Validation;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-12 統合受入：検証 Prefab／Data の参照整合を担保する。敵 Prefab の必須 Component、仮 UI（頭上バー・デバッグ）の結線、
    /// Missing Script 無し、敵 Archetype Data の検証（Stable ID 重複・必須値）、統合編成（<see cref="EnemyTestComposition"/>）の内訳を
    /// EditMode で決定的に確認する。専用検証 Scene の生成は <see cref="Phase3EnemyTestFieldBuilderTests"/> が担う。
    /// </summary>
    public sealed class Phase3IntegrationTests
    {
        private const string Melee = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string Ranged = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Ranged_Prototype.prefab";
        private const string Elite = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Elite_Prototype.prefab";

        private static GameObject Load(string p)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            Assert.IsNotNull(go, "Prefab が無い: " + p);
            return go;
        }

        private static void AssertRequiredEnemyComponents(string path)
        {
            GameObject go = Load(path);
            Assert.IsNotNull(go.GetComponentInChildren<EnemyActor>(true), path + " EnemyActor");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyMotor>(true), path + " EnemyMotor");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyBrain>(true), path + " EnemyBrain");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyAttackController>(true), path + " EnemyAttackController");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyPerception>(true), path + " EnemyPerception");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyThreatTracker>(true), path + " EnemyThreatTracker");
            Assert.IsNotNull(go.GetComponentInChildren<Rigidbody>(true), path + " Rigidbody");
            Assert.IsNotNull(go.GetComponentInChildren<Collider>(true), path + " Collider");
            // P3-11 仮 UI の結線（頭上バー・デバッグオーバレイ）。
            Assert.IsNotNull(go.GetComponentInChildren<EnemyOverheadBars>(true), path + " EnemyOverheadBars");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyAiDebugOverlay>(true), path + " EnemyAiDebugOverlay");
        }

        private static void AssertNoMissingScripts(string path)
        {
            GameObject go = Load(path);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                    "Missing Script: " + path + " / " + t.name);
            }
        }

        [Test]
        public void EnemyPrefabs_HaveRequiredComponents()
        {
            AssertRequiredEnemyComponents(Melee);
            AssertRequiredEnemyComponents(Ranged);
            AssertRequiredEnemyComponents(Elite);
        }

        [Test]
        public void EnemyPrefabs_HaveNoMissingScripts()
        {
            AssertNoMissingScripts(Melee);
            AssertNoMissingScripts(Ranged);
            AssertNoMissingScripts(Elite);
        }

        [Test]
        public void TestFormations_MatchSpec()
        {
            Assert.AreEqual(0, EnemyTestComposition.For(EnemyTestFormation.Clear).Total, "Clear は 0 体。");

            Assert.AreEqual(1, EnemyTestComposition.For(EnemyTestFormation.Melee1).Melee);
            Assert.AreEqual(1, EnemyTestComposition.For(EnemyTestFormation.Ranged1).Ranged);
            Assert.AreEqual(1, EnemyTestComposition.For(EnemyTestFormation.Elite1).Elite);

            EnemyTestComposition g = EnemyTestComposition.For(EnemyTestFormation.Group3);
            Assert.AreEqual(2, g.Melee);
            Assert.AreEqual(1, g.Ranged);
            Assert.AreEqual(3, g.Total, "3 体混成（近接2＋遠距離1）。");

            Assert.AreEqual(6, EnemyTestComposition.For(EnemyTestFormation.Melee6).Total);
            Assert.AreEqual(6, EnemyTestComposition.For(EnemyTestFormation.Mixed6).Total);
            Assert.AreEqual(4, EnemyTestComposition.For(EnemyTestFormation.Mixed6).Melee);
            Assert.AreEqual(2, EnemyTestComposition.For(EnemyTestFormation.Mixed6).Ranged);
            Assert.AreEqual(8, EnemyTestComposition.For(EnemyTestFormation.Max8).Total, "最大 8 体。");
        }

        [Test]
        public void EnemyArchetypeData_ValidatesWithoutErrors_AndUniqueStableIds()
        {
            var assets = new List<GameDataAsset>();
            foreach (string p in new[]
            {
                "Assets/_Project/Data/Enemies/SO_Enemy_Melee_Prototype.asset",
                "Assets/_Project/Data/Enemies/SO_Enemy_Ranged_Prototype.asset",
                "Assets/_Project/Data/Enemies/SO_Enemy_Elite_Prototype.asset",
                "Assets/_Project/Data/Enemies/SO_Enemy_GuardVariant.asset",
                "Assets/_Project/Data/Enemies/SO_Enemy_EvadeVariant.asset",
            })
            {
                var a = AssetDatabase.LoadAssetAtPath<GameDataAsset>(p);
                Assert.IsNotNull(a, "Data 資産が無い: " + p);
                assets.Add(a);
            }

            DataValidationReport report = ProjectDataValidator.Validate(assets);
            Assert.IsFalse(report.HasErrors,
                "敵 Archetype Data の検証エラー: " + string.Join(" | ", report.Errors));
        }
    }
}
