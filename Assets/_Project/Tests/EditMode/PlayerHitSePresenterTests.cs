using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// ヒット音（P3.5-08B/09）：<see cref="PlayerHitSePresenter"/> が主人公の攻撃命中（Damage）で段別ヒット SE を鳴らすことを検証する。
    /// 1・2 段目＝共通、3 段目・必殺技＝共通、敵攻撃者では鳴らさない、Damage 以外では鳴らさない、同一 HitId の重複禁止、別 Swing での再発火、
    /// SE 未割当での無例外を確認する。攻撃段は攻撃者（<see cref="IAttackSwingSource"/>）から読む。
    /// </summary>
    public sealed class PlayerHitSePresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeAttacker : ICombatActor, IAttackSwingSource
        {
            public CombatFaction FactionValue = CombatFaction.Player;
            public int Stage;
            public CombatFaction Faction => FactionValue;
            public int FloorId => 0;
            public Vector3 WorldPosition => Vector3.zero;
            public Vector3 Forward => Vector3.forward;
            public bool IsSwingHitboxActive => Stage != 0;
            public int SwingStage => Stage;
            public Vector3 SwingCenter => Vector3.zero;
            public Vector3 SwingHalfExtents => Vector3.zero;
            public Vector3 SwingForward => Vector3.forward;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private PlayerHitSePresenter New(out CombatSePlayer se, out CombatFeedbackChannel ch)
        {
            var go = new GameObject("PlayerHitSe");
            _spawned.Add(go);
            var p = go.AddComponent<PlayerHitSePresenter>();

            var seGo = new GameObject("HitSePlayer");
            _spawned.Add(seGo);
            se = seGo.AddComponent<CombatSePlayer>();
            p.Se = se;

            ch = new CombatFeedbackChannel();
            p.Bind(ch);
            return p;
        }

        private static void PublishDamage(CombatFeedbackChannel ch, ICombatActor attacker, int hitId)
        {
            HitResult r = HitResult.Damage(HitId.Single(hitId), attacker, null, HitDamage.None);
            ch.Publish(new CombatFeedbackEvent(r, CombatFeedbackMap.Resolve(HitResultKind.Damage)));
        }

        [Test]
        public void Combo1And2_PlayHit1()
        {
            PlayerHitSePresenter p = New(out CombatSePlayer se, out CombatFeedbackChannel ch);
            var atk = new FakeAttacker { Stage = 1 };

            PublishDamage(ch, atk, 1);
            Assert.AreEqual("SE_Player_Hit1", p.LastSeId, "1 段目は Hit1。");

            atk.Stage = 2;
            PublishDamage(ch, atk, 2);
            Assert.AreEqual("SE_Player_Hit1", p.LastSeId, "2 段目も共通 Hit1。");
            Assert.AreEqual(2, p.PlayCount);
        }

        [Test]
        public void Combo3AndSpecial_PlayHit2()
        {
            PlayerHitSePresenter p = New(out CombatSePlayer se, out CombatFeedbackChannel ch);
            var atk = new FakeAttacker { Stage = 3 };

            PublishDamage(ch, atk, 1);
            Assert.AreEqual("SE_Player_Hit2", p.LastSeId, "3 段目は Hit2。");
            Assert.AreEqual("SE_Player_Hit2", se.LastRequestedSeId);

            atk.Stage = AttackSwing.SpecialStage;
            PublishDamage(ch, atk, 2);
            Assert.AreEqual("SE_Player_Hit2", p.LastSeId, "必殺技も共通 Hit2。");
            Assert.AreEqual(2, p.PlayCount);
        }

        [Test]
        public void EnemyAttacker_DoesNotPlay()
        {
            PlayerHitSePresenter p = New(out CombatSePlayer se, out CombatFeedbackChannel ch);
            var enemy = new FakeAttacker { FactionValue = CombatFaction.Enemy, Stage = 1 };

            PublishDamage(ch, enemy, 1);
            Assert.AreEqual(0, p.PlayCount, "敵→主人公の被弾では鳴らさない。");
        }

        [Test]
        public void NonDamage_DoesNotPlay()
        {
            PlayerHitSePresenter p = New(out CombatSePlayer se, out CombatFeedbackChannel ch);
            var atk = new FakeAttacker { Stage = 1 };

            var guard = HitResult.Guard(HitId.Single(1), atk, null, HitDamage.None);
            ch.Publish(new CombatFeedbackEvent(guard, CombatFeedbackMap.Resolve(HitResultKind.Guard)));
            Assert.AreEqual(0, p.PlayCount, "Damage 以外では鳴らさない。");
        }

        [Test]
        public void SameHitId_MultipleTargets_PlaysOnce_DifferentSwingReplays()
        {
            PlayerHitSePresenter p = New(out CombatSePlayer se, out CombatFeedbackChannel ch);
            var atk = new FakeAttacker { Stage = 1 };

            PublishDamage(ch, atk, 5); // 1 体目
            PublishDamage(ch, atk, 5); // 同一 Swing の 2 体目 → 重複禁止
            Assert.AreEqual(1, p.PlayCount, "同一 HitId は 1 回だけ（複数体命中でも重複しない）。");

            PublishDamage(ch, atk, 6); // 次の Swing
            Assert.AreEqual(2, p.PlayCount, "別 Swing（HitId）では再発火。");
        }

        [Test]
        public void NoSePlayer_NoException()
        {
            PlayerHitSePresenter p = New(out CombatSePlayer se, out CombatFeedbackChannel ch);
            p.Se = null;
            var atk = new FakeAttacker { Stage = 1 };

            Assert.DoesNotThrow(() => PublishDamage(ch, atk, 1), "SE 未割当でも無例外。");
            Assert.AreEqual(1, p.PlayCount);
        }
    }
}
