using System.Collections.Generic;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// 1 エリアに置いた<b>複数の遭遇戦</b>をまとめる窓口（P6A-04。仕様 §11「B に独立した遭遇戦二つ」）。
    ///
    /// P5 は 1 エリア 1 遭遇戦で、受付条件・Interact・仲間の活動 Context はそれぞれ 1 つの
    /// <see cref="AreaEncounterRunner"/> を見ていた。ここはそれらに「どれか 1 つでも戦闘中か」を同じ契約で見せる。
    /// 各遭遇戦は独立 ID・独立の戦闘セッションを持ち、クリアも別々に記録される。
    ///
    /// <b>同時に 2 つは始まらない</b>（開始は探索中だけ。片方が戦闘中ならもう片方の Trigger は WrongMode で断られる）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaEncounterGroup : MonoBehaviour, IRetreatableEncounterState, IAreaEncounterActivitySource
    {
        [Tooltip("このエリアの遭遇戦。")]
        [SerializeField] private List<AreaEncounterRunner> _runners = new List<AreaEncounterRunner>();

        /// <summary>遭遇戦（読み取り専用）。</summary>
        public IReadOnlyList<AreaEncounterRunner> Runners => _runners;

        /// <summary>配線する（Builder・テスト）。</summary>
        public void Bind(IEnumerable<AreaEncounterRunner> runners)
        {
            _runners = new List<AreaEncounterRunner>();
            if (runners == null)
            {
                return;
            }

            foreach (AreaEncounterRunner r in runners)
            {
                if (r != null)
                {
                    _runners.Add(r);
                }
            }
        }

        /// <summary>戦闘中の遭遇戦（無ければ null）。</summary>
        public AreaEncounterRunner Engaged
        {
            get
            {
                for (int i = 0; i < _runners.Count; i++)
                {
                    if (_runners[i] != null && _runners[i].IsEncounterActive)
                    {
                        return _runners[i];
                    }
                }

                return null;
            }
        }

        /// <inheritdoc />
        public bool IsEncounterActive => Engaged != null;

        /// <inheritdoc />
        public bool AllowsRetreatNow
        {
            get
            {
                AreaEncounterRunner engaged = Engaged;
                return engaged != null && engaged.AllowsRetreatNow;
            }
        }

        /// <inheritdoc />
        public CombatSessionState? ActivitySession
        {
            get
            {
                AreaEncounterRunner engaged = Engaged;
                return engaged != null ? engaged.ActivitySession : null;
            }
        }

        /// <summary>全遭遇戦の敵の攻撃力の倍率（campaign のテスト専用の調整。P6A）。</summary>
        public void SetEnemyAttackPowerScale(float scale)
        {
            for (int i = 0; i < _runners.Count; i++)
            {
                _runners[i]?.SetEnemyAttackPowerScale(scale);
            }
        }

        /// <summary>入場のたび：記録からクリア済みを復元する。</summary>
        public void RestoreAllFromRecord()
        {
            for (int i = 0; i < _runners.Count; i++)
            {
                _runners[i]?.RestoreFromRecord();
            }
        }

        /// <summary>
        /// 入場のたび：挑戦途中のまま置いていった遭遇戦を捨てる（撤退の確定。次回は最初の Wave から）。
        /// 捨てた数を返す。
        /// </summary>
        public int AbandonUnfinished()
        {
            int n = 0;
            for (int i = 0; i < _runners.Count; i++)
            {
                if (_runners[i] != null && _runners[i].AbandonChallenge())
                {
                    n++;
                }
            }

            return n;
        }
    }
}
