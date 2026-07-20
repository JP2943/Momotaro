using System;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中の同一性を表す識別子（P2-01）。「1 回の攻撃発動（Instance）」と
    /// 「その中の多段（Stage）」の組で構成する。多重ヒット防止のキーに用いる（依頼 §4 の多段識別子・§6）。
    ///
    /// - 同一 <see cref="InstanceId"/> かつ同一 <see cref="Stage"/> … 同じ命中。同一対象へは 1 回だけ通す。
    /// - <see cref="Stage"/> が異なる（例：3 段コンボの各段） … 別命中として同一対象へ再度通せる。
    /// - <see cref="InstanceId"/> が異なる（別の攻撃発動） … 別命中として同一対象へ再度通せる。
    /// </summary>
    public readonly struct HitId : IEquatable<HitId>
    {
        /// <summary>攻撃発動ごとに一意な識別子（<see cref="HitInstanceAllocator"/> で採番）。</summary>
        public int InstanceId { get; }

        /// <summary>多段の段識別子（単発は 0）。</summary>
        public int Stage { get; }

        /// <summary>発動識別子と段を指定して生成する。</summary>
        public HitId(int instanceId, int stage)
        {
            InstanceId = instanceId;
            Stage = stage;
        }

        /// <summary>単発（Stage=0）として生成する。</summary>
        public static HitId Single(int instanceId)
        {
            return new HitId(instanceId, 0);
        }

        /// <inheritdoc />
        public bool Equals(HitId other)
        {
            return InstanceId == other.InstanceId && Stage == other.Stage;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is HitId other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (InstanceId * 397) ^ Stage;
            }
        }

        /// <summary>等価比較。</summary>
        public static bool operator ==(HitId left, HitId right)
        {
            return left.Equals(right);
        }

        /// <summary>非等価比較。</summary>
        public static bool operator !=(HitId left, HitId right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return "HitId(" + InstanceId + ":" + Stage + ")";
        }
    }
}
