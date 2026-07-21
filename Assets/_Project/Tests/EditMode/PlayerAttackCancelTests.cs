using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-03B 受入修正：Guard キャンセルが PlayerStateController で実際に状態遷移することを検証する統合テスト。
    /// 各段のキャンセル窓（1・2 段目=判定終了後、3 段目=0.48 秒以降）、成立後の Guard 遷移、攻撃状態の中立化、
    /// 窓より前からの Guard 保持が窓到達時に成立することを確認する。
    /// </summary>
    public sealed class PlayerAttackCancelTests
    {
        private static readonly MethodInfo UpdateMethod =
            typeof(PlayerStateController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            PlayerInputProvider.Current = null;
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private static void SetPrivate(object target, string field, object value)
        {
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            return target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
        }

        private static void Tick(PlayerStateController c)
        {
            UpdateMethod.Invoke(c, null);
        }

        private AttackData MakeStage(float startup, float active, float recovery, float cancelStart)
        {
            var a = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(a);
            SetPrivate(a, "_startupSeconds", startup);
            SetPrivate(a, "_activeSeconds", active);
            SetPrivate(a, "_recoverySeconds", recovery);
            SetPrivate(a, "_cancelWindowStartSeconds", cancelStart);
            return a;
        }

        private PlayerAttackComboData MakeCombo3()
        {
            var combo = ScriptableObject.CreateInstance<PlayerAttackComboData>();
            _spawned.Add(combo);
            var stages = new[]
            {
                MakeStage(0.10f, 0.10f, 0.20f, 0f),   // s1 ActiveEnd 0.20
                MakeStage(0.12f, 0.10f, 0.23f, 0f),   // s2 ActiveEnd 0.22
                MakeStage(0.18f, 0.12f, 0.35f, 0.48f) // s3 cancel window 0.48
            };
            SetPrivate(combo, "_stages", stages);
            SetPrivate(combo, "_bufferSeconds", 0.30f);
            return combo;
        }

        private (PlayerStateController c, PlayerFacing facing, PlayerMotor motor, PlayerInputState input) MakeController()
        {
            var go = new GameObject("CancelTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var motor = go.AddComponent<PlayerMotor>();
            var c = go.AddComponent<PlayerStateController>();
            SetPrivate(c, "_facing", facing);
            SetPrivate(c, "_motor", motor);
            SetPrivate(c, "_attackCombo", MakeCombo3());
            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            return (c, facing, motor, input);
        }

        private static AttackComboMachine Combo(PlayerStateController c)
        {
            return (AttackComboMachine)GetPrivate(c, "_combo");
        }

        // 攻撃を開始し（stage1）、内部コンボを指定の段・経過へ直接進めるヘルパ。
        private static void DriveTo(PlayerStateController c, int stage, float elapsedInStage)
        {
            AttackComboMachine m = Combo(c);
            m.Interrupt();
            m.TryStart(); // stage1 elapsed 0
            for (int s = 1; s < stage; s++)
            {
                // 判定終了まで進めてから次段へ。
                m.Tick(0.30f);
                m.TryAdvance();
            }

            m.Tick(elapsedInStage);
        }

        private (PlayerStateController c, PlayerFacing facing, PlayerMotor motor, PlayerInputState input) StartAttack()
        {
            var t = MakeController();

            // EditMode では Awake・入力バッファ・Time.deltaTime に依存しないよう、コンボ機と Buffer を直接注入して
            // stage1 を開始する（開始経路そのものは PlayerAttackGatingTests が検証済み）。
            var timings = new[]
            {
                new StageTiming(0.10f, 0.10f, 0.20f, 0f),
                new StageTiming(0.12f, 0.10f, 0.23f, 0f),
                new StageTiming(0.18f, 0.12f, 0.35f, 0.48f)
            };
            var combo = new AttackComboMachine(timings);
            SetPrivate(t.c, "_combo", combo);
            SetPrivate(t.c, "_attackBuffer", new AttackInputBuffer(0.30f));
            combo.TryStart(); // stage1

            Tick(t.c); // 状態機械を同期（attacking=true → Attack、MovementSuppressed=true）
            Assert.AreEqual(PlayerState.Attack, t.c.Current, "前提：攻撃状態で開始。");
            return t;
        }

        [Test]
        public void Stage1_DuringActive_NotCancelledByGuard()
        {
            var t = StartAttack();
            DriveTo(t.c, 1, 0.12f); // 判定中（0.10..0.20）
            t.input.SetGuard(true);
            Tick(t.c);
            Assert.AreEqual(PlayerState.Attack, t.c.Current, "判定中は Guard でキャンセルされない。");
        }

        [Test]
        public void Stage1_AfterActive_CancelledByGuard()
        {
            var t = StartAttack();
            DriveTo(t.c, 1, 0.30f); // 判定終了後
            t.input.SetGuard(true);
            Tick(t.c);
            Assert.AreEqual(PlayerState.GuardIdle, t.c.Current, "判定終了後は Guard でキャンセルされ Guard 状態へ。");
        }

        [Test]
        public void Stage2_AfterActive_CancelledByGuard()
        {
            var t = StartAttack();
            DriveTo(t.c, 2, 0.30f); // stage2 判定終了後(>=0.22)
            t.input.SetGuard(true);
            Tick(t.c);
            Assert.AreEqual(PlayerState.GuardIdle, t.c.Current);
        }

        [Test]
        public void Stage3_Before048_NotCancelled()
        {
            var t = StartAttack();
            DriveTo(t.c, 3, 0.30f); // stage3 判定終了後だが 0.48 未満
            t.input.SetGuard(true);
            Tick(t.c);
            Assert.AreEqual(PlayerState.Attack, t.c.Current, "3 段目は 0.48 秒未満ではキャンセルされない。");
        }

        [Test]
        public void Stage3_After048_Cancelled()
        {
            var t = StartAttack();
            DriveTo(t.c, 3, 0.50f); // stage3 0.48 秒以降
            t.input.SetGuard(true);
            Tick(t.c);
            Assert.AreEqual(PlayerState.GuardIdle, t.c.Current, "3 段目は 0.48 秒以降にキャンセルされる。");
        }

        [Test]
        public void GuardHeldBeforeWindow_CancelsWhenWindowReached()
        {
            var t = StartAttack();
            t.input.SetGuard(true); // 窓より前から Guard 保持

            DriveTo(t.c, 1, 0.12f); // 判定中
            Tick(t.c);
            Assert.AreEqual(PlayerState.Attack, t.c.Current, "窓前は保持していてもキャンセルされない。");

            DriveTo(t.c, 1, 0.25f); // 判定終了後（窓到達）
            Tick(t.c);
            Assert.AreEqual(PlayerState.GuardIdle, t.c.Current, "窓到達時点でキャンセル成立。");
        }

        [Test]
        public void AfterCancel_AttackStateIsNeutralised()
        {
            var t = StartAttack();
            DriveTo(t.c, 1, 0.30f);
            t.input.SetGuard(true);
            Tick(t.c);

            Assert.AreEqual(PlayerState.GuardIdle, t.c.Current, "Attack ではなく Guard 状態。");
            Assert.IsFalse(Combo(t.c).IsActive, "コンボ非アクティブ（Hitbox は次フレーム走らない）。");
            Assert.IsFalse(Combo(t.c).HitboxActive, "Hitbox 無効。");
            Assert.IsFalse(t.motor.MovementSuppressed, "移動抑制が解除。");
            Assert.AreEqual(Vector3.zero, t.motor.StepVelocity, "踏み込み速度ゼロ。");

            var buffer = (AttackInputBuffer)GetPrivate(t.c, "_attackBuffer");
            Assert.IsFalse(buffer.HasBuffered, "攻撃 Buffer が空。");

            // 攻撃の向きロックは解除される（Guard 保持中は Guard がロックするため、Guard を離すと解放される）。
            t.input.SetGuard(false);
            Tick(t.c);
            Assert.IsFalse(t.facing.IsLocked, "攻撃由来の向きロックは残らない。");
        }
    }
}
