using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Companion;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 命中結果を仮フィードバック（VFX/SE ID・ヒットストップ要求）へ変換して配信する（Phase2 P2-11 / Phase3.5 P3.5-05B）。仕様書 §10.2 / §900。
    /// 主人公（<see cref="PlayerVitalsHolder"/>）・<see cref="CombatDummy"/>・実戦の敵（<see cref="EnemyActor"/>）・
    /// 仲間（<see cref="CompanionFeedbackRegistry"/> 経由。P4-FIX F04）の結果チャネルを購読し、
    /// <see cref="CombatFeedbackMap"/> で Cue を解決して <see cref="Feedback"/> へ配信する。これにより主人公→敵の命中でも敵側の点滅・ヒットストップ・
    /// カメラ揺れ・SE が発生する。Gameplay ロジックには一切干渉しない（読み取り専用）。無効化・破棄で確実に購読解除し、シーン再読込後は
    /// <see cref="Rescan"/>（OnEnable / 定期）で購読し直す。VFX/SE 実体・完成 HitStop は Presentation 側の担当。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackDispatcher : MonoBehaviour, IHitResultListener, ICompanionFeedbackObserver
    {
        [Tooltip("購読対象を再取得する間隔（秒）。シーン再読込・動的生成に追従する。")]
        [SerializeField] private float _refreshInterval = 1f;

        private readonly List<CombatDummy> _dummies = new List<CombatDummy>();
        private readonly List<EnemyActor> _enemies = new List<EnemyActor>();
        // 仲間はチャネルそのものを持つ（Unity Object を持つと、破棄後に null 判定で飛ばされて解除できない）。
        private readonly List<HitResultChannel> _companionChannels = new List<HitResultChannel>();
        private bool _registryHooked;
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

        /// <summary>購読対象（主人公・ダミー・敵）を取得し直す。既存購読は一旦解除して重複を避ける。</summary>
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

            // ダミー：一旦すべて解除してから再取得・再購読（remove→clear→add で重複通知を避ける）。
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

            // 敵（実戦の骸骨兵・侍骸骨など）：ダミーと同型。動的生成・撃破に追従し、重複購読しない。
            foreach (EnemyActor e in _enemies)
            {
                if (e != null)
                {
                    e.Results.RemoveListener(this);
                }
            }

            _enemies.Clear();
            _enemies.AddRange(FindObjectsByType<EnemyActor>(FindObjectsSortMode.None));
            foreach (EnemyActor e in _enemies)
            {
                e.Results.AddListener(this);
            }

            // 仲間（P4-FIX F04）は探さない。<see cref="CompanionFeedbackBinder"/> が登録所へ差し出したチャネルを
            // 押し出しで受け取る。周期スキャンだと生成直後の一撃を取りこぼし、破棄後の旧チャネルを外せない。
            // ここでは登録所の現在の内容と突き合わせるだけ（購読側が後から現れた場合の採り込み）。
            EnsureRegistryHooked();
            SyncCompanionChannels();
        }

        /// <summary>登録所の増減通知を受け取れるようにする（冪等）。</summary>
        private void EnsureRegistryHooked()
        {
            if (_registryHooked)
            {
                return;
            }

            CompanionFeedbackRegistry.AddObserver(this);
            _registryHooked = true;
        }

        private void UnhookRegistry()
        {
            if (!_registryHooked)
            {
                return;
            }

            CompanionFeedbackRegistry.RemoveObserver(this);
            _registryHooked = false;
        }

        /// <summary>登録所の現在の内容へ購読を合わせる（増えた分を足し、外れた分を落とす）。</summary>
        private void SyncCompanionChannels()
        {
            // 対称な解除が走らなかった登録（Scene 破棄など）をここで回収する。
            CompanionFeedbackRegistry.Prune();

            IReadOnlyList<HitResultChannel> current = CompanionFeedbackRegistry.Channels;

            for (int i = _companionChannels.Count - 1; i >= 0; i--)
            {
                HitResultChannel held = _companionChannels[i];
                if (!Contains(current, held))
                {
                    held.RemoveListener(this);
                    _companionChannels.RemoveAt(i);
                }
            }

            for (int i = 0; i < current.Count; i++)
            {
                OnCompanionChannelAdded(current[i]);
            }
        }

        private static bool Contains(IReadOnlyList<HitResultChannel> list, HitResultChannel channel)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], channel))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void OnCompanionChannelAdded(HitResultChannel channel)
        {
            if (channel == null || _companionChannels.Contains(channel))
            {
                return; // 二重購読しない。
            }

            _companionChannels.Add(channel);
            channel.AddListener(this);
        }

        /// <inheritdoc />
        public void OnCompanionChannelRemoved(HitResultChannel channel)
        {
            if (channel == null || !_companionChannels.Remove(channel))
            {
                return;
            }

            // 保持しているのはチャネル本体なので、GameObject が破棄済みでも確実に外せる。
            channel.RemoveListener(this);
        }

        /// <inheritdoc />
        public void OnHitResult(in HitResult result)
        {
            // 読み取りのみ：結果種別から仮 Cue を解決し配信する。Gameplay 状態は変更しない。
            CombatFeedbackCue cue = CombatFeedbackMap.Resolve(result.Kind);
            Feedback.Publish(new CombatFeedbackEvent(result, cue));
        }

        private void OnDestroy()
        {
            // 無効化を経ずに破棄される経路でも登録所に残らない（登録所側も破棄済みを落とすが、
            // 解除は「壊れる前に自分で外す」を基本にしておく）。
            UnhookRegistry();
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

            foreach (EnemyActor e in _enemies)
            {
                if (e != null)
                {
                    e.Results.RemoveListener(this);
                }
            }

            _enemies.Clear();

            UnhookRegistry();
            foreach (HitResultChannel channel in _companionChannels)
            {
                channel.RemoveListener(this);
            }

            _companionChannels.Clear();

            if (_playerVitals != null)
            {
                _playerVitals.Results.RemoveListener(this);
            }

            _playerVitals = null;
        }
    }
}
