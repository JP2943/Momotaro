using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// PF_Player_Momotaro の構造を検査する（Phase1 P1-01 受入）。
    /// Prefab が未作成のうちは Ignore し、作成後に構造を検証する。
    /// </summary>
    public sealed class PlayerPrefabStructureTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";

        private static GameObject LoadRequiredPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab,
                "PF_Player_Momotaro が見つかりません（" + PrefabPath + "）。P1-01 の手順で作成してください。");
            return prefab;
        }

        [Test]
        public void Prefab_HasPlayerRoot_WithValidStructure()
        {
            GameObject prefab = LoadRequiredPrefab();

            var root = prefab.GetComponent<PlayerRoot>();
            Assert.IsNotNull(root, "ルートに PlayerRoot コンポーネントが必要です。");

            bool ok = root.HasValidStructure(out string error);
            Assert.IsTrue(ok, "Prefab 構造が不正: " + error);
        }

        [Test]
        public void Prefab_RigidbodyRotation_IsFrozen()
        {
            GameObject prefab = LoadRequiredPrefab();

            var body = prefab.GetComponentInChildren<Rigidbody>();
            Assert.IsNotNull(body, "Rigidbody が必要です。");

            const RigidbodyConstraints freezeRotation =
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;
            Assert.AreEqual(freezeRotation, body.constraints & freezeRotation, "回転が固定されていません。");
        }

        [Test]
        public void Prefab_ColliderAndVisual_AreSeparated()
        {
            GameObject prefab = LoadRequiredPrefab();

            var collider = prefab.GetComponentInChildren<CapsuleCollider>();
            var root = prefab.GetComponent<PlayerRoot>();
            Assert.IsNotNull(collider, "CapsuleCollider が必要です。");
            Assert.IsNotNull(root, "PlayerRoot が必要です。");
            Assert.IsNotNull(root.VisualRoot, "VisualRoot が必要です。");
            Assert.AreNotEqual(collider.gameObject, root.VisualRoot.gameObject,
                "Collider と Visual は別 GameObject に分離してください。");
        }

        [Test]
        public void Prefab_HasNoMissingScripts()
        {
            GameObject prefab = LoadRequiredPrefab();

            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            foreach (Component c in components)
            {
                Assert.IsNotNull(c, "Missing Script が含まれています（いずれかのコンポーネント参照が壊れています）。");
            }
        }
    }
}
