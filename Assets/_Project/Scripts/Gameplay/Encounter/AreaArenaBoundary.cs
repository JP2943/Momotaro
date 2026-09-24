using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Navigation;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// 戦闘中だけ有効になるアリーナ境界（P5-07。仕様書 v1.1 §8.2 手順 5、§8.4 手順 5）。
    ///
    /// <b>有効化する前に、内側に安全に居られるかを確かめる</b>（§8.2 末尾）。
    /// 先に封鎖してから配置を直すと、Collider に食い込んだ状態が 1 フレーム作られ、
    /// 物理がプレイヤーを外へ弾き出す。確かめてから閉じる。
    ///
    /// <b>犬丸が外に居たら内側へ置き直す</b>（手順 5）。値（HP・Down・CD）は保持したまま、
    /// 位置だけを P5-05 の安全ワープ規則で選ぶ。安全な内部候補が無ければ<b>封鎖しない</b>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaArenaBoundary : MonoBehaviour, IArenaBoundary
    {
        [Tooltip("封鎖する Collider（既定は無効。戦闘中だけ有効化する）。")]
        [SerializeField] private List<Collider> _blockers = new List<Collider>();

        [Tooltip("XZ の広がり（幅, 奥行）。中心はこの Transform の位置。")]
        [SerializeField] private Vector2 _size = new Vector2(16f, 16f);

        [Tooltip("内側と認める余白（m）。封鎖 Collider に食い込んだ位置を「内側」にしない。")]
        [SerializeField] private float _margin = 1.0f;

        [Tooltip("主人公の根。")]
        [SerializeField] private Transform _player;

        [Tooltip("犬丸の追従（外に居たら内側へ置き直す）。")]
        [SerializeField] private CompanionFollowController _companion;

        /// <inheritdoc />
        public bool IsEnabled { get; private set; }

        /// <summary>境界を有効化した回数（診断・テスト用）。</summary>
        public int EnableCount { get; private set; }

        /// <summary>犬丸を内側へ置き直した回数（診断・テスト用）。</summary>
        public int RelocateCount { get; private set; }

        /// <summary>
        /// いま<b>実際に有効な</b>封鎖 Collider の数（診断・テスト・Validator 用）。
        /// <see cref="IsEnabled"/> は意図で、こちらは実体。解放したつもりで壁が残る形を外から見分ける。
        /// </summary>
        public int ActiveBlockerCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _blockers.Count; i++)
                {
                    if (_blockers[i] != null && _blockers[i].enabled)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>配線されている封鎖 Collider の数（Validator 用）。</summary>
        public int BlockerCount => _blockers.Count;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _blockers.Count > 0 && _player != null;

        /// <summary>安全に居られる内側の範囲（余白ぶん狭めた矩形）。</summary>
        public Bounds SafeBounds
        {
            get
            {
                Vector3 c = transform.position;
                float w = Mathf.Max(0f, _size.x - _margin * 2f);
                float d = Mathf.Max(0f, _size.y - _margin * 2f);
                return new Bounds(new Vector3(c.x, 0f, c.z), new Vector3(w, 2f, d));
            }
        }

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(IEnumerable<Collider> blockers, Transform player,
            CompanionFollowController companion, Vector2 size)
        {
            if (blockers != null)
            {
                _blockers = new List<Collider>();
                foreach (Collider c in blockers)
                {
                    if (c != null)
                    {
                        _blockers.Add(c);
                    }
                }
            }

            if (player != null)
            {
                _player = player;
            }

            if (companion != null)
            {
                _companion = companion;
            }

            if (size.x > 0f && size.y > 0f)
            {
                _size = size;
            }

            SetBlockers(false);
        }

        /// <inheritdoc />
        public bool TryEnable(out string error)
        {
            if (!IsWired)
            {
                error = "アリーナ境界が未配線です（封鎖 Collider か主人公）。";
                return false;
            }

            Bounds safe = SafeBounds;
            if (safe.size.x <= 0f || safe.size.z <= 0f)
            {
                error = "アリーナが余白より狭く、内側がありません。";
                return false;
            }

            if (!Contains(safe, _player.position))
            {
                error = "主人公が境界の内側に居ません（位置 " + _player.position + "）。";
                return false;
            }

            // 犬丸を内側へ。値は保持したまま位置だけを直す（§8.2 手順 5）。
            if (_companion != null && !Contains(safe, _companion.transform.position))
            {
                if (!_companion.TryRelocateInside(safe, out SafeWarpRejection rejection))
                {
                    error = "犬丸を境界の内側へ安全に配置できません（" + rejection + "）。";
                    return false;
                }

                RelocateCount++;
            }

            SetBlockers(true);
            IsEnabled = true;
            EnableCount++;
            error = null;
            return true;
        }

        /// <inheritdoc />
        public void Disable()
        {
            SetBlockers(false);
            IsEnabled = false;
        }

        private static bool Contains(Bounds bounds, Vector3 position)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return position.x >= min.x && position.x <= max.x
                && position.z >= min.z && position.z <= max.z;
        }

        private void SetBlockers(bool enabled)
        {
            for (int i = 0; i < _blockers.Count; i++)
            {
                if (_blockers[i] != null)
                {
                    _blockers[i].enabled = enabled;
                }
            }
        }

        private void OnDisable()
        {
            // Scene 離脱で一時境界を残さない（§8.4 手順 5）。
            Disable();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(
                new Vector3(transform.position.x, transform.position.y, transform.position.z),
                new Vector3(_size.x, 0.1f, _size.y));
        }
    }
}
