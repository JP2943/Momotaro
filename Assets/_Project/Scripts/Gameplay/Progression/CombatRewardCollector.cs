using Momotaro.Core.Logging;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Scenes;
using UnityEngine;

namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 撃破報酬（<see cref="EnemyRewardRequest"/>）の受け手（P4-00）。Phase 3 で「発行のみ」だった報酬要求を購読し、
    /// <see cref="PlayerProgressHolder"/> へ実付与する。購読元は <see cref="CombatSessionController.EnemyDefeated"/> 一本で、
    /// 敵の探索・再スキャンは行わない（敵の登録経路は <c>WaveRunner</c> → <c>CombatSessionController.RegisterEnemy</c> のまま一本に保つ）。
    /// Session 側が初回撃破のみを受理して発火するため、敵インスタンス単位の重複排除は本コンポーネントでは行わない。
    ///
    /// 付与の一回性（GrantOnce）は Reward の安定 ID 単位で <see cref="PlayerProgressState"/> が担保する。
    /// アイテム付与・Inventory は本 Task の対象外で、非空の ItemId を受け取った場合は未実装である旨を一度だけ警告する。
    /// 報酬未設定の敵（<c>RewardData</c> が null）は正常系として無視する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatRewardCollector : MonoBehaviour
    {
        [Tooltip("撃破通知の購読元（Scene 構築・テストが注入）。")]
        [SerializeField] private CombatSessionController _session;

        [Tooltip("付与先の進行データ（Scene 構築・テストが注入）。")]
        [SerializeField] private PlayerProgressHolder _progress;

        private bool _subscribed;

        /// <summary>購読元 Session（配線確認・Validator・テスト用）。</summary>
        public CombatSessionController Session => _session;

        /// <summary>付与先の進行データ（配線確認・Validator・テスト用）。</summary>
        public PlayerProgressHolder Progress => _progress;

        /// <summary>実際に付与した回数（テスト・診断用）。</summary>
        public int GrantedCount { get; private set; }

        /// <summary>GrantOnce の重複で付与しなかった回数（テスト・診断用）。</summary>
        public int AlreadyGrantedCount { get; private set; }

        /// <summary>報酬未設定の撃破を受け取った回数（テスト・診断用）。</summary>
        public int NoRewardCount { get; private set; }

        /// <summary>直近に付与した徳量（テスト・診断用）。</summary>
        public int LastGrantedVirtue { get; private set; }

        /// <summary>購読元・付与先を注入する（null は無視して既存を保つ。同一参照の再 Bind は無視）。</summary>
        public void Bind(CombatSessionController session, PlayerProgressHolder progress)
        {
            if (progress != null)
            {
                _progress = progress;
            }

            if (session == null || _session == session)
            {
                return;
            }

            Unsubscribe();
            _session = session;
            if (isActiveAndEnabled)
            {
                Subscribe();
            }
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
            if (_subscribed || _session == null)
            {
                return;
            }

            _session.EnemyDefeated += OnEnemyDefeated;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _session == null)
            {
                return;
            }

            _session.EnemyDefeated -= OnEnemyDefeated;
            _subscribed = false;
        }

        /// <summary>撃破 1 件を受け取り、載っている報酬要求を付与する。素材・Data 未設定でも例外なく継続する。</summary>
        private void OnEnemyDefeated(EnemyDefeatedEvent defeated)
        {
            RewardSnapshot reward = RewardSnapshot.From(defeated.Reward.Reward);
            if (!reward.HasReward)
            {
                // 報酬未設定の敵（Archetype に RewardData 未割当）。試遊では正常系として無視する。
                NoRewardCount++;
                return;
            }

            if (_progress == null)
            {
                GameLog.WarningOnce(LogCategory.Combat, "reward_progress_unbound",
                    "撃破報酬の付与先（PlayerProgressHolder）が未配線のため、報酬を付与できません。", reward.RewardId.Value);
                return;
            }

            // アイテム付与・Inventory は P4-00 の対象外。受信したことだけを ItemId ごとに一度だけ報告する。
            if (!reward.ItemId.IsEmpty)
            {
                GameLog.WarningOnce(LogCategory.Combat, "reward_item_unimplemented:" + reward.ItemId.Value,
                    "アイテム報酬 '" + reward.ItemId.Value + "' を受信しましたが、アイテム付与は未実装のため無視しました（P4-00 対象外）。",
                    reward.RewardId.Value);
            }

            RewardGrantResult result = _progress.Grant(reward, out int grantedVirtue);
            switch (result)
            {
                case RewardGrantResult.Granted:
                    GrantedCount++;
                    LastGrantedVirtue = grantedVirtue;
                    break;

                case RewardGrantResult.GrantedWithoutId:
                    GrantedCount++;
                    LastGrantedVirtue = grantedVirtue;
                    GameLog.WarningOnce(LogCategory.Validation, "reward_missing_id",
                        "GrantOnce の報酬に安定 ID が設定されていないため、重複付与を防げません（Reward Data の Stable ID を設定してください）。");
                    break;

                case RewardGrantResult.AlreadyGranted:
                    AlreadyGrantedCount++;
                    break;

                default:
                    NoRewardCount++;
                    break;
            }
        }
    }
}
