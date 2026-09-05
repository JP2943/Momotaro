using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の索敵（P4-03）。<see cref="PerceptionTargetRegistry"/> から敵対・有効な候補を集め、
    /// <see cref="CompanionTargetSelection"/> に「今狙う 1 体」を決めさせる。判断規則は持たず、収集と保持だけを担う。
    ///
    /// 候補収集は毎フレーム確保を避けるため使い回しのバッファで行う（既存の敵側と同じ方針。Phase3 §0.2「Find* を使わない」）。
    /// ダウン・退場・無効化のときは対象を手放し、購読・参照を残さない。
    ///
    /// 対象へ実際に近づく・攻撃するのは本 Task の対象外（P4-03 後半）。ここは「誰を狙っているか」までを確定させる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionTargetTracker : MonoBehaviour
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        private readonly List<IThreatTarget> _buffer = new List<IThreatTarget>();
        private readonly List<IPerceptionTarget> _candidates = new List<IPerceptionTarget>();

        /// <summary>現在の対象（無ければ null）。</summary>
        public IPerceptionTarget CurrentTarget { get; private set; }

        /// <summary>対象を保持しているか。</summary>
        public bool HasTarget => CompanionTargetSelection.IsUsable(CurrentTarget);

        /// <summary>直近に収集した候補数（テスト・診断用）。</summary>
        public int CandidateCount => _candidates.Count;

        /// <summary>対象が切り替わった回数（テスト・診断用）。</summary>
        public int TargetChanges { get; private set; }

        /// <summary>状態・Data の供給元を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor)
        {
            if (actor != null)
            {
                _actor = actor;
            }
        }

        /// <summary>
        /// 索敵を 1 回進める（Update から呼ばれるが、テストは直接呼べる）。戦闘に参加できない状態では対象を手放す。
        /// </summary>
        public void TickTargeting()
        {
            ResolveActor();
            if (_actor == null || !CanEngage(_actor.State))
            {
                _candidates.Clear();
                SetTarget(null);
                return;
            }

            CollectCandidates();

            float acquire = _actor.Data != null ? _actor.Data.TargetAcquireRange : DefaultAcquireRange;
            float lose = _actor.Data != null ? _actor.Data.TargetLoseRange : DefaultLoseRange;

            CompanionTargetSelection.TrySelect(
                _candidates, transform.position, CurrentTarget, acquire, lose, out IPerceptionTarget selected);
            SetTarget(selected);
        }

        /// <summary>Data 未割当時の捕捉距離（m）。</summary>
        public const float DefaultAcquireRange = 8f;

        /// <summary>Data 未割当時の見失い距離（m）。</summary>
        public const float DefaultLoseRange = 12f;

        /// <summary>この状態のとき戦闘対象を持てるか（ダウン・退場・ひるみ中は持たない）。</summary>
        public static bool CanEngage(CompanionState state)
        {
            return state != CompanionState.Away
                && state != CompanionState.Down
                && state != CompanionState.Recovering
                && state != CompanionState.Stagger;
        }

        private void CollectCandidates()
        {
            // 見失い距離まで含めて集める（捕捉距離での絞り込みは選択側が行う。維持判定に必要なため広めに取る）。
            float lose = _actor.Data != null ? _actor.Data.TargetLoseRange : DefaultLoseRange;
            PerceptionTargetRegistry.CollectHostileThreatTargets(
                transform.position, CombatFaction.Ally, lose, _buffer);

            _candidates.Clear();
            for (int i = 0; i < _buffer.Count; i++)
            {
                _candidates.Add(_buffer[i]);
            }
        }

        private void SetTarget(IPerceptionTarget target)
        {
            if (ReferenceEquals(CurrentTarget, target))
            {
                return;
            }

            CurrentTarget = target;
            TargetChanges++;
        }

        private void ResolveActor()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }
        }

        private void Update()
        {
            TickTargeting();
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で対象参照を残さない（§2.3 後始末）。
            _buffer.Clear();
            _candidates.Clear();
            CurrentTarget = null;
        }
    }
}
