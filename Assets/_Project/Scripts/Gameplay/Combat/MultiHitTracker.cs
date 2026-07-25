using System;
using System.Collections.Generic;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 同一攻撃（同一 <see cref="HitId"/>）による同一対象への多重ヒットを防止する Runtime 構造（P2-01・依頼 §6）。
    /// 攻撃の発生源が 1 つの攻撃発動につき 1 個を保持し、命中候補ごとに <see cref="TryRegisterHit"/> を呼ぶ。
    ///
    /// - 同一 <see cref="HitId"/> かつ同一対象 … 2 回目以降は false（多重ヒットを弾く）。
    /// - <see cref="HitId"/> が異なる（別発動・別段） … true（再度命中可能）。
    /// - 対象が異なる … true（それぞれに命中可能）。
    ///
    /// public static を用いず、インスタンスとして所有する（依頼 設計条件）。
    /// </summary>
    public sealed class MultiHitTracker
    {
        private readonly HashSet<Key> _registered = new HashSet<Key>();

        /// <summary>登録済みの命中件数（診断・テスト用）。</summary>
        public int Count => _registered.Count;

        /// <summary>
        /// 指定の命中（<paramref name="hitId"/> × <paramref name="target"/>）を登録する。
        /// まだ登録されていなければ登録して true、既に登録済みなら false を返す。
        /// </summary>
        public bool TryRegisterHit(HitId hitId, IDamageable target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return _registered.Add(new Key(hitId, target.DamageableId));
        }

        /// <summary>指定の命中が既に登録済みかを返す（副作用なし）。</summary>
        public bool HasHit(HitId hitId, IDamageable target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return _registered.Contains(new Key(hitId, target.DamageableId));
        }

        /// <summary>登録内容をすべて破棄する（攻撃の発動をまたいで再利用する場合に用いる）。</summary>
        public void Clear()
        {
            _registered.Clear();
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly HitId _hitId;
            private readonly int _targetId;

            public Key(HitId hitId, int targetId)
            {
                _hitId = hitId;
                _targetId = targetId;
            }

            public bool Equals(Key other)
            {
                return _hitId == other._hitId && _targetId == other._targetId;
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (_hitId.GetHashCode() * 397) ^ _targetId;
                }
            }
        }
    }
}
