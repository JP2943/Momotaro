using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 被弾側の通常ガード状態を表す読み取り専用契約（Phase2 P2-06）。命中解決側（<see cref="IDamageable"/> 実装）が
    /// 「今ガード中か」「ガード方向はどちらか」だけを取得するための最小契約。ガード方向は押下時に固定された前方で、
    /// 前方 180°（<see cref="GuardGeometry"/>）の内外判定に用いる。
    ///
    /// Input System / Animator / Scene には依存しない。Player は <c>PlayerStateController</c> が実装し、Phase 3 の
    /// 敵ガードも同じ契約へ発展できる（ガード専用の Combat 経路を作らない）。
    /// </summary>
    public interface IGuardState
    {
        /// <summary>通常ガード中か（GuardIdle/GuardMove 等）。</summary>
        bool IsGuarding { get; }

        /// <summary>ガード方向（押下時に固定した前方。World XZ 想定）。</summary>
        Vector3 GuardForward { get; }
    }
}
