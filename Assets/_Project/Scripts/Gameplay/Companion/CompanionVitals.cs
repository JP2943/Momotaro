using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Vitals;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の生存値 Runtime（P4-04）。HP・ひるみ・被弾後無敵・ダウンと復帰待ちを保持する純粋クラスで、
    /// 時間は外部から注入する（EditMode で決定的に検証できる）。数値の正本は <see cref="CompanionData"/>。
    ///
    /// 敵の <c>EnemyVitals</c> と同じ形を採るが、<b>体幹（Poise）とスタンは持たない</b>。仲間の体幹は仕様に定義が無く、
    /// Data にも項目が無いため、ここで独自に作らない（未定義の系統を先回りで作らない）。仲間が受ける体幹ダメージは
    /// 適用値 0 として結果に載せる。必要になった時点で Data ごと追加する。
    ///
    /// ダウンは終端ではない。仲間は <see cref="CompanionData.LeaveRecoverySeconds"/> 秒後に復帰する（敵の Down と違う点）。
    /// </summary>
    public sealed class CompanionVitals
    {
        /// <summary>1 回の命中を適用した結果。</summary>
        public readonly struct HitApplication
        {
            /// <summary>実際に適用された HP／体幹／ひるませ値。</summary>
            public HitDamage Applied { get; }

            /// <summary>この命中で新たにひるんだか。</summary>
            public bool NewlyFlinching { get; }

            /// <summary>この命中で新たにダウンしたか。</summary>
            public bool NewlyDowned { get; }

            public HitApplication(HitDamage applied, bool newlyFlinching, bool newlyDowned)
            {
                Applied = applied;
                NewlyFlinching = newlyFlinching;
                NewlyDowned = newlyDowned;
            }
        }

        /// <summary>Data 未割当時の最大 HP。</summary>
        public const int DefaultMaxHp = 100;

        /// <summary>Data 未割当時のひるみ耐性。</summary>
        public const float DefaultFlinchResistance = 40f;

        /// <summary>Data 未割当時のひるみ時間（秒）。</summary>
        public const float DefaultFlinchSeconds = 0.8f;

        /// <summary>Data 未割当時の被弾後無敵（秒）。</summary>
        public const float DefaultPostHitInvincibleSeconds = 0.5f;

        /// <summary>Data 未割当時の復帰待ち（秒）。</summary>
        public const float DefaultRecoverySeconds = 5f;

        /// <summary>Data 未割当時の復帰 HP 割合。</summary>
        public const float DefaultReviveHpRatio = 0.5f;

        private readonly FlinchState _flinch;
        private readonly float _postHitInvincibleSeconds;
        private readonly float _recoverySeconds;
        private readonly float _reviveHpRatio;

        private float _postHitInvincibleRemaining;
        private float _recoveryRemaining;

        /// <summary>HP。</summary>
        public Vital Health { get; }

        /// <summary>ダウン中か（HP0）。復帰待ちを含む。</summary>
        public bool IsDown { get; private set; }

        /// <summary>ひるみ中か。</summary>
        public bool IsFlinching => _flinch.IsFlinching;

        /// <summary>ひるみ蓄積量（診断・テスト用）。</summary>
        public float FlinchAccumulation => _flinch.Accumulation;

        /// <summary>被弾後無敵中か。</summary>
        public bool IsPostHitInvincible => _postHitInvincibleRemaining > 0f;

        /// <summary>復帰までの残り秒（ダウン中でなければ 0）。</summary>
        public float RecoveryRemaining => _recoveryRemaining;

        /// <summary>Data から生成する（null なら既定値）。</summary>
        public CompanionVitals(CompanionData data)
        {
            int maxHp = data != null && data.MaxHp > 0 ? data.MaxHp : DefaultMaxHp;
            Health = new Vital(maxHp);

            float resistance = data != null && data.FlinchResistance > 0f ? data.FlinchResistance : DefaultFlinchResistance;
            float flinchSeconds = data != null && data.FlinchSeconds > 0f ? data.FlinchSeconds : DefaultFlinchSeconds;
            _flinch = new FlinchState(resistance, flinchSeconds: flinchSeconds);

            _postHitInvincibleSeconds = data != null && data.PostHitInvincibleSeconds > 0f
                ? data.PostHitInvincibleSeconds
                : DefaultPostHitInvincibleSeconds;

            _recoverySeconds = data != null && data.LeaveRecoverySeconds > 0f
                ? data.LeaveRecoverySeconds
                : DefaultRecoverySeconds;

            _reviveHpRatio = data != null && data.ReviveHpRatio > 0f ? data.ReviveHpRatio : DefaultReviveHpRatio;
        }

        /// <summary>
        /// 命中を適用する（防御適用は本メソッド内）。ダウン中は何も適用しない。
        /// 被弾後無敵の開始は実 HP ダメージが入ったときだけ行う（かすり続けて無敵が伸び続けないようにする）。
        /// </summary>
        /// <param name="hit">命中情報（攻撃側寄与）。</param>
        /// <param name="defense">対象の防御力。</param>
        public HitApplication ApplyHit(in HitInfo hit, float defense)
        {
            if (IsDown)
            {
                return new HitApplication(HitDamage.None, false, false);
            }

            int appliedHp = DamageApplication.ApplyHpDamage(Health, hit.Damage.Hp, defense);

            bool wasFlinching = _flinch.IsFlinching;
            float appliedFlinch = _flinch.AddFlinch(hit.Damage.Flinch);
            bool newlyFlinching = !wasFlinching && _flinch.IsFlinching;

            bool newlyDowned = false;
            if (Health.Current <= 0)
            {
                IsDown = true;
                _recoveryRemaining = _recoverySeconds;
                _flinch.Reset();
                _postHitInvincibleRemaining = 0f;
                newlyDowned = true;
            }
            else if (appliedHp >= 1)
            {
                _postHitInvincibleRemaining = _postHitInvincibleSeconds;
            }

            // 体幹は仲間に定義が無いため常に 0（上のクラス説明を参照）。
            return new HitApplication(new HitDamage(appliedHp, 0f, appliedFlinch), newlyFlinching, newlyDowned);
        }

        /// <summary>時間を進める（ひるみ・被弾後無敵・復帰待ち）。復帰したら true を返す。</summary>
        public bool Tick(float deltaTime)
        {
            float dt = deltaTime < 0f ? 0f : deltaTime;

            if (_postHitInvincibleRemaining > 0f)
            {
                _postHitInvincibleRemaining -= dt;
                if (_postHitInvincibleRemaining < 0f)
                {
                    _postHitInvincibleRemaining = 0f;
                }
            }

            if (IsDown)
            {
                _recoveryRemaining -= dt;
                if (_recoveryRemaining > 0f)
                {
                    return false;
                }

                Revive();
                return true;
            }

            _flinch.Tick(dt);
            return false;
        }

        /// <summary>ダウンから復帰する（HP を割合で回復し、ひるみ・無敵を消す）。</summary>
        public void Revive()
        {
            IsDown = false;
            _recoveryRemaining = 0f;
            _flinch.Reset();
            _postHitInvincibleRemaining = 0f;

            int revived = (int)(Health.Max * _reviveHpRatio);
            Health.SetCurrent(revived < 1 ? 1 : revived);
        }

        /// <summary>全快させて初期化する（加入・Retry・Scene 再構築）。</summary>
        public void Reset()
        {
            IsDown = false;
            _recoveryRemaining = 0f;
            _postHitInvincibleRemaining = 0f;
            _flinch.Reset();
            Health.SetCurrent(Health.Max);
        }
    }
}
