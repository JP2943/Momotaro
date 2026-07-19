using System;
using UnityEngine;

namespace Momotaro.Core.Identification
{
    /// <summary>
    /// 人間可読の安定ID（仕様書 11.9）。表示名や Instance ID の代わりに保存・参照に用いる。
    /// Inspector 上では文字列として編集でき、書式は <see cref="StableIdFormat"/> に従う。
    /// 値は生成後に変更しない運用とし、削除済みIDは再利用しない。
    /// </summary>
    [Serializable]
    public struct StableId : IEquatable<StableId>
    {
        [SerializeField] private string _value;

        /// <summary>
        /// 文字列値から生成する。書式検証は行わないため、必要に応じ <see cref="IsValid"/> を確認する。
        /// </summary>
        public StableId(string value)
        {
            _value = value;
        }

        /// <summary>生の文字列値。未設定時は空文字を返す。</summary>
        public string Value => _value ?? string.Empty;

        /// <summary>書式が規約に適合するか。</summary>
        public bool IsValid => StableIdFormat.IsValid(_value);

        /// <summary>値が未設定（空）か。</summary>
        public bool IsEmpty => string.IsNullOrEmpty(_value);

        /// <inheritdoc />
        public bool Equals(StableId other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is StableId other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Value;
        }
    }
}
