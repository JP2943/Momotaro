using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間を敵の認識・ヘイト候補として登録する（P4-03）。主人公の <c>PerceptionTargetBinder</c> と同じ役割だが、
    /// 脅威プロファイル（基礎ヘイト・獲得倍率）を <see cref="CompanionActor.Data"/> から読み、ダウン・退場を
    /// <see cref="CompanionState"/> から判定する点が異なる。
    ///
    /// <b>敵 AI は一行も書き換えない。</b><see cref="PerceptionTargetRegistry.IsHostile"/> は既に Enemy↔Ally を敵対と定義しており、
    /// 敵側は <see cref="IThreatTarget"/> の <see cref="IsActive"/>／<see cref="IsDown"/> で対象を即時に有効化・無効化する契約
    /// （Phase3 §7.2）を持つ。本コンポーネントを付けるだけで、犬丸は既存の知覚・ヘイト・攻撃スロットに候補として載る。
    ///
    /// ダウン中と退場中は脅威 0（<see cref="IsDown"/>=true・<see cref="IsActive"/>=false）とし、敵が新規に狙わず、
    /// 現在対象なら即座に切り替わるようにする。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionThreatBinder : MonoBehaviour, IThreatTarget
    {
        [Tooltip("脅威プロファイルの供給元（未設定なら親から自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        /// <summary>Data 未割当時の基礎ヘイト（仲間は 0 が基本。§7.1）。</summary>
        public const float DefaultBaseThreat = 0f;

        /// <summary>Data 未割当時の獲得ヘイト補正（犬＝1.5。§7.1）。</summary>
        public const float DefaultAcquiredThreatMultiplier = 1.5f;

        /// <summary>脅威プロファイルの供給元（配線確認・テスト用）。</summary>
        public CompanionActor Actor => ResolveActor();

        /// <inheritdoc />
        public int ActorId => GetInstanceID();

        /// <inheritdoc />
        public CombatFaction Faction => CombatFaction.Ally;

        /// <inheritdoc />
        public Vector3 Position => transform.position;

        /// <inheritdoc />
        /// <remarks>ダウン・退場中は感知対象として無効にし、敵が新規に捕捉しないようにする。</remarks>
        public bool IsActive => isActiveAndEnabled && !IsDown;

        /// <inheritdoc />
        /// <remarks>ダウンだけでなく退場（未加入・交代・Scene 離脱）も脅威 0 として扱う（場に居ないため）。</remarks>
        public bool IsDown
        {
            get
            {
                CompanionActor actor = ResolveActor();
                return actor == null || actor.IsDown || actor.IsAway;
            }
        }

        /// <inheritdoc />
        public float BaseThreat
        {
            get
            {
                CompanionActor actor = ResolveActor();
                return actor != null && actor.Data != null ? actor.Data.BaseThreat : DefaultBaseThreat;
            }
        }

        /// <inheritdoc />
        public float AcquiredThreatMultiplier
        {
            get
            {
                CompanionActor actor = ResolveActor();
                return actor != null && actor.Data != null
                    ? actor.Data.AcquiredThreatMultiplier
                    : DefaultAcquiredThreatMultiplier;
            }
        }

        /// <summary>脅威プロファイルの供給元を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor)
        {
            if (actor != null)
            {
                _actor = actor;
            }
        }

        private CompanionActor ResolveActor()
        {
            if (_actor == null)
            {
                _actor = GetComponentInParent<CompanionActor>();
            }

            return _actor;
        }

        private void OnEnable() => PerceptionTargetRegistry.Register(this);

        private void OnDisable() => PerceptionTargetRegistry.Unregister(this);
    }
}
