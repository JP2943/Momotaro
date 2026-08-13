using System.Collections;
using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Defense;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-10：<see cref="PhysicsEnemyDangerSense"/> が「観測可能な危険」（プレイヤーの攻撃の予備動作／判定中＝
    /// <see cref="ICombatActivityState.IsPoiseVulnerableAction"/>）に反応し、入力そのものを読まないことを実 Collider で検証する。
    /// 攻撃していないプレイヤーや半径外は危険とみなさない。
    /// </summary>
    public sealed class EnemyDangerSensePlayTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject g in _spawned)
            {
                if (g != null) Object.DestroyImmediate(g);
            }

            _spawned.Clear();
        }

        private sealed class FakePlayer : MonoBehaviour, ICombatActor, ICombatActivityState
        {
            public bool Attacking;
            public CombatFaction Faction => CombatFaction.Player;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => transform.forward;
            public bool IsPoiseVulnerableAction => Attacking;
        }

        private FakePlayer MakePlayer(Vector3 pos)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.transform.position = pos;
            var col = go.AddComponent<BoxCollider>();
            col.size = Vector3.one;
            return go.AddComponent<FakePlayer>();
        }

        [UnityTest]
        public IEnumerator Sense_DetectsAttackingPlayer_Within_NotIdle_NotFar()
        {
            var sense = new PhysicsEnemyDangerSense(radius: 2.5f);
            FakePlayer player = MakePlayer(new Vector3(0, 0, 1.5f));
            yield return new WaitForFixedUpdate();

            // 攻撃していない（観測可能な危険なし）→ 検知しない（入力ではなく攻撃事象に反応する）。
            player.Attacking = false;
            EnemyDangerStimulus none = sense.Sense(Vector3.zero, Vector3.forward, selfDamageableId: 1);
            Assert.IsFalse(none.HasDanger, "非攻撃中のプレイヤーは危険としない。");

            // 攻撃中（予備動作／判定中）→ 危険を検知。進行方向は 危険源→自分（-Z 方向）。
            player.Attacking = true;
            EnemyDangerStimulus danger = sense.Sense(Vector3.zero, Vector3.forward, selfDamageableId: 1);
            Assert.IsTrue(danger.HasDanger, "攻撃中のプレイヤーを危険として観測。");
            Assert.Less(danger.IncomingDirection.z, 0f, "危険源→自分は -Z 方向。");

            // 半径外は攻撃中でも検知しない。
            player.transform.position = new Vector3(0, 0, 10f);
            yield return new WaitForFixedUpdate();
            EnemyDangerStimulus far = sense.Sense(Vector3.zero, Vector3.forward, selfDamageableId: 1);
            Assert.IsFalse(far.HasDanger, "半径外は危険としない。");
        }
    }
}
