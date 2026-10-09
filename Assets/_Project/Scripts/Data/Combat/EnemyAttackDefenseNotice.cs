namespace Momotaro.Data.Combat
{
    /// <summary>
    /// 敵攻撃の防御可否の注意書き（P6C 仕様 §6・§7）。表示・操作説明で同じ文言を使うための 1 か所。
    /// 通常のガード不能（打撃・斬撃）は「通常ガード不可・ジャスガ可能」、ジャスガも不可の例外は別の文言と記号にする
    /// （色だけに依存しない）。通常攻撃は注意書きなし。
    /// </summary>
    public static class EnemyAttackDefenseNotice
    {
        /// <summary>通常のガード不能の文言。</summary>
        public const string UnblockableJustGuardable = "通常ガード不可・ジャスガ可能";

        /// <summary>ジャスガも不可の例外の文言（理由を後ろに付ける）。</summary>
        public const string NoJustGuardException = "ガード不可・ジャスガ不可";

        /// <summary>文言を返す（注意書き不要なら空）。</summary>
        public static string Describe(EnemyAttackData data)
        {
            if (data == null || data.AttackClass != EnemyAttackClass.Unblockable)
            {
                return string.Empty;
            }

            if (data.IsJustGuardException)
            {
                string reason = data.JustGuardExceptionReason;
                return string.IsNullOrWhiteSpace(reason) ? NoJustGuardException : NoJustGuardException + "（" + reason + "）";
            }

            return UnblockableJustGuardable;
        }

        /// <summary>記号（通常のガード不能は「！」、例外は「×」、それ以外は空）。</summary>
        public static string Symbol(EnemyAttackData data)
        {
            if (data == null || data.AttackClass != EnemyAttackClass.Unblockable)
            {
                return string.Empty;
            }

            return data.IsJustGuardException ? "×" : "！";
        }
    }
}
