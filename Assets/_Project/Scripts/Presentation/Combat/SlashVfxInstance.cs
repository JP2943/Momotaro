using System;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 剣閃 VFX の 1 インスタンス（Phase3.5 P3.5-05）。<see cref="SpriteRenderer"/> で 3 コマ（発生→最大→減衰）を
    /// 指定時間で再生する表示専用オブジェクト。当たり判定・ダメージは一切持たない（Collider を付けない）。
    /// 再生完了・停止で <see cref="Completed"/> を通知し、<see cref="SlashVfxPool"/> が再利用する。
    ///
    /// 時間は <see cref="SlashVfxPool.TickActive"/>→<see cref="Tick"/> で外部から与える（Pause 時は Presenter が
    /// スケール時間を渡すため進まない）。自前 Update は持たず、駆動点を一つにしてテストを決定的にする。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SlashVfxInstance : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Sprite[] _frames;
        private float _duration;
        private float _elapsed;
        private bool _playing;

        /// <summary>再生完了・停止で通知（Pool が再利用のため購読）。</summary>
        public Action<SlashVfxInstance> Completed;

        /// <summary>再生中か。</summary>
        public bool IsPlaying => _playing;

        /// <summary>現在表示中のコマ（テスト・検証用）。</summary>
        public Sprite CurrentSprite => _renderer != null ? _renderer.sprite : null;

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

        /// <summary>指定位置で 3 コマを <paramref name="duration"/> 秒かけて再生する。空フレームは即完了（asset 未割当でも安全）。</summary>
        public void Play(Sprite[] frames, Vector3 worldPosition, float duration, int sortingOrder)
        {
            _frames = frames;
            _duration = duration <= 0f ? 0.0001f : duration;
            _elapsed = 0f;
            _playing = frames != null && frames.Length > 0;

            transform.position = worldPosition;
            SpriteRenderer r = EnsureRenderer();
            r.sortingOrder = sortingOrder;
            r.enabled = _playing;
            if (_playing)
            {
                r.sprite = _frames[0];
            }

            gameObject.SetActive(true);

            if (!_playing)
            {
                Complete(); // フレーム未割当：表示せず即座に完了（例外・警告連打を出さない）。
            }
        }

        /// <summary>時間を進めてコマを更新する。完了で <see cref="Completed"/> を通知する。</summary>
        public void Tick(float deltaTime)
        {
            if (!_playing)
            {
                return;
            }

            _elapsed += deltaTime < 0f ? 0f : deltaTime;
            int n = _frames.Length;
            int idx = (int)(_elapsed / _duration * n);
            if (idx >= n)
            {
                Complete();
                return;
            }

            _renderer.sprite = _frames[idx];
        }

        /// <summary>再生を打ち切る（攻撃中断・Active 終了・Disable・Scene 離脱）。残留を残さない。</summary>
        public void Stop()
        {
            if (_playing)
            {
                Complete();
            }
        }

        private void Complete()
        {
            _playing = false;
            if (_renderer != null)
            {
                _renderer.enabled = false;
            }

            gameObject.SetActive(false);
            Completed?.Invoke(this);
        }
    }
}
