using System;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 命中フィードバックの仮 SE（効果音）を鳴らす Presentation 再生器（Phase3.5 P3.5-05B）。<c>Cue.SeId</c> ごとに差し替え可能な
    /// <see cref="SeSlot"/>（鍵＋AudioClip）を持ち、一致スロットの Clip を <see cref="AudioSource.PlayOneShot"/> で鳴らす。
    ///
    /// SE 実素材は未確定のため、Clip 未割当・未登録 SeId・空文字は無音で無処理（例外・警告連打を出さない）。実 Clip は後で
    /// スロットへ差し込む。<see cref="LastRequestedSeId"/>／<see cref="LastPlayedSeId"/> で配線・テストを検証できる。Gameplay 非干渉。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatSePlayer : MonoBehaviour
    {
        /// <summary>SeId ごとの差し替えスロット（鍵＋任意 Clip＋音量）。</summary>
        [Serializable]
        public sealed class SeSlot
        {
            [Tooltip("Cue.SeId と一致させる鍵（例：SE_Hit_Normal / SE_JustGuard）。")]
            public string seId;

            [Tooltip("差し替え用 AudioClip（未割当なら無音・無例外）。")]
            public AudioClip clip;

            [Range(0f, 1f)]
            [Tooltip("この SE の音量。")]
            public float volume = 1f;
        }

        [Tooltip("SeId → Clip の対応表（差し替え可能）。")]
        [SerializeField] private SeSlot[] _slots;

        [Tooltip("再生に使う AudioSource（未割当なら自動生成）。")]
        [SerializeField] private AudioSource _source;

        /// <summary>直近に要求された SeId（テスト・検証用。未登録でも記録）。</summary>
        public string LastRequestedSeId { get; private set; }

        /// <summary>直近に実際に鳴らした SeId（Clip 割当済みで再生できたときのみ更新）。</summary>
        public string LastPlayedSeId { get; private set; }

        /// <summary>SE スロット表（Scene 構築 P3.5-06・テストが設定）。</summary>
        public SeSlot[] Slots { get => _slots; set => _slots = value; }

        private AudioSource EnsureSource()
        {
            if (_source == null)
            {
                _source = GetComponent<AudioSource>();
                if (_source == null)
                {
                    _source = gameObject.AddComponent<AudioSource>();
                }

                _source.playOnAwake = false;
            }

            return _source;
        }

        /// <summary>指定 SeId の仮 SE を鳴らす。未登録・空・Clip 未割当は無音で無処理（無例外）。</summary>
        public void Play(string seId)
        {
            LastRequestedSeId = seId;

            if (string.IsNullOrEmpty(seId) || _slots == null)
            {
                return;
            }

            for (int i = 0; i < _slots.Length; i++)
            {
                SeSlot s = _slots[i];
                if (s == null || s.seId != seId)
                {
                    continue;
                }

                if (s.clip != null)
                {
                    EnsureSource().PlayOneShot(s.clip, Mathf.Clamp01(s.volume));
                    LastPlayedSeId = seId;
                }

                return; // 一致スロットあり（Clip 未割当でも無音で終了）。
            }

            // 未登録 SeId：無処理（無例外）。
        }
    }
}
