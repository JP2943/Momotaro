using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 通常ヒットバック／ガードバックの外部変位を受け付ける Motor 契約（Phase3.5 P3.5-08A。仕様書 §7.4）。命中解決側（被弾者の
    /// <c>ReceiveHit</c>）が、方向・距離・時間を指定して押し出しを要求する。実装（PlayerMotor／EnemyMotor）は既存の Rigidbody 速度
    /// 経路で XZ のみ移動し、Y 座標を変えず、壁・障害物で停止する。必殺技の大きな Knockback（<c>IKnockbackReceiver</c>）とは別契約。
    /// </summary>
    public interface IReactionMotor
    {
        /// <summary>
        /// 指定方向（XZ へ平坦化）へ <paramref name="distance"/> を <paramref name="seconds"/> かけて押し出す。既存の押し出しは上書きする。
        /// 距離・時間が 0 以下、方向がゼロなら無処理。HP・状態・Y 座標の正本は変更しない（表示・体感のための移動）。
        /// </summary>
        void PushReaction(Vector3 direction, float distance, float seconds);

        /// <summary>進行中の押し出しを打ち切る（Disable・Defeated・Intermission・Retry・Scene 離脱で残留を残さない）。</summary>
        void ClearReaction();
    }
}
