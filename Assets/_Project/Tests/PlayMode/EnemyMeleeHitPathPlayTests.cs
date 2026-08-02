using System.Collections;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-05 受入修正：実 Collider・実 Rigidbody・実 MonoBehaviour ライフサイクルで、敵の被弾により HP 減少・体幹0で Stunned・
    /// HP0で Down へ遷移し、EnemyBrain が被弾状態を上書きしないことを検証する。加えて主人公の攻撃 Hitbox と同一の
    /// <see cref="Physics.OverlapBox"/>→<c>GetComponentInParent&lt;IDamageable&gt;</c> 解決経路で敵を検出できることを検証する。
    /// 物理は既存 PlayMode テストに倣い手動シミュレーション（<see cref="SimulationMode.Script"/>＋<see cref="Physics.Simulate"/>）で
    /// 決定的に確認し、simulationMode を復元する。PF_Enemy_Melee_Prototype と同一構成を実行時に組む。
    /// </summary>
    public sealed class EnemyMeleeHitPathPlayTests
    {
        private GameObject _enemy;
        private SimulationMode _prevMode;

        [SetUp]
        public void SetUp()
        {
            _prevMode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script; // 手動ステップで Collider 登録・ブロードフェーズを決定的にする。
        }

        [TearDown]
        public void TearDown()
        {
            Physics.simulationMode = _prevMode;
            if (_enemy != null)
            {
                Object.Destroy(_enemy);
            }
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

        private static EnemyArchetypeData MakeArchetype()
        {
            var a = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            SetField(a, "_maxHp", 40);
            SetField(a, "_defense", 0f);
            SetField(a, "_poiseMax", 30f);
            SetField(a, "_flinchResistance", 20f);
            SetField(a, "_stunSeconds", 3f);
            return a;
        }

        private GameObject BuildEnemy()
        {
            var go = new GameObject("EnemyRuntime");
            go.transform.position = Vector3.zero;
            go.SetActive(false);

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            // 接地基準：全回転＋Y 位置固定（EnemyMotor.Awake が上書き設定するのと同じ 116）。
            rb.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;

            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            col.center = new Vector3(0f, 0.5f, 0f); // 原点直上 0..1（接地）。

            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", MakeArchetype());
            go.AddComponent<EnemyMotor>();
            go.AddComponent<EnemyBrain>();

            go.SetActive(true);
            return go;
        }

        // 解決経路（主人公 PollHitbox と同一）を決定的に検証する静的ターゲット（CombatDummy 相当）。
        private GameObject BuildStaticTarget()
        {
            var go = new GameObject("StaticTarget");
            go.transform.position = Vector3.zero;
            go.SetActive(false);
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            col.center = Vector3.zero;
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", MakeArchetype());
            go.SetActive(true);
            return go;
        }

        private static IDamageable ResolveViaOverlap(Vector3 center, Transform selfRootExclude)
        {
            Physics.SyncTransforms();
            var buffer = new Collider[16];
            int count = Physics.OverlapBoxNonAlloc(center, new Vector3(0.7f, 0.7f, 0.7f), buffer, Quaternion.identity, ~0,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                if (buffer[i] == null)
                {
                    continue;
                }

                var target = buffer[i].GetComponentInParent<IDamageable>();
                if (target != null && (!(target is Component tc) || tc.transform.root != selfRootExclude))
                {
                    return target;
                }
            }

            return null;
        }

        private static HitInfo Hit(IDamageable target, float hp, float poise, float flinch)
        {
            return new HitInfo(null, target, Vector3.forward, Vector3.zero, new HitDamage(hp, poise, flinch),
                guardable: true, justGuardable: true, HitId.Single(Random.Range(1, 1000000)));
        }

        [UnityTest]
        public IEnumerator RealLifecycle_HpDown_PoiseStun_ThenDown_StateHolds()
        {
            _enemy = BuildEnemy();
            yield return null; // Awake/OnEnable
            yield return null; // Update（Brain/vitals）

            var actor = _enemy.GetComponent<EnemyActor>();
            int hpStart = actor.CurrentHp;
            float poiseStart = actor.CurrentPoise;
            Assert.Greater(hpStart, 0);

            actor.ReceiveHit(Hit(actor, hp: 10f, poise: 0f, flinch: 0f));
            Assert.Less(actor.CurrentHp, hpStart, "被弾で HP が減る。");

            actor.ReceiveHit(Hit(actor, hp: 0f, poise: 50f, flinch: 0f));
            Assert.IsTrue(actor.IsStunned, "体幹0でスタン。");
            yield return null;
            yield return null;
            Assert.IsTrue(actor.IsStunned, "スタン維持。");
            Assert.AreEqual(EnemyState.Stunned, actor.State, "EnemyState=Stunned（Brain が上書きしない）。");
            Assert.Less(actor.CurrentPoise, poiseStart);

            actor.ReceiveHit(Hit(actor, hp: 9999f, poise: 0f, flinch: 0f));
            Assert.IsTrue(actor.IsDefeated, "HP0 で撃破。");
            yield return null;
            Assert.AreEqual(EnemyState.Down, actor.State, "EnemyState=Down。");
        }

        [UnityTest]
        public IEnumerator PlayerHitboxOverlap_ResolvesEnemyDamageable()
        {
            _enemy = BuildStaticTarget();
            yield return null;            // Awake/OnEnable（Collider 登録）
            Physics.Simulate(0.02f);      // 手動ステップでブロードフェーズを更新

            var actor = _enemy.GetComponent<EnemyActor>();
            // selfRootExclude は「攻撃者自身」を除外するためのもの。本テストに主人公は居ないので null（何も除外しない）。
            // 実 PollHitbox は主人公ルートを渡すため敵は除外されない。
            IDamageable target = ResolveViaOverlap(actor.WorldPosition, null);
            Assert.IsNotNull(target, "OverlapBox で敵 IDamageable を検出（解決経路成立）。");
            Assert.AreSame(actor, target, "検出対象は EnemyActor。");

            int hp0 = actor.CurrentHp;
            target.ReceiveHit(Hit(target, hp: 5f, poise: 0f, flinch: 0f));
            Assert.Less(actor.CurrentHp, hp0, "解決した IDamageable への被弾で HP が減る。");
        }

        [UnityTest]
        public IEnumerator FlinchAccumulation_ReachesStagger()
        {
            _enemy = BuildEnemy();
            yield return null;
            yield return null;

            var actor = _enemy.GetComponent<EnemyActor>();
            actor.ReceiveHit(Hit(actor, hp: 0f, poise: 0f, flinch: 25f));
            Assert.IsTrue(actor.IsFlinching, "耐性超過でひるみ。");
            yield return null;
            Assert.AreEqual(EnemyState.Stagger, actor.State, "EnemyState=Stagger（Hurt 表示に対応）。");
        }
    }
}
