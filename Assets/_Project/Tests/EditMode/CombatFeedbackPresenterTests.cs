using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05B：<see cref="CombatFeedbackPresenter"/> が命中フィードバックを種別ごとにサブ効果へ振り分けることを検証する。
    /// ダメージ＝ヒットストップ＋点滅＋揺れ＋SE、通常ガード＝点滅のみ、ジャストガード＝強調（長いヒットストップ＋揺れ）、
    /// 回避＝SE のみ、サブ効果未割当でも無例外、を確認する。時間停止はテスト後に必ず復帰する。
    /// </summary>
    public sealed class CombatFeedbackPresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeDamageable : MonoBehaviour, IDamageable
        {
            public int DamageableId { get; set; } = 1;
            public void ReceiveHit(in HitInfo hit) { }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
            Time.timeScale = 1f;
        }

        private T Add<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private CombatFeedbackPresenter NewFull(out CombatFeedbackChannel channel, out HitStopController hs,
            out HitFlashPresenter fl, out CameraShakePresenter sh, out CombatSePlayer se)
        {
            Time.timeScale = 1f;
            var go = new GameObject("Coordinator");
            _spawned.Add(go);
            var p = go.AddComponent<CombatFeedbackPresenter>();
            hs = Add<HitStopController>();
            fl = Add<HitFlashPresenter>();
            sh = Add<CameraShakePresenter>();
            se = Add<CombatSePlayer>();
            p.HitStop = hs;
            p.Flash = fl;
            p.CameraShake = sh;
            p.Se = se;

            channel = new CombatFeedbackChannel();
            p.Bind(channel);
            return p;
        }

        private IDamageable NewTarget()
        {
            var go = new GameObject("Target");
            _spawned.Add(go);
            var dmg = go.AddComponent<FakeDamageable>();
            var child = new GameObject("Sprite", typeof(SpriteRenderer));
            _spawned.Add(child);
            child.transform.SetParent(go.transform, false);
            return dmg;
        }

        private static void PublishKind(CombatFeedbackChannel channel, HitResultKind kind, IDamageable target)
        {
            HitResult result;
            switch (kind)
            {
                case HitResultKind.Damage: result = HitResult.Damage(default, null, target, HitDamage.None); break;
                case HitResultKind.Guard: result = HitResult.Guard(default, null, target, HitDamage.None); break;
                case HitResultKind.JustGuard: result = HitResult.JustGuard(default, null, target, HitDamage.None); break;
                case HitResultKind.Evade: result = HitResult.Evade(default, null, target); break;
                default: result = HitResult.Rejected(default, null, target); break;
            }

            CombatFeedbackCue cue = CombatFeedbackMap.Resolve(kind);
            channel.Publish(new CombatFeedbackEvent(result, cue));
        }

        [Test]
        public void Damage_TriggersHitStop_Flash_Shake_Se()
        {
            CombatFeedbackPresenter p = NewFull(out CombatFeedbackChannel ch, out HitStopController hs, out HitFlashPresenter fl, out CameraShakePresenter sh, out CombatSePlayer se);
            IDamageable target = NewTarget();

            PublishKind(ch, HitResultKind.Damage, target);

            Assert.IsTrue(hs.IsStopping, "ダメージでヒットストップ。");
            Assert.AreEqual(1, fl.ActiveCount, "被弾点滅。");
            Assert.IsTrue(sh.IsShaking, "カメラ揺れ。");
            Assert.AreEqual("SE_Hit_Normal", se.LastRequestedSeId, "SE 要求。");
        }

        [Test]
        public void Guard_FlashOnly_NoShake()
        {
            CombatFeedbackPresenter p = NewFull(out CombatFeedbackChannel ch, out HitStopController hs, out HitFlashPresenter fl, out CameraShakePresenter sh, out CombatSePlayer se);
            IDamageable target = NewTarget();

            PublishKind(ch, HitResultKind.Guard, target);

            Assert.AreEqual(1, fl.ActiveCount, "通常ガードは点滅する。");
            Assert.IsFalse(sh.IsShaking, "通常ガードは揺らさない。");
            Assert.AreEqual("SE_Guard", se.LastRequestedSeId);
        }

        [Test]
        public void JustGuard_IsEmphasized_LongerHitStop_AndShake()
        {
            CombatFeedbackPresenter p = NewFull(out CombatFeedbackChannel ch, out HitStopController hs, out HitFlashPresenter fl, out CameraShakePresenter sh, out CombatSePlayer se);
            IDamageable target = NewTarget();

            PublishKind(ch, HitResultKind.JustGuard, target);

            Assert.IsTrue(hs.IsStopping);
            Assert.GreaterOrEqual(hs.Remaining, 0.08f, "JG は通常ダメージ(0.05)より長いヒットストップ(0.09)で強調。");
            Assert.IsTrue(sh.IsShaking, "JG は揺れで強調。");
            Assert.AreEqual(1, fl.ActiveCount);
            Assert.AreEqual("SE_JustGuard", se.LastRequestedSeId);
        }

        [Test]
        public void Evade_SeOnly_NoHitStop_NoFlash_NoShake()
        {
            CombatFeedbackPresenter p = NewFull(out CombatFeedbackChannel ch, out HitStopController hs, out HitFlashPresenter fl, out CameraShakePresenter sh, out CombatSePlayer se);
            IDamageable target = NewTarget();

            PublishKind(ch, HitResultKind.Evade, target);

            Assert.IsFalse(hs.IsStopping, "回避はヒットストップなし。");
            Assert.AreEqual(0, fl.ActiveCount, "回避は点滅なし。");
            Assert.IsFalse(sh.IsShaking, "回避は揺れなし。");
            Assert.AreEqual("SE_Evade", se.LastRequestedSeId);
        }

        [Test]
        public void NoSubEffects_NoException()
        {
            Time.timeScale = 1f;
            var go = new GameObject("BareCoordinator");
            _spawned.Add(go);
            var p = go.AddComponent<CombatFeedbackPresenter>();
            var channel = new CombatFeedbackChannel();
            p.Bind(channel);
            IDamageable target = NewTarget();

            Assert.DoesNotThrow(() => PublishKind(channel, HitResultKind.Damage, target), "サブ効果未割当でも無例外。");
        }

        [Test]
        public void Unbind_StopsReceiving()
        {
            CombatFeedbackPresenter p = NewFull(out CombatFeedbackChannel ch, out HitStopController hs, out HitFlashPresenter fl, out CameraShakePresenter sh, out CombatSePlayer se);
            IDamageable target = NewTarget();

            p.Bind(null); // 購読解除。
            PublishKind(ch, HitResultKind.Damage, target);

            Assert.IsFalse(hs.IsStopping, "購読解除後は反応しない。");
            Assert.AreEqual(0, fl.ActiveCount);
        }
    }
}
