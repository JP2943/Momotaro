using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat
{
    /// <summary>
    /// 特定分類（ガード不能）の「直近の攻撃選択に占める割合」を明示的な上限へ収める頻度ガバナ（Phase3 P3-09。§9.3
    /// 「ガード不能は全選択の 20% 以下」）。Score へ重みを掛けるだけでは最高 Score 必ず選択方式で割合を保証できないため、
    /// 選択履歴に基づく最小間隔（min-gap）で上限を固定する。上限側：直前のガード不能選択から一定回数（<see cref="_minGap"/>）
    /// の他攻撃を挟むまでガード不能を解禁しない（＝任意連続 (<see cref="_minGap"/>+1) 回のうち最大 1 回 → 割合 ≤ 上限）。
    /// 下限側：解禁かつ使用可能なとき上位で強制選択させる運用と組み合わせ、0%（全く使われない）を避ける（呼び出し側で強制）。
    /// 純粋・決定的（乱数・時間・Unity API に依存しない）。上限を「唯一の候補だから」といって破らない（解禁前は常に不許可）。
    /// </summary>
    public sealed class AttackFrequencyGovernor
    {
        private readonly int _minGap;   // 直近のガード不能選択から次の解禁までに必要な他攻撃の回数。
        private int _sinceCapped;       // 直近のガード不能選択以降に行った選択回数（ガード不能自身は含めない）。
        private int _total;             // これまでの総選択回数。
        private int _capped;            // これまでのガード不能選択回数。

        /// <summary>上限割合（0&lt;ratio≤1）から最小間隔を導いて構築する。ratio=0.2 → 5 回に 1 回（min-gap=4）。</summary>
        public AttackFrequencyGovernor(float maxRatio)
        {
            float r = Mathf.Clamp(maxRatio, 0.0001f, 1f);
            // 連続するガード不能選択の間隔 G（回）は G ≥ ceil(1/ratio) で割合 1/G ≤ ratio を保証する。min-gap = G-1。
            int gap = Mathf.CeilToInt(1f / r);
            _minGap = Mathf.Max(0, gap - 1);
            _sinceCapped = int.MaxValue / 2; // 初回は解禁済み（>0% を確保）。
        }

        /// <summary>必要な最小間隔（他攻撃の回数）。</summary>
        public int MinGap => _minGap;

        /// <summary>総選択回数（Debug/テスト用）。</summary>
        public int TotalSelections => _total;

        /// <summary>ガード不能を選択した回数（Debug/テスト用）。</summary>
        public int CappedSelections => _capped;

        /// <summary>今ガード不能を選んでよいか（上限を破らない範囲で解禁済みか）。使用可否（距離・角度・Cooldown）は別判定。</summary>
        public bool CappedEligible => _sinceCapped >= _minGap;

        /// <summary>1 回の攻撃選択を記録する（成立した攻撃開始ごとに呼ぶ）。ガード不能なら間隔をリセットする。</summary>
        public void RecordSelection(bool wasCapped)
        {
            _total++;
            if (wasCapped)
            {
                _capped++;
                _sinceCapped = 0;
            }
            else if (_sinceCapped < int.MaxValue / 2)
            {
                _sinceCapped++;
            }
        }
    }
}
