using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-08C：<see cref="PlayerAttackSwingSePresenter"/> が段（<see cref="IAttackSwingSource.SwingStage"/>）の出現で段別スイング SE を
    /// 1 回鳴らし、同段継続中は再発火せず、連続コンボ（0 を挟まない 1→2→3）も各段で鳴り、必殺技段では専用 SE、敵段・非攻撃では
    /// 鳴らさず、SE 未割当でも無例外なことを検証する。判定（Active）ではなく段番号の変化で発火する（＝Startup から早めに鳴る）。
    /// </summary>
    public sealed class PlayerAttackSwingSePresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeSwing : IAttackSwingSource
        {
            public bool IsSwingHitboxActive { get; set; }
            public int SwingStage { get; set; }
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

        private PlayerAttackSwingSePresenter NewPresenter(out FakeSwing src, out CombatSePlayer se, bool withSe = true)
        {
            var go = new GameObject("SwingSe");
            _spawned.Add(go);
            var p = go.AddComponent<PlayerAttackSwingSePresenter>();

            se = null;
            if (withSe)
            {
                var seGo = new GameObject("CombatSePlayer");
                _spawned.Add(seGo);
                se = seGo.AddComponent<CombatSePlayer>();
                p.Se = se;
            }

            src = new FakeSwing();
            p.Bind(src);
            return p;
        }

        [Test]
        public void StageAppears_PlaysStage1Se_Once()
        {
            PlayerAttackSwingSePresenter p = NewPresenter(out FakeSwing src, out CombatSePlayer se);

            src.SwingStage = 0;
            p.Tick();
            Assert.AreEqual(0, p.SwingCount, "非攻撃(0)では鳴らさない。");

            src.SwingStage = 1; // 段 1 が出現（Startup 開始）。
            p.Tick();
            Assert.AreEqual(1, p.SwingCount, "段の出現で 1 回鳴る。");
            Assert.AreEqual("SE_Player_Attack1", p.LastSwingSeId);
            Assert.AreEqual("SE_Player_Attack1", se.LastRequestedSeId);

            // 同段継続（Startup→Active→Recovery）では再発火しない。
            p.Tick();
            p.Tick();
            Assert.AreEqual(1, p.SwingCount, "同段継続では再発火しない。");
        }

        [Test]
        public void SeamlessCombo_1to2to3_FiresEachStage()
        {
            PlayerAttackSwingSePresenter p = NewPresenter(out FakeSwing src, out CombatSePlayer se);

            // 0 を挟まずに段が切り替わる連続コンボ（判定の隙間に依存しない発火を確認）。
            src.SwingStage = 1; p.Tick();
            src.SwingStage = 2; p.Tick();
            src.SwingStage = 3; p.Tick();

            Assert.AreEqual(3, p.SwingCount, "1→2→3 の各段で鳴る（シームレス切替でも取りこぼさない）。");
            Assert.AreEqual("SE_Player_Attack3", p.LastSwingSeId);
            Assert.AreEqual("SE_Player_Attack3", se.LastRequestedSeId);
        }

        [Test]
        public void ReturnToZeroThenSameStage_FiresAgain()
        {
            PlayerAttackSwingSePresenter p = NewPresenter(out FakeSwing src, out CombatSePlayer se);

            src.SwingStage = 1; p.Tick(); // 1 回目。
            src.SwingStage = 0; p.Tick(); // 攻撃終了。
            src.SwingStage = 1; p.Tick(); // 再度 1 段目。

            Assert.AreEqual(2, p.SwingCount, "0 を挟んだ同段の再攻撃は再発火する。");
        }

        [Test]
        public void Special_PlaysSpecialSe()
        {
            PlayerAttackSwingSePresenter p = NewPresenter(out FakeSwing src, out CombatSePlayer se);

            src.SwingStage = AttackSwing.SpecialStage;
            p.Tick();

            Assert.AreEqual(1, p.SwingCount);
            Assert.AreEqual("SE_Player_Special", p.LastSwingSeId);
            Assert.AreEqual("SE_Player_Special", se.LastRequestedSeId);
        }

        [Test]
        public void EnemyStage_And_NonAttack_DoNotFire()
        {
            PlayerAttackSwingSePresenter p = NewPresenter(out FakeSwing src, out CombatSePlayer se);

            src.SwingStage = AttackSwing.EnemyMeleeNormal; // 敵段は主人公スイング SE の対象外。
            p.Tick();
            Assert.AreEqual(0, p.SwingCount, "敵段では鳴らさない。");

            src.SwingStage = 0;
            p.Tick();
            Assert.AreEqual(0, p.SwingCount, "非攻撃(0)では鳴らさない。");
        }

        [Test]
        public void NoSePlayer_NoException_StillCountsEdge()
        {
            PlayerAttackSwingSePresenter p = NewPresenter(out FakeSwing src, out CombatSePlayer se, withSe: false);

            src.SwingStage = 1;
            Assert.DoesNotThrow(() => p.Tick(), "SE 再生器未割当でも無例外。");
            Assert.AreEqual(1, p.SwingCount);
            Assert.AreEqual("SE_Player_Attack1", p.LastSwingSeId);
        }
    }
}
