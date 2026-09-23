using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Navigation
{
    /// <summary>ワープ先を選べなかった理由（P5-05。仕様書 v1.1 §10.2）。</summary>
    public enum SafeWarpRejection
    {
        /// <summary>選べた。</summary>
        None = 0,

        /// <summary>そもそも今はワープしてよい状態ではない（攻撃・防御・被弾・Down／Away・探索占有）。</summary>
        NotAllowed = 1,

        /// <summary>候補が 1 つも与えられていない。</summary>
        NoCandidate = 2,

        /// <summary>すべての候補が危険だった（床でない・塞がっている・主人公と繋がっていない）。</summary>
        AllUnsafe = 3,
    }

    /// <summary>
    /// ワープ候補 1 点の安全性を調べる（P5-05。§10.2）。
    /// 実装は Infrastructure（物理・NavMesh）で、テストは Fake を差す。
    /// </summary>
    public interface IWarpCandidateProbe
    {
        /// <summary>そこは床・NavMesh の上か。</summary>
        bool IsOnNavigableGround(Vector3 position);

        /// <summary>そこに置くと壁・水・閉じた門に重なるか。</summary>
        bool IsBlocked(Vector3 position);

        /// <summary>
        /// 主人公と<b>同じ通行可能側</b>か（主人公への短い接続経路があるか。§10.2）。
        /// これが無いと、閉じた門の未開通側へ先回りさせてしまう。
        /// </summary>
        bool IsConnectedToLeader(Vector3 position, Vector3 leaderPosition);
    }

    /// <summary>
    /// 安全なワープ先を固定順で選ぶ（P5-05。仕様書 v1.1 §10.2）。
    ///
    /// <b>候補は与えられた順に見る。</b> §10.2 が「固定順で検査し」と言うのは、
    /// 同じ状況なら毎回同じ場所へ出るということ。近い順に並べ替えたりすると、
    /// 主人公が少し動いただけで出現位置が変わり、見ている側には理由が分からない。
    ///
    /// <b>候補が無ければ止まる。</b> §10.2 の「壁内へ強制ワープしない」がここの要点で、
    /// 「どこにも置けないから仕方なく最後の候補へ」という逃げ道を作らない。
    /// 呼び出し側は間隔を空けて再評価する。
    ///
    /// <b>経路計算が失敗したことは、閉門の向こうへ出てよい理由にならない</b>（§10.2 の 1 行目）。
    /// 主人公と繋がっているかを候補ごとに確かめるのはそのため。
    /// </summary>
    public static class SafeWarpSelector
    {
        /// <summary>
        /// 安全な候補を 1 つ選ぶ。
        /// </summary>
        /// <param name="candidates">主人公近傍の隊列候補（<b>固定順</b>）。</param>
        /// <param name="leaderPosition">主人公の位置。</param>
        /// <param name="warpAllowed">
        /// いまワープしてよいか。攻撃 Active・防御行動・被弾・Down・Away・探索占有中は false（§10.2）。
        /// </param>
        /// <param name="probe">1 点ずつの安全性を答える供給元。</param>
        public static bool TrySelect(
            IReadOnlyList<Vector3> candidates,
            Vector3 leaderPosition,
            bool warpAllowed,
            IWarpCandidateProbe probe,
            out Vector3 chosen,
            out SafeWarpRejection reason)
        {
            chosen = default;

            if (!warpAllowed)
            {
                reason = SafeWarpRejection.NotAllowed;
                return false;
            }

            if (candidates == null || candidates.Count == 0)
            {
                reason = SafeWarpRejection.NoCandidate;
                return false;
            }

            if (probe == null)
            {
                // 安全かどうかが分からない＝置かない（安全側へ倒す）。
                reason = SafeWarpRejection.AllUnsafe;
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                Vector3 candidate = candidates[i];

                if (!probe.IsOnNavigableGround(candidate))
                {
                    continue;
                }

                if (probe.IsBlocked(candidate))
                {
                    continue;
                }

                if (!probe.IsConnectedToLeader(candidate, leaderPosition))
                {
                    continue;
                }

                chosen = candidate;
                reason = SafeWarpRejection.None;
                return true;
            }

            reason = SafeWarpRejection.AllUnsafe;
            return false;
        }
    }
}
