using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-06：敵剣閃の敵タイプ鍵（SlashVfxKey）が archetype（<see cref="EnemyArchetypeData"/>）駆動で解決されることを検証する。
    /// <see cref="EnemyAttackController"/> は archetype の鍵を優先し、未設定（空）のときのみ自身の直列化値へフォールバックする。
    /// これにより侍骸骨（Elite archetype）を "Medium" にするだけで強・ガード不能斬撃の素材が引き当たる。戦闘挙動には影響しない。
    /// </summary>
    public sealed class EnemySlashVfxKeyTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private static void SetField(object target, string name, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private EnemyArchetypeData MakeArchetype(string key)
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(a);
            SetField(a, "_maxHp", 100);
            SetField(a, "_defense", 0f);
            SetField(a, "_poiseMax", 100f);
            SetField(a, "_flinchResistance", 60f);
            SetField(a, "_slashVfxKey", key);
            return a;
        }

        private EnemyAttackController MakeController(EnemyArchetypeData archetype, string serializedFallback)
        {
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.SetActive(false); // Awake を走らせず、_actor を直接注入して getter のみ検証する。
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", archetype);
            var ctrl = go.AddComponent<EnemyAttackController>();
            SetField(ctrl, "_actor", actor);
            if (serializedFallback != null)
            {
                SetField(ctrl, "_slashVfxKey", serializedFallback);
            }

            return ctrl;
        }

        [Test]
        public void Archetype_SlashVfxKey_ReturnsSetValue()
        {
            EnemyArchetypeData a = MakeArchetype("Medium");
            Assert.AreEqual("Medium", a.SlashVfxKey, "archetype の鍵を返す。");
        }

        [Test]
        public void Controller_PrefersArchetypeKey()
        {
            EnemyAttackController ctrl = MakeController(MakeArchetype("Medium"), "Small");
            Assert.AreEqual("Medium", ctrl.SlashVfxKey, "鍵は archetype を優先する（侍骸骨=Medium）。");
        }

        [Test]
        public void Controller_FallsBackToSerialized_WhenArchetypeKeyEmpty()
        {
            EnemyAttackController ctrl = MakeController(MakeArchetype(string.Empty), "FallbackKey");
            Assert.AreEqual("FallbackKey", ctrl.SlashVfxKey, "archetype 未設定（空）なら直列化値へフォールバック。");
        }
    }
}
