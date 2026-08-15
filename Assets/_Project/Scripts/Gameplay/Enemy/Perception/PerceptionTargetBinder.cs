using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Threat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// GameObject を認識・脅威対象として <see cref="PerceptionTargetRegistry"/> へ登録する薄い MonoBehaviour（Phase3 §4／P3-06 §7）。
    /// 主人公（や将来の仲間）に付与する。位置は Transform から読み取り、Input／Presentation へ依存しない。<see cref="IThreatTarget"/> を
    /// 実装し、自らの脅威プロファイル（基礎ヘイト・獲得倍率・ダウン中か）を宣言する。これにより敵 AI を書き換えずに Phase 4 の仲間を
    /// 候補へ追加できる（§15）。主人公は基礎ヘイト 50・獲得倍率 1.0（§7.1 対象補正）。
    ///
    /// Phase3.5 P3.5-02：同一エンティティに <see cref="IPlayerDefeatState"/>（主人公＝<see cref="Momotaro.Gameplay.Player.PlayerVitalsHolder"/>）が
    /// あれば、その死亡を <see cref="IsDown"/>=true・<see cref="IsActive"/>=false へ反映する。これにより既存の EnemyThreatTable／
    /// EnemyAttackController（IsActive/IsDown で対象を即時無効化する契約）だけで、敵は新規追跡・攻撃を止め、進行中攻撃を終えて Slot を解放する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerceptionTargetBinder : MonoBehaviour, IThreatTarget
    {
        [Tooltip("この対象の陣営（主人公=Player）。")]
        [SerializeField] private CombatFaction _faction = CombatFaction.Player;

        [Tooltip("基礎ヘイト（§7.1 対象補正。主人公=50）。減衰されず、有効な限り脅威の下限として維持される。")]
        [SerializeField] private float _baseThreat = 50f;

        [Tooltip("獲得ヘイトへの対象補正（§7.1。主人公=1.0／犬1.5／猿1.2／雉0.5）。")]
        [SerializeField] private float _acquiredThreatMultiplier = 1f;

        private IPlayerDefeatState _defeatState;
        private bool _defeatStateResolved;

        private IPlayerDefeatState ResolveDefeatState()
        {
            if (!_defeatStateResolved)
            {
                _defeatState = GetComponentInParent<IPlayerDefeatState>();
                _defeatStateResolved = true;
            }

            return _defeatState;
        }

        /// <summary>この対象が死亡（Defeated）扱いか。死亡状態提供が無い将来の仲間等では常に false。</summary>
        private bool DefeatedTarget
        {
            get
            {
                IPlayerDefeatState d = ResolveDefeatState();
                return d != null && d.IsDefeated;
            }
        }

        /// <inheritdoc />
        public int ActorId => GetInstanceID();
        /// <inheritdoc />
        public CombatFaction Faction => _faction;
        /// <inheritdoc />
        public Vector3 Position => transform.position;
        /// <inheritdoc />
        /// <remarks>死亡確定後は感知対象として無効（非活動）とし、敵が新規に捕捉しないようにする（Phase3.5 §4.1）。</remarks>
        public bool IsActive => isActiveAndEnabled && !DefeatedTarget;
        /// <inheritdoc />
        /// <remarks>死亡（＝ゲームオーバー）で true。脅威テーブルが即時に脅威 0・切替する（Phase3.5 §4.1）。将来の仲間もダウンを反映する。</remarks>
        public bool IsDown => DefeatedTarget;
        /// <inheritdoc />
        public float BaseThreat => _baseThreat;
        /// <inheritdoc />
        public float AcquiredThreatMultiplier => _acquiredThreatMultiplier;

        private void OnEnable() => PerceptionTargetRegistry.Register(this);
        private void OnDisable() => PerceptionTargetRegistry.Unregister(this);
    }
}
