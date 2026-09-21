namespace Momotaro.Gameplay.Companion
{
    /// <summary>始めようとしている行動の種類（v1.0 §8.3 の表の行）。</summary>
    public enum CompanionActionKind
    {
        /// <summary>自動ガード（危険を見て構える）。</summary>
        AutoGuard = 0,

        /// <summary>自動回避（ガード不能な危険を退避する）。</summary>
        AutoEvade = 1,

        /// <summary>自動攻撃の開始。</summary>
        AutoAttack = 2,

        /// <summary>守護の成立（主人公の被弾を肩代わりする）。</summary>
        GuardianTransfer = 3,

        /// <summary>探索の開始（P4-07。ここでは語彙と規則だけ先に置く）。</summary>
        Investigate = 4,
    }

    /// <summary>規則の答え。</summary>
    public enum CompanionActionVerdict
    {
        /// <summary>始めてよい（進行中の行動を止める必要はない）。</summary>
        Allowed = 0,

        /// <summary>進行中の行動を<b>中断してから</b>始めてよい。</summary>
        Interrupt = 1,

        /// <summary>始めてはいけない。</summary>
        Denied = 2,
    }

    /// <summary>
    /// どの状態でどの行動を始めてよいかの表（P4-FIX F02c。v1.0 §8.3）。
    ///
    /// F02b で入れた所有権（Follow &lt; Combat &lt; Defense &lt; Guardian）は「誰が強いか」しか言えない。
    /// たとえば<b>振っている最中（AttackActive）に自動ガードを始めない</b>という規則は、
    /// 防御のほうが強い持ち主である以上、所有権だけでは表現できない。
    /// 状態ごとの可否は状態で決めるしかないので、表としてここに 1 つだけ置く。
    ///
    /// 条件式を各駆動へ散らさないのは、散らすと必ず食い違うため。実際
    /// 「防御中は攻撃を始めない」は戦闘側に、「倒れていたら防御しない」は防御側にあって、
    /// <b>その逆（攻撃中に防御を始めない）はどこにも無かった</b>。
    ///
    /// 表に無い状態（Protect・Warp・Recovering など）は、v1.0 §8.3 の「表より優先される規則」に従って
    /// 個別に決めている。判断に迷う状態を既定で通さないのは、増えた状態が黙って戦闘扱いになるのを避けるため。
    ///
    /// <b>探索中の列（c8c0ddf §5「探索中の行動競合表」）。</b>探索の Moving／Investigating 中は、健康な犬丸も通常 AI の
    /// 行動所有権を探索へ渡している。そこで<b>自動 Follow／Chase／Attack／Guard／Evade の開始はすべて拒否</b>する。
    /// 探索を解くのは「危険候補」ではなく<b>実際の出来事</b>——実命中・明示の戦闘開始・会話／イベント・退場——で、
    /// それらは同期的に探索を中断・解放してから元の処理へ進む（受け口・守護・活動 Context・駆動の入口が担う）。
    /// 守護要求も探索所有中はこの表では Denied（守護側が先に探索を解放し、解放後の状態で改めて評価する）。
    /// 以前は「調査中は戦う・構える・避ける・庇うのいずれも中断として通る」としていたが、これは裁定の逆で、
    /// 危険情報だけで別系統の戦闘開始を作ってしまう（レビュー R2-05）。
    /// </summary>
    public static class CompanionActionRules
    {
        /// <summary>
        /// この状態でこの行動を始めてよいか。
        /// </summary>
        /// <param name="kind">始めようとしている行動。</param>
        /// <param name="current">いまの状態。</param>
        public static CompanionActionVerdict Evaluate(CompanionActionKind kind, CompanionState current)
        {
            // 表より優先される規則：場に居ない・倒れている・ひるんでいる・イベント中は何も始めない。
            // HP0 による Down はすべての通常行動を中断する。Stagger 中は通常行動も守護も始めない。
            if (current == CompanionState.Away
                || current == CompanionState.Down
                || current == CompanionState.Recovering
                || current == CompanionState.Stagger
                || current == CompanionState.Event)
            {
                return CompanionActionVerdict.Denied;
            }

            switch (kind)
            {
                case CompanionActionKind.AutoAttack:
                    return EvaluateAutoAttack(current);

                case CompanionActionKind.AutoGuard:
                    return EvaluateAutoGuard(current);

                case CompanionActionKind.AutoEvade:
                    return EvaluateAutoEvade(current);

                case CompanionActionKind.GuardianTransfer:
                    return EvaluateGuardianTransfer(current);

                case CompanionActionKind.Investigate:
                    return EvaluateInvestigate(current);

                default:
                    return CompanionActionVerdict.Denied;
            }
        }

        /// <summary>平常時（自由に次の行動を選べる）か。</summary>
        private static bool IsNeutral(CompanionState state)
        {
            return state == CompanionState.Idle
                || state == CompanionState.Follow
                || state == CompanionState.Chase;
        }

        /// <summary>
        /// 調査中か（c8c0ddf §5「探索中の行動競合表」の行）。
        ///
        /// <b>調査は平常時に含めない。</b>調査中の自動行動はすべて拒否で、調査を解くのは実際の出来事だけ。
        /// </summary>
        private static bool IsInvestigating(CompanionState state)
        {
            return state == CompanionState.Investigate;
        }

        /// <summary>
        /// 自動攻撃：平常時のみ。二重開始も、防御中・守護中・移動中・<b>調査中</b>の開始もしない（§5：拒否・予約しない）。
        /// </summary>
        private static CompanionActionVerdict EvaluateAutoAttack(CompanionState current)
        {
            return IsNeutral(current) ? CompanionActionVerdict.Allowed : CompanionActionVerdict.Denied;
        }

        /// <summary>
        /// 自動ガード：予兆・後隙なら攻撃を中断して構えてよいが、<b>判定中（AttackActive）は不可</b>。
        /// 振り切る前に構えに化けると、当たるはずの一撃が消える。回避中も不可。
        /// </summary>
        private static CompanionActionVerdict EvaluateAutoGuard(CompanionState current)
        {
            if (IsNeutral(current))
            {
                return CompanionActionVerdict.Allowed;
            }

            switch (current)
            {
                case CompanionState.AttackPrepare:
                case CompanionState.AttackRecovery:
                    return CompanionActionVerdict.Interrupt;

                case CompanionState.Guard:
                    return CompanionActionVerdict.Allowed; // 継続（終了判定は防御側が持つ）。

                default:
                    // AttackActive・Evade・Protect・Warp・Investigate（§5：探索表示と並行起動しない）。
                    return CompanionActionVerdict.Denied;
            }
        }

        /// <summary>
        /// 自動回避：ガードと同じく判定中は不可。ガード中はガードを解いて回避へ移ってよい。
        /// 回避中は<b>同じ回避を継続</b>する（無敵が切れただけで別行動へ移らない）。
        /// </summary>
        private static CompanionActionVerdict EvaluateAutoEvade(CompanionState current)
        {
            if (IsNeutral(current))
            {
                return CompanionActionVerdict.Allowed;
            }

            switch (current)
            {
                case CompanionState.AttackPrepare:
                case CompanionState.AttackRecovery:
                case CompanionState.Guard:
                    return CompanionActionVerdict.Interrupt;

                case CompanionState.Evade:
                    return CompanionActionVerdict.Allowed; // 同じ回避の継続。

                default:
                    // AttackActive・Protect・Warp・Investigate（§5：危険候補だけでは探索を解かない）。
                    return CompanionActionVerdict.Denied;
            }
        }

        /// <summary>
        /// 守護の成立：攻撃はどの段でも中断してよい（庇うほうが優先）。ガードも解いて移る。
        /// ただし<b>回避中は不可</b>（回避は動作全体で 1 行動。途中で庇いに化けない）。
        /// <b>探索所有中もそのままでは不可</b>（§5）。主人公への実命中では守護側が先に探索を同期解放し、
        /// 解放後の状態（Follow）で改めて評価する。距離・クールダウン・活動状態は守護側が別に見る。
        /// </summary>
        private static CompanionActionVerdict EvaluateGuardianTransfer(CompanionState current)
        {
            if (IsNeutral(current))
            {
                return CompanionActionVerdict.Allowed;
            }

            switch (current)
            {
                case CompanionState.AttackPrepare:
                case CompanionState.AttackActive:
                case CompanionState.AttackRecovery:
                case CompanionState.Guard:
                    return CompanionActionVerdict.Interrupt;

                case CompanionState.Protect:
                    return CompanionActionVerdict.Allowed; // 連続の可否はクールダウンが決める。

                default:
                    // Evade・Warp・Investigate。
                    return CompanionActionVerdict.Denied;
            }
        }

        /// <summary>
        /// 探索の開始：平常時のみ（v1.0 §8.3「探索開始：平常探索時のみ可」）。戦闘中かどうかは
        /// <see cref="CompanionActivity.CanInvestigate"/> が別に見る（Wave 幕間・開始待ち Encounter も戦闘中）。
        /// ここは<b>戦闘 Actor を探索へ引き渡してよい状態か</b>だけを決める。Down／Away の加入済み犬丸による探索は
        /// 独立した表示代理で行うため、この表を通らない（§8.3 補足）。
        ///
        /// <b>調査中の新しい調査は不可</b>（別の調査を実行中なら拒否し、現在の依頼を維持する。§4.3）。
        /// </summary>
        private static CompanionActionVerdict EvaluateInvestigate(CompanionState current)
        {
            return IsNeutral(current) ? CompanionActionVerdict.Allowed : CompanionActionVerdict.Denied;
        }
    }
}
