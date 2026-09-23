using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Infrastructure.Navigation
{
    /// <summary>
    /// このエリアの経路まわりを配線する（P5-05。仕様書 v1.1 §10.1／§10.2）。
    ///
    /// <b>Gameplay は NavMesh を知らない。</b> §10.1 は「Infrastructure の NavMesh Adapter を
    /// <c>BindPathProvider(...)</c> 等で明示注入する」と決めているので、その注入をここが行う。
    /// <c>GetComponent</c> 頼みにしないので、テストは Fake を差せる。
    ///
    /// もう 1 つの仕事は<b>門が開いたことを経路へ伝える</b>こと（§10.1 の再探索条件）。
    /// 伝えないと、物理的には通れるのに仲間は閉まっているつもりで迂回し続ける。
    /// くり抜き（<c>NavMeshObstacle</c>）を外すのは門自身の仕事で、ここは
    /// 「探し直してよい」と言うだけ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaNavigationBinder : MonoBehaviour
    {
        [Tooltip("このエリアの根（レバーの明示参照を持つ）。")]
        [SerializeField] private AreaRoot _areaRoot;

        [Tooltip("経路を使う追従（犬丸）。")]
        [SerializeField] private CompanionFollowController _follow;

        private readonly System.Collections.Generic.List<AreaFlagLever> _subscribed =
            new System.Collections.Generic.List<AreaFlagLever>();

        /// <summary>経路を更新した回数（診断・テスト用）。</summary>
        public int PathUpdateCount { get; private set; }

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _areaRoot != null && _follow != null;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(AreaRoot areaRoot, CompanionFollowController follow)
        {
            if (areaRoot != null)
            {
                _areaRoot = areaRoot;
            }

            if (follow != null)
            {
                _follow = follow;
            }
        }

        private void OnEnable()
        {
            if (_follow == null)
            {
                GameLog.WarningOnce(LogCategory.Scene, "area_nav_unwired",
                    "経路の配線先（追従）が未設定です。仲間は長距離の迂回を行いません（§10.1）。");
                return;
            }

            _follow.BindPathProvider(new NavMeshPathProvider());
            _follow.BindWarpProbe(new NavMeshWarpCandidateProbe());

            SubscribeLevers();
        }

        private void OnDisable()
        {
            for (int i = 0; i < _subscribed.Count; i++)
            {
                if (_subscribed[i] != null)
                {
                    _subscribed[i].Opened -= OnFlagOpened;
                }
            }

            _subscribed.Clear();
        }

        private void SubscribeLevers()
        {
            if (_areaRoot == null)
            {
                return;
            }

            for (int i = 0; i < _areaRoot.Levers.Count; i++)
            {
                AreaFlagLever lever = _areaRoot.Levers[i];
                if (lever == null || _subscribed.Contains(lever))
                {
                    continue;
                }

                lever.Opened += OnFlagOpened;
                _subscribed.Add(lever);
            }
        }

        /// <summary>門が開いた。間隔を待たずに経路を探し直させる（§10.1）。</summary>
        private void OnFlagOpened(StableId flagId)
        {
            PathUpdateCount++;
            _follow?.NotifyWorldChanged();
        }
    }
}
