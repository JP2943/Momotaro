using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-02：<see cref="EnemyPerception"/> の統合を決定的に検証する。直接視認→Alert＋警戒声発行、被弾即 Alert、
    /// 警戒共有→受信者は Suspicious かつ再共有しない（連鎖停止）を、公開シーム（EvaluateOnce/OnNoise/OnHitResult）で駆動する
    /// （EditMode では OnEnable/Update が走らないため）。LOS は Fake を注入し物理に依存しない。
    /// </summary>
    public sealed class EnemyPerceptionTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class AlwaysVisibleProbe : ILineOfSightProbe
        {
            public bool HasLineOfSight(Vector3 from, Vector3 to) => true;
        }

        private sealed class FakeTarget : IPerceptionTarget
        {
            public int ActorId { get; set; } = 999;
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;
        }

        private sealed class AlertVoiceCounter : INoiseListener
        {
            public int AlertVoices;
            public NoiseStimulus Last;
            public void OnNoise(in NoiseStimulus s)
            {
                if (s.Kind == NoiseKind.EnemyAlertVoice) { AlertVoices++; Last = s; }
            }
        }

        [SetUp]
        public void SetUp()
        {
            PerceptionTargetRegistry.Clear();
            NoiseBus.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            PerceptionTargetRegistry.Clear();
            NoiseBus.Reset();
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
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

        private EnemyPerception MakeEnemy(Vector3 position)
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch); // 既定値（視野120/通常8/警戒10/背後2/完全認識0.25/喪失3）を使う

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            go.transform.position = position;
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            var perception = go.AddComponent<EnemyPerception>();
            perception.SetLineOfSightProbe(new AlwaysVisibleProbe());
            return perception;
        }

        private static EnemyActor ActorOf(EnemyPerception p) => p.GetComponent<EnemyActor>();

        [Test]
        public void DirectSight_ReachesAlert_EmitsAlertVoice_AndReflectsState()
        {
            var voices = new AlertVoiceCounter();
            NoiseBus.Channel.AddListener(voices);

            var enemy = MakeEnemy(Vector3.zero);
            var target = new FakeTarget { Position = new Vector3(0, 0, 5f) }; // 正面
            PerceptionTargetRegistry.Register(target);

            enemy.EvaluateOnce(0.3f); // 0.3 >= 完全認識 0.25 → Alert

            Assert.AreEqual(PerceptionPhase.Alert, enemy.Phase);
            Assert.AreEqual(EnemyState.Alert, ActorOf(enemy).State, "認識結果が Actor 状態へ反映される。");
            Assert.AreEqual(1, voices.AlertVoices, "直接 Alert 化で警戒声を 1 回発行。");
            Assert.AreEqual(6f, voices.Last.Radius, 1e-4f, "警戒声の半径は 6.0。");
        }

        [Test]
        public void Hit_ImmediatelyAlerts_RegardlessOfSight()
        {
            var enemy = MakeEnemy(Vector3.zero);
            var actor = ActorOf(enemy);

            // 視線・音なしでも被弾で即 Alert（背後からの攻撃相当）。
            enemy.OnHitResult(HitResult.Damage(HitId.Single(1), null, actor, new HitDamage(5f, 0f, 0f)));

            Assert.AreEqual(PerceptionPhase.Alert, enemy.Phase);
            Assert.AreEqual(EnemyState.Alert, actor.State);
        }

        [Test]
        public void AlertShare_MakesReceiverSuspicious_AndDoesNotRechain()
        {
            var voices = new AlertVoiceCounter();
            NoiseBus.Channel.AddListener(voices);

            // A：直接視認で Alert → 警戒声発行（voices=1）。
            var a = MakeEnemy(Vector3.zero);
            var target = new FakeTarget { Position = new Vector3(0, 0, 5f) };
            PerceptionTargetRegistry.Register(target);
            a.EvaluateOnce(0.3f);
            Assert.AreEqual(1, voices.AlertVoices);
            NoiseStimulus shared = voices.Last;

            // B：A の警戒声（共有半径内）を受信 → Suspicious のみ（Alert にしない）、かつ再共有しない。
            var b = MakeEnemy(new Vector3(0, 0, 4f)); // 発生地点(0,0,5)から距離1 <= 6
            b.OnNoise(shared);

            Assert.AreEqual(PerceptionPhase.Suspicious, b.Phase, "共有受信は Suspicious 止まり（直接視認まで）。");
            Assert.AreEqual(EnemyState.Suspicious, ActorOf(b).State);
            Assert.AreEqual(1, voices.AlertVoices, "共有を受けた敵は再共有しない（連鎖は最大 1 回）。");
        }
    }
}
