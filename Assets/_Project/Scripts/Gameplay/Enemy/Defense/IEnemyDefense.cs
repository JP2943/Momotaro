namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 敵の防御状態を命中処理側（<see cref="EnemyActor"/>）が読み取る小さな契約（Phase3 P3-10）。具体的な Guard/Evade 駆動は
    /// <see cref="EnemyDefenseController"/> が持ち、Actor はこの読み取りのみで被ダメージ軽減・無敵を反映する（相互の直接依存を避ける）。
    /// </summary>
    public interface IEnemyDefenseState
    {
        /// <summary>今ガードを構えているか（前方命中を軽減する）。</summary>
        bool IsGuarding { get; }

        /// <summary>今回避の無敵中か（命中を無効化する）。</summary>
        bool IsEvadeInvulnerable { get; }

        /// <summary>今 防御行動（ガード構え／回避モーション）中か。Brain はこの間 移動・攻撃判断を防御へ委譲する。</summary>
        bool IsDefending { get; }
    }

    /// <summary>
    /// 撃破確定時に 1 回だけ呼ばれる後始末契約（Phase3 P3-10。§9「Down 時に攻撃・衝突・Slot を解除」）。攻撃制御・防御制御などが
    /// 実装し、<see cref="EnemyActor"/> が撃破時にまとめて呼ぶ（攻撃中断・Slot 解放・能力リセット等を各自が行う）。
    /// </summary>
    public interface IEnemyDefeatCleanup
    {
        /// <summary>所有敵が撃破された（Down 確定）ときに 1 回呼ばれる。</summary>
        void OnOwnerDefeated();
    }
}
