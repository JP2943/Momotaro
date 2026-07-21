using Momotaro.Data.Characters;
using Momotaro.Gameplay.Vitals;
using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 検証用の被弾ダミー（Phase2 P2-04。仕様書 §13）。AI を持たず、共通の受け手契約 <see cref="IDamageable"/> /
    /// <see cref="ICombatActor"/> を実装する（Dummy 専用の Combat 経路は作らず、Phase 3 の敵 AI へそのまま発展できる）。
    ///
    /// P2-04 では HP のみを扱う：命中の攻撃側寄与（<see cref="HitInfo.HitDamage"/> の Hp＝防御適用前）へ自身の防御を
    /// <see cref="HpDamageCalculator.ResolveFinal"/> で適用し、<see cref="Vital"/>（PlayerVitals と同じ HP システム）へ
    /// 減算する。結果は型付き <see cref="HitResult"/> として通知する。体幹・ひるみ・スタンは P2-05、死亡処理は対象外
    /// （HP0 は撃破フラグのみ、<see cref="ResetHp"/> で復帰可能）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDummy : MonoBehaviour, ICombatActor, IDamageable
    {
        [Tooltip("HP・防御などの基礎データ（EnemyData）。標準ダミーは HP100 / 防御20。")]
        [SerializeField] private EnemyData _data;

        private Vital _hp;

        /// <summary>被弾結果の通知チャネル（HUD 等が P2-11 で購読）。</summary>
        public HitResultChannel Results { get; } = new HitResultChannel();

        /// <summary>現在 HP。</summary>
        public int CurrentHp
        {
            get
            {
                EnsureHp();
                return _hp.Current;
            }
        }

        /// <summary>最大 HP。</summary>
        public int MaxHp
        {
            get
            {
                EnsureHp();
                return _hp.Max;
            }
        }

        /// <summary>撃破済みか（HP0。死亡処理そのものは対象外）。</summary>
        public bool IsDefeated
        {
            get
            {
                EnsureHp();
                return _hp.Current <= 0;
            }
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

        private void Awake()
        {
            EnsureHp();
        }

        private void EnsureHp()
        {
            if (_hp == null)
            {
                int max = _data != null ? _data.MaxHp : 1;
                _hp = new Vital(max);
            }
        }

        /// <summary>HP を最大まで戻す（検証の再試行用）。</summary>
        public void ResetHp()
        {
            EnsureHp();
            _hp.SetCurrent(_hp.Max);
        }

        /// <inheritdoc />
        public void ReceiveHit(in HitInfo hit)
        {
            EnsureHp();

            float defense = _data != null ? _data.Defense : 0f;
            int finalHp = HpDamageCalculator.ResolveFinal(hit.Damage.Hp, defense);

            _hp.Change(-finalHp);

            var applied = new HitDamage(finalHp, hit.Damage.Poise, hit.Damage.Flinch);
            Results.Publish(HitResult.Damage(hit.HitId, hit.Attacker, this, applied));
        }
    }
}
