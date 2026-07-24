using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 命中結果を仮フィードバック（VFX/SE ID・ヒットストップ要求）へ変換して配信する（Phase2 P2-11。仕様書 §10.2 / §900）。
    /// 主人公（<see cref="PlayerVitalsHolder"/>）と <see cref="CombatDummy"/> の結果チャネルを購読し、<see cref="CombatFeedbackMap"/> で
    /// Cue を解決して <see cref="Feedback"/> へ配信する。Gameplay ロジックには一切干渉しない（読み取り専用）。無効化・破棄で確実に購読解除し、
    /// シーン再読込後は <see cref="Rescan"/>（OnEnable / 定期）で購読し直す。VFX/SE 実体・完成 HitStop は本 Task 対象外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackDispatcher : MonoBehaviour, IHitResultListener
    {
        [Tooltip("購読対象を再取得する間隔（秒）。シーン再読込・動的生成に追従する。")]
        [SerializeField] private float _refreshInterval = 1f;

        private readonly List<CombatDummy> _dummies = new List<CombatDummy>();
        private PlayerVitalsHolder _playerVitals;
        private float _nextRefresh;

        /// <summary>フィードバック配信チャネル（VFX/SE/HitStop の Presentation が購読）。</summary>
        public CombatFeedbackChannel Feedback { get; } = new CombatFeedbackChannel();

        private void OnEnable()
        {
            Rescan();
        }

        private void Update()
        {
            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + Mathf.Max(0.1f, _refreshInterval);
                Rescan();
            }
        }

        /// <summary>購読対象（主人公・ダミー）を取得し直す。既存購読は一旦解除して重複を避ける。</summary>
        public void Rescan()
        {
            // 主人公の再生成・シーン再読込に追従：購読中と検出結果が変わったら旧購読を解除し、新しい対象へ購読し直す。
            // 参照比較（ReferenceEquals）で判定し、Unity の破棄済み(=fake null)でも取りこぼさず、同一対象への重複購読はしない。
            PlayerVitalsHolder found = FindFirstObjectByType<PlayerVitalsHolder>();
            if (!ReferenceEquals(found, _playerVitals))
            {
                if (_playerVitals != null)
                {
                    _playerVitals.Results.RemoveListener(this);
                }

                _playerVitals = found;
                if (_playerVitals != null)
                {
                    _playerVitals.Results.AddListener(this);
                }
            }

            foreach (CombatDummy d in _dummies)
            {
                if (d != null)
                {
                    d.Results.RemoveListener(this);
                }
            }

            _dummies.Clear();
            _dummies.AddRange(FindObjectsByType<CombatDummy>(FindObjectsSortMode.None));
            foreach (CombatDummy d in _dummies)
            {
                d.Results.AddListener(this);
            }
        }

        /// <inheritdoc />
        public void OnHitResult(in HitResult result)
        {
            // 読み取りのみ：結果種別から仮 Cue を解決し配信する。Gameplay 状態は変更しない。
            CombatFeedbackCue cue = CombatFeedbackMap.Resolve(result.Kind);
            Feedback.Publish(new CombatFeedbackEvent(result, cue));
        }

        private void OnDisable()
        {
            foreach (CombatDummy d in _dummies)
            {
                if (d != null)
                {
                    d.Results.RemoveListener(this);
                }
            }

            _dummies.Clear();

            if (_playerVitals != null)
            {
                _playerVitals.Results.RemoveListener(this);
            }

            _playerVitals = null;
        }
    }
}
