using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Scenes;
using Momotaro.Tests.Support;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P4-FIX-R2（レビュー R2-08）：活動 Context が<b>無い</b>Runtime は実際に止まり、<b>ある</b>Runtime は動く。
    ///
    /// 以前ここにあったのは「移行期フォールバックが出荷構成では使われない」の証明だったが、フォールバック自体を
    /// 撤去した（供給元が無ければ停止）。代わりに、実 Update 経路（Unity のフレーム進行）で
    /// 「Context 無し → 位置・ワープ・攻撃が動かない」「Context の破棄 → その場から止まる」を固定する。
    ///
    /// <b>2 本で 1 組。</b>止まることだけを確かめると、配線ミスで何も動いていない場合にも緑になる。
    /// 同じ組立で Context を置けば動く、という対になる 1 本を必ず置く。
    /// </summary>
    public sealed class CompanionActivityStopPlayTests : CompanionActivityFixture
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeModes : IGameModeService
        {
            public GameMode Current { get; set; } = GameMode.Exploration;

            public bool CanPause => true;

#pragma warning disable 67 // このテストでは発火しない。
            public event System.Action<GameModeChanged> ModeChanged;
#pragma warning restore 67

            public bool ChangeMode(GameMode next)
            {
                Current = next;
                return true;
            }

            public void AddListener(IGameModeListener listener)
            {
            }

            public void RemoveListener(IGameModeListener listener)
            {
            }
        }

        private sealed class Rig
        {
            public GameObject Leader;
            public GameObject Root;
            public CompanionActor Actor;
            public CompanionMotor Motor;
            public CompanionFollowController Follow;
            public CompanionCombatController Combat;
        }

        [SetUp]
        public void SetUp()
        {
            GameModeProvider.Current = new FakeModes();
            PerceptionTargetRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.Destroy(o);
                }
            }

            _spawned.Clear();
            CompanionActivityProvider.Current = null;
            GameModeProvider.Current = null;
            PerceptionTargetRegistry.Clear();
        }

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Type t = target.GetType();
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

        private CompanionData MakeData()
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_moveSpeed", 5f);
            return data;
        }

        /// <summary>活動 Context を置く（Builder が作る Scene と同じ形）。返すのは Context の GameObject。</summary>
        private GameObject InstallActivityContext()
        {
            var sessionGo = new GameObject("CombatSession");
            _spawned.Add(sessionGo);
            var session = sessionGo.AddComponent<CombatSessionController>();

            var contextGo = new GameObject("CompanionActivityContext");
            _spawned.Add(contextGo);
            contextGo.AddComponent<CompanionActivityContext>().Bind(session);
            return contextGo;
        }

        /// <summary>出荷される仲間と同じ駆動の顔ぶれで組む（Prefab は PlayMode から読めない）。主人公から 30m 離して置く。</summary>
        private Rig MakeCompanion()
        {
            var leader = new GameObject("Player");
            _spawned.Add(leader);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = new Vector3(0f, 0f, 30f);
            go.SetActive(false); // Data を差し込んでから起こす（PlayMode は AddComponent で Awake が走る）。

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData());

            var motor = go.AddComponent<CompanionMotor>();
            var follow = go.AddComponent<CompanionFollowController>();
            follow.Bind(leader.transform, actor, motor);

            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            var combat = go.AddComponent<CompanionCombatController>();
            combat.Bind(actor, motor, tracker);

            CompanionHitReceiver receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);
            go.AddComponent<CompanionDefenseController>().Bind(actor);
            go.AddComponent<CompanionGuardianController>().Bind(actor, receiver, leader.transform);

            go.SetActive(true);
            return new Rig { Leader = leader, Root = go, Actor = actor, Motor = motor, Follow = follow, Combat = combat };
        }

        /// <summary>対になる 1 本：Context が置かれた構成では追従が動く（30m 離れているのでワープして戻る）。</summary>
        [UnityTest]
        public IEnumerator WiredScene_RunsUnderTheContext()
        {
            InstallActivityContext();
            Rig rig = MakeCompanion();

            yield return null;
            Assert.IsTrue(CompanionActivityProvider.Current is CompanionActivityContext, "前提：実 Context が供給元になっている。");

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.Greater(rig.Motor.WarpCount, 0, "配線済みなら追従が動く（距離超過でワープする）。");
            Assert.Less(rig.Actor.WorldPosition.z, 20f, "隊列位置へ戻っている。");
        }

        /// <summary>Context が無ければ、実フレームが進んでも位置・ワープ・状態が一切動かない。</summary>
        [UnityTest]
        public IEnumerator WithoutContext_RuntimeStaysStopped()
        {
            CompanionActivityProvider.Current = null; // 共通 Fixture の Fake も外す＝供給元が無い。
            Rig rig = MakeCompanion();
            Vector3 start = rig.Actor.WorldPosition;

            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.IsNull(CompanionActivityProvider.Current, "前提：供給元が居ない。");
            Assert.AreEqual(0, rig.Motor.WarpCount, "供給元が無ければワープしない。");
            Assert.AreEqual(start.z, rig.Actor.WorldPosition.z, 1e-3f, "供給元が無ければ動かない。");
            Assert.IsFalse(rig.Motor.HasMoveTarget, "移動指示も残っていない。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "状態も動かさない。");
        }

        /// <summary>動いている最中に Context が破棄されたら、その場で止まる（許可側へ戻らない）。</summary>
        [UnityTest]
        public IEnumerator ContextDestroyedWhileRunning_StopsInPlace()
        {
            GameObject contextGo = InstallActivityContext();
            Rig rig = MakeCompanion();
            yield return null;
            yield return null;
            Assert.Greater(rig.Motor.WarpCount, 0, "前提：配線済みで動いている。");

            // 隊列位置へ戻ったあと、主人公を離す → 追従が歩き出すはず。その直前に Context を壊す。
            rig.Leader.transform.position = new Vector3(0f, 0f, 6f);
            Object.Destroy(contextGo);
            yield return null; // 破棄が確定するフレーム。

            Assert.IsNull(CompanionActivityProvider.Current, "破棄で供給元が外れている。");
            yield return null; // 停止ゲートが 1 度は通り、速度も消えたあとの位置を基準にする。
            Vector3 frozen = rig.Actor.WorldPosition;
            for (int i = 0; i < 10; i++)
            {
                yield return null;
            }

            Assert.AreEqual(frozen.z, rig.Actor.WorldPosition.z, 1e-3f, "Context を失った瞬間から動かない。");
            Assert.IsFalse(rig.Motor.HasMoveTarget);
        }
    }
}
