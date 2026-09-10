using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の守護（「かばう」）の駆動（P4-05）。P4-01 で定義した <see cref="IGuardianResolver"/> を実装し、
    /// 主人公への命中のうち<b>回避・ジャストガード・ガードのいずれも成立しなかったもの</b>だけを肩代わりする。
    ///
    /// 判断の材料は 3 つで、いずれも <see cref="Data.Characters.CompanionData"/> と自分の状態から取る。
    /// <list type="number">
    /// <item><description><b>距離</b>：守護対象から <see cref="Data.Characters.CompanionData.GuardianRange"/> 以内に居ること。
    /// 離れた場所から瞬間移動して庇うことはしない。</description></item>
    /// <item><description><b>状態</b>：倒れている・退場している・ひるんでいる間は引き受けない
    /// （引き受け可否の最終判断は受け口の <see cref="IGuardianReceiver.CanTakeOver"/>）。</description></item>
    /// <item><description><b>クールダウン</b>：成立後 <see cref="Data.Characters.CompanionData.GuardianCooldownSeconds"/> 秒は
    /// 引き受けない。連続で肩代わりし続けて主人公が無敵になることを防ぐ。</description></item>
    /// </list>
    ///
    /// 自分自身を守護対象（<see cref="IGuardianHost"/>）へ登録し、無効化・退場で解除する。登録先は追従が持っている
    /// 主人公の Transform から解決するため、Scene 側の配線変更は要らない（<c>Find*</c> も使わない）。
    ///
    /// <b>現時点の制約</b>：守護対象は「守護判断先」を 1 つだけ持つ契約のため、仲間が複数居る場合は最後に有効化された
    /// 1 体が守護を担う。誰が庇うかを仲間の間で調停するのは交代・指示の話（P4-07）なので、ここでは扱わない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionGuardianController : MonoBehaviour, IGuardianResolver
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("肩代わりを実際に受ける口（未設定なら自動取得）。")]
        [SerializeField] private CompanionHitReceiver _receiver;

        [Tooltip("守護対象（主人公）。未設定なら追従の対象から解決する。")]
        [SerializeField] private Transform _protectedTarget;

        private IGuardianHost _host;
        private CompanionFollowController _follow;
        private float _cooldownRemaining;

        /// <summary>クールダウンの残り秒（テスト・診断用）。</summary>
        public float CooldownRemaining => _cooldownRemaining;

        /// <summary>これまでに肩代わりした回数（テスト・診断用）。</summary>
        public int TransferCount { get; private set; }

        /// <summary>守護対象（テスト・診断用）。</summary>
        public Transform ProtectedTarget => _protectedTarget;

        /// <summary>いま引き受けられるか（距離を除く条件。テスト・診断用）。</summary>
        public bool IsReady => _cooldownRemaining <= 0f && CanProtect();

        /// <summary>Actor・受け口・守護対象を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionActor actor, CompanionHitReceiver receiver = null, Transform protectedTarget = null)
        {
            if (actor != null)
            {
                _actor = actor;
            }

            if (receiver != null)
            {
                _receiver = receiver;
            }

            if (protectedTarget != null && !ReferenceEquals(_protectedTarget, protectedTarget))
            {
                Unregister();
                _protectedTarget = protectedTarget;
                if (isActiveAndEnabled)
                {
                    Register();
                }
            }
        }

        /// <inheritdoc />
        public bool TryResolveGuardian(in HitInfo hit, out IGuardianReceiver guardian)
        {
            guardian = null;
            EnsureRefs();

            // 活動停止中（Pause・会話・イベント）は肩代わりしない。主人公の通常 Damage へ戻す（§8.5）。
            if (!CompanionActivityProvider.Activity.CanAct)
            {
                return false;
            }

            if (_receiver == null || _cooldownRemaining > 0f || !CanProtect())
            {
                return false;
            }

            if (!WithinGuardianRange())
            {
                return false;
            }

            guardian = _receiver;
            return true;
        }

        /// <inheritdoc />
        /// <remarks>
        /// 副作用（クールダウン開始・状態遷移）はここで行う。判断（<see cref="TryResolveGuardian"/>）が true でも
        /// 受け口が引き受け不可なら本メソッドは呼ばれないため、成立しなかった肩代わりでクールダウンを消費しない。
        /// </remarks>
        public void NotifyTransferred(in HitInfo transferred, IGuardianReceiver guardian)
        {
            EnsureRefs();
            _cooldownRemaining = ResolveCooldownSeconds();
            TransferCount++;

            // 庇った、という状態を明示する（表示・後続の判断が読む）。ひるみ・ダウンは被弾解決側が上書きする。
            _actor?.RequestState(CompanionState.Protect, CompanionStateChangeReason.Protected);
        }

        /// <summary>クールダウンを 1 Tick 進める（Update から呼ばれるが、テストは決定的に直接呼べる）。</summary>
        public void TickGuardian(float deltaTime)
        {
            if (_cooldownRemaining <= 0f)
            {
                return;
            }

            // Pause・会話中はクールダウンを進めない（P4-FIX F05。判断を Update 側に置くと、
            // 外から直接 Tick された瞬間に素通りする）。
            if (!CompanionActivityProvider.Activity.ClocksRun)
            {
                return;
            }

            _cooldownRemaining -= deltaTime < 0f ? 0f : deltaTime;
            if (_cooldownRemaining < 0f)
            {
                _cooldownRemaining = 0f;
            }
        }

        /// <summary>クールダウンを初期化する（加入・Retry）。</summary>
        public void ResetGuardian()
        {
            _cooldownRemaining = 0f;
        }

        // ---- 内部 ----

        /// <summary>この状態で庇えるか（倒れている・退場・ひるみ中は庇えない）。</summary>
        private bool CanProtect()
        {
            if (_actor == null)
            {
                return false;
            }

            CompanionState state = _actor.State;
            return state != CompanionState.Away
                && state != CompanionState.Down
                && state != CompanionState.Recovering
                && state != CompanionState.Stagger;
        }

        private bool WithinGuardianRange()
        {
            if (_protectedTarget == null)
            {
                return false; // 守護対象が分からないうちは庇わない。
            }

            float range = _actor != null && _actor.Data != null ? _actor.Data.GuardianRange : 0f;
            if (range <= 0f)
            {
                return false;
            }

            return FormationSlot.HorizontalDistance(_actor.WorldPosition, _protectedTarget.position) <= range;
        }

        private float ResolveCooldownSeconds()
        {
            return _actor != null && _actor.Data != null ? _actor.Data.GuardianCooldownSeconds : 0f;
        }

        private void EnsureRefs()
        {
            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }

            if (_receiver == null)
            {
                _receiver = GetComponent<CompanionHitReceiver>();
            }

            if (_follow == null)
            {
                _follow = GetComponent<CompanionFollowController>();
            }

            // 追従が知っている主人公をそのまま守護対象にする（Find* を使わずに解決できる唯一の参照）。
            if (_protectedTarget == null && _follow != null && _follow.Leader != null)
            {
                _protectedTarget = _follow.Leader;
                Register();
            }
        }

        private void Register()
        {
            if (_protectedTarget == null)
            {
                return;
            }

            var host = _protectedTarget.GetComponentInParent<IGuardianHost>();
            if (host == null)
            {
                return; // 守護を受け付けない対象（＝この Scene では肩代わりが起きない）。既存挙動を変えない。
            }

            _host = host;
            _host.SetGuardianResolver(this);
        }

        private void Unregister()
        {
            if (_host == null)
            {
                return;
            }

            // 自分が登録されている場合だけ解除される（一致判定は登録先が行う）。
            // 無条件に null を入れると、後から有効化された別の守護者の登録まで消してしまう。
            _host.ClearGuardianResolver(this);
            _host = null;
        }

        private void Update()
        {
            EnsureRefs();

            // 停止の判断は TickGuardian の入口へ移した（P4-FIX F05）。外から直接呼ばれる経路も塞ぐため。
            TickGuardian(Time.deltaTime);
        }

        private void OnEnable()
        {
            EnsureRefs();
            Register();
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で守護対象に参照を残さない（§2.3 後始末）。
            Unregister();
            _cooldownRemaining = 0f;
        }

    }
}
