namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// ダメージ（命中）を受け付ける側の契約（P2-01）。通常攻撃・ガード・ジャストガード・
    /// ステップ・必殺技のいずれも、同じ <see cref="HitInfo"/> を通してこの受付口へ命中を渡す。
    ///
    /// P2-01 では契約定義のみで、実際のダメージ適用（HP 減算・体幹・ひるみ・スタン等）は実装しない（依頼 §10）。
    /// 具体的な受付実装は主人公・仲間・敵それぞれの後続タスクで行う。
    /// </summary>
    public interface IDamageable
    {
        /// <summary>この対象を同定する識別子。多重ヒット防止のキーに用いる。</summary>
        int DamageableId { get; }

        /// <summary>
        /// 命中を受け付ける。<paramref name="hit"/> は実行時の命中情報（攻撃データ原本とは分離）。
        /// P2-01 時点では呼び出し契約のみを定め、数値適用は行わない。
        /// </summary>
        /// <param name="hit">解決済みの命中情報。</param>
        void ReceiveHit(in HitInfo hit);
    }
}
