using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 敵が観測した「危険刺激」（Phase3 P3-10。§9「Evade：危険刺激から退避」「入力を直接読まない」）。プレイヤーの入力そのもの
    /// ではなく、観測可能な事象（攻撃の予備動作／判定中など）から作る不変値。<see cref="HasDanger"/> のとき、危険源から自分への
    /// 進行方向（<see cref="IncomingDirection"/>）と、通常ガードで受けられない危険か（<see cref="Unblockable"/>）を持つ。
    /// </summary>
    public readonly struct EnemyDangerStimulus
    {
        /// <summary>危険を観測しているか。</summary>
        public bool HasDanger { get; }

        /// <summary>危険源のワールド位置。</summary>
        public Vector3 SourcePosition { get; }

        /// <summary>危険源→自分の進行方向（XZ 正規化。命中方向と同じ向き）。回避の退避方向・ガード方向判定に使う。</summary>
        public Vector3 IncomingDirection { get; }

        /// <summary>通常ガードで受けられない危険か（例：ガード不能・背後）。true なら回避を優先する。</summary>
        public bool Unblockable { get; }

        public EnemyDangerStimulus(Vector3 sourcePosition, Vector3 incomingDirection, bool unblockable)
        {
            HasDanger = true;
            SourcePosition = sourcePosition;
            Vector3 d = incomingDirection;
            d.y = 0f;
            IncomingDirection = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
            Unblockable = unblockable;
        }

        /// <summary>危険なし。</summary>
        public static EnemyDangerStimulus None => default;
    }

    /// <summary>
    /// 危険刺激の観測契約（Phase3 P3-10）。プレイヤー入力を直接読まず、観測可能な危険（攻撃の予備動作／判定中など）を返す。
    /// 時刻・Camera・物理に依存する実装（<see cref="PhysicsEnemyDangerSense"/>）と、テスト用 Fake の双方を注入できる（§11）。
    /// </summary>
    public interface IEnemyDangerSense
    {
        /// <summary>自分の位置・前方から観測できる危険を返す（無ければ <see cref="EnemyDangerStimulus.None"/>）。</summary>
        EnemyDangerStimulus Sense(Vector3 selfPosition, Vector3 selfForward, int selfDamageableId);
    }
}
