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
    /// <b>成立は 1 つの同期取引</b>（P4-FIX-R2。v1.0 §8.5、c8c0ddf §2.4）：
    /// 可否確定（<see cref="TryResolveGuardian"/>）→ 受け口が HitId を新しく受理 → <see cref="OnTransferAccepted"/> で
    /// 旧攻撃／防御を<b>同期中断</b>（Protect へ割込み。奪われた側は <see cref="ICompanionActionParticipant"/> で
    /// その場で判定・能力を止める）→ 受け口が被害を解決（旧ガードはもう無い）→ <see cref="NotifyTransferred"/> で
    /// CD・専用通知を 1 回確定し、Protect の所有権をその場で返す。
    /// 拒否（二重受理・退場・ダウン・距離外・CD 中）なら受理確定フックが呼ばれず、旧行動・CD・通知は不変。
    /// 転送で Down／Stagger になった場合は被弾側の強制遷移が Protect の券を無効にしているので、そのまま維持される。
    ///
    /// Protect は<b>継続状態ではなく一瞬の取引</b>として扱う。持続する演出は状態ではなく <see cref="Transfers"/> の通知で
    /// 表示側が出す（§8.5「専用演出は状態保持時間に依存させず通知で表示する」）。
    /// 継続状態にしていたころは券を捨てていて、CD 満了でも Guardian の所有権が返らず通常行動へ戻れなかった（R2-06 影響 B）。
    ///
    /// <b>現時点の制約</b>：守護対象は「守護判断先」を 1 つだけ持つ契約のため、仲間が複数居る場合は最後に有効化された
    /// 1 体が守護を担う。誰が庇うかを仲間の間で調停するのは交代・指示の話（P8）なので、ここでは扱わない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionGuardianController : MonoBehaviour, IGuardianResolver, ICompanionTransferAcceptanceHook,
        ICompanionActionParticipant
    {
        [Tooltip("状態・Data の供給元（未設定なら自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        [Tooltip("肩代わりを実際に受ける口（未設定なら自動取得）。")]
        [SerializeField] private CompanionHitReceiver _receiver;

        [Tooltip("守護対象（主人公）。未設定なら追従の対象から解決する。")]
        [SerializeField] private Transform _protectedTarget;

        [Tooltip("状態要求の唯一の窓口（未設定なら自動取得）。Actor へは直接書かない。")]
        [SerializeField] private CompanionStateArbiter _states;

        private IGuardianHost _host;
        private CompanionFollowController _follow;
        private float _cooldownRemaining;
        private CompanionActionHandle _protect; // 取引中だけ持つ Protect の券。取引が終われば必ず返す。

        /// <summary>クールダウンの残り秒（テスト・診断用）。</summary>
        public float CooldownRemaining => _cooldownRemaining;

        /// <summary>これまでに肩代わりした回数（テスト・診断用）。</summary>
        public int TransferCount { get; private set; }

        /// <summary>受理確定フックが呼ばれた回数（テスト・診断用。TransferCount と一致するのが正常）。</summary>
        public int AcceptedCount { get; private set; }

        /// <summary>守護成立の専用通知（表示側が購読する。犬丸側の 1 本。主人公側の通知とは別）。</summary>
        public GuardianTransferChannel Transfers { get; } = new GuardianTransferChannel();

        /// <summary>取引中か（受理確定から通知確定まで。テスト・診断用）。</summary>
        public bool IsTransferInProgress => _protect.IsValid;

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

            // 主人公への実命中は、探索中の犬丸から探索を先に解放する（c8c0ddf §5）。解放は所有権と表示の話で、
            // HP・CD には触らない。解放したあとの資格（状態・CD・距離）で守護を評価する。
            _states?.InterruptOwner(CompanionActionOwner.Investigate);

            if (_receiver == null || _cooldownRemaining > 0f || !CanProtect() || _protect.IsValid)
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
        /// 受け口が転送を<b>新しく受理した直後・被害を解決する前</b>に呼ばれる（同一呼び出し内）。
        /// ここで Protect へ割り込む＝旧攻撃／防御の持ち主がその場で判定・能力を止める。
        /// このあと受け口が被害を解決するとき、旧ガードはもう無い。
        /// 可否は許可表が持つ（Evade 中は Denied だが、その場合 <see cref="TryResolveGuardian"/> が先に拒否している）。
        /// </remarks>
        public void OnTransferAccepted(in HitInfo transferred)
        {
            EnsureRefs();
            AcceptedCount++;

            if (_states == null || _protect.IsValid)
            {
                return;
            }

            _states.TryStartAction(
                CompanionActionOwner.Guardian, CompanionActionKind.GuardianTransfer, CompanionState.Protect,
                CompanionStateChangeReason.Protected, out _protect);
        }

        /// <inheritdoc />
        /// <remarks>
        /// 転送が解決し終わったあとに 1 回だけ呼ばれる。ここで CD と専用通知を確定し、<b>取引で持った Protect の
        /// 所有権をその場で返す</b>（Protect を継続状態にしない。§8.5「短い成立表示」は通知で表示側が出す）。
        ///
        /// <b>ひるみ・ダウンは上書きしない</b>（F02c／E18）。転送の一撃で倒れた・ひるんだ場合、受け口の強制遷移が
        /// Protect の券を既に無効にしているので、券の一致検査で自然に「何もしない」になる。条件式は置かない。
        /// 判断（<see cref="TryResolveGuardian"/>）が true でも受け口が引き受けなければ本メソッドは呼ばれないため、
        /// 成立しなかった肩代わりで CD を消費しない。
        /// </remarks>
        public void NotifyTransferred(in HitInfo transferred, IGuardianReceiver guardian)
        {
            EnsureRefs();
            _cooldownRemaining = ResolveCooldownSeconds();
            TransferCount++;

            CompleteProtect();

            Transfers.Publish(new GuardianTransferEvent(
                transferred.HitId, transferred.Attacker, null, guardian, transferred.HitPoint));
        }

        /// <inheritdoc />
        /// <remarks>取引中に被弾（Down／Stagger）・退場で券を奪われた。券を捨てるだけ（状態は奪った側のもの）。</remarks>
        public void OnActionInterrupted(in CompanionActionHandle lost)
        {
            if (lost.IsValid && lost.Owner == CompanionActionOwner.Guardian)
            {
                _protect = default;
            }
        }

        /// <summary>
        /// 取引で持った Protect を正常終了して追従へ戻す（券が今の行動と一致するときだけ通る。
        /// Down／Stagger で無効化されていれば何も起きない）。どちらでも券は手放す。
        /// </summary>
        private void CompleteProtect()
        {
            if (!_protect.IsValid)
            {
                return;
            }

            if (_states != null && _states.IsCurrent(_protect))
            {
                _states.TryComplete(_protect, CompanionState.Follow, CompanionStateChangeReason.FollowResumed);
            }

            _protect = default;
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

        /// <summary>
        /// この状態で庇えるか。判断は許可表（<see cref="CompanionActionRules"/>）に委ねる（F02c）。
        /// 倒れている・退場・復帰待ち・ひるみ中に加えて、<b>回避中も庇わない</b>（回避は動作全体で 1 行動で、
        /// 途中で庇いに化けない）。ここに条件式を写すと、表と食い違ったときに黙って別々の答えを出す。
        /// </summary>
        private bool CanProtect()
        {
            if (_actor == null)
            {
                return false;
            }

            return CompanionActionRules.Evaluate(CompanionActionKind.GuardianTransfer, _actor.State)
                != CompanionActionVerdict.Denied;
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

            if (_states == null)
            {
                _states = GetComponent<CompanionStateArbiter>();
            }

            if (isActiveAndEnabled)
            {
                _states?.RegisterParticipant(CompanionActionOwner.Guardian, this); // 冪等。EditMode は OnEnable を呼ばない。
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
            // 無効化・Scene 離脱で守護対象に参照を残さない（§2.3 後始末）。取引中の券も持ち越さない。
            Unregister();
            _states?.UnregisterParticipant(this);
            if (_protect.IsValid)
            {
                _states?.Release(_protect);
                _protect = default;
            }

            _cooldownRemaining = 0f;
        }

    }
}
