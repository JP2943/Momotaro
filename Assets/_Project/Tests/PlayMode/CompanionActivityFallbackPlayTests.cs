using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P4-08R：移行期フォールバックが<b>出荷される構成では通らない</b>ことを実際に確かめる。
    ///
    /// <see cref="CompanionActivityProvider.Fallback"/> は残してある（停止側へ倒すと、自前で配線している
    /// 大量の EditMode テストが一斉に動かなくなるため）。残す以上、「Builder が作る Scene では使われない」を
    /// 言葉ではなく機械で示す必要がある。ここがその証明。
    ///
    /// <b>2 本で 1 組。</b>配線済みで 0 件であることだけを確かめると、計数そのものが動いていない場合に
    /// 何も検証していないのに緑になる。配線を外せば実際に増える、という対になる 1 本を必ず置く。
    /// </summary>
    public sealed class CompanionActivityFallbackPlayTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        /// <summary>モードの正本役（このテストが読むのは <see cref="Current"/> だけ）。</summary>
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

        [SetUp]
        public void SetUp()
        {
            CompanionActivityProvider.Current = null;
            CompanionActivityProvider.ResetFallbackCount();
            GameModeProvider.Current = new FakeModes();
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
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
            CompanionActivityProvider.ResetFallbackCount();
            GameModeProvider.Current = null;
            PerceptionTargetRegistry.Clear();
            InvestigationPointRegistry.Clear();
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
            SetPrivateField(data, "_canInvestigate", true);
            SetPrivateField(data, "_investigateRange", 6f);
            SetPrivateField(data, "_investigateLeashDistance", 6f);
            return data;
        }

        /// <summary>活動 Context を置く（Builder が作る Scene と同じ形）。</summary>
        private void InstallActivityContext()
        {
            var sessionGo = new GameObject("CombatSession");
            _spawned.Add(sessionGo);
            var session = sessionGo.AddComponent<CombatSessionController>();

            var contextGo = new GameObject("CompanionActivityContext");
            _spawned.Add(contextGo);
            contextGo.AddComponent<CompanionActivityContext>().Bind(session);
        }

        /// <summary>
        /// 出荷される仲間と同じ駆動一式を載せる。
        /// PlayMode からは AssetDatabase が使えないので Prefab そのものは読めないが、
        /// <b>載っている駆動の顔ぶれ</b>は Prefab Builder と同じにしてある。
        /// </summary>
        private void MakeCompanion()
        {
            var leader = new GameObject("Player");
            _spawned.Add(leader);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.SetActive(false); // Data を差し込んでから起こす（PlayMode は AddComponent で Awake が走る）。

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData());

            var motor = go.AddComponent<CompanionMotor>();
            var follow = go.AddComponent<CompanionFollowController>();
            follow.Bind(leader.transform, actor, motor);

            var tracker = go.AddComponent<CompanionTargetTracker>();
            tracker.Bind(actor);
            go.AddComponent<CompanionCombatController>().Bind(actor, motor, tracker);

            CompanionHitReceiver receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);
            go.AddComponent<CompanionDefenseController>().Bind(actor);
            go.AddComponent<CompanionGuardianController>().Bind(actor, receiver, leader.transform);
            go.AddComponent<CompanionInvestigationController>().Bind(actor, follow);
            go.AddComponent<CompanionOrders>();

            go.SetActive(true);
        }

        /// <summary>
        /// <b>活動 Context が置かれた構成では、フォールバックが 1 度も使われない。</b>
        /// Builder が作る Scene はこの形なので、移行期の経路は出荷される構成には残っていない。
        /// </summary>
        [UnityTest]
        public IEnumerator WiredScene_NeverUsesTheTransitionalFallback()
        {
            InstallActivityContext();
            MakeCompanion();

            yield return null; // 有効化と OnEnable を通す。

            Assert.IsNotNull(CompanionActivityProvider.Current, "前提：活動 Context が供給元になっている。");

            // 立ち上がりの 1 フレーム（Context の OnEnable より前に走った Update）は数えない。
            // 見たいのは「動いているあいだ通らない」ことであって、初期化順ではない。
            CompanionActivityProvider.ResetFallbackCount();

            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            Assert.AreEqual(0, CompanionActivityProvider.FallbackCount,
                "配線済みの構成では移行期フォールバックを 1 度も通らない。");
        }

        /// <summary>
        /// 対になる 1 本：活動 Context を置かなければ、フォールバックは<b>実際に</b>使われる。
        /// これが無いと、上のテストは計数が動いていないだけでも緑になる。
        /// </summary>
        [UnityTest]
        public IEnumerator WithoutContext_TheFallbackIsActuallyUsed()
        {
            MakeCompanion(); // 活動 Context を置かない。

            yield return null;
            CompanionActivityProvider.ResetFallbackCount();

            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.IsNull(CompanionActivityProvider.Current, "前提：供給元が居ない。");
            Assert.Greater(CompanionActivityProvider.FallbackCount, 0,
                "Context が無ければフォールバックを通る（計数が機能していることの裏取り）。");
        }
    }
}
