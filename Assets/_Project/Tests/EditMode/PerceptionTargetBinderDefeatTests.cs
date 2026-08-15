using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-02：プレイヤー死亡で <see cref="PerceptionTargetBinder"/> が非活動（IsActive=false）・ダウン（IsDown=true）を報告し、
    /// <see cref="PerceptionTargetRegistry"/> の敵対対象収集から除外されることを検証する。これが、既存の EnemyThreatTable／
    /// EnemyAttackController（IsActive/IsDown で対象を即時無効化する契約）に接続し、敵の新規追跡・攻撃を止める根拠になる（仕様書 §4.1）。
    /// </summary>
    public sealed class PerceptionTargetBinderDefeatTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => PerceptionTargetRegistry.Clear();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
            PerceptionTargetRegistry.Clear();
        }

        private sealed class FakeDefeatState : MonoBehaviour, IPlayerDefeatState
        {
            public bool Defeated;
            public bool IsDefeated => Defeated;
        }

        private (PerceptionTargetBinder binder, FakeDefeatState defeat) MakePlayerTarget()
        {
            var go = new GameObject("PlayerTarget");
            _spawned.Add(go);
            var defeat = go.AddComponent<FakeDefeatState>();
            var binder = go.AddComponent<PerceptionTargetBinder>(); // 既定 faction=Player。OnEnable で Registry へ登録。
            return (binder, defeat);
        }

        [Test]
        public void Alive_IsActiveTrue_IsDownFalse_AndCollectedAsHostile()
        {
            var (binder, defeat) = MakePlayerTarget();
            defeat.Defeated = false;

            Assert.IsTrue(binder.IsActive, "生存中は感知有効。");
            Assert.IsFalse(binder.IsDown, "生存中は Down でない。");

            var buffer = new List<IThreatTarget>();
            PerceptionTargetRegistry.CollectHostileThreatTargets(Vector3.zero, CombatFaction.Enemy, 0f, buffer);
            Assert.Contains(binder, buffer, "敵は生存プレイヤーを脅威対象として収集する。");

            Assert.IsTrue(
                PerceptionTargetRegistry.TryGetNearestHostile(Vector3.zero, CombatFaction.Enemy, out _),
                "敵は生存プレイヤーを最寄り敵対対象として取得できる。");
        }

        [Test]
        public void Defeated_IsActiveFalse_IsDownTrue_AndExcludedFromCollection()
        {
            var (binder, defeat) = MakePlayerTarget();
            defeat.Defeated = true;

            Assert.IsFalse(binder.IsActive, "死亡で非活動（感知対象から除外）。");
            Assert.IsTrue(binder.IsDown, "死亡で Down（脅威 0・即時切替）。");

            var buffer = new List<IThreatTarget>();
            PerceptionTargetRegistry.CollectHostileThreatTargets(Vector3.zero, CombatFaction.Enemy, 0f, buffer);
            Assert.IsFalse(buffer.Contains(binder), "敵は死亡プレイヤーを脅威対象に収集しない。");

            Assert.IsFalse(
                PerceptionTargetRegistry.TryGetNearestHostile(Vector3.zero, CombatFaction.Enemy, out _),
                "敵は死亡プレイヤーを新規に捕捉しない。");
        }

        [Test]
        public void NoDefeatStateProvider_StaysActive()
        {
            // 死亡状態提供が無い対象（将来の仲間の初期など）は従来どおり有効・非 Down。
            var go = new GameObject("PlainTarget");
            _spawned.Add(go);
            var binder = go.AddComponent<PerceptionTargetBinder>();

            Assert.IsTrue(binder.IsActive);
            Assert.IsFalse(binder.IsDown);
        }
    }
}
