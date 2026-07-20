using System;

namespace Momotaro.Gameplay.Vitals
{
    /// <summary>
    /// 0〜最大値の範囲を持つ Runtime リソース（HP・スタミナ等）の基礎（Phase1 P1-10）。
    /// 現在値は常に [0, Max] へ Clamp され、変更時は型付きで通知する。純粋クラスでテスト可能。
    /// ScriptableObject 原本は保持せず、Runtime State としてのみ値を持つ。
    ///
    /// Phase 1 では被ダメージ・自然回復・死亡は扱わず、値の生成・増減・Clamp・通知の基礎のみを提供する。
    /// </summary>
    public sealed class Vital
    {
        /// <summary>最大値（0 以上）。</summary>
        public int Max { get; private set; }

        /// <summary>現在値（0〜Max）。</summary>
        public int Current { get; private set; }

        /// <summary>現在値が変化、または最大値が変化した際に発火する。</summary>
        public event Action<VitalChanged> Changed;

        /// <summary>最大値で満たした状態で生成する。</summary>
        public Vital(int max)
            : this(max, max)
        {
        }

        /// <summary>最大値と初期現在値を指定して生成する。現在値は範囲へ Clamp される。</summary>
        public Vital(int max, int current)
        {
            Max = max < 0 ? 0 : max;
            Current = ClampToRange(current);
        }

        /// <summary>現在値の割合（0〜1）。Max が 0 のときは 0。</summary>
        public float Ratio => Max <= 0 ? 0f : (float)Current / Max;

        /// <summary>現在値を設定する（Clamp）。</summary>
        public void SetCurrent(int value)
        {
            ApplyCurrent(ClampToRange(value));
        }

        /// <summary>現在値を増減する（Clamp）。負値で減少。</summary>
        public void Change(int delta)
        {
            ApplyCurrent(ClampToRange(Current + delta));
        }

        /// <summary>
        /// 最大値を変更する。現在値は新しい範囲へ Clamp される（新 Max が現在値未満なら現在値も下がる）。
        /// 最大値または現在値が変化した場合に通知する。
        /// </summary>
        public void SetMax(int newMax)
        {
            int clampedMax = newMax < 0 ? 0 : newMax;
            int prevMax = Max;
            int prevCurrent = Current;

            Max = clampedMax;
            Current = ClampToRange(Current);

            if (Max != prevMax || Current != prevCurrent)
            {
                Changed?.Invoke(new VitalChanged(prevCurrent, Current, Max));
            }
        }

        private int ClampToRange(int value)
        {
            if (value < 0)
            {
                return 0;
            }

            return value > Max ? Max : value;
        }

        private void ApplyCurrent(int next)
        {
            if (next == Current)
            {
                return;
            }

            int previous = Current;
            Current = next;
            Changed?.Invoke(new VitalChanged(previous, Current, Max));
        }
    }
}
