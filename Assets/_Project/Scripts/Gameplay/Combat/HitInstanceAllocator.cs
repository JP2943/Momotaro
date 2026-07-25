namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 攻撃発動ごとに一意な <see cref="HitId.InstanceId"/> を採番するインスタンス（P2-01）。
    /// public static な万能マネージャは作らない方針（依頼 設計条件）のため、
    /// 採番器はインスタンスとして保持し、攻撃の発生源（主人公・仲間・敵）が各自で所有する想定。
    /// </summary>
    public sealed class HitInstanceAllocator
    {
        private int _next;

        /// <summary>採番の開始値を指定して生成する（既定 0）。</summary>
        public HitInstanceAllocator(int start = 0)
        {
            _next = start;
        }

        /// <summary>次の発動識別子を返し、内部カウンタを進める。</summary>
        public int Next()
        {
            int value = _next;
            _next++;
            return value;
        }

        /// <summary>単発命中用の <see cref="HitId"/>（Stage=0）を採番する。</summary>
        public HitId NextSingle()
        {
            return HitId.Single(Next());
        }
    }
}
