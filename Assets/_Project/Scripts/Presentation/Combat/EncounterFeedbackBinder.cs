using Momotaro.Gameplay.Encounter;
using Momotaro.Presentation.Diagnostics;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 生成した敵を<b>その場で</b>命中フィードバックの購読へ載せる（P5-07。仕様書 v1.1 §8.2 手順 7）。
    ///
    /// <see cref="CombatFeedbackDispatcher"/> は購読対象を周期（既定 1 秒）で探し直す。
    /// Scene 再読込や動的生成に追従するための作りだが、Encounter は<b>生成した直後から殴り合う</b>ので、
    /// その周期を待つと最初の何発かが「当たったのに手応えが無い」状態になる。
    /// 生成の側が「いま増えた」と言える通知（<see cref="IEncounterSpawner.SpawnedActivated"/>）を
    /// 購読して、活動を許可した直後に探し直させる。
    ///
    /// <b>Gameplay には触らない。</b> 読むのは通知だけで、配信役の再探索を呼ぶ以外のことはしない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EncounterFeedbackBinder : MonoBehaviour
    {
        [Tooltip("生成の通知元。")]
        [SerializeField] private AreaEncounterSpawner _spawner;

        [Tooltip("命中フィードバックの配信役。")]
        [SerializeField] private CombatFeedbackDispatcher _dispatcher;

        private bool _subscribed;

        /// <summary>再探索させた回数（診断・テスト用）。</summary>
        public int RescanCount { get; private set; }

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _spawner != null && _dispatcher != null;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(AreaEncounterSpawner spawner, CombatFeedbackDispatcher dispatcher)
        {
            Unsubscribe();

            if (spawner != null)
            {
                _spawner = spawner;
            }

            if (dispatcher != null)
            {
                _dispatcher = dispatcher;
            }

            Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || _spawner == null)
            {
                return;
            }

            _spawner.SpawnedActivated += OnSpawnedActivated;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _spawner == null)
            {
                return;
            }

            _spawner.SpawnedActivated -= OnSpawnedActivated;
            _subscribed = false;
        }

        private void OnSpawnedActivated()
        {
            if (_dispatcher == null)
            {
                return;
            }

            _dispatcher.Rescan();
            RescanCount++;
        }
    }
}
