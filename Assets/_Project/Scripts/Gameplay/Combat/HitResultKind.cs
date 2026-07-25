namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中解決の結果種別（P2-01 受入修正）。1 回の命中がどう処理されたかを型で識別する。
    /// 実際の解決ロジック（ダメージ適用・ガード判定など）は後続 Task で実装し、
    /// P2-01 では結果を表す語彙と、それを運ぶ型付き契約のみを定義する。
    /// </summary>
    public enum HitResultKind
    {
        /// <summary>ダメージが適用された。</summary>
        Damage = 0,

        /// <summary>通常ガードで防御された。</summary>
        Guard = 1,

        /// <summary>ジャストガードが成立した。</summary>
        JustGuard = 2,

        /// <summary>無敵・回避などで命中が回避された。</summary>
        Evade = 3,

        /// <summary>陣営不一致・多重ヒット・不正など、命中として棄却された。</summary>
        Rejected = 4,
    }
}
