using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Presentation.Enemy;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-08 受入修正：投射物スプライト切替の回帰（§9.2）。実 <b>矢 Prefab</b> をインスタンス化し、<see cref="EnemyProjectile.Initialize"/> に
    /// 各方向を渡すと、<see cref="IProjectileVisual.OnProjectileLaunched"/> 経由で <see cref="SpriteRenderer.sprite"/> が対応する 4 方向スプライトへ
    /// 即時に切り替わること、Gameplay Root の回転が identity のままであることを検証する（Pick／量子化の単体テストだけでなく実経路を確認）。
    /// </summary>
    public sealed class ArrowSpriteSwitchTests
    {
        private const string ArrowPath = "Assets/_Project/Prefabs/Enemies/PF_Enemy_ArrowProjectile.prefab";

        private GameObject _instance;

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
        }

        private static object GetField(object t, string n)
        {
            FieldInfo f = t.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + n);
            return f.GetValue(t);
        }

        private static EnemyAttackSnapshot Snapshot()
        {
            var d = ScriptableObject.CreateInstance<EnemyAttackData>();
            SetPrivate(d, "_attackClass", EnemyAttackClass.Projectile);
            SetPrivate(d, "_projectileSpeed", 10f);
            SetPrivate(d, "_projectileMaxDistance", 20f);
            SetPrivate(d, "_projectileLifetimeSeconds", 3f);
            EnemyAttackSnapshot s = EnemyAttackSnapshot.From(d);
            Object.DestroyImmediate(d);
            return s;
        }

        private static void SetPrivate(object t, string n, object v)
        {
            FieldInfo f = t.GetType().GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "field not found: " + n);
            f.SetValue(t, v);
        }

        [Test]
        public void Initialize_SwitchesSpriteRendererPerDirection_RootStaysIdentity()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPath);
            Assert.IsNotNull(prefab, "矢 Prefab が見つからない: " + ArrowPath);
            _instance = Object.Instantiate(prefab);

            var proj = _instance.GetComponentInChildren<EnemyProjectile>(true);
            var adapter = _instance.GetComponentInChildren<EnemyProjectileVisualAdapter>(true);
            var renderer = _instance.GetComponentInChildren<SpriteRenderer>(true);
            Assert.IsNotNull(proj, "EnemyProjectile");
            Assert.IsNotNull(adapter, "EnemyProjectileVisualAdapter");
            Assert.IsNotNull(renderer, "SpriteRenderer");

            Sprite down = GetField(adapter, "_down") as Sprite;
            Sprite up = GetField(adapter, "_up") as Sprite;
            Sprite left = GetField(adapter, "_left") as Sprite;
            Sprite right = GetField(adapter, "_right") as Sprite;

            EnemyAttackSnapshot snap = Snapshot();

            // -Z = Down（手前）
            proj.Initialize(snap, Vector3.zero, new Vector3(0, 0, -1), null, 0f, HitId.Single(1));
            Assert.AreSame(down, renderer.sprite, "Down 方向で down スプライト。");

            // +Z = Up（奥）
            proj.Initialize(snap, Vector3.zero, new Vector3(0, 0, 1), null, 0f, HitId.Single(2));
            Assert.AreSame(up, renderer.sprite, "Up 方向で up スプライト。");

            // +X = Right
            proj.Initialize(snap, Vector3.zero, new Vector3(1, 0, 0), null, 0f, HitId.Single(3));
            Assert.AreSame(right, renderer.sprite, "Right 方向で right スプライト。");

            // -X = Left
            proj.Initialize(snap, Vector3.zero, new Vector3(-1, 0, 0), null, 0f, HitId.Single(4));
            Assert.AreSame(left, renderer.sprite, "Left 方向で left スプライト。");

            Assert.AreEqual(Quaternion.identity, _instance.transform.localRotation, "Gameplay Root は identity のまま。");
        }
    }
}
