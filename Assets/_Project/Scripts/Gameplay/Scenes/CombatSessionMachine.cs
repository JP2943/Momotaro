using System;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 戦闘試遊セッションの状態機（Phase3.5 P3.5-03。仕様書 §5 / Table4）。純粋クラスで MonoBehaviour 非依存にし、全遷移・不正遷移・
    /// 重複遷移を決定的に検証できる（PlayerStateMachine と同方針）。許可された遷移のみ適用し、変化した場合だけ true を返す。
    ///
    /// 許可遷移：
    ///  Preparing → Playing（第1 Wave 開始）／Intermission → Playing（次 Wave 開始）＝<see cref="StartWave"/>
    ///  Playing → Intermission（Wave 間休止）＝<see cref="ToIntermission"/>
    ///  Playing → Victory（全 Wave 完了）＝<see cref="ToVictory"/>
    ///  Playing/Intermission → Defeat（Player 死亡）＝<see cref="ToDefeat"/>
    ///  Victory/Defeat → Reloading（Retry）＝<see cref="ToReloading"/>
    /// これ以外は不正遷移として拒否する（false）。Reloading は終端で、Scene 再読込後に新しい Session が Preparing から始まる。
    /// </summary>
    public sealed class CombatSessionMachine
    {
        /// <summary>現在の状態。</summary>
        public CombatSessionState Current { get; private set; } = CombatSessionState.Preparing;

        /// <summary>状態が変化した瞬間のみ発火する。</summary>
        public event Action<CombatSessionState> StateChanged;

        /// <summary>Wave を開始する（Preparing または Intermission から Playing へ）。適用したら true。</summary>
        public bool StartWave() => TryMove(CombatSessionState.Playing, CombatSessionState.Preparing, CombatSessionState.Intermission);

        /// <summary>Wave 間休止へ入る（Playing → Intermission）。</summary>
        public bool ToIntermission() => TryMove(CombatSessionState.Intermission, CombatSessionState.Playing);

        /// <summary>勝利へ遷移する（Playing → Victory）。重複呼び出しは拒否（false）。</summary>
        public bool ToVictory() => TryMove(CombatSessionState.Victory, CombatSessionState.Playing);

        /// <summary>敗北へ遷移する（Playing/Intermission → Defeat）。重複呼び出しは拒否（false）。</summary>
        public bool ToDefeat() => TryMove(CombatSessionState.Defeat, CombatSessionState.Playing, CombatSessionState.Intermission);

        /// <summary>Scene 再読込へ遷移する（Victory/Defeat → Reloading）。二重要求は拒否（false）。</summary>
        public bool ToReloading() => TryMove(CombatSessionState.Reloading, CombatSessionState.Victory, CombatSessionState.Defeat);

        /// <summary>指定状態へ遷移可能か（適用はしない。診断・UI 用）。</summary>
        public bool CanEnter(CombatSessionState to)
        {
            switch (to)
            {
                case CombatSessionState.Playing:
                    return Current == CombatSessionState.Preparing || Current == CombatSessionState.Intermission;
                case CombatSessionState.Intermission:
                    return Current == CombatSessionState.Playing;
                case CombatSessionState.Victory:
                    return Current == CombatSessionState.Playing;
                case CombatSessionState.Defeat:
                    return Current == CombatSessionState.Playing || Current == CombatSessionState.Intermission;
                case CombatSessionState.Reloading:
                    return Current == CombatSessionState.Victory || Current == CombatSessionState.Defeat;
                default:
                    return false;
            }
        }

        private bool TryMove(CombatSessionState to, CombatSessionState from0)
        {
            if (Current != from0)
            {
                return false;
            }

            Current = to;
            StateChanged?.Invoke(to);
            return true;
        }

        private bool TryMove(CombatSessionState to, CombatSessionState from0, CombatSessionState from1)
        {
            if (Current != from0 && Current != from1)
            {
                return false;
            }

            Current = to;
            StateChanged?.Invoke(to);
            return true;
        }
    }
}
