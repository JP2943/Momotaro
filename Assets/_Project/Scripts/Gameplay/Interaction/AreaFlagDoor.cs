using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// 1 つの FlagId で恒久開通する門（P5-04。仕様書 v1.1 §7.3）。
    ///
    /// <b>正本は Flag、門はその表れ。</b> 開通したかどうかは Session の Area 記録が持ち、
    /// この門は「開いた状態を適用する」だけを担う。Scene を往復しても記録が残るのはそのため。
    ///
    /// <b>通行不能なまま見た目だけ開かない。</b> §7.3 は「見た目だけ開いて通行不能な状態を受入にしない」と
    /// 決めている。そこで適用の順序を<b>通行（Collider）→ 見た目</b>に固定し、
    /// 通行を開けられなかったら見た目も変えずに失敗として返す。逆順だと、失敗したときに
    /// 「開いて見えるのに通れない」という一番たちの悪い状態が残る。
    ///
    /// 反転・時間制限・複数条件は持たない（§7.3）。戦闘アリーナの封鎖とは別所有者にする。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaFlagDoor : MonoBehaviour
    {
        [Tooltip("この門を開ける FlagId（レバーと同じ値を差すこと）。")]
        [SerializeField] private string _flagId = string.Empty;

        [Tooltip("通行を阻む Collider。開通時に無効化する。未配線は失敗として扱う。")]
        [SerializeField] private Collider _blocker;

        [Tooltip("閉じているときだけ見せる見た目（任意）。")]
        [SerializeField] private GameObject _closedVisual;

        [Tooltip("閉じている間 NavMesh をくり抜く障害物（任意）。開通時に無効化する。")]
        [SerializeField] private UnityEngine.AI.NavMeshObstacle _navObstacle;

        /// <summary>この門の FlagId。</summary>
        public StableId FlagId => string.IsNullOrEmpty(_flagId) ? default : new StableId(_flagId);

        /// <summary>開いた状態を適用済みか（表示・診断用）。</summary>
        public bool IsOpened { get; private set; }

        /// <summary>開通を適用した回数（診断・テスト用）。<b>1 を超えてはいけない</b>（§7.3）。</summary>
        public int AppliedCount { get; private set; }

        /// <summary>直近の適用失敗の理由（成功なら空）。</summary>
        public string LastFailure { get; private set; } = string.Empty;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(
            StableId flagId, Collider blocker, GameObject closedVisual,
            UnityEngine.AI.NavMeshObstacle navObstacle = null)
        {
            _flagId = flagId.Value ?? string.Empty;
            _blocker = blocker;
            _closedVisual = closedVisual;
            _navObstacle = navObstacle;
        }

        /// <summary>くり抜き用の障害物（Validator・テスト用）。</summary>
        public UnityEngine.AI.NavMeshObstacle NavObstacle => _navObstacle;

        /// <summary>
        /// 開いた状態を適用する。<b>通行を開けてから見た目を変える。</b>
        /// すでに適用済みなら何もせず true（再入・再表示で二重に適用しない）。
        /// </summary>
        public bool TryApplyOpened(out string error)
        {
            error = string.Empty;

            if (IsOpened)
            {
                return true;
            }

            if (_blocker == null)
            {
                error = "門の Collider が未配線です（flag=" + _flagId + "）。";
                LastFailure = error;
                GameLog.Error(LogCategory.Scene, "Door could not be opened: " + error);
                return false;
            }

            // 1. 通行を開ける。ここが通らなければ見た目も変えない。
            _blocker.enabled = false;

            // 1b. 経路にも反映する（§10.2 末尾「門の通行状態変更を Navigation へ反映し」）。
            //     ここを忘れると、物理的には通れるのに仲間は閉まっているつもりで迂回し続ける。
            if (_navObstacle != null)
            {
                _navObstacle.enabled = false;
            }

            // 2. 見た目を閉じ側から外す（任意配線）。
            if (_closedVisual != null)
            {
                _closedVisual.SetActive(false);
            }

            IsOpened = true;
            AppliedCount++;
            LastFailure = string.Empty;
            return true;
        }

        /// <summary>
        /// Area の初期化時に、記録から開通状態を復元する（§4.3。通知は出さない）。
        /// 復元は「すでに開いていた」のであって、いま開通したのではない。
        /// </summary>
        public bool RestoreFrom(AreaRuntimeState state)
        {
            if (state == null || FlagId.IsEmpty || !state.IsOpen(FlagId))
            {
                return false;
            }

            return TryApplyOpened(out _);
        }
    }
}
