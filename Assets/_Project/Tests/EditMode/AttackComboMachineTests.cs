using Momotaro.Gameplay.Combat;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-03B：3 段コンボ状態機械のフェーズ進行・段別総時間・連鎖窓・キャンセル窓・中断・段送りを検証する。
    /// 値は仕様書 Table 7 に準拠（段1: 0.10/0.10/0.20、段2: 0.12/0.10/0.23、段3: 0.18/0.12/0.35、段3 cancel=0.48）。
    /// </summary>
    public sealed class AttackComboMachineTests
    {
        private static AttackComboMachine Make()
        {
            var stages = new[]
            {
                new StageTiming(0.10f, 0.10f, 0.20f, 0f),
                new StageTiming(0.12f, 0.10f, 0.23f, 0f),
                new StageTiming(0.18f, 0.12f, 0.35f, 0.48f),
            };
            return new AttackComboMachine(stages);
        }

        [Test]
        public void Start_EntersStageOneStartup_WithJustStartedEdge()
        {
            var m = Make();
            Assert.IsTrue(m.TryStart());
            Assert.IsTrue(m.IsActive);
            Assert.AreEqual(1, m.Stage);
            Assert.AreEqual(AttackPhase.Startup, m.Phase);
            Assert.IsTrue(m.StageJustStarted, "開始フレームは JustStarted。");

            m.Tick(0.001f);
            Assert.IsFalse(m.StageJustStarted, "Tick で JustStarted はクリア。");
        }

        [Test]
        public void PhaseProgression_StartupActiveRecovery_HitboxOnlyDuringActive()
        {
            var m = Make();
            m.TryStart();

            m.Tick(0.05f); // 0.05 < 0.10
            Assert.AreEqual(AttackPhase.Startup, m.Phase);
            Assert.IsFalse(m.HitboxActive);

            m.Tick(0.10f); // 0.15 in [0.10,0.20)
            Assert.AreEqual(AttackPhase.Active, m.Phase);
            Assert.IsTrue(m.HitboxActive, "判定中のみ Hitbox 有効。");

            m.Tick(0.10f); // 0.25 >= 0.20
            Assert.AreEqual(AttackPhase.Recovery, m.Phase);
            Assert.IsFalse(m.HitboxActive);
        }

        [Test]
        public void AcceptingChain_OnlyAfterActiveEnds()
        {
            var m = Make();
            m.TryStart();

            m.Tick(0.15f); // Active
            Assert.IsFalse(m.AcceptingChain, "判定中は連鎖不可。");

            m.Tick(0.10f); // 0.25 >= ActiveEnd(0.20)
            Assert.IsTrue(m.AcceptingChain, "判定終了後は連鎖可。");
        }

        [Test]
        public void NoChain_CompletesAfterTotalThenEnds()
        {
            var m = Make();
            m.TryStart();
            m.Tick(0.40f); // Total(0.40)
            Assert.IsTrue(m.IsComplete, "総時間で完了。");
            m.End();
            Assert.IsFalse(m.IsActive, "連鎖しなければ終了。");
        }

        [Test]
        public void Chain_AdvancesToNextStage_ResettingElapsed()
        {
            var m = Make();
            m.TryStart();
            m.Tick(0.20f); // reach ActiveEnd
            Assert.IsTrue(m.AcceptingChain);

            Assert.IsTrue(m.TryAdvance());
            Assert.AreEqual(2, m.Stage);
            Assert.AreEqual(AttackPhase.Startup, m.Phase);
            Assert.IsTrue(m.StageJustStarted, "段送りも JustStarted。");
            Assert.AreEqual(0f, m.StageElapsed);
        }

        [Test]
        public void FinalStage_DoesNotAcceptChain()
        {
            var m = Make();
            m.TryStart();
            m.Tick(0.20f); m.TryAdvance(); // stage 2
            m.Tick(0.22f); m.TryAdvance(); // stage 3
            Assert.AreEqual(3, m.Stage);

            m.Tick(0.30f); // past stage3 ActiveEnd(0.30)
            Assert.IsFalse(m.AcceptingChain, "最終段は連鎖しない。");
            Assert.IsFalse(m.TryAdvance());
        }

        [Test]
        public void CanCancel_Stage1_AfterActiveEnd()
        {
            var m = Make();
            m.TryStart();
            m.Tick(0.15f); // Active
            Assert.IsFalse(m.CanCancel, "判定前/中はキャンセル不可。");
            m.Tick(0.06f); // 0.21 >= 0.20
            Assert.IsTrue(m.CanCancel, "判定終了後はキャンセル可。");
        }

        [Test]
        public void CanCancel_Stage3_UsesDataWindow_048()
        {
            var m = Make();
            m.TryStart();
            m.Tick(0.20f); m.TryAdvance(); // stage2
            m.Tick(0.22f); m.TryAdvance(); // stage3
            m.Tick(0.30f); // recovery start of stage3, but < 0.48
            Assert.IsFalse(m.CanCancel, "3 段目は後隙後半(0.48)までキャンセル不可。");
            m.Tick(0.20f); // 0.50 >= 0.48
            Assert.IsTrue(m.CanCancel);
        }

        [Test]
        public void Interrupt_DeactivatesImmediately()
        {
            var m = Make();
            m.TryStart();
            m.Tick(0.15f);
            m.Interrupt();
            Assert.IsFalse(m.IsActive);
            Assert.AreEqual(0, m.Stage);
            Assert.IsFalse(m.HitboxActive);
        }

        [Test]
        public void CannotStartWhileActive()
        {
            var m = Make();
            Assert.IsTrue(m.TryStart());
            Assert.IsFalse(m.TryStart(), "攻撃中は再開始しない。");
        }
    }
}
