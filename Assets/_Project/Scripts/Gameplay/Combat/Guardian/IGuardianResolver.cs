namespace Momotaro.Gameplay.Combat.Guardian
{
    /// <summary>
    /// 「この命中を誰かが肩代わりするか」を判断する契約（P4-01）。守護対象（主人公）と同じ GameObject に置かれ、
    /// 被弾解決の最終 Damage 直前に一度だけ問い合わせられる。
    ///
    /// 距離・クールダウン・守護モードの有無・対象選択といった判断はすべて実装側（P4-05 の守護コンポーネント）の責務で、
    /// 被弾解決側は結果だけを受け取る。未配線（実装が存在しない）なら肩代わりは起こらず、既存の被弾挙動は一切変わらない。
    /// </summary>
    public interface IGuardianResolver
    {
        /// <summary>
        /// 肩代わりする守護者を解決する。引き受け手がいれば true と <paramref name="guardian"/> を返す。
        /// 本メソッドは判断のみを行い、命中の適用・クールダウンの消費といった副作用は
        /// 呼び出し側が肩代わりを実行した後に <see cref="NotifyTransferred"/> で通知してから行う。
        /// </summary>
        bool TryResolveGuardian(in HitInfo hit, out IGuardianReceiver guardian);

        /// <summary>
        /// 肩代わりが実際に成立し、守護者へ命中を渡し終えたことを通知する（クールダウン開始等の副作用はここで行う）。
        /// <see cref="TryResolveGuardian"/> が true を返しても、守護者が引き受け不可であれば本メソッドは呼ばれない。
        /// </summary>
        void NotifyTransferred(in HitInfo transferred, IGuardianReceiver guardian);
    }
}
