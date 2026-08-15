using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵の被弾・状態遷移を実プレイで可視化する診断ロガー（Phase3 P3-05 受入。既定は無効＝オプトイン）。<see cref="EnemyActor"/> の
    /// 被弾チャネル・状態チャネルを購読し、各命中後の HP／体幹／ひるみ蓄積／EnemyState と、状態遷移（旧→新・理由）を Console へ
    /// 出力する。「どの段階で値または状態が途切れているか」を実 Collider 経路で確認するための一時ツール。表示専用で Gameplay に
    /// 干渉しない。本番挙動を分岐しない（読み取りのみ）。<see cref="_logEnabled"/> を Inspector で有効化して使う。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyCombatDebugLogger : MonoBehaviour, IHitResultListener, IEnemyStateListener
    {
        [Tooltip("被弾・状態を Console へ出力するか（診断用。既定 無効）。")]
        [SerializeField] private bool _logEnabled;

        [Tooltip("対象の敵（未指定なら親から取得）。")]
        [SerializeField] private EnemyActor _actor;

        private void Awake()
        {
            if (_actor == null)
            {
                _actor = GetComponentInParent<EnemyActor>();
            }
        }

        private void OnEnable()
        {
            if (_actor != null)
            {
                _actor.Results.AddListener(this);
                _actor.States.AddListener(this);
            }
        }

        private void OnDisable()
        {
            if (_actor != null)
            {
                _actor.Results.RemoveListener(this);
                _actor.States.RemoveListener(this);
            }
        }

        /// <inheritdoc />
        public void OnHitResult(in HitResult result)
        {
            if (!_logEnabled || _actor == null)
            {
                return;
            }

            Debug.Log(
                "[EnemyCombat] hit=" + result.Kind
                + " Hp=" + _actor.CurrentHp + "/" + _actor.MaxHp
                + " Poise=" + _actor.CurrentPoise.ToString("0") + "/" + _actor.MaxPoise.ToString("0")
                + " Flinch=" + _actor.FlinchAccumulation.ToString("0")
                + " State=" + _actor.State, _actor);
        }

        /// <inheritdoc />
        public void OnEnemyStateChanged(in EnemyStateChanged change)
        {
            if (!_logEnabled)
            {
                return;
            }

            Debug.Log("[EnemyCombat] state " + change.Previous + " -> " + change.Current + " (" + change.Reason + ")",
                _actor);
        }
    }
}
