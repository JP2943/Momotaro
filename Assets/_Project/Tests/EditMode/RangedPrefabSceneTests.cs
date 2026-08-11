using System.Reflection;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Presentation.Diagnostics;
using Momotaro.Presentation.Enemy;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-08 受入修正：遠距離 Prefab／矢 Prefab／検証 Scene の参照健全性（§9.2）。必要 Component、矢の 4 方向スプライト参照、
    /// Launcher→矢 Prefab 参照、負 Scale 不使用、接地規約（Root Collider）の近接一致、Scene の Missing 参照無しと警告サービス配置を検証する。
    /// </summary>
    public sealed class RangedPrefabSceneTests
    {
        private const string Ranged = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Ranged_Prototype.prefab";
        private const string Melee = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";
        private const string Arrow = "Assets/_Project/Prefabs/Enemies/PF_Enemy_ArrowProjectile.prefab";
        private const string Scene = "Assets/_Project/Scenes/SCN_VS_Field.unity";

        private static GameObject Load(string p)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            Assert.IsNotNull(go, "Prefab が見つからない: " + p);
            return go;
        }

        private static object GetField(object t, string n)
        {
            FieldInfo f = t.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + n);
            return f.GetValue(t);
        }

        [Test]
        public void RangedPrefab_HasRequiredComponents()
        {
            GameObject go = Load(Ranged);
            Assert.IsNotNull(go.GetComponentInChildren<EnemyActor>(true), "EnemyActor");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyMotor>(true), "EnemyMotor");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyBrain>(true), "EnemyBrain");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyAttackController>(true), "EnemyAttackController");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyPerception>(true), "EnemyPerception");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyThreatTracker>(true), "EnemyThreatTracker");
            Assert.IsNotNull(go.GetComponentInChildren<EnemyProjectileLauncher>(true), "EnemyProjectileLauncher");
            Assert.IsNotNull(go.GetComponentInChildren<Rigidbody>(true), "Rigidbody");
            Assert.IsNotNull(go.GetComponentInChildren<BoxCollider>(true), "BoxCollider");
        }

        [Test]
        public void RangedLauncher_ReferencesValidProjectilePrefab()
        {
            var launcher = Load(Ranged).GetComponentInChildren<EnemyProjectileLauncher>(true);
            var proj = GetField(launcher, "_projectilePrefab") as EnemyProjectile;
            Assert.IsNotNull(proj, "Launcher の projectile prefab 参照が有効。");
            Assert.IsNotNull(proj.GetComponent<EnemyProjectile>(), "参照先は EnemyProjectile を持つ。");
        }

        [Test]
        public void ArrowPrefab_Has4DirectionSprites_AndNoRootRotation()
        {
            GameObject go = Load(Arrow);
            Assert.IsNotNull(go.GetComponentInChildren<EnemyProjectile>(true), "EnemyProjectile");
            var adapter = go.GetComponentInChildren<EnemyProjectileVisualAdapter>(true);
            Assert.IsNotNull(adapter, "EnemyProjectileVisualAdapter");

            var sprites = new[]
            {
                GetField(adapter, "_down") as Sprite,
                GetField(adapter, "_up") as Sprite,
                GetField(adapter, "_left") as Sprite,
                GetField(adapter, "_right") as Sprite,
            };
            foreach (Sprite s in sprites)
            {
                Assert.IsNotNull(s, "4 方向スプライトが全て設定されている。");
            }

            Assert.AreEqual(4, new System.Collections.Generic.HashSet<Sprite>(sprites).Count, "4 方向は互いに異なる。");

            // Gameplay Root は回転させない（identity）。
            Assert.AreEqual(Quaternion.identity, go.transform.localRotation, "Root は無回転。");
        }

        [Test]
        public void Prefabs_HaveNoNegativeScale()
        {
            foreach (string p in new[] { Ranged, Arrow })
            {
                foreach (Transform t in Load(p).GetComponentsInChildren<Transform>(true))
                {
                    Vector3 s = t.localScale;
                    Assert.GreaterOrEqual(s.x, 0f, "負 Scale 不使用(x): " + p + "/" + t.name);
                    Assert.GreaterOrEqual(s.y, 0f, "負 Scale 不使用(y): " + p + "/" + t.name);
                    Assert.GreaterOrEqual(s.z, 0f, "負 Scale 不使用(z): " + p + "/" + t.name);
                }
            }
        }

        [Test]
        public void RangedGrounding_MatchesMelee_RootCollider()
        {
            var mc = Load(Melee).GetComponent<BoxCollider>();
            var rc = Load(Ranged).GetComponent<BoxCollider>();
            Assert.IsNotNull(mc, "melee root collider");
            Assert.IsNotNull(rc, "ranged root collider");
            Assert.AreEqual(mc.center, rc.center, "Collider center が近接と一致（接地規約）。");
            Assert.AreEqual(mc.size, rc.size, "Collider size が近接と一致。");
        }

        [Test]
        public void Scene_HasNoMissingScripts_AndWarningService()
        {
            Scene s = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Additive);
            try
            {
                bool hasWarning = false;
                foreach (GameObject root in s.GetRootGameObjects())
                {
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    {
                        Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                            "Scene に Missing Script がある（参照切れ）: " + t.name);
                    }

                    if (root.GetComponentInChildren<EnemyEdgeWarningView>(true) != null)
                    {
                        hasWarning = true;
                    }
                }

                Assert.IsTrue(hasWarning, "画面端警告サービス（EnemyEdgeWarningView）が Scene に配置されている。");
            }
            finally
            {
                EditorSceneManager.CloseScene(s, true);
            }
        }

        [Test]
        public void Scene_ContainsRangedEnemyInstance_FromPrefab_Grounded_NoMissing_NoNegScale()
        {
            Scene s = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Additive);
            try
            {
                EnemyProjectileLauncher launcher = null; // 遠距離敵にのみ付く＝遠距離インスタンスの目印。
                foreach (GameObject root in s.GetRootGameObjects())
                {
                    launcher = root.GetComponentInChildren<EnemyProjectileLauncher>(true);
                    if (launcher != null)
                    {
                        break;
                    }
                }

                Assert.IsNotNull(launcher, "SCN_VS_Field に遠距離敵（EnemyProjectileLauncher 保持）が配置されている。");
                GameObject inst = launcher.gameObject;

                // 名前一致ではなく Prefab 参照元を確認する。
                Object source = PrefabUtility.GetCorrespondingObjectFromSource(inst);
                Assert.IsNotNull(source, "遠距離敵は Prefab インスタンスである。");
                string srcPath = AssetDatabase.GetAssetPath(source);
                StringAssert.Contains("PF_Enemy_Ranged_Prototype", srcPath, "参照元は PF_Enemy_Ranged_Prototype。");

                // Missing Script 無し（インスタンス配下）。
                foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
                {
                    Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject),
                        "遠距離敵に Missing Script: " + t.name);
                    Vector3 sc = t.localScale;
                    Assert.IsTrue(sc.x >= 0f && sc.y >= 0f && sc.z >= 0f, "負スケール不使用: " + t.name);
                }

                // Launcher→投射物 Prefab 参照が有効。
                var proj = GetField(launcher, "_projectilePrefab") as EnemyProjectile;
                Assert.IsNotNull(proj, "Launcher の投射物 Prefab 参照が有効。");

                // 接地規約：ルート world Y = 0。
                Assert.AreEqual(0f, inst.transform.position.y, 1e-3f, "遠距離敵の接地 Y=0。");
            }
            finally
            {
                EditorSceneManager.CloseScene(s, true);
            }
        }
    }
}
