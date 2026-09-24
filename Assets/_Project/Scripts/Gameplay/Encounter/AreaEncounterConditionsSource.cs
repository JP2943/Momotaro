using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// §8.2 手順 2 の受付条件を Scene の実体から読む（P5-07）。
    ///
    /// 未配線の参照は<b>「分からない＝始めない」</b>へ倒す。ここを「分からないから通す」にすると、
    /// 配線漏れのまま戦闘が始まり、AreaReady 前の Actor へ敵をぶつけることになる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaEncounterConditionsSource : MonoBehaviour, IAreaEncounterConditions
    {
        [Tooltip("このエリアの初期化状態（AreaReady の供給元）。")]
        [SerializeField] private AreaContext _area;

        [Tooltip("主人公の生存。")]
        [SerializeField] private PlayerVitalsHolder _vitals;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _area != null && _vitals != null;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(AreaContext area, PlayerVitalsHolder vitals)
        {
            if (area != null)
            {
                _area = area;
            }

            if (vitals != null)
            {
                _vitals = vitals;
            }
        }

        /// <inheritdoc />
        public bool IsAreaReady => _area != null && _area.IsAreaReady;

        /// <inheritdoc />
        public bool IsExploration =>
            GameModeProvider.Current != null && GameModeProvider.Current.Current == GameMode.Exploration;

        /// <inheritdoc />
        public bool IsPlayerAlive => _vitals != null && !_vitals.IsDefeated;

        /// <summary>
        /// エリア遷移が走っているか。
        ///
        /// 遷移の所有者（Infrastructure の遷移サービス）を Gameplay から参照できないので、
        /// <b>遷移が止めている Gameplay 時計</b>を見る（§6.2 手順 3 で凍結される）。
        /// 受理済みの遷移中はここが true になり、Trigger の開始要求は通らない（§8.3 の競合表）。
        /// </summary>
        public bool IsTransitioning => GameplayClockProvider.IsFrozen;
    }
}
