namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 敵撃破を購読・集計するための供給契約（Phase3.5 P3.5-03）。CombatSessionController が具象 EnemyActor に依存せず、登録した敵の
    /// 撃破（<see cref="EnemyDefeatChannel"/>）を型付き購読し、生存数を管理できるようにする。<see cref="EnemyActor"/> が実装する。
    /// </summary>
    public interface IEnemyDefeatSource
    {
        /// <summary>撃破（Down 確定）の通知チャネル。Session はここへ購読する。</summary>
        EnemyDefeatChannel Defeats { get; }

        /// <summary>被弾同定 ID（撃破通知の <see cref="EnemyDefeatedEvent.EnemyId"/> と対応。重複判定の鍵）。</summary>
        int DamageableId { get; }

        /// <summary>既に撃破済みか（登録時点で死亡している敵を生存数へ数えないため）。</summary>
        bool IsDefeated { get; }
    }
}
