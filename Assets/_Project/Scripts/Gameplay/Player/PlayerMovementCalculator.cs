using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// 入力から XZ 平面の移動速度ベクトルを求める純粋ロジック（Phase1 P1-03）。
    /// Vector2(x, y) を World の Vector3(x, 0, z) へ変換し、斜め入力を正規化して
    /// 斜め方向だけ速くならないようにする。時間刻み（dt）には依存しない。
    /// </summary>
    public static class PlayerMovementCalculator
    {
        /// <summary>
        /// 移動入力と速度から、XZ 平面上の速度ベクトルを返す。Y は常に 0。
        /// 入力の大きさが 1 を超える場合のみ正規化する（Stick の小入力はそのまま活かす）。
        /// </summary>
        public static Vector3 ToPlanarVelocity(Vector2 input, float speed)
        {
            var direction = new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude > 1f)
            {
                direction = direction.normalized;
            }

            return direction * speed;
        }
    }
}
