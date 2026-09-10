using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 調査地点の仮実装（P4-07A）。Scene に置くと自分でレジストリへ登録し、無効化・破棄で外れる。
    ///
    /// 本物の地点（宝箱・隠し通路・匂いの跡）は P5 のマップ・探索基盤が用意する。ここで作るのは
    /// <b>仲間が調べに行くところまでを実機で確かめられる最小の相手</b>で、調べ終えたら
    /// 「調べ済み」になるだけ。何かが出てくる処理は先回りして作らない（設計の約束）。
    ///
    /// 一度きりか繰り返せるかは <see cref="_repeatable"/> で選ぶ。犬に何度も嗅がせて挙動を見たいことが
    /// あるため、繰り返し可も残してある。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CompanionInvestigationPoint : MonoBehaviour, IInvestigationPoint
    {
        [Tooltip("調べ終えても、また調べられる状態へ戻す（挙動確認用）。")]
        [SerializeField] private bool _repeatable;

        [Tooltip("最初から調査済みにしておく（他の地点を選ばせたいときに使う）。")]
        [SerializeField] private bool _startInvestigated;

        private bool _investigated;

        /// <inheritdoc />
        public int PointId => GetInstanceID();

        /// <inheritdoc />
        public Vector3 Position => transform.position;

        /// <inheritdoc />
        public bool IsAvailable => isActiveAndEnabled && !_investigated;

        /// <summary>これまでに調べられた回数（テスト・診断用）。</summary>
        public int InvestigatedCount { get; private set; }

        /// <summary>最後に調べた仲間の Actor ID（まだなら 0。テスト・診断用）。</summary>
        public int LastInvestigatedBy { get; private set; }

        /// <summary>調査済みか（テスト・診断用）。</summary>
        public bool IsInvestigated => _investigated;

        /// <inheritdoc />
        public void OnInvestigated(int companionActorId)
        {
            InvestigatedCount++;
            LastInvestigatedBy = companionActorId;
            _investigated = !_repeatable;
        }

        /// <summary>調査済みの記録を消す（Retry・Scene 再構築）。</summary>
        public void ResetPoint()
        {
            _investigated = _startInvestigated;
            InvestigatedCount = 0;
            LastInvestigatedBy = 0;
        }

        private void Awake()
        {
            _investigated = _startInvestigated;
        }

        private void OnEnable()
        {
            InvestigationPointRegistry.Register(this);
        }

        private void OnDisable()
        {
            // 無効化・Scene 離脱でレジストリに参照を残さない（§2.3 後始末）。
            InvestigationPointRegistry.Unregister(this);
        }

        private void OnDrawGizmos()
        {
            // 調べられる地点は青、調べ済みは灰色。仮素材が無くても Scene 上で見分けられるようにする。
            Gizmos.color = _investigated ? new Color(1f, 1f, 1f, 0.25f) : new Color(0.3f, 0.8f, 1f, 0.7f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.4f);
        }
    }
}
