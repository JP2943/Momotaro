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
    /// 位置に加え回転も外部から受け取り（P3.5-06）、カメラへ正対（billboard）した向きで表示できる。
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
        /// 位置のみ指定（回転は identity）で再生する後方互換オーバーロード。
        /// </summary>
        public void Play(Sprite[] frames, Vector3 worldPosition, float duration, int sortingOrder, Color color)
        {
            Play(frames, worldPosition, Quaternion.identity, duration, sortingOrder, color);
        }

        /// <summary>
        /// 指定位置・回転・色でコマを <paramref name="duration"/> 秒かけて再生する。空フレームは即完了（asset 未割当でも安全）。
        /// 色はプール再利用時に前回値が残らないよう毎回必ず設定する。<paramref name="rotation"/> はカメラ正対（billboard）用。
        /// <paramref name="duration"/> が 0 以下でも無限再生しない。
        /// </summary>
        public void Play(Sprite[] frames, Vector3 worldPosition, Quaternion rotation, float duration, int sortingOrder, Color color)
        {
            _frames = frames;
            _duration = duration <= 0f ? 0.0001f : duration;
            _elapsed = 0f;
            _playing = frames != null && frames.Length > 0;

            transform.position = worldPosition;
            transform.rotation = rotation; // billboard（カメラ正対）用。再利用時に前回の向きを残さない。
            SpriteRenderer r = EnsureRenderer();
            r.sortingOrder = sortingOrder;
            r.color = color; // 再利用時に前回の色を残さない。
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

        /// <summary>
        /// 再生中に表示位置・回転だけを更新する（コマ進行・再生状態には触れない。P3.5-09）。
        /// 必殺技のように判定中心が Active 中に前方へ進む攻撃で、剣閃を判定へ追従させるために Presenter が毎フレーム呼ぶ。
        /// </summary>
        public void SetPose(Vector3 worldPosition, Quaternion rotation)
        {
            transform.position = worldPosition;
            transform.rotation = rotation;
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
