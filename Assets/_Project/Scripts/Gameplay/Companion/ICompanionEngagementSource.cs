namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間が「いま戦闘で移動を握っているか」を追従側へ知らせる最小契約（P4-03）。
    ///
    /// 追従（<see cref="CompanionFollowController"/>）と戦闘（<see cref="CompanionCombatController"/>）は
    /// 同じ <see cref="CompanionMotor"/> を使う。両方が毎フレーム移動先を書くと、隊列位置と敵の間で震えてしまう。
    /// そこで「戦闘中は追従が譲る」という一方向の規則にし、追従側は本契約だけを見る（戦闘側の具象へ依存しない）。
    /// </summary>
    public interface ICompanionEngagementSource
    {
        /// <summary>戦闘行動（接近・待機・攻撃）で移動を握っているか。true の間、追従は移動指示を出さない。</summary>
        bool IsEngaged { get; }
    }
}
