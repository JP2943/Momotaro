namespace Momotaro.Gameplay.Vitals
{
    /// <summary>
    /// Vital（HP/スタミナ等）の変更を表す型付き通知（Phase1 P1-10）。
    /// 変更前後の現在値と、その時点の最大値を保持する。
    /// </summary>
    public readonly struct VitalChanged
    {
        /// <summary>変更前の現在値。</summary>
        public int Previous { get; }

        /// <summary>変更後の現在値。</summary>
        public int Current { get; }

        /// <summary>その時点の最大値。</summary>
        public int Max { get; }

        public VitalChanged(int previous, int current, int max)
        {
            Previous = previous;
            Current = current;
            Max = max;
        }
    }
}
