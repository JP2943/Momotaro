using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// GameObject を認識対象として <see cref="PerceptionTargetRegistry"/> へ登録する薄い MonoBehaviour（Phase3 §4）。
    /// 主人公（や将来の仲間）に付与する。位置は Transform から読み取り、Input／Presentation へ依存しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PerceptionTargetBinder : MonoBehaviour, IPerceptionTarget
    {
        [Tooltip("この対象の陣営（主人公=Player）。")]
        [SerializeField] private CombatFaction _faction = CombatFaction.Player;

        /// <inheritdoc />
        public int ActorId => GetInstanceID();
        /// <inheritdoc />
        public CombatFaction Faction => _faction;
        /// <inheritdoc />
        public Vector3 Position => transform.position;
        /// <inheritdoc />
        public bool IsActive => isActiveAndEnabled;

        private void OnEnable() => PerceptionTargetRegistry.Register(this);
        private void OnDisable() => PerceptionTargetRegistry.Unregister(this);
    }
}
