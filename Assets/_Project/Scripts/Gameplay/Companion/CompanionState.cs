namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の基本状態（P4-01）。追従・戦闘・防御・守護・被弾・退場を一つの語彙で表す。遷移優先度は
    /// <see cref="CompanionStatePriority"/> が持ち、実際の遷移駆動（追従 P4-02／戦闘 P4-03／防御 P4-04／
    /// 守護 P4-05／Down・復帰 P4-06／探索 P4-07）は後続タスクが接続する。
    ///
    /// 敵（<c>EnemyState</c>）とは意図的に別語彙にする。仲間には巡回・帰還・スタンが無く、代わりに追従・ワープ・
    /// 守護・退場・復帰がある。共有するのは「被弾由来の強制状態を優先度で割り込む」という構造だけとする。
    /// </summary>
    public enum CompanionState
    {
        /// <summary>待機（その場に留まる。プレイヤー指示・イベント中）。</summary>
        Idle = 0,

        /// <summary>主人公への追従（隊列位置へ移動する非戦闘の既定状態）。</summary>
        Follow = 1,

        /// <summary>距離超過・経路失敗によるワープ（追従へ復帰するための瞬間的な再配置。P4-02）。</summary>
        Warp = 2,

        /// <summary>戦闘対象への接近。</summary>
        Chase = 3,

        /// <summary>攻撃予兆。</summary>
        AttackPrepare = 4,

        /// <summary>攻撃判定中。</summary>
        AttackActive = 5,

        /// <summary>攻撃後隙。</summary>
        AttackRecovery = 6,

        /// <summary>ガード。</summary>
        Guard = 7,

        /// <summary>回避。</summary>
        Evade = 8,

        /// <summary>守護（「かばう」の割込み中。主人公の被弾を肩代わりする体勢。P4-05）。</summary>
        Protect = 9,

        /// <summary>ひるみ（のけぞり）。</summary>
        Stagger = 10,

        /// <summary>戦闘不能（ダウン）。</summary>
        Down = 11,

        /// <summary>ダウンからの復帰待ち（再合流までの猶予。P4-06）。</summary>
        Recovering = 12,

        /// <summary>退場（未加入・交代待機・Scene 離脱。戦闘にも探索にも参加しない）。</summary>
        Away = 13,

        /// <summary>イベント強制（会話・演出）。</summary>
        Event = 14,
    }
}
