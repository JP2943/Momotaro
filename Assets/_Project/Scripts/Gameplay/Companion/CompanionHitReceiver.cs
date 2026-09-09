using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の被弾受け口（P4-04）。主人公・敵と同じ <see cref="IDamageable"/> 契約で命中を受け、共通の解決順に載せる。
    /// 仲間専用の被弾経路は作らない。
    ///
    /// 解決順（主人公 <c>PlayerVitalsHolder</c> と同じ並び）：
    /// <list type="number">
    /// <item><description>退場中・ダウン中は一切受け付けない（結果も通知も出さない）</description></item>
    /// <item><description>同一命中の二重受理を弾く（<see cref="ReceivedHitTracker"/>。判定からの直接命中と
    /// 肩代わりによる転送が<b>どちらの順で届いても</b>1 回だけ受理する。P4-01 の契約）</description></item>
    /// <item><description>被弾後無敵</description></item>
    /// <item><description>回避の無敵（<see cref="ICompanionDefenseState"/>。未装備なら素通り）</description></item>
    /// <item><description>ガード（<c>Guardable</c> かつ前方 180°）</description></item>
    /// <item><description>ダメージ適用 → ひるみ → ダウン</description></item>
    /// </list>
    ///
    /// 状態遷移（Stagger／Down／復帰）は本コンポーネントが <see cref="CompanionActor"/> へ要求する。数値は
    /// <see cref="CompanionVitals"/> が Data から読む。時間は Update から注入し、テストは <see cref="TickVitals"/> を直接呼べる。
    ///
    /// ジャストガード・ジャスト回避は仲間には持たせない（仕様に定義が無く、AI に「ジャスト」の裁量を与える設計判断が要る）。
    /// 必要になったら P4-04b 以降で Data ごと追加する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionHitReceiver : MonoBehaviour, IDamageable, IGuardianReceiver
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        private CompanionVitals _vitals;
        private readonly ReceivedHitTracker _received = new ReceivedHitTracker();
        private ICompanionDefenseState _defense;
        private bool _defenseResolved;

        /// <summary>被弾結果の通知チャネル（HUD・フィードバック・Debug が購読）。</summary>
        public HitResultChannel Results { get; } = new HitResultChannel();

        /// <inheritdoc />
        public int DamageableId => GetInstanceID();

        /// <inheritdoc />
        public Vector3 WorldPosition => transform.position;

        /// <inheritdoc />
        /// <remarks>
        /// 倒れている・退場している・復帰待ちの間は引き受けない。引き受けられないときは主人公への通常ダメージへ
        /// フォールバックする（肩代わりが「消える」ことは無い）。距離・クールダウンの判断は
        /// <see cref="CompanionGuardianController"/> が持つ（本契約は本人の状態だけを表す。P4-01）。
        /// </remarks>
        public bool CanTakeOver
        {
            get
            {
                EnsureRuntime();
                if (_actor == null || _vitals == null || !isActiveAndEnabled)
                {
                    return false;
                }

                return !_actor.IsAway
                    && !_vitals.IsDown
                    && _actor.State != CompanionState.Recovering;
            }
        }

        /// <summary>生存値（テスト・Debug 用）。</summary>
        public CompanionVitals Vitals
        {
            get
            {
                EnsureRuntime();
                return _vitals;
            }
        }

        /// <summary>現在 HP（診断用）。</summary>
        public int CurrentHp => Vitals.Health.Current;

        /// <summary>最大 HP（診断用）。</summary>
        public int MaxHp => Vitals.Health.Max;

        /// <summary>状態・Data の供給元を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor)
        {
            if (actor != null)
            {
                _actor = actor;
            }
        }

        /// <inheritdoc />
        public void ReceiveHit(in HitInfo hit)
        {
            EnsureRuntime();
            if (_actor == null || _vitals == null)
            {
                return;
            }

            // 場に居ない（退場）・倒れている（ダウン）ときは受け付けない。結果も出さない。
            if (_actor.IsAway || _vitals.IsDown)
            {
                return;
            }

            // 同一命中は 1 回だけ。直接命中と肩代わり転送の到達順に依存しない（P4-01）。
            if (!_received.TryAccept(hit.HitId))
            {
                return;
            }

            if (_vitals.IsPostHitInvincible)
            {
                Results.Publish(HitResult.Evade(hit.HitId, hit.Attacker, this, hit.HitPoint, hit.AttackDirection));
                return;
            }

            ICompanionDefenseState defense = ResolveDefense();

            if (defense != null && defense.IsEvadeInvulnerable && hit.Steppable)
            {
                Results.Publish(HitResult.Evade(hit.HitId, hit.Attacker, this, hit.HitPoint, hit.AttackDirection));
                return;
            }

            bool withinArc = GuardGeometry.IsWithinGuardArc(_actor.Forward, hit.AttackDirection);
            bool guarding = defense != null && defense.IsGuarding;
            if (GuardResolver.Resolve(guarding, hit.Guardable, withinArc) == GuardOutcome.Guarded)
            {
                Results.Publish(HitResult.Guard(hit.HitId, hit.Attacker, this, HitDamage.None, hit.HitPoint, hit.AttackDirection));
                return;
            }

            float defenseValue = _actor.Data != null ? _actor.Data.Defense : 0f;
            CompanionVitals.HitApplication applied = _vitals.ApplyHit(hit, defenseValue);

            if (applied.NewlyDowned)
            {
                _actor.ForceHitState(CompanionState.Down, CompanionStateChangeReason.Defeated);
            }
            else if (applied.NewlyFlinching)
            {
                _actor.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered);
            }

            Results.Publish(HitResult.Damage(
                hit.HitId, hit.Attacker, this, applied.Applied, hit.HitPoint, hit.AttackDirection));
        }

        /// <summary>
        /// 生存値の時間を 1 Tick 進め、状態（ひるみ終了・ダウンからの復帰）へ反映する
        /// （Update から呼ばれるが、テストは決定的に直接呼べる）。
        /// </summary>
        public void TickVitals(float deltaTime)
        {
            EnsureRuntime();
            if (_actor == null || _vitals == null || _actor.IsAway)
            {
                return;
            }

            bool revived = _vitals.Tick(deltaTime);

            if (revived)
            {
                // ダウンからの復帰。追従へ戻し、以後は通常どおり戦闘に参加できる。
                _actor.RequestState(CompanionState.Follow, CompanionStateChangeReason.Recovered);
                _received.Clear(); // 倒れている間に届いた命中の記録は持ち越さない。
                return;
            }

            // ひるみが明けたら追従へ戻す（Down 中はここへ来ない）。
            if (_actor.State == CompanionState.Stagger && !_vitals.IsFlinching)
            {
                _actor.RequestState(CompanionState.Follow, CompanionStateChangeReason.Recovered);
            }
        }

        /// <summary>全快させて初期化する（加入・Retry・Scene 再構築）。</summary>
        public void ResetVitals()
        {
            EnsureRuntime();
            _vitals?.Reset();
            _received.Clear();
        }

        private void Update()
        {
            if (!IsGameplayActive())
            {
                return; // Pause／会話中は無敵・ひるみ・復帰待ちを進めない。
            }

            TickVitals(Time.deltaTime);
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で受理記録を残さない（§2.3 後始末）。
            _received.Clear();
        }

        private void EnsureRuntime()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_vitals == null && _actor != null)
            {
                _vitals = new CompanionVitals(_actor.Data);
            }
        }

        /// <summary>
        /// 防御状態の供給元を解決する（同一 GameObject。未装備なら null のまま＝構えも退避もしない）。
        /// interface 参照は Unity の null 演算子が効かないため、破棄済み Object を明示的に捨てて取り直す。
        /// </summary>
        private ICompanionDefenseState ResolveDefense()
        {
            if (_defense is Object destroyed && destroyed == null)
            {
                _defense = null;
                _defenseResolved = false;
            }

            if (!_defenseResolved || _defense == null)
            {
                _defense = GetComponent<ICompanionDefenseState>();
                _defenseResolved = true;
            }

            return _defense;
        }

        private static bool IsGameplayActive()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return true; // 未初期化（単体テスト等）は許可。
            }

            GameMode mode = modes.Current;
            return mode == GameMode.Exploration || mode == GameMode.Combat;
        }
    }
}
