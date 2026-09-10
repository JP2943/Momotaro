using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 仲間の移動と論理的な向きを<b>実際に書く唯一の場所</b>（P4-FIX F02a）。
    ///
    /// これまでは追従と戦闘がそれぞれ <see cref="CompanionMotor"/> を直接触っていた。両方が同じフレームに
    /// 書くと、どちらが後だったかで結果が決まる。実際に「戦闘へ移ったのに追従が Stop を上書きする」
    /// 「攻撃中に追従が向きを変えてガード方向がずれる」といった競合が起こり得る形だった。
    ///
    /// 直し方は条件式を足すことではなく、<b>書き手を 1 つにする</b>こと。各駆動系は「こうしたい」という
    /// 意図（<see cref="CompanionMoveRequest"/>）を出すだけで、Motor へ何を渡すかはここが決める。
    ///
    /// 規則は 3 つだけ。
    /// <list type="number">
    /// <item><description><b>強い持ち主が勝つ。</b>Forced &gt; Combat &gt; Follow。</description></item>
    /// <item><description><b>古い持ち主は新しい持ち主を上書きできない。</b>弱い意図は黙って捨てる。
    /// 手放し（<see cref="Release"/>）も、自分が持っているときだけ効く。</description></item>
    /// <item><description><b>同じフレームで強制停止と競合したら停止が勝つ。</b>ひるみ・ダウン・退場は
    /// 次のフレームまで待たない（1 フレーム分でも動くと、倒れたはずの仲間が滑る）。</description></item>
    /// </list>
    ///
    /// 適用は<b>その場で</b>行う（意図を溜めて後でまとめて書かない）。溜める形にすると、
    /// 「指示した直後の状態」を見る側が 1 フレーム古い値を読むことになり、原因の分かりにくい遅延が生まれる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionMovementArbiter : MonoBehaviour
    {
        [Tooltip("移動実行（未設定なら同じ GameObject から自動取得）。")]
        [SerializeField] private CompanionMotor _motor;

        [Tooltip("論理的な向きの書き込み先（未設定なら同じ GameObject から自動取得）。")]
        [SerializeField] private CompanionActor _actor;

        private CompanionMovementOwner _owner = CompanionMovementOwner.None;
        private bool _forcedThisFrame;

        /// <summary>いま移動を握っている持ち主（テスト・診断用）。</summary>
        public CompanionMovementOwner Owner => _owner;

        /// <summary>このフレームに強制停止が入ったか（テスト・診断用）。</summary>
        public bool ForcedThisFrame => _forcedThisFrame;

        /// <summary>最後に適用した意図の種類（テスト・診断用）。</summary>
        public CompanionMoveKind LastApplied { get; private set; }

        /// <summary>Motor・Actor を注入する（Prefab 構築・テスト。null は無視）。</summary>
        public void Bind(CompanionMotor motor, CompanionActor actor = null)
        {
            if (motor != null)
            {
                _motor = motor;
            }

            if (actor != null)
            {
                _actor = actor;
            }
        }

        /// <summary>
        /// 移動意図を出す。受理されたら true。
        /// 自分より強い持ち主が握っている間は false を返し、Motor には何も書かない。
        /// </summary>
        public bool Submit(CompanionMovementOwner owner, in CompanionMoveRequest request)
        {
            EnsureRefs();

            if (owner == CompanionMovementOwner.None)
            {
                return false;
            }

            // 強制停止が入ったフレームは、通常の移動決定を通さない（停止を優先する）。
            if (_forcedThisFrame && owner != CompanionMovementOwner.Forced)
            {
                return false;
            }

            // 自分より強い持ち主が居るなら黙って捨てる（古い持ち主の指示で新しい持ち主を壊さない）。
            if (owner < _owner)
            {
                return false;
            }

            if (request.Kind == CompanionMoveKind.None)
            {
                // 向きだけの指定。移動の所有権は動かさない。
                ApplyFacing(request);
                return true;
            }

            _owner = owner;
            Apply(request);
            return true;
        }

        /// <summary>
        /// 移動の所有権を手放す。<b>自分が持っているときだけ</b>効く
        /// （既に別の持ち主へ移っていたら、その持ち主を巻き込まない）。
        /// </summary>
        public bool Release(CompanionMovementOwner owner)
        {
            if (_owner != owner || owner == CompanionMovementOwner.None)
            {
                return false;
            }

            _owner = CompanionMovementOwner.None;
            return true;
        }

        /// <summary>
        /// 強制的に止める（ひるみ・ダウン・退場・活動停止）。所有権に関わらず通り、
        /// そのフレームは以後の通常の移動意図を受け付けない。
        /// </summary>
        public void ForceStop()
        {
            EnsureRefs();
            _forcedThisFrame = true;
            _owner = CompanionMovementOwner.None; // 次のフレームは誰でも取り直せる。
            _motor?.Stop();
            LastApplied = CompanionMoveKind.Stop;
        }

        /// <summary>
        /// フレーム境界の処理（強制停止の一時ラッチを落とす）。
        /// <c>LateUpdate</c> から呼ばれるが、テストは決定的に直接呼べる。
        /// </summary>
        public void EndFrame()
        {
            _forcedThisFrame = false;
        }

        // ---- 内部 ----

        private void Apply(in CompanionMoveRequest request)
        {
            LastApplied = request.Kind;
            ApplyFacing(request);

            if (_motor == null)
            {
                return;
            }

            switch (request.Kind)
            {
                case CompanionMoveKind.Move:
                    _motor.Configure(request.Speed, request.StopRadius);
                    _motor.SetMoveTarget(request.Target);
                    break;

                case CompanionMoveKind.Warp:
                    _motor.WarpTo(request.Target);
                    break;

                default:
                    _motor.Stop();
                    break;
            }
        }

        private void ApplyFacing(in CompanionMoveRequest request)
        {
            if (!request.HasFacing || _actor == null)
            {
                return;
            }

            _actor.SetFacing(request.Facing);
        }

        private void EnsureRefs()
        {
            if (_motor == null)
            {
                _motor = GetComponent<CompanionMotor>();
            }

            if (_actor == null)
            {
                _actor = GetComponent<CompanionActor>();
            }
        }

        private void LateUpdate()
        {
            EndFrame();
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱で所有権も速度も残さない（§2.3 後始末）。
            _owner = CompanionMovementOwner.None;
            _forcedThisFrame = false;
            EnsureRefs();
            _motor?.Stop();
        }
    }
}
