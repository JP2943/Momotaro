using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// §6.1 の受付条件を Scene の実体から読む（P5-03b）。
    ///
    /// <b>受付条件の正本は §6.1 ひとつ</b>で（裁定 6）、Snapshot 側の Capture 前提はその帰結。
    /// だから判定はここ 1 か所に集め、Actor 値の採取側は別の受付判定を持たない。
    ///
    /// 犬丸が Down／Away／調査中でも主人公の遷移は妨げない（§6.1）ので、仲間の状態は見ない。
    /// 未配線の参照は「分からない＝安全側」に倒し、遷移を許可しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransitionConditionsSource : MonoBehaviour, IAreaTransitionConditions
    {
        [Tooltip("このエリアの初期化状態（AreaReady の供給元）。")]
        [SerializeField] private AreaContext _area;

        [Tooltip("主人公の行動状態。攻撃・Guard・Step・Hurt・GuardBreak 中は遷移しない。")]
        [SerializeField] private PlayerStateController _player;

        [Tooltip("主人公の生存。")]
        [SerializeField] private PlayerVitalsHolder _vitals;

        [Tooltip("戦闘の活動状態（開始予約〜勝敗処理）。未配線なら戦闘なしとみなさず、遷移を許可しない。")]
        [SerializeField] private MonoBehaviour _encounterSource;

        private IAreaEncounterState _encounter;
        private bool _encounterResolved;

        /// <summary>配線する（Area 初期化担当・テストが呼ぶ）。</summary>
        public void Bind(AreaContext area, PlayerStateController player, PlayerVitalsHolder vitals,
            IAreaEncounterState encounter)
        {
            if (area != null)
            {
                _area = area;
            }

            if (player != null)
            {
                _player = player;
            }

            if (vitals != null)
            {
                _vitals = vitals;
            }

            if (encounter != null)
            {
                _encounter = encounter;
                _encounterSource = encounter as MonoBehaviour;
            }

            _encounterResolved = _encounter != null;
        }

        /// <summary>供給元がそろっているか（Scene 検査・診断用）。</summary>
        public bool IsWired => _area != null && _player != null && _vitals != null;

        /// <inheritdoc />
        public bool IsAreaReady => _area != null && _area.IsAreaReady;

        /// <inheritdoc />
        public GameMode Mode =>
            GameModeProvider.Current != null ? GameModeProvider.Current.Current : GameMode.Loading;

        /// <inheritdoc />
        public bool IsPlayerAlive => _vitals != null && !_vitals.IsDefeated;

        /// <inheritdoc />
        public bool IsPlayerBusy
        {
            get
            {
                if (_player == null)
                {
                    return true; // 分からないなら遷移させない。
                }

                return !_player.IsFreeToTravel;
            }
        }

        /// <inheritdoc />
        public bool IsEncounterActive
        {
            get
            {
                ResolveEncounter();

                // P5-07 で Encounter を載せるまでは未配線が正常なので、
                // 「配線が無い＝戦闘していない」で通す。配線があるのに壊れている場合だけ安全側へ倒す。
                return _encounter != null && _encounter.IsEncounterActive;
            }
        }

        private void ResolveEncounter()
        {
            if (_encounterResolved)
            {
                return;
            }

            _encounterResolved = true;
            _encounter = _encounterSource as IAreaEncounterState;
        }
    }

    /// <summary>
    /// Encounter が活動中か（開始予約中・戦闘中・勝敗処理中）を外へ見せる狭い契約。
    /// 実体は P5-07 で作る。ここでは遷移の受付条件が参照する語彙だけを置く。
    /// </summary>
    public interface IAreaEncounterState
    {
        /// <summary>開始予約中・戦闘中・勝敗処理中のいずれかか（§8.2 の Starting〜Resolving）。</summary>
        bool IsEncounterActive { get; }
    }
}
