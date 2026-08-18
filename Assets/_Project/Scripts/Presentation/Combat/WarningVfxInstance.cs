using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// ガード不能予告 VFX の 1 インスタンス（Phase3.5 P3.5-05）。剣閃（一発再生）と異なり、予兆（Prepare）区間が続く間
    /// コマをループ再生して「継続表示」する表示専用オブジェクト。当たり判定・ダメージは持たない（Collider を付けない）。
    /// 位置は毎フレーム更新でき（敵に追従）、予兆終了・破棄で <see cref="Hide"/> して残さない。
    /// 時間は <see cref="Tick"/> で外部から与える（駆動点を一つにしてテストを決定的にする）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WarningVfxInstance : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Sprite[] _frames;
        private float _loopSeconds;
        private float _elapsed;
        private bool _shown;

        /// <summary>表示中か。</summary>
        public bool IsShown => _shown;

        /// <summary>現在表示中のコマ（テスト・検証用）。</summary>
        public Sprite CurrentSprite => _renderer != null ? _renderer.sprite : null;

        /// <summary>現在の Tint 色（テスト・検証用）。</summary>
        public Color CurrentColor => _renderer != null ? _renderer.color : Color.white;

        private SpriteRenderer EnsureRenderer()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
                if (_renderer == null)
                {
                    _renderer = gameObject.AddComponent<SpriteRenderer>();
                }
            }

            return _renderer;
        }

        /// <summary>
        /// 指定位置・色で予告のループ再生を開始する。空フレームは表示しない（asset 未割当でも安全）。
        /// 色は再利用時に前回値が残らないよう毎回必ず設定する（ガード不能予告は赤系 Tint 前提）。
        /// </summary>
        public void Show(Sprite[] frames, Vector3 worldPosition, int sortingOrder, float loopSeconds, Color color)
        {
            _frames = frames;
            _loopSeconds = loopSeconds <= 0f ? 0.4f : loopSeconds;
            _elapsed = 0f;
            _shown = frames != null && frames.Length > 0;

            transform.position = worldPosition;
            SpriteRenderer r = EnsureRenderer();
            r.sortingOrder = sortingOrder;
            r.color = color; // 再利用時に前回の色を残さない。
            r.enabled = _shown;
            if (_shown)
            {
                r.sprite = _frames[0];
            }

            gameObject.SetActive(_shown);
            if (!_shown)
            {
                Hide();
            }
        }

        /// <summary>表示位置を更新する（敵に追従）。</summary>
        public void SetPosition(Vector3 worldPosition)
        {
            if (_shown)
            {
                transform.position = worldPosition;
            }
        }

        /// <summary>時間を進めてコマをループ更新する。</summary>
        public void Tick(float deltaTime)
        {
            if (!_shown)
            {
                return;
            }

            _elapsed += deltaTime < 0f ? 0f : deltaTime;
            int n = _frames.Length;
            int idx = (int)(_elapsed / _loopSeconds * n) % n; // 予兆が続く間ループ。
            _renderer.sprite = _frames[idx];
        }

        /// <summary>表示を消す（予兆終了・破棄・Disable・Scene 離脱。残留を残さない）。</summary>
        public void Hide()
        {
            _shown = false;
            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            gameObject.SetActive(false);
        }
    }
}
