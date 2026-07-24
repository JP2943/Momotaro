using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-09：実際の <see cref="PlayerStateController"/> 更新経路でのステップ開始を検証する。無入力なら後方、入力方向（斜め含む）へ、
    /// スタミナ 25 を消費、残量不足なら不発、Mode/Disable でクリーンに解除。時間依存を避け「開始フレーム」で確認する。
    /// </summary>
    public sealed class PlayerStepIntegrationTests
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
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            f.SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            return f.GetValue(target);
        }

        private static void Tick(PlayerStateController c) => UpdateMethod.Invoke(c, null);

        private static Vector3 StepDirection(PlayerStateController c)
        {
            var step = (StepState)GetPrivate(c, "_step");
            return step.Direction;
        }

        private (PlayerStateController c, PlayerMotor motor, PlayerVitalsHolder holder, PlayerInputState input) MakeController(int maxStamina = 100)
        {
            var go = new GameObject("StepTest");
            _spawned.Add(go);
            var facing = go.AddComponent<PlayerFacing>();
            var motor = go.AddComponent<PlayerMotor>();
            var holder = go.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetPrivate(data, "_maxHp", 100);
            SetPrivate(data, "_maxStamina", maxStamina);
            SetPrivate(holder, "_data", data);
            var c = go.AddComponent<PlayerStateController>();
            SetPrivate(c, "_facing", facing);
            SetPrivate(c, "_motor", motor);

            var input = new PlayerInputState();
            PlayerInputProvider.Current = input;
            return (c, motor, holder, input);
        }

        [Test]
        public void NoMoveInput_StepsBackward()
        {
            var (c, _, _, input) = MakeController();
            input.SetStep(true);
            Tick(c);

            Assert.AreEqual(PlayerState.Step, c.Current, "ステップ状態へ。");
            Assert.IsTrue(c.IsStepping);
            Vector3 expected = (-c.Forward).normalized;
            Assert.AreEqual(expected.x, StepDirection(c).x, 1e-3f, "無入力は後方（-Forward）。");
            Assert.AreEqual(expected.z, StepDirection(c).z, 1e-3f);
        }

        [Test]
        public void DiagonalMoveInput_StepsInThatDirection()
        {
            var (c, _, _, input) = MakeController();
            input.SetMove(new Vector2(1f, 1f));
            input.SetStep(true);
            Tick(c);

            Assert.IsTrue(c.IsStepping);
            Vector3 d = StepDirection(c);
            Assert.AreEqual(1f, d.magnitude, 1e-3f, "斜めも大きさ 1。");
            Assert.AreEqual(0.70710677f, d.x, 1e-3f, "入力(1,1)方向。");
            Assert.AreEqual(0.70710677f, d.z, 1e-3f);
        }

        [Test]
        public void Step_ConsumesTwentyFiveStamina()
        {
            var (c, _, holder, input) = MakeController(maxStamina: 100);
            input.SetStep(true);
            Tick(c);

            Assert.IsTrue(c.IsStepping);
            Assert.AreEqual(75, holder.Vitals.Stamina.Current, "ステップは 25 消費。");
        }

        [Test]
        public void InsufficientStamina_NoStep_NoConsume()
        {
            var (c, _, holder, input) = MakeController(maxStamina: 24); // 25 未満
            input.SetStep(true);
            Tick(c);

            Assert.IsFalse(c.IsStepping, "残量不足ではステップ不発。");
            Assert.AreNotEqual(PlayerState.Step, c.Current);
            Assert.AreEqual(24, holder.Vitals.Stamina.Current, "消費なし（不発）。");
        }

        [Test]
        public void Step_IsInvincibleContract_ExposedByController()
        {
            // 開始直後(elapsed 0)は無敵前だが、ステップ状態であることと契約実装を確認。
            var (c, _, _, input) = MakeController();
            input.SetStep(true);
            Tick(c);
            Assert.IsTrue(c.IsStepping);
            Assert.IsTrue(typeof(Momotaro.Gameplay.Combat.IEvadeState).IsAssignableFrom(typeof(PlayerStateController)),
                "PlayerStateController は IEvadeState を実装（無敵を明示状態として供給）。");
        }

        [Test]
        public void ModeReset_ClearsStep_AndMotor()
        {
            var (c, motor, _, input) = MakeController();
            input.SetStep(true);
            Tick(c);
            Assert.IsTrue(c.IsStepping);

            c.ResetToNeutral(); // Disable 相当

            Assert.IsFalse(c.IsStepping, "解除でステップが残らない。");
            Assert.IsFalse(c.IsInvincible, "無敵も残らない。");
            Assert.IsFalse(motor.MovementSuppressed, "移動抑制が解除。");
            Assert.AreEqual(Vector3.zero, motor.StepVelocity, "ステップ速度ゼロ。");
        }

        [Test]
        public void GameModeGateClose_ImmediatelyCancelsInProgressStep()
        {
            // 実際の GameMode 遮断経路（input.SetActive(false) → Update）で、実行中ステップ・無敵・速度・抑制が即時解除される。
            var (c, motor, _, input) = MakeController();
            input.SetStep(true);
            Tick(c);
            Assert.IsTrue(c.IsStepping);
            Assert.IsTrue(motor.MovementSuppressed, "ステップ中は移動抑制。");

            input.SetActive(false); // 会話・Pause・UI 等でゲートが閉じる
            Tick(c);

            Assert.IsFalse(c.IsStepping, "遮断で実行中ステップを即時解除。");
            Assert.AreNotEqual(PlayerState.Step, c.Current);
            Assert.IsFalse(c.IsInvincible, "無敵も即時解除。");
            Assert.IsFalse(motor.MovementSuppressed, "移動抑制が解除。");
            Assert.AreEqual(Vector3.zero, motor.StepVelocity, "ステップ速度ゼロ。");
        }

        [Test]
        public void StepPressDuringGuardBreak_IsDropped_NoStepAfterRecovery()
        {
            var (c, _, holder, input) = MakeController(maxStamina: 100);
            holder.ConsumeStamina(100f); // スタミナ 0 → ガードブレイク
            Assert.IsTrue(holder.IsGuardBroken);

            input.SetStep(true);
            Tick(c); // ブレイク中：ステップ押下は破棄される
            Assert.AreEqual(PlayerState.GuardBreak, c.Current);
            Assert.IsFalse(c.IsStepping);

            holder.Tick(1.5f); // ブレイク終了（最大の25%＝25 回復）
            Assert.IsFalse(holder.IsGuardBroken);

            Tick(c); // 破棄済みなので、押しっぱなしでもステップは発動しない
            Assert.IsFalse(c.IsStepping, "ブレイク中の押下は復帰後へ残らない。");
            Assert.AreNotEqual(PlayerState.Step, c.Current);
        }

        [Test]
        public void EnemyLayer_Configured_PlayerPassesEnemy_ButCollidesWall()
        {
            int enemy = Momotaro.Gameplay.Combat.CombatLayers.EnemyLayer;
            int player = Momotaro.Gameplay.Combat.CombatLayers.PlayerLayer;
            Assert.GreaterOrEqual(enemy, 0, "Enemy レイヤーが TagManager に定義されている。");
            Assert.GreaterOrEqual(player, 0, "Player（Default）レイヤーが定義されている。");

            bool prev = Physics.GetIgnoreLayerCollision(enemy, player);
            var go = new GameObject("EnemyBody");
            _spawned.Add(go);
            Momotaro.Gameplay.Combat.CombatLayers.ConfigureEnemy(go);

            Assert.AreEqual(enemy, go.layer, "敵は Enemy レイヤーへ配置。");
            Assert.IsTrue(Physics.GetIgnoreLayerCollision(enemy, player), "Player↔Enemy は衝突無効（敵すり抜け）。");
            Assert.IsFalse(Physics.GetIgnoreLayerCollision(player, player), "Player↔壁(Default)は衝突維持（壁停止）。");

            Physics.IgnoreLayerCollision(enemy, player, prev); // グローバル状態を復元
        }
    }
}
