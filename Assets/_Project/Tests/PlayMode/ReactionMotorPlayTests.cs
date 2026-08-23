using System.Collections;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3.5-08A：外部変位（ヒットバック）が実 Rigidbody 経路で XZ のみ動かし、Y 座標を変えず、時間経過で自然停止することを
    /// 実物理（<see cref="EnemyMotor"/>）で検証する（仕様書 §7.4：距離・時間を正本、Y 不変、壁停止は物理）。壁停止そのものは
    /// レイヤ・衝突設定に依存するため手動受入に委ね、本テストは XZ 移動・Y 不変・停止を確認する。
    /// </summary>
    public sealed class ReactionMotorPlayTests
    {
        [UnityTest]
        public IEnumerator Hitback_MovesXZ_KeepsY_ThenStops()
        {
            var go = new GameObject("EnemyMotorRig", typeof(Rigidbody), typeof(EnemyMotor));
            go.transform.position = new Vector3(0f, 1f, 0f);
            var motor = go.GetComponent<EnemyMotor>();
            var body = go.GetComponent<Rigidbody>();

            // Awake で body 構成（重力 off・Y 位置固定）済み。数フレーム進めて安定させる。
            yield return new WaitForFixedUpdate();
            float y0 = go.transform.position.y;
            float x0 = go.transform.position.x;

            ((IReactionMotor)motor).PushReaction(Vector3.right, 0.5f, 0.2f);

            // 0.2 秒（供給時間）＋余韻を進める。
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Vector3 p = go.transform.position;
            Assert.Greater(p.x - x0, 0.1f, "AttackDirection(+X) へ実移動する。");
            Assert.AreEqual(y0, p.y, 1e-3f, "Y 座標は不変（XZ のみ制御）。");

            Vector3 velAfter = body.linearVelocity;
            Assert.LessOrEqual(Mathf.Abs(velAfter.x), 0.05f, "供給時間経過後は停止する（残留速度なし）。");

            Object.Destroy(go);
            yield return null;
        }
    }
}
