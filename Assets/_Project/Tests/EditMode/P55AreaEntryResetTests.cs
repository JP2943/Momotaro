using System.Reflection;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// <b>入場ごとに前の入場の残りを捨てる</b>（P5.5 §6.2 手順 5。工程 P55-10b）。
    ///
    /// 旧実装では入場ごとに Scene を読み直していたので、Actor は毎回新品だった——
    /// 攻撃モーションや先行入力が持ち越されることは<b>原理的に起こらなかった</b>。
    /// 旧 Area を保持して再利用すると（§6.2 手順 11）同じ Actor へ戻ってくるので、
    /// 出て行ったときの途中動作がそのまま残る。
    ///
    /// <b>ここで固めるのは「捨てること」と「捨てすぎないこと」の両方である。</b>
    /// 入力の参照まで捨てると、入場直後の主人公が操作を受け取らなくなる
    /// （<see cref="PlayerStateController.ResetToNeutral"/> は Disable 用なのでそれを行う）。
    /// </summary>
    public sealed class P55AreaEntryResetTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
                _go = null;
            }
        }

        /// <summary>
        /// 途中動作・速度倍率・向きのロックが落ち、状態機械が Idle へ戻る。
        /// </summary>
        [Test]
        public void ResetForAreaEntry_ReturnsThePlayerToNeutral()
        {
            PlayerStateController controller = NewController(out PlayerMotor motor, out PlayerFacing facing);

            Machine(controller).Tick(true, true, true); // -> GuardMove
            Assert.AreNotEqual(PlayerState.Idle, controller.Current, "前提：Idle ではない状態を作った。");
            motor.SpeedMultiplier = 0.4f;
            motor.MovementSuppressed = true;
            facing.IsLocked = true;

            controller.ResetForAreaEntry();

            Assert.AreEqual(PlayerState.Idle, controller.Current, "状態は Idle へ戻る。");
            Assert.AreEqual(1f, motor.SpeedMultiplier, "速度倍率が戻る。");
            Assert.IsFalse(motor.MovementSuppressed, "移動抑制が解ける。");
            Assert.IsFalse(facing.IsLocked, "向きロックが解ける。");
        }

        /// <summary>
        /// <b>入力の参照は捨てない。</b> 捨てるのは Disable のときだけ——
        /// 入場直後の主人公は、その場で操作を受け取らなければならない。
        ///
        /// ここが「捨てすぎない」側の検査である。同じ中立化を流用して
        /// <see cref="PlayerStateController.ResetToNeutral"/> を呼ぶ実装にすると、
        /// 入場した瞬間に操作不能になる（次のフレームに Provider から拾い直すまで）。
        /// </summary>
        [Test]
        public void ResetForAreaEntry_KeepsTheInputWired()
        {
            PlayerStateController controller = NewController(out _, out _);
            var input = new PlayerInputState();
            SetPrivate(controller, "_input", input);

            controller.ResetForAreaEntry();

            Assert.AreSame(input, GetPrivate(controller, "_input"),
                "入場ごとの中立化は入力を外さない。");

            controller.ResetToNeutral();

            Assert.IsNull(GetPrivate(controller, "_input"),
                "対照：Disable 用の中立化は外す（外すのはこちらの仕事）。");
        }

        /// <summary>
        /// 押しっぱなしの必殺ボタンは<b>離すまで再チャージしない</b>。
        ///
        /// 出入口の跳ね返り止め（<c>DisarmOnArrival</c>）と同じ考え方である——
        /// <b>押しっぱなしを新しい押下と解釈しない</b>。
        /// </summary>
        [Test]
        public void ResetForAreaEntry_LocksTheSpecialUntilTheButtonIsReleased()
        {
            PlayerStateController controller = NewController(out _, out _);
            SetPrivate(controller, "_specialRequiresRelease", false);

            controller.ResetForAreaEntry();

            Assert.IsTrue((bool)GetPrivate(controller, "_specialRequiresRelease"),
                "ボタンを離すまでロックする。");
        }

        /// <summary>
        /// <b>死亡再開の中立化と、入場の中立化は同じものである</b>（工程 P55-10b で気付いた）。
        ///
        /// 違うのは呼び出し側で、再開はこのあと全回復を、入場は Snapshot を適用する。
        /// 同じ処理を 2 か所に書くと片方だけ直るので、委譲が切れていないことを見ておく。
        /// </summary>
        [Test]
        public void ResetForCampaignRespawn_DoesTheSameAsAreaEntry()
        {
            PlayerStateController a = NewController(out PlayerMotor motorA, out PlayerFacing facingA);
            Machine(a).Tick(true, true, true);
            motorA.SpeedMultiplier = 0.4f;
            facingA.IsLocked = true;
            SetPrivate(a, "_specialRequiresRelease", false);
            a.ResetForAreaEntry();

            GameObject second = new GameObject("PlayerB");
            try
            {
                var b = second.AddComponent<PlayerStateController>();
                var motorB = second.AddComponent<PlayerMotor>();
                var facingB = second.AddComponent<PlayerFacing>();
                SetPrivate(b, "_motor", motorB);
                SetPrivate(b, "_facing", facingB);
                Machine(b).Tick(true, true, true);
                motorB.SpeedMultiplier = 0.4f;
                facingB.IsLocked = true;
                SetPrivate(b, "_specialRequiresRelease", false);

                b.ResetForCampaignRespawn();

                Assert.AreEqual(a.Current, b.Current, "状態が同じ。");
                Assert.AreEqual(motorA.SpeedMultiplier, motorB.SpeedMultiplier, "速度倍率が同じ。");
                Assert.AreEqual(facingA.IsLocked, facingB.IsLocked, "向きロックが同じ。");
                Assert.AreEqual(
                    GetPrivate(a, "_specialRequiresRelease"),
                    GetPrivate(b, "_specialRequiresRelease"),
                    "必殺のロックも同じ。");
            }
            finally
            {
                Object.DestroyImmediate(second);
            }
        }

        // ------------------------------------------------ 窓口（AreaActorTransferPort）

        /// <summary>
        /// 窓口は<b>主人公の根から</b>状態を引いて中立化する（Scene 側の配線に頼らない）。
        /// </summary>
        [Test]
        public void ThePort_NeutralizesThroughThePlayerRoot()
        {
            PlayerStateController controller = NewController(out PlayerMotor motor, out _);
            var port = _go.AddComponent<AreaActorTransferPort>();
            port.BindRoots(_go.transform, null);

            Machine(controller).Tick(true, true, true);
            motor.SpeedMultiplier = 0.4f;

            port.ResetForAreaEntry();

            Assert.AreEqual(1, port.AreaEntryResetCount, "数えている。");
            Assert.AreEqual(PlayerState.Idle, controller.Current, "主人公が中立へ戻る。");
            Assert.AreEqual(1f, motor.SpeedMultiplier, "速度倍率も戻る。");
        }

        /// <summary>
        /// 何も配線されていない窓口でも落ちない。
        ///
        /// 中立化は<b>失敗しうる段より前</b>に呼ぶので、ここで例外を出すと
        /// 「配線が足りない Scene では入場できない」になる。足りない配線は
        /// 復元（<c>TryApply</c>）が理由付きで断るのが役割分担である。
        /// </summary>
        [Test]
        public void ThePort_WithNothingWired_DoesNotThrow()
        {
            _go = new GameObject("EmptyPort");
            var port = _go.AddComponent<AreaActorTransferPort>();

            Assert.DoesNotThrow(() => port.ResetForAreaEntry());
            Assert.AreEqual(1, port.AreaEntryResetCount, "呼ばれたことは数える。");
        }

        // ---------------------------------------------------------------- 補助

        private PlayerStateController NewController(out PlayerMotor motor, out PlayerFacing facing)
        {
            _go = new GameObject("P55AreaEntryResetPlayer");
            var controller = _go.AddComponent<PlayerStateController>();
            motor = _go.AddComponent<PlayerMotor>();
            facing = _go.AddComponent<PlayerFacing>();
            SetPrivate(controller, "_motor", motor);
            SetPrivate(controller, "_facing", facing);
            return controller;
        }

        private static PlayerStateMachine Machine(PlayerStateController controller) =>
            (PlayerStateMachine)GetPrivate(controller, "_machine");

        private static void SetPrivate(object target, string field, object value) =>
            Field(target, field).SetValue(target, value);

        private static object GetPrivate(object target, string field) =>
            Field(target, field).GetValue(target);

        private static FieldInfo Field(object target, string field)
        {
            FieldInfo f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(f, "フィールド '" + field + "' が見つかりません（名前が変わった？）。");
            return f;
        }
    }
}
