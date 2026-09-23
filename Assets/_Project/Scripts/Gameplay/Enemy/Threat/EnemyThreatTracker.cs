using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Threat
{
    /// <summary>
    /// 敵のヘイト・ターゲット選択ドライバ（Phase3 P3-06。§7）。観測可能な戦闘結果を <see cref="EnemyThreatTable"/> の行動加算へ変換し
    /// （被弾 HP／体幹、ひるみ、ジャストガード反射）、在圏の敵対 <see cref="IThreatTarget"/> を毎フレーム候補として渡して現在対象を
    /// 決定する。時間は Game Time で進み、Pause／会話中は停止する。Find* を使わず <see cref="PerceptionTargetRegistry"/> と再利用
    /// バッファで候補を集め、毎フレーム確保・LINQ を行わない（§0.2）。現在対象・脅威・次回再評価は読み取り専用で公開し（Debug／Phase 4）、
    /// 主人公のみでも同一経路を通す（§7 目的）。近接攻撃中（<see cref="EnemyAttackController.IsAttacking"/>）は嗜好切替を保留する（§7.2）。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyActor))]
    public sealed class EnemyThreatTracker : MonoBehaviour, IHitResultListener, IEnemyStateListener, IPerceptionFocusSource
    {
        [Tooltip("ヘイト評価の調整値（§7.1／§7.2）。既定は仕様書の試作値。")]
        [SerializeField] private ThreatSettings _settings = ThreatSettings.Default;

        [Tooltip("脅威対象として扱う最大距離（m。0=アーキタイプの活動半径を使用。範囲外は候補から外れ即時切替に至る）。")]
        [SerializeField] private float _maxTargetRange;

        private EnemyActor _actor;
        private EnemyAttackController _combat;
        private EnemyThreatTable _table;
        private readonly List<IThreatTarget> _candidates = new List<IThreatTarget>(8);
        private IThreatTarget _lastDamageTarget; // ひるみ・JG 反射の帰属先（直近に有効打を与えた対象）。
        private bool _subscribed;

        /// <summary>現在選択中の対象 ActorId（未選択は <see cref="EnemyThreatTable.NoTarget"/>）。読み取り専用。</summary>
        public int CurrentTargetId => _table != null ? _table.CurrentTargetId : EnemyThreatTable.NoTarget;

        /// <summary>次回再評価までの残り秒（Debug）。</summary>
        public float TimeToReevaluate => _table != null ? _table.TimeToReevaluate : 0f;

        /// <summary>脅威を追跡している対象数（Debug）。</summary>
        public int TrackedCount => _table != null ? _table.TrackedCount : 0;

        /// <summary>現在対象の脅威値（見つからなければ 0。Debug）。</summary>
        public float CurrentThreat
        {
            get
            {
                IThreatTarget t = CurrentTarget;
                return t != null && _table != null ? _table.GetThreat(t) : 0f;
            }
        }

        /// <summary>現在対象の <see cref="IThreatTarget"/>（候補中に無ければ null。Debug／Phase 4）。</summary>
        public IThreatTarget CurrentTarget
        {
            get
            {
                int id = CurrentTargetId;
                if (id == EnemyThreatTable.NoTarget)
                {
                    return null;
                }

                for (int i = 0; i < _candidates.Count; i++)
                {
                    if (_candidates[i] != null && _candidates[i].ActorId == id)
                    {
                        return _candidates[i];
                    }
                }

                return null;
            }
        }

        /// <summary>内部テーブル（テスト・Debug のための読み取りアクセス）。</summary>
        public EnemyThreatTable Table => _table;

        /// <summary>
        /// 直近に収集した脅威候補（読み取り専用。診断表示が候補全員の脅威を並べるために読む）。
        /// 表示専用の窓で、判断は一切行わない（P4-03 受入）。
        /// </summary>
        public IReadOnlyList<IThreatTarget> Candidates => _candidates;

        /// <summary>候補 1 体の現在脅威＝基礎＋獲得（無効・Down なら 0）。診断用。</summary>
        public float ThreatOf(IThreatTarget target)
        {
            return _table != null ? _table.GetThreat(target) : 0f;
        }

        /// <summary>候補 1 体の獲得ヘイト（基礎を含まない。減衰の対象）。診断用。</summary>
        public float AcquiredOf(IThreatTarget target)
        {
            return _table != null && target != null ? _table.GetAcquired(target.ActorId) : 0f;
        }

        /// <inheritdoc />
        /// <remarks>認識（<see cref="EnemyPerception"/>）が追う対象を Threat 最大対象へ接続する（req1）。未選択時は false。</remarks>
        public bool TryGetFocusTarget(out IPerceptionTarget target)
        {
            target = CurrentTarget;
            return target != null;
        }

        private void Awake()
        {
            EnsureRefs();
        }

        private void OnEnable()
        {
            EnsureRefs();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void EnsureRefs()
        {
            if (_actor == null)
            {
                _actor = GetComponent<EnemyActor>();
            }

            if (_combat == null)
            {
                _combat = GetComponent<EnemyAttackController>();
            }

            if (_table == null)
            {
                _table = new EnemyThreatTable(EffectiveSettings());
            }
            else
            {
                _table.Configure(EffectiveSettings());
            }
        }

        private ThreatSettings EffectiveSettings()
        {
            // 全ゼロで直列化された場合（設定忘れ）は既定へフォールバックする。
            return _settings.ReevaluateInterval > 0f ? _settings : ThreatSettings.Default;
        }

        private void Subscribe()
        {
            if (_subscribed || _actor == null)
            {
                return;
            }

            _actor.Results.AddListener(this);
            _actor.States.AddListener(this);
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _actor == null)
            {
                return;
            }

            _actor.Results.RemoveListener(this);
            _actor.States.RemoveListener(this);
            _subscribed = false;
        }

        private void Update()
        {
            if (!IsGameplayActive())
            {
                return; // Pause／会話／ローディング中は再評価・減衰を止める。
            }

            TickSelection(Time.deltaTime);
        }

        /// <summary>選択と減衰を 1 フレーム進める（Update から、またはテストが決定的に呼ぶ）。</summary>
        public void TickSelection(float deltaTime)
        {
            // 遷移中は Gameplay 時計を進めない。Update からでも直接呼ばれても同じ（§6.2 手順 3、P5-E07）。
            if (Session.GameplayClockProvider.IsFrozen)
            {
                return;
            }

            using var _perf = EnemyProfilerMarkers.Threat.Auto(); // P3-11：Threat 選択の負荷計測。
            EnsureRefs();
            if (_actor == null || _table == null)
            {
                return;
            }

            float range = _maxTargetRange > 0f
                ? _maxTargetRange
                : (_actor.Archetype != null ? _actor.Archetype.ActivityRadius : 0f);

            PerceptionTargetRegistry.CollectHostileThreatTargets(_actor.WorldPosition, _actor.Faction, range, _candidates);
            bool attackLocked = _combat != null && _combat.IsAttacking;
            _table.UpdateSelection(_candidates, deltaTime, attackLocked);
        }

        /// <inheritdoc />
        public void OnHitResult(in HitResult result)
        {
            EnsureRefs();
            if (_table == null || !ReferenceEquals(result.Target, _actor))
            {
                return; // 自分の被弾のみをヘイト加算源にする。
            }

            HitDamage applied = result.AppliedDamage;

            if (result.Attacker == null)
            {
                // 攻撃者不在で体幹のみ適用＝ジャストガード反射（Phase 2）。JG した対象＝敵の現在対象に帰属させる（§7.1: +30）。
                if (applied.Poise > 0f)
                {
                    IThreatTarget jgTarget = CurrentTarget ?? _lastDamageTarget;
                    if (jgTarget != null)
                    {
                        _table.AddThreat(jgTarget, ThreatSource.JustGuard);
                    }
                }

                return;
            }

            if (result.Kind != HitResultKind.Damage)
            {
                return; // ガード成立・回避・棄却は敵へダメージを与えていない。
            }

            if (!PerceptionTargetRegistry.TryResolveThreatTarget(result.Attacker, out IThreatTarget target))
            {
                return;
            }

            _lastDamageTarget = target;

            // 口火を切った一撃（試遊フィードバック 2026-09-21）。まだ誰とも交戦していない敵にとって、
            // 最初に実害を与えた相手は「自分を襲った者」であり、基礎ヘイトの高い主人公より先に狙うべき相手になる。
            // 空振り同然（HP も体幹も 0）の命中で 1 回限りの権利を消費しないよう、実際に通った命中だけを口火とみなす。
            if ((applied.Hp > 0f || applied.Poise > 0f) && _table.TryConsumeFirstStrike())
            {
                _table.AddThreat(target, ThreatSource.FirstStrike);
            }

            if (applied.Hp > 0f)
            {
                _table.AddThreat(target, ThreatSource.HpDamage, applied.Hp);
            }

            if (applied.Poise > 0f)
            {
                _table.AddThreat(target, ThreatSource.PoiseDamage, applied.Poise);
            }
        }

        /// <inheritdoc />
        public void OnEnemyStateChanged(in EnemyStateChanged change)
        {
            if (_table == null || _actor == null || change.ActorId != _actor.DamageableId)
            {
                return;
            }

            // ひるみ成立（§7.1: +20）。ひるみを起こした対象＝直近の有効打の攻撃者へ帰属させる。
            if (change.Current == EnemyState.Stagger && change.Reason == EnemyStateChangeReason.Staggered
                && _lastDamageTarget != null)
            {
                _table.AddThreat(_lastDamageTarget, ThreatSource.Flinch);
            }

            // 戦闘終了：撃破（Down）または帰還完了（Return→通常）で脅威を初期化する（§7.2）。
            if (change.Current == EnemyState.Down)
            {
                _table.Reset(rearmFirstStrike: false); // 撃破。以後この敵の脅威は使われない。
                _lastDamageTarget = null;
            }
            else if (change.Previous == EnemyState.Return
                     && (change.Current == EnemyState.Idle || change.Current == EnemyState.Patrol))
            {
                _table.Reset(); // 帰還完了＝戦闘の終わり。次に襲われたら、また先制した側を狙う。
                _lastDamageTarget = null;
            }
        }

        private static bool IsGameplayActive()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return true; // 未初期化（単体テスト等）は許可。
            }

            GameMode m = modes.Current;
            return m == GameMode.Exploration || m == GameMode.Combat;
        }
    }
}
