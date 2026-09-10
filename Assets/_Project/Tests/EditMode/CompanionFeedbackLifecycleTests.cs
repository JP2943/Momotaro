using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Presentation.Companion;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 仲間の演出接続の<b>寿命</b>を検証する（N09・N10。レビュー §3.2）。
    ///
    /// <c>CombatFeedbackDispatcherTests</c> が見ているのは Dispatcher 側の購読管理で、
    /// <b>仲間自身</b>が無効化・破棄されたときの解除は別の話である。以前はそこが混同されていて、
    /// 「仲間の Disable を検査した」つもりで Dispatcher の Disable を呼んでいた。
    ///
    /// 特に危ないのが破棄の経路。GameObject が破棄されても<b>結果チャネルはただの C# オブジェクト</b>なので、
    /// 誰かが参照を持っていれば生き続ける。そこへ通知すると、解除できていない購読者に届いてしまう
    /// （＝次の Scene の演出として出る）。破棄済み判定で飛ばす作りでは、これを塞げない。
    /// </summary>
    public sealed class CompanionFeedbackLifecycleTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp() => CompanionFeedbackRegistry.ClearAll();

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

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        private sealed class FakeFeedback : ICombatFeedbackListener
        {
            public int Count { get; private set; }
            public void OnCombatFeedback(in CombatFeedbackEvent feedback) => Count++;
        }

        private sealed class Rig
        {
            public GameObject Root;
            public CompanionHitReceiver Receiver;
            public CompanionFeedbackBinder Binder;
        }

        private Rig MakeCompanion()
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", 80);
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
            go.SetActive(true);
            InvokePrivate(binder, "OnEnable"); // EditMode では自動で走らない。

            return new Rig { Root = go, Receiver = receiver, Binder = binder };
        }

        private CombatFeedbackDispatcher MakeDispatcher()
        {
            var go = new GameObject("Dispatcher");
            _spawned.Add(go);
            var dispatcher = go.AddComponent<CombatFeedbackDispatcher>();
            dispatcher.Rescan(); // EditMode の OnEnable 代わり（登録所と繋ぐ）。
            return dispatcher;
        }

        /// <summary>
        /// 実機と同じ順で仲間を破棄する。
        /// <b>EditMode では Unity が <c>OnDisable</c>／<c>OnDestroy</c> を呼ばない</b>ので、明示的に走らせる
        /// （<c>OnEnable</c> を手で呼んでいるのと同じ理由。実際の呼び出し順そのものは PlayMode 側で検証する）。
        /// </summary>
        private static void DestroyCompanion(Rig companion)
        {
            InvokePrivate(companion.Binder, "OnDisable");
            InvokePrivate(companion.Binder, "OnDestroy");
            Object.DestroyImmediate(companion.Root);
        }

        private static HitResult Damage(IDamageable target, int id)
        {
            return HitResult.Damage(HitId.Single(id), null, target, new HitDamage(6f, 0f, 0f));
        }

        // ---- N09：仲間自身の無効化 ----

        [Test]
        public void CompanionDisable_RemovesSubscription()
        {
            Rig companion = MakeCompanion();
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            var feedback = new FakeFeedback();
            dispatcher.Feedback.AddListener(feedback);

            Assert.AreEqual(1, companion.Receiver.Results.ListenerCount, "前提：有効な仲間は繋がっている。");

            // 仲間自身を無効化する（Dispatcher は動いたまま）。
            InvokePrivate(companion.Binder, "OnDisable");

            Assert.AreEqual(0, companion.Receiver.Results.ListenerCount,
                "仲間を無効化したその瞬間に外れる（次の再スキャンを待たない）。");

            companion.Receiver.Results.Publish(Damage(companion.Receiver, 61));

            Assert.AreEqual(0, feedback.Count,
                "無効化した仲間の旧チャネルへ通知しても演出は出ない。");
        }

        [Test]
        public void CompanionReEnable_DoesNotDuplicateSubscription()
        {
            Rig companion = MakeCompanion();
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            var feedback = new FakeFeedback();
            dispatcher.Feedback.AddListener(feedback);

            InvokePrivate(companion.Binder, "OnDisable");
            InvokePrivate(companion.Binder, "OnEnable");
            InvokePrivate(companion.Binder, "OnEnable"); // 二重呼び出しにも耐える。

            Assert.AreEqual(1, companion.Receiver.Results.ListenerCount, "再有効化で二重購読しない。");

            companion.Receiver.Results.Publish(Damage(companion.Receiver, 62));

            Assert.AreEqual(1, feedback.Count, "通知は 1 回だけ。");
        }

        // ---- N10：破棄前に保持されたチャネル ----

        [Test]
        public void CompanionDestroy_RemovesOldChannelSubscription()
        {
            Rig companion = MakeCompanion();
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            var feedback = new FakeFeedback();
            dispatcher.Feedback.AddListener(feedback);

            // 破棄「前」にチャネルへの参照を持っておく。GameObject が消えても、このオブジェクトは生き続ける。
            HitResultChannel heldChannel = companion.Receiver.Results;
            Assert.AreEqual(1, heldChannel.ListenerCount, "前提：繋がっている。");

            DestroyCompanion(companion);

            Assert.AreEqual(0, heldChannel.ListenerCount,
                "破棄で購読が外れる。破棄済み判定で飛ばす作りだと、ここに購読が残ってしまう。");

            heldChannel.Publish(Damage(null, 63));

            Assert.AreEqual(0, feedback.Count,
                "破棄前に保持していたチャネルへ通知しても、現在の Dispatcher へは流れない。");
        }

        [Test]
        public void DestroyedCompanion_LeavesNoRegistryResidue()
        {
            Rig companion = MakeCompanion();
            Assert.AreEqual(1, CompanionFeedbackRegistry.Count);

            DestroyCompanion(companion);

            Assert.AreEqual(0, CompanionFeedbackRegistry.Count,
                "登録所に旧 Scene のチャネルを残さない。");
        }

        /// <summary>
        /// 対称な解除が走らなかった場合の安全網。Scene 破棄など <c>OnDisable</c>／<c>OnDestroy</c> を
        /// 経ない経路が実在するため、登録所は持ち主が破棄済みの登録を落とせなければならない。
        /// これが無いと、旧 Scene のチャネルを掴んだまま次の Scene の演出へ流れ込む。
        /// </summary>
        [Test]
        public void OrphanedRegistration_IsPrunedWithoutSymmetricUnregister()
        {
            Rig companion = MakeCompanion();
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            HitResultChannel heldChannel = companion.Receiver.Results;
            Assert.AreEqual(1, heldChannel.ListenerCount, "前提：繋がっている。");

            // 解除を呼ばずに破棄する（＝対称な後始末が走らなかった状況）。
            Object.DestroyImmediate(companion.Root);

            CompanionFeedbackRegistry.Prune();

            Assert.AreEqual(0, CompanionFeedbackRegistry.Count, "持ち主が居ない登録は落ちる。");
            Assert.AreEqual(0, heldChannel.ListenerCount, "落とすときに購読も外す。");
            Assert.IsNotNull(dispatcher);
        }

        /// <summary>
        /// 購読側が解除せずに破棄された場合も、登録所に居座らせない。
        /// static な <c>event</c> のままだと破棄済みの購読者を掴み続け、次の Scene で通知を受けてしまう。
        /// </summary>
        [Test]
        public void DestroyedObserver_IsDroppedOnNextNotification()
        {
            CombatFeedbackDispatcher dispatcher = MakeDispatcher();
            Assert.AreEqual(1, CompanionFeedbackRegistry.ObserverCount, "前提：購読者として載っている。");

            Object.DestroyImmediate(dispatcher.gameObject); // 解除を呼ばずに破棄。

            MakeCompanion(); // 次の通知で破棄済みの購読者が落ちる。

            Assert.AreEqual(0, CompanionFeedbackRegistry.ObserverCount,
                "破棄済みの購読者を掴み続けない。");
        }

        // ---- 登録の順序に依存しない ----

        [Test]
        public void DispatcherCreatedAfterCompanion_StillSubscribes()
        {
            Rig companion = MakeCompanion();           // 先に仲間。
            CombatFeedbackDispatcher dispatcher = MakeDispatcher(); // あとから演出側。

            var feedback = new FakeFeedback();
            dispatcher.Feedback.AddListener(feedback);
            companion.Receiver.Results.Publish(Damage(companion.Receiver, 64));

            Assert.AreEqual(1, feedback.Count, "先に居た仲間も採り込む（生成順に依存しない）。");
        }

        [Test]
        public void CompanionCreatedAfterDispatcher_SubscribesImmediately()
        {
            CombatFeedbackDispatcher dispatcher = MakeDispatcher(); // 先に演出側。
            var feedback = new FakeFeedback();
            dispatcher.Feedback.AddListener(feedback);

            Rig companion = MakeCompanion();           // あとから仲間（動的生成相当）。

            Assert.AreEqual(1, companion.Receiver.Results.ListenerCount,
                "有効化した時点で繋がる（再スキャンを待たない）。");

            companion.Receiver.Results.Publish(Damage(companion.Receiver, 65));

            Assert.AreEqual(1, feedback.Count, "生成直後の最初の一撃が届く。");
        }
    }
}
