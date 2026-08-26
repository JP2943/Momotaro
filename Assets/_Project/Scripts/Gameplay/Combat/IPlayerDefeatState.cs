namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// プレイヤーの死亡（Defeated）状態の被参照契約（Phase3.5 P3.5-02）。命中解決（<see cref="IDamageable"/> 実装）が致死で
    /// ラッチし、状態機械（PlayerStateController）と敵の認識・脅威側（PerceptionTargetBinder）が具象へ依存せずに参照する。
    ///
    /// - 状態機械側：<see cref="IsDefeated"/> を最優先状態として全入力・全行動を停止する。
    /// - 認識・脅威側：<see cref="IsDefeated"/> を対象の非活動（IsActive=false）／ダウン（IsDown=true）へ反映し、敵の新規追跡・
    ///   攻撃を止める（既存の EnemyThreatTable／EnemyAttackController が IsActive/IsDown で即時切替する契約に接続する）。
    /// </summary>
    public interface IPlayerDefeatState
    {
        /// <summary>致死により死亡が確定したか（一度 true になったら復帰しない。Retry は Scene 再読込で初期化）。</summary>
        bool IsDefeated { get; }
    }
}
