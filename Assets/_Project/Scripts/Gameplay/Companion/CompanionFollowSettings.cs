using Momotaro.Data.Characters;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 追従判断の設定 Snapshot（P4-02）。原本 <see cref="CompanionData"/> から必要な値だけを複製した不変値型で、
    /// 判断中に SO 原本が変化しても挙動が揺れない（本書 §2.2）。
    ///
    /// 停止と再開に別のしきい値を持つ（<see cref="StopDistance"/> &lt; <see cref="ResumeDistance"/>）のは、
    /// 隊列位置の境目で「進む・止まる」を毎フレーム往復させないため。
    /// </summary>
    public readonly struct CompanionFollowSettings
    {
        /// <summary>隊列の基準間隔（m）。</summary>
        public float Spacing { get; }

        /// <summary>この距離以内まで近づいたら停止する（m）。</summary>
        public float StopDistance { get; }

        /// <summary>停止中にこの距離以上離れたら再び追従を始める（m）。停止距離より大きいこと。</summary>
        public float ResumeDistance { get; }

        /// <summary>この距離以上離れたら移動をあきらめてワープする（m。距離超過）。</summary>
        public float WarpDistance { get; }

        /// <summary>移動しているのに近づけない状態がこの秒数続いたらワープする（経路失敗）。0 以下で無効。</summary>
        public float StuckSeconds { get; }

        /// <summary>「近づけていない」と判定する 1 Tick あたりの前進量のしきい値（m）。</summary>
        public float StuckProgressEpsilon { get; }

        /// <summary>既定の前進量しきい値（1 Tick で 1mm も詰められなければ停滞とみなす）。</summary>
        public const float DefaultStuckProgressEpsilon = 0.001f;

        /// <summary>各値を指定して生成する（負値は 0 に丸める）。</summary>
        public CompanionFollowSettings(float spacing, float stopDistance, float resumeDistance,
            float warpDistance, float stuckSeconds, float stuckProgressEpsilon = DefaultStuckProgressEpsilon)
        {
            Spacing = spacing < 0f ? 0f : spacing;
            StopDistance = stopDistance < 0f ? 0f : stopDistance;
            ResumeDistance = resumeDistance < 0f ? 0f : resumeDistance;
            WarpDistance = warpDistance < 0f ? 0f : warpDistance;
            StuckSeconds = stuckSeconds < 0f ? 0f : stuckSeconds;
            StuckProgressEpsilon = stuckProgressEpsilon < 0f ? 0f : stuckProgressEpsilon;
        }

        /// <summary>Data 原本から Snapshot を生成する。null なら既定値（<see cref="Default"/>）。</summary>
        public static CompanionFollowSettings From(CompanionData data)
        {
            if (data == null)
            {
                return Default;
            }

            return new CompanionFollowSettings(
                data.FollowSpacing,
                data.FollowStopDistance,
                data.FollowResumeDistance,
                data.WarpDistance,
                data.StuckSeconds);
        }

        /// <summary>Data 未割当時の安全な既定値（素材・Data が無くても例外なく追従できる）。</summary>
        public static CompanionFollowSettings Default =>
            new CompanionFollowSettings(1.6f, 0.35f, 0.8f, 8f, 1.5f);
    }

    /// <summary>追従判断の入力（P4-02）。1 Tick ぶんの観測値をまとめた不変値型。</summary>
    public readonly struct CompanionFollowInput
    {
        /// <summary>主人公（追従対象）の現在位置。</summary>
        public Vector3 LeaderPosition { get; }

        /// <summary>主人公の論理前方（XZ。隊列の向きを決める）。</summary>
        public Vector3 LeaderForward { get; }

        /// <summary>仲間自身の現在位置。</summary>
        public Vector3 SelfPosition { get; }

        /// <summary>隊列番号（0 始まり）。</summary>
        public int SlotIndex { get; }

        public CompanionFollowInput(Vector3 leaderPosition, Vector3 leaderForward, Vector3 selfPosition, int slotIndex)
        {
            LeaderPosition = leaderPosition;
            LeaderForward = leaderForward;
            SelfPosition = selfPosition;
            SlotIndex = slotIndex;
        }
    }
}
