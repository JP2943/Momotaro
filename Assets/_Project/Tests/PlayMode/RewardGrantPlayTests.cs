using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data.Characters;
using Momotaro.Data.Progression;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P4-00：撃破報酬の実行時ライフサイクルを検証する。実際の Awake/OnEnable/OnDisable 経路で購読・付与が行われること、
    /// 破棄後に購読が残らない（例外・二重付与を起こさない）ことを確認する。試遊 Scene への配線は EditMode の
    /// <c>Phase35CombatTrialValidatorTests</c>（生成直後の Scene が Validator を無エラーで通ること）で担保する。
    /// 数値規則そのものは EditMode（<c>PlayerProgressStateTests</c> ほか）で検証済みで、本テストは実行時の配管のみを見る。
    /// </summary>
    public sealed class RewardGrantPlayTests
    {
        private readonly List<UnityEngine.Object> _spawned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _spawned)
            {
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private sealed class FakeEnemy : IEnemyDefeatSource
        {
            public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();
            public int DamageableId { get; set; }
            public bool IsDefeated { get; set; }
            public RewardData Reward { get; set; }

            public void Kill()
            {
                Defeats.Publish(new EnemyDefeatedEvent(DamageableId,
                    new EnemyRewardRequest(DamageableId, EnemyRole.Melee, Reward, Vector3.zero)));
            }
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private RewardData MakeReward(string id, int virtue, bool grantOnce)
        {
            var data = ScriptableObject.CreateInstance<RewardData>();
            data.name = "SO_Reward_PlayTest";
            SetPrivateField(data, "_id", new StableId(id));
            SetPrivateField(data, "_displayName", "PlayMode Reward");
            SetPrivateField(data, "_virtueAmount", virtue);
            SetPrivateField(data, "_itemId", new StableId(null));
            SetPrivateField(data, "_grantOnce", grantOnce);
            _spawned.Add(data);
            return data;
        }

        private T MakeComponent<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [UnityTest]
        public IEnumerator Defeat_GrantsVirtue_ThroughRuntimeLifecycle()
        {
            CombatSessionController session = MakeComponent<CombatSessionController>("Session");
            PlayerProgressHolder progress = MakeComponent<PlayerProgressHolder>("PlayerProgress");
            CombatRewardCollector collector = MakeComponent<CombatRewardCollector>("RewardCollector");
            collector.Bind(session, progress);

            yield return null; // Awake/OnEnable を通す。

            var enemy = new FakeEnemy { DamageableId = 101, Reward = MakeReward("reward_play_melee", 10, false) };
            session.RegisterEnemy(enemy);

            enemy.Kill();
            yield return null;

            Assert.AreEqual(10, progress.Virtue, "実行時経路（OnEnable 購読）で徳が付与される。");
            Assert.AreEqual(1, collector.GrantedCount);
        }

        [UnityTest]
        public IEnumerator MissingReward_DoesNotThrow()
        {
            CombatSessionController session = MakeComponent<CombatSessionController>("Session");
            PlayerProgressHolder progress = MakeComponent<PlayerProgressHolder>("PlayerProgress");
            CombatRewardCollector collector = MakeComponent<CombatRewardCollector>("RewardCollector");
            collector.Bind(session, progress);

            yield return null;

            var enemy = new FakeEnemy { DamageableId = 102, Reward = null };
            session.RegisterEnemy(enemy);

            Assert.DoesNotThrow(() => enemy.Kill(), "報酬未設定の敵でも例外なく継続する。");
            yield return null;

            Assert.AreEqual(0, progress.Virtue);
            Assert.AreEqual(1, collector.NoRewardCount);
        }

        [UnityTest]
        public IEnumerator DestroyedCollector_LeavesNoSubscription()
        {
            CombatSessionController session = MakeComponent<CombatSessionController>("Session");
            PlayerProgressHolder progress = MakeComponent<PlayerProgressHolder>("PlayerProgress");
            CombatRewardCollector collector = MakeComponent<CombatRewardCollector>("RewardCollector");
            collector.Bind(session, progress);

            yield return null;

            var enemy = new FakeEnemy { DamageableId = 103, Reward = MakeReward("reward_play_melee", 10, false) };
            session.RegisterEnemy(enemy);

            UnityEngine.Object.Destroy(collector.gameObject);
            yield return null; // 破棄と OnDisable（購読解除）を確定させる。

            Assert.DoesNotThrow(() => enemy.Kill(), "破棄済み受け手への通知で例外を出さない（購読が残らない）。");
            yield return null;

            Assert.AreEqual(0, progress.Virtue, "破棄後は付与されない。");
        }
    }
}
