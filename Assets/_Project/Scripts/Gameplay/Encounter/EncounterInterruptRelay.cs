using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Companion.Investigation;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// 戦闘開始の直前に、進行中の探索を<b>同期的に</b>撤収する（P5-07。仕様書 v1.1 §8.2 手順 4、§8.3）。
    ///
    /// <b>調査の成功確定は保持する。</b> §8.3 の表は「調査未確定と戦闘開始 → 調査を中断して戦闘へ」
    /// 「調査成功確定後と戦闘開始 → 成功は保持して表示を撤収」と分けている。
    /// 既存の <c>InvestigationCoordinator.InterruptAllForCombat</c> がその区別を持っているので、
    /// ここは<b>その窓口を呼ぶだけ</b>にする。撤収の判断を作り直さない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EncounterInterruptRelay : MonoBehaviour, IEncounterInterruptSink
    {
        [Tooltip("調査の調停役。中断と代理表示の撤収を行う。")]
        [SerializeField] private InvestigationCoordinator _investigation;

        [Tooltip("犬丸の追従。戦闘の間は経路追従を止める。")]
        [SerializeField] private CompanionFollowController _follow;

        /// <summary>撤収を要求した回数（診断・テスト用）。</summary>
        public int InterruptCount { get; private set; }

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _investigation != null;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(InvestigationCoordinator investigation, CompanionFollowController follow)
        {
            if (investigation != null)
            {
                _investigation = investigation;
            }

            if (follow != null)
            {
                _follow = follow;
            }
        }

        /// <inheritdoc />
        public void InterruptForEncounter()
        {
            InterruptCount++;

            // 調査を中断し、代理表示を撤収する（成功確定済みは保持される）。
            _investigation?.InterruptAllForCombat();

            // 経路の途中状態を持ち越さない。戦闘の配置から改めて評価させる。
            _follow?.NotifyWorldChanged();
        }
    }
}
