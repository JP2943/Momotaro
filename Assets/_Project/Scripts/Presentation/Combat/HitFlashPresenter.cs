using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 被弾対象のスプライトを一瞬だけ指定色へ点滅させる Presentation 効果（Phase3.5 P3.5-05B）。命中フィードバックを受けて
    /// <see cref="Trigger"/>（被弾対象＝<see cref="IDamageable"/>）または <see cref="TriggerRenderer"/> で開始し、短時間で元色へ戻す。
    ///
    /// 時間は <see cref="Tick"/> で外部から与える（unscaled 前提。ヒットストップ中も点滅が進む）。同じ Renderer への多重点滅は
    /// 元色を保持したまま点滅色・時間だけリセットする（前回の点滅色を元色と誤認しない）。Disable で元色へ戻し残さない。表示専用（Gameplay 非干渉）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitFlashPresenter : MonoBehaviour
    {
        [Tooltip("点滅の長さ（秒）。")]
        [SerializeField] private float _flashSeconds = 0.08f;

        private sealed class FlashState
        {
            public SpriteRenderer Renderer;
            public Color Orig;
            public Color FlashColor;
            public float Elapsed;
            public float Duration;
        }

        private readonly List<FlashState> _active = new List<FlashState>();

        /// <summary>点滅中の対象数（テスト・検証用）。</summary>
        public int ActiveCount => _active.Count;

        /// <summary>点滅の長さ（秒。Scene 構築 P3.5-06・テストが設定）。</summary>
        public float FlashSeconds { get => _flashSeconds; set => _flashSeconds = value; }

        /// <summary>被弾対象（<see cref="IDamageable"/>＝Component 前提）の SpriteRenderer を点滅させる。破棄済み・未取得は無処理。</summary>
        public void Trigger(IDamageable target, Color flashColor)
        {
            if (!(target is Component comp) || comp == null)
            {
                return; // Component でない／Unity 破棄済み(fake-null)は無処理。
            }

            SpriteRenderer r = comp.GetComponentInChildren<SpriteRenderer>(true);
            TriggerRenderer(r, flashColor);
        }

        /// <summary>指定 Renderer を点滅させる（null は無処理）。</summary>
        public void TriggerRenderer(SpriteRenderer r, Color flashColor)
        {
            if (r == null)
            {
                return;
            }

            FlashState existing = Find(r);
            if (existing != null)
            {
                // 既に点滅中：元色は保持し、点滅色・時間だけリセット（前回点滅色を元色にしない）。
                existing.FlashColor = flashColor;
                existing.Elapsed = 0f;
                existing.Duration = _flashSeconds;
                r.color = flashColor;
                return;
            }

            _active.Add(new FlashState
            {
                Renderer = r,
                Orig = r.color,
                FlashColor = flashColor,
                Elapsed = 0f,
                Duration = _flashSeconds,
            });
            r.color = flashColor;
        }

        /// <summary>時間を進めてコマを更新する（unscaled 前提）。満了で元色へ戻す。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                deltaTime = 0f;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                FlashState f = _active[i];
                if (f.Renderer == null)
                {
                    _active.RemoveAt(i); // 破棄済みは追跡解除（残留なし）。
                    continue;
                }

                f.Elapsed += deltaTime;
                float dur = f.Duration <= 0f ? 0.0001f : f.Duration;
                float t = f.Elapsed / dur;
                if (t >= 1f)
                {
                    f.Renderer.color = f.Orig; // 元色へ確実に復帰。
                    _active.RemoveAt(i);
                    continue;
                }

                f.Renderer.color = Color.Lerp(f.FlashColor, f.Orig, t);
            }
        }

        /// <summary>全点滅を打ち切り元色へ戻す（Disable・Scene 離脱・Retry）。</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Renderer != null)
                {
                    _active[i].Renderer.color = _active[i].Orig;
                }
            }

            _active.Clear();
        }

        private FlashState Find(SpriteRenderer r)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].Renderer == r)
                {
                    return _active[i];
                }
            }

            return null;
        }

        private void OnDisable()
        {
            ClearAll();
        }

        private void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }
    }
}
