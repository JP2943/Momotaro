namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の戦闘判断（P4-03）。「近づくか・待つか・攻撃するか」だけを決める純粋関数で、Transform も Physics も
    /// 索敵結果の収集も行わない（対象は <see cref="CompanionTargetTracker"/> が決め、実行は
    /// <see cref="CompanionCombatController"/> が行う）。EditMode で決定的に検証できる。
    ///
    /// 規則は 3 つ。
    /// <list type="number">
    /// <item><description><b>接近</b>：攻撃開始距離より遠ければ近づく（待機を命じられていれば近づかない。P4-07B）。</description></item>
    /// <item><description><b>攻撃</b>：攻撃開始距離・使用角度の内側で、クールダウンが明けていれば攻撃する。</description></item>
    /// <item><description><b>待機</b>：内側でクールダウン待ち・角度待ちのときは、その場で対象へ向いて待つ。</description></item>
    /// </list>
    ///
    /// 攻撃の可否を判定するのは <see cref="CompanionAttackSettings.AttackStartDistance"/> であり、判定が届く距離
    /// （<see cref="CompanionAttackSettings.UseRange"/>）ではない。届く距離ぎりぎりで攻撃を始めると、予兆のあいだに
    /// 対象が離れて必ず空振りする（P4-03 受入で実際に起きた）。内側で始め、差分を予兆中の移動の余裕に充てる。
    ///
    /// 攻撃を持たない構成（<see cref="CompanionAttackSettings.HasAttack"/> が false）では
    /// <see cref="CompanionEngageDecision.Idle"/> を返す。攻撃できないのに敵へ寄っていくと、殴られるだけになるため。
    /// </summary>
    public static class CompanionEngagement
    {
        /// <summary>1 Tick ぶんの判断を返す。</summary>
        /// <param name="hasTarget">狙う対象を保持しているか。</param>
        /// <param name="canEngage">戦闘に参加できる状態か（ダウン・ひるみ・退場中は false）。</param>
        /// <param name="distance">対象までの水平距離（m）。</param>
        /// <param name="angleDegrees">自分の前方と対象方向の角度（度。0＝正対）。</param>
        /// <param name="settings">攻撃設定 Snapshot。</param>
        /// <param name="cooldownRemaining">クールダウンの残り秒（0 以下で攻撃可能）。</param>
        /// <param name="mayApproach">
        /// 敵へ<b>寄っていって</b>よいか（P4-07B。待機を命じられていれば false）。
        /// false でも間合いの内側なら攻撃・待機は行う。指示は「何をしに行くか」を決めるもので、
        /// 目の前に来た敵を殴るなという意味ではない。
        /// 既定 true は、指示の仕組みを持たない構成を従来どおり動かすため。
        /// </param>
        public static CompanionEngageDecision Decide(
            bool hasTarget,
            bool canEngage,
            float distance,
            float angleDegrees,
            in CompanionAttackSettings settings,
            float cooldownRemaining,
            bool mayApproach = true)
        {
            if (!hasTarget || !canEngage || !settings.HasAttack)
            {
                return CompanionEngageDecision.Idle;
            }

            if (distance > settings.AttackStartDistance)
            {
                // 寄ってはいけないなら、遠い敵には関与しない（Hold にすると戦闘扱いのまま
                // 追従が譲り続け、待機の姿勢へ入れなくなる）。
                return mayApproach ? CompanionEngageDecision.Chase : CompanionEngageDecision.Idle;
            }

            bool inAngle = settings.UseAngle <= 0f || angleDegrees <= settings.UseAngle;
            if (inAngle && cooldownRemaining <= 0f)
            {
                return CompanionEngageDecision.Attack;
            }

            return CompanionEngageDecision.Hold;
        }
    }
}
