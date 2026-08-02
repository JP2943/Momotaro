using Momotaro.Gameplay.Enemy;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-05 受入修正：敵 Prefab の「物理・AI 用接地基準」を静的に検証する。ルート原点を地面（feet= 原点）に置き、
    /// BoxCollider は原点直上（0..1）へ、Rigidbody は全回転＋Y 位置を固定して押し出し由来の浮き上がりを防ぐ。VisualRoot は
    /// スプライトの足元（8px 透明余白＝0.08）が原点へ来るよう -0.08 に置き、Collider を持つルート・親階層に負の Scale／負の
    /// Collider Size を一切用いない（"BoxCollider does not support negative scale or size" 警告条件を排除）。SpriteRenderer は
    /// VisualRoot 配下に置き、向き・ビルボードは Visual 子だけで扱う（ルートは回さない）。
    /// </summary>
    public sealed class EnemyPrefabGroundingTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_Melee_Prototype.prefab";

        // 全回転固定（112）＋Y 位置固定（4）＝116。
        private const RigidbodyConstraints GroundedConstraints =
            RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

        private static GameObject LoadPrefab()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(go, "Prefab が見つからない: " + PrefabPath);
            return go;
        }

        private static Transform FindChild(GameObject root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        [Test]
        public void Root_OriginIsGrounded_AtLocalYZero()
        {
            GameObject prefab = LoadPrefab();
            Assert.AreEqual(0f, prefab.transform.localPosition.y, 1e-5f,
                "接地基準：ルート原点を地面（Y=0）に置く（実プレイで Y が高いと主人公攻撃が空振りする不具合を防ぐ）。");
        }

        [Test]
        public void BoxCollider_CentersAboveGround_WithPositiveSize()
        {
            GameObject prefab = LoadPrefab();
            var col = prefab.GetComponent<BoxCollider>();
            Assert.IsNotNull(col, "接地基準の BoxCollider がルートに必要。");

            Assert.AreEqual(new Vector3(0f, 0.5f, 0f), col.center, "Center は原点直上 0.5（size 1 と合わせ world 0..1）。");
            Assert.AreEqual(Vector3.one, col.size, "Size は (1,1,1)。");
            Assert.Greater(col.size.x, 0f, "負の Size 不可（警告条件）。");
            Assert.Greater(col.size.y, 0f, "負の Size 不可（警告条件）。");
            Assert.Greater(col.size.z, 0f, "負の Size 不可（警告条件）。");

            // ルート原点基準の Collider の縦スパンが地面（0）から始まる。
            float minY = col.center.y - col.size.y * 0.5f;
            float maxY = col.center.y + col.size.y * 0.5f;
            Assert.AreEqual(0f, minY, 1e-5f, "Collider 下端が地面（root 原点）に接する。");
            Assert.AreEqual(1f, maxY, 1e-5f, "Collider 上端は 1（立ち姿勢の胴）。");
        }

        [Test]
        public void Rigidbody_FreezesAllRotationAndY_PreventsFloatUp()
        {
            GameObject prefab = LoadPrefab();
            var rb = prefab.GetComponent<Rigidbody>();
            Assert.IsNotNull(rb, "Rigidbody が必要。");
            Assert.AreEqual(GroundedConstraints, rb.constraints,
                "全回転＋Y 位置を固定（=116）。押し出しによる浮き上がりと姿勢崩れを防ぐ。");
            Assert.IsFalse(rb.useGravity, "接地敵は重力を使わない（Y は固定）。");
        }

        [Test]
        public void VisualRoot_OffsetGroundsSpriteFeet_AtRootOrigin()
        {
            GameObject prefab = LoadPrefab();
            Transform visual = FindChild(prefab, "VisualRoot");
            Assert.IsNotNull(visual, "VisualRoot 子が必要。");
            // スプライトは BottomCenter ピボット、底部に 8px（=0.08 unit）の透明余白があるため -0.08 で足元が原点へ来る。
            Assert.AreEqual(-0.08f, visual.localPosition.y, 1e-4f,
                "VisualRoot は -0.08（スプライト足元＝ルート原点＝地面 に一致）。");
            Assert.AreEqual(0f, visual.localPosition.x, 1e-5f, "水平ズレ無し。");
            Assert.AreEqual(0f, visual.localPosition.z, 1e-5f, "奥行ズレ無し。");
        }

        [Test]
        public void SpriteRenderer_IsUnderVisualRoot_NotOnGroundedRoot()
        {
            GameObject prefab = LoadPrefab();
            Transform visual = FindChild(prefab, "VisualRoot");
            var sr = prefab.GetComponentInChildren<SpriteRenderer>(true);
            Assert.IsNotNull(sr, "SpriteRenderer が必要。");
            Assert.IsNotNull(visual, "VisualRoot が必要。");

            // SpriteRenderer は VisualRoot 配下（ビルボード・向きは Visual 子だけで扱い、接地ルートは回さない）。
            bool underVisual = false;
            for (Transform t = sr.transform; t != null; t = t.parent)
            {
                if (t == visual)
                {
                    underVisual = true;
                    break;
                }
            }

            Assert.IsTrue(underVisual, "SpriteRenderer は VisualRoot 配下に置く。");
            Assert.AreEqual(0, sr.flipX ? 1 : 0, "左右反転は 4 方向スプライトで表現（flipX/負 Scale を使わない）。");
        }

        [Test]
        public void NoNegativeScale_AnywhereInHierarchy()
        {
            GameObject prefab = LoadPrefab();
            foreach (Transform t in prefab.GetComponentsInChildren<Transform>(true))
            {
                Vector3 s = t.localScale;
                Assert.Greater(s.x, 0f, $"負の Scale.x を含む: {t.name}");
                Assert.Greater(s.y, 0f, $"負の Scale.y を含む: {t.name}");
                Assert.Greater(s.z, 0f, $"負の Scale.z を含む: {t.name}");
            }
        }
    }
}
