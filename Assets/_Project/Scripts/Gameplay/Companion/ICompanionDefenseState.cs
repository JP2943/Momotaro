namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の防御状態を被弾側へ知らせる契約（P4-04）。判断（いつ構えるか・いつ退避するか）は実装側が持ち、
    /// 被弾の解決（<see cref="CompanionHitReceiver"/>）は本契約の読み取りだけを行う。
    ///
    /// 主人公の <c>IGuardState</c> ／ <c>IEvadeState</c>、敵の <c>IEnemyDefenseState</c> と同じ役割で、
    /// 被弾解決とガード・回避の判断を分離するためにある。未実装（本契約を持つコンポーネントが付いていない）なら、
    /// 仲間は構えも退避もしない、というだけで被弾解決は成立する。
    ///
    /// 実処理は P4-04b（ガード／回避判断）で実装する。ここでは解決順に穴を空けておくだけで、空実装は置かない。
    /// </summary>
    public interface ICompanionDefenseState
    {
        /// <summary>ガード中か（<c>Guardable</c> な命中を前方 180°で防ぐ）。</summary>
        bool IsGuarding { get; }

        /// <summary>回避の無敵中か（種別を問わず命中を無効化する）。</summary>
        bool IsEvadeInvulnerable { get; }
    }
}
