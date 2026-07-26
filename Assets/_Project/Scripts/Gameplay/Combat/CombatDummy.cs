using Momotaro.Data.Characters;
using Momotaro.Gameplay.Enemy;
using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 検証用の被弾ダミー（Phase2 P2-04/P2-05。仕様書 §13）。AI を持たず、共通の受け手契約 <see cref="IDamageable"/> /
    /// <see cref="ICombatActor"/> を実装する（Dummy 専用の Combat 経路は作らない）。
    ///
    /// Phase3 P3-01：被弾数値（HP・体幹・ひるみ・スタン）は共通 Runtime <see cref="EnemyVitals"/> へ抽出し、本ダミーと
    /// <see cref="EnemyActor"/> が同一ロジックを共有する（重複排除）。挙動は従来と同一で、結果は型付き <see cref="HitResult"/>
    /// で通知（AppliedDamage は実際に適用された HP／体幹／ひるみ量）。死亡処理・敵 AI・攻撃は対象外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDummy : MonoBehaviour, ICombatActor, IDamageable, ICombatActivityState, IKnockbackReceiver
    {
        [Tooltip("HP・防御・体幹・ひるみ耐性などの基礎データ（EnemyData）。標準ダミーは HP100/防御20/体幹100/耐性60。")]
        [SerializeField] private EnemyData _data;

        [Tooltip("検証用の攻撃行動フェーズ。Startup/Active のとき体幹の攻撃中補正(×1.5)の対象になる。既定 None。")]
        [SerializeField] private CombatActionPhase _debugActionPhase = CombatActionPhase.None;

        private EnemyVitals _vitals;

        /// <summary>被弾結果の通知チャネル（HUD 等が購読）。</summary>
        public HitResultChannel Results { get; } = new HitResultChannel();

        /// <summary>現在 HP。</summary>
        public int CurrentHp
        {
            get { EnsureRuntime(); return _vitals.CurrentHp; }
        }

        /// <summary>最大 HP。</summary>
        public int MaxHp
        {
            get { EnsureRuntime(); return _vitals.MaxHp; }
        }

        /// <summary>撃破済みか（HP0。死亡処理そのものは対象外）。</summary>
        public bool IsDefeated
        {
            get { EnsureRuntime(); return _vitals.IsDefeated; }
        }

        /// <summary>現在体幹。</summary>
        public float CurrentPoise
        {
            get { EnsureRuntime(); return _vitals.CurrentPoise; }
        }

        /// <summary>最大体幹。</summary>
        public float MaxPoise
        {
            get { EnsureRuntime(); return _vitals.MaxPoise; }
        }

        /// <summary>スタン中か。</summary>
        public bool IsStunned
        {
            get { EnsureRuntime(); return _vitals.IsStunned; }
        }

        /// <summary>ひるみ中か。</summary>
        public bool IsFlinching
        {
            get { EnsureRuntime(); return _vitals.IsFlinching; }
        }

        /// <summary>現在のひるみ蓄積量。</summary>
        public float FlinchAccumulation
        {
            get { EnsureRuntime(); return _vitals.FlinchAccumulation; }
        }

        /// <inheritdoc />
        public int DamageableId => GetInstanceID();

        /// <inheritdoc />
        public CombatFaction Faction => CombatFaction.Enemy;

        /// <inheritdoc />
        public int FloorId => 0;

        /// <inheritdoc />
        public Vector3 WorldPosition => transform.position;

        /// <inheritdoc />
        public Vector3 Forward => transform.forward;

        /// <summary>検証用の攻撃行動フェーズ（切替可能）。</summary>
        public CombatActionPhase ActionPhase
        {
            get => _debugActionPhase;
            set => _debugActionPhase = value;
        }

        /// <summary>検証用の攻撃行動フェーズを設定する。</summary>
        public void SetActionPhase(CombatActionPhase phase)
        {
            _debugActionPhase = phase;
        }

        /// <inheritdoc />
        /// <remarks>
        /// スタン・ひるみ・撃破中は攻撃が中断されるため、たとえ <see cref="_debugActionPhase"/> が Startup/Active でも
        /// 攻撃中補正の対象にしない（状態競合の抑止）。通常状態の Startup/Active のみ true。
        /// </remarks>
        public bool IsPoiseVulnerableAction =>
            !IsStunned && !IsFlinching && !IsDefeated && _debugActionPhase.IsPoiseVulnerable();

        /// <summary>ボス（大型敵）か（EnemyData 由来）。ボスはノックバック無効。</summary>
        public bool IsBoss => _data != null && _data.IsBoss;

        /// <summary>直近に受けたノックバック力（検証用。ボスは常に 0）。</summary>
        public float LastKnockback { get; private set; }

        /// <inheritdoc />
        /// <remarks>Phase 2 の仮反応：物理は動かさず、受けた力を記録するだけ。ボス（<see cref="IsBoss"/>）は無効（0）。</remarks>
        public void ReceiveKnockback(Vector3 direction, float force)
        {
            LastKnockback = IsBoss ? 0f : force;
        }

        private void Awake()
        {
            EnsureRuntime();
            // 敵は Enemy レイヤーへ。Player は敵をすり抜け、壁（Default）では停止する（仕様書 §3.4 / P2-09）。
            CombatLayers.ConfigureEnemy(gameObject);
        }

        private void EnsureRuntime()
        {
            if (_vitals == null)
            {
                // 数値の出所は EnemyData（IEnemyVitalsConfig）。null 時は最小既定（従来同様）。
                _vitals = new EnemyVitals(_data);
            }
        }

        /// <summary>HP を最大まで戻す（検証の再試行用）。</summary>
        public void ResetHp()
        {
            EnsureRuntime();
            _vitals.ResetHp();
        }

        /// <summary>HP・体幹・ひるみを最大/初期へ戻す（検証の再試行用）。</summary>
        public void ResetState()
        {
            EnsureRuntime();
            _vitals.ResetState();
            _debugActionPhase = CombatActionPhase.None;
        }

        private void Update()
        {
            EnsureRuntime();
            _vitals.Tick(Time.deltaTime);
        }

        /// <inheritdoc />
        public void ReceiveHit(in HitInfo hit)
        {
            EnsureRuntime();

            // 被弾数値の適用は共通 Runtime（EnemyVitals）へ委譲。必殺技の防御一部無視・スタン中 HP 倍率（置き換え）・
            // 対象被体幹倍率・JG 反射の回復延長は同ロジック内で処理される（挙動は従来と同一）。
            EnemyVitals.HitApplication app = _vitals.Apply(hit);

            // スタン／ひるみが新規発生したら攻撃行動は中断される。検証表示と内部状態を合わせるため ActionPhase を None へ戻す。
            if (app.NewlyStunned || app.NewlyFlinching)
            {
                _debugActionPhase = CombatActionPhase.None;
            }

            Results.Publish(HitResult.Damage(hit.HitId, hit.Attacker, this, app.Applied));
        }
    }
}
