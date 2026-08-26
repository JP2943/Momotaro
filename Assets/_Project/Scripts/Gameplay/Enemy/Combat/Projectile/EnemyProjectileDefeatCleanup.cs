using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// プレイヤー死亡で残存する敵 Projectile を一括掃除する橋渡し（Phase3.5 P3.5-02）。<see cref="PlayerDefeatChannel"/> を購読し、
    /// 通知を受けたら <see cref="EnemyProjectileRegistry.DespawnAll"/> を呼ぶだけの薄いコンポーネント。判定・表示・Session は持たない。
    ///
    /// チャネルは同一階層の <see cref="IPlayerDefeatSource"/>（主人公の PlayerVitalsHolder）から解決する。主人公ルートへ 1 つ置けば、
    /// 追加のシーン配線や毎フレーム検索なしで機能する。テスト・将来の Scene 構築では <see cref="Bind"/> で明示注入もできる。
    /// P3.5-03 の CombatSessionController も同じ PlayerDefeatChannel を購読できる（本コンポーネントはそれと排他ではない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyProjectileDefeatCleanup : MonoBehaviour, IPlayerDefeatListener
    {
        private PlayerDefeatChannel _channel;

        private void OnEnable()
        {
            if (_channel == null)
            {
                IPlayerDefeatSource source = GetComponentInParent<IPlayerDefeatSource>();
                if (source != null)
                {
                    _channel = source.Defeats;
                }
            }

            _channel?.AddListener(this);
        }

        private void OnDisable()
        {
            _channel?.RemoveListener(this);
        }

        /// <summary>購読するチャネルを明示的に差し替える（テスト・Scene 構築）。冪等で、旧チャネルの購読は解除する。</summary>
        public void Bind(PlayerDefeatChannel channel)
        {
            if (_channel == channel)
            {
                return;
            }

            _channel?.RemoveListener(this);
            _channel = channel;
            _channel?.AddListener(this);
        }

        /// <inheritdoc />
        public void OnPlayerDefeated(in PlayerDefeatedEvent defeated)
        {
            // 残存する敵由来 Projectile を破棄する（矢の残留を防ぐ。§4.1）。二重通知でも DespawnAll は冪等。
            EnemyProjectileRegistry.DespawnAll();
        }
    }
}
