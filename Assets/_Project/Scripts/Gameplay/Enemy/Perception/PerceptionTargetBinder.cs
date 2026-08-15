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

        /// <inheritdoc />
        public int ActorId => GetInstanceID();
        /// <inheritdoc />
        public CombatFaction Faction => _faction;
        /// <inheritdoc />
        public Vector3 Position => transform.position;
        /// <inheritdoc />
        public bool IsActive => isActiveAndEnabled;
        /// <inheritdoc />
        /// <remarks>主人公のダウン（＝ゲームオーバー）は本 Phase では扱わないため常に false。将来の仲間はダウン状態を反映する。</remarks>
        public bool IsDown => false;
        /// <inheritdoc />
        public float BaseThreat => _baseThreat;
        /// <inheritdoc />
        public float AcquiredThreatMultiplier => _acquiredThreatMultiplier;

        private void OnEnable() => PerceptionTargetRegistry.Register(this);
        private void OnDisable() => PerceptionTargetRegistry.Unregister(this);
    }
}
