namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中解決の型付き結果（P2-01 受入修正）。どの命中（<see cref="HitId"/>）が、誰から誰へ、
    /// どの種別（<see cref="HitResultKind"/>）で解決され、実際に適用された HP／体幹／ひるませ値
    /// （<see cref="AppliedDamage"/>）がいくつだったかを保持する不変の値型。
    ///
    /// P2-01 では結果を運ぶ契約のみを定義し、実際のダメージ解決は行わない。生成には種別ごとの
    /// ファクトリを用い、回避・棄却は適用値 0（<see cref="HitDamage.None"/>）とする。
    /// </summary>
    public readonly struct HitResult
    {
        /// <summary>結果種別。</summary>
        public HitResultKind Kind { get; }

        /// <summary>対象となった命中の同一性。</summary>
        public HitId HitId { get; }

        /// <summary>攻撃者。</summary>
        public ICombatActor Attacker { get; }

        /// <summary>被弾対象。</summary>
        public IDamageable Target { get; }

        /// <summary>実際に適用された HP／体幹／ひるませ値（回避・棄却は 0）。</summary>
        public HitDamage AppliedDamage { get; }

        /// <summary>すべての要素を指定して生成する。</summary>
        public HitResult(HitResultKind kind, HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage)
        {
            Kind = kind;
            HitId = hitId;
            Attacker = attacker;
            Target = target;
            AppliedDamage = appliedDamage;
        }

        /// <summary>ダメージ結果を生成する。</summary>
        public static HitResult Damage(HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage)
        {
            return new HitResult(HitResultKind.Damage, hitId, attacker, target, appliedDamage);
        }

        /// <summary>通常ガード結果を生成する（HP は 0、体幹・ひるませは適用値に従う）。</summary>
        public static HitResult Guard(HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage)
        {
            return new HitResult(HitResultKind.Guard, hitId, attacker, target, appliedDamage);
        }

        /// <summary>ジャストガード結果を生成する。</summary>
        public static HitResult JustGuard(HitId hitId, ICombatActor attacker, IDamageable target, HitDamage appliedDamage)
        {
            return new HitResult(HitResultKind.JustGuard, hitId, attacker, target, appliedDamage);
        }

        /// <summary>回避結果を生成する（適用値 0）。</summary>
        public static HitResult Evade(HitId hitId, ICombatActor attacker, IDamageable target)
        {
            return new HitResult(HitResultKind.Evade, hitId, attacker, target, HitDamage.None);
        }

        /// <summary>棄却結果を生成する（適用値 0）。</summary>
        public static HitResult Rejected(HitId hitId, ICombatActor attacker, IDamageable target)
        {
            return new HitResult(HitResultKind.Rejected, hitId, attacker, target, HitDamage.None);
        }
    }
}
