using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Modes;
using Momotaro.Presentation.Companion;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// 仲間の演出接続を<b>実際のライフサイクルごと</b>検証する（N12。レビュー §3.2）。
    ///
    /// EditMode の寿命テストは <c>OnEnable</c>／<c>OnDisable</c> をリフレクションで呼んでいる。
    /// それは規則の固定には十分だが、「実機で本当にその順に呼ばれるか」は見ていない。
    /// ここでは Unity に本物の有効化をさせ、<b>生成した直後の一撃</b>が待ち時間なしで一度だけ演出へ届くことを見る。
    ///
    /// 周期スキャンで仲間を探していたときに落ちていたのが、まさにこの一撃だった。
    /// </summary>
    public sealed class CompanionFeedbackLifecyclePlayTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            // 静的状態は前のテストから持ち越される（PlayMode で実際に踏んだ事故）。
            GameModeProvider.Current = null;
            CompanionFeedbackRegistry.ClearAll();
        }

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
            CompanionFeedbackRegistry.ClearAll();
            GameModeProvider.Current = null;
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

        private sealed class FakeFeedback : ICombatFeedbackListener
        {
            public int Count { get; private set; }
            public HitResultKind LastKind { get; private set; }

            public void OnCombatFeedback(in CombatFeedbackEvent feedback)
            {
                Count++;
                LastKind = feedback.Result.Kind;
            }
        }

        private CombatFeedbackDispatcher MakeDispatcher()
        {
            var go = new GameObject("Dispatcher");
            _spawned.Add(go);
            return go.AddComponent<CombatFeedbackDispatcher>();
        }

        /// <summary>実機と同じ順で仲間を組む。PlayMode では <c>AddComponent</c> の時点で <c>Awake</c> が走るため、
        /// 非アクティブで組み立ててから起こす（Data 未設定のまま Runtime が確定するのを防ぐ）。</summary>
        private CompanionHitReceiver MakeCompanion(int maxHp = 80)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", maxHp);
            SetPrivateField(data, "_defense", 0f);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.SetActive(false);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(data);
            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);
            var binder = go.AddComponent<CompanionFeedbackBinder>();
            binder.Bind(receiver);

            go.SetActive(true); // ここで本物の OnEnable が走る。
            return receiver;
        }

        private static HitInfo Hit(IDamageable target, int instanceId, float hp = 10f)
        {
            return new HitInfo(
                null, target, Vector3.forward, Vector3.zero,
                new HitDamage(hp, 0f, 0f),
                guardable: false, justGuardable: false,
                hitId: HitId.Single(instanceId));
        }

        [UnityTest]
        public IEnumerator EnabledCompanion_FirstHitReachesFeedbackOnce()
        {
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            yield return null; // Dispatcher の OnEnable。

            var feedback = new FakeFeedback();
            dispatcher.Feedback.AddListener(feedback);

            CompanionHitReceiver companion = MakeCompanion();

            // 待たない。生成・有効化した「その場」で最初の一撃を入れる。
            // 周期スキャンで拾っていたころは、ここが次のスキャンまで無購読だった。
            companion.ReceiveHit(Hit(companion, instanceId: 71));

            Assert.AreEqual(1, feedback.Count,
                "有効化した直後の最初の命中が演出へ届く（再スキャンを待たない）。");
            Assert.AreEqual(HitResultKind.Damage, feedback.LastKind);

            yield return null;

            Assert.AreEqual(1, feedback.Count, "1 フレーム進めても重複配信しない。");
        }

        [UnityTest]
        public IEnumerator DisabledCompanion_StopsReachingFeedback()
        {
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            yield return null;

            var feedback = new FakeFeedback();
            dispatcher.Feedback.AddListener(feedback);

            CompanionHitReceiver companion = MakeCompanion();
            companion.ReceiveHit(Hit(companion, instanceId: 72));
            Assert.AreEqual(1, feedback.Count, "前提：繋がっている。");

            companion.gameObject.SetActive(false); // 本物の OnDisable。
            yield return null;

            companion.Results.Publish(HitResult.Damage(
                HitId.Single(73), null, companion, new HitDamage(5f, 0f, 0f)));

            Assert.AreEqual(1, feedback.Count,
                "無効化した仲間の結果は演出へ流れない（Dispatcher の再スキャン周期に依存しない）。");
        }

        /// <summary>
        /// 破棄の後始末を<b>実際のライフサイクル</b>で確認する。
        /// EditMode 側は <c>OnDestroy</c> を手で呼んで規則を固定しているので、本当に呼ばれるかはここで見る。
        /// </summary>
        [UnityTest]
        public IEnumerator DestroyedCompanion_LeavesNoRegistryResidue()
        {
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            yield return null;

            CompanionHitReceiver companion = MakeCompanion();
            yield return null;

            Assert.AreEqual(1, CompanionFeedbackRegistry.Count, "前提：登録されている。");
            HitResultChannel heldChannel = companion.Results;

            Object.DestroyImmediate(companion.gameObject);
            yield return null;

            Assert.AreEqual(0, CompanionFeedbackRegistry.Count, "破棄で登録が残らない。");
            Assert.AreEqual(0, heldChannel.ListenerCount, "破棄前に保持したチャネルの購読も外れる。");
            Assert.IsNotNull(dispatcher);
        }
    }
}
