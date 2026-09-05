using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の所属・同定・状態を読み取るための共通窓口（P4-01）。犬丸（P4-02〜）と、後続の猿・雉（P8）が同じ契約で載る。
    ///
    /// 既存の戦闘契約とは重複させない：被弾は <see cref="IDamageable"/>、攻撃者同定は <see cref="ICombatActor"/>、
    /// 敵 AI のヘイト候補は <c>IThreatTarget</c>、肩代わりは <c>IGuardianReceiver</c> がそれぞれ担う。本契約は
    /// 「この Actor は誰で、いま何をしていて、場に居るのか」だけを公開し、仲間を扱う側（切替・HUD・探索指示・Validator）が
    /// 具象クラスへ依存せずに済むようにする。
    /// </summary>
    public interface ICompanionActor
    {
        /// <summary>Actor 同定 ID（<see cref="CompanionStateChanged.ActorId"/> と対応）。</summary>
        int ActorId { get; }

        /// <summary>陣営。仲間は常に <see cref="CombatFaction.Ally"/>。</summary>
        CombatFaction Faction { get; }

        /// <summary>役割（犬・猿・雉）。ヘイト補正と戦闘上の役割差の切り分けに用いる。</summary>
        CompanionRole Role { get; }

        /// <summary>基礎データ（未割当なら null。数値は必ずここを正本とする）。</summary>
        CompanionData Data { get; }

        /// <summary>現在状態。</summary>
        CompanionState State { get; }

        /// <summary>現在位置（World）。</summary>
        Vector3 WorldPosition { get; }

        /// <summary>戦闘不能（<see cref="CompanionState.Down"/>）か。ヘイト対象からの即時除外に用いる。</summary>
        bool IsDown { get; }

        /// <summary>退場（未加入・交代待機・Scene 離脱）中か。true の間は戦闘にも探索にも参加しない。</summary>
        bool IsAway { get; }

        /// <summary>状態遷移の通知チャネル（表示・切替・Debug が購読）。</summary>
        CompanionStateChannel States { get; }
    }
}
