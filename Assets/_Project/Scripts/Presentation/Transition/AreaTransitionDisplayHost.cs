using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Presentation.Transition
{
    /// <summary>
    /// 遷移中の表示代理を<b>常駐で</b>持つ担当（P5.5 仕様書 §7.2。工程 P55-04b）。
    ///
    /// <b>常駐 Rig と同じ物体に住む。</b> 代理は出発 Scene が撤去されても運び続ける必要があり、
    /// かつ Camera と同じ寿命・同じ唯一性で管理したい（§11 の P12／P14）。
    /// <see cref="Cameras.AreaCameraRigHost"/> が自分の物体へこれを足すので、
    /// Prefab 側の配線も Builder の変更も要らない——常駐 Rig が 1 つなら、これも 1 つになる。
    ///
    /// <b>ここは「窓口と寿命」だけを持つ。</b> 代理の中身（Sprite の複製・コマ送り・隠す順序）は
    /// <see cref="AreaTransitionDisplayProxySet"/> が持ち、EditMode で検証済み。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaTransitionDisplayHost : MonoBehaviour, IAreaTransitionDisplay
    {
        private AreaTransitionDisplayProxySet _set;

        /// <summary>いま生きている常駐の表示担当（無ければ null。テスト用）。</summary>
        public static AreaTransitionDisplayHost Instance { get; private set; }

        /// <inheritdoc />
        public bool IsActive => _set != null && !_set.IsReleased;

        /// <inheritdoc />
        public int ProxyCount => _set != null ? _set.Count : 0;

        /// <inheritdoc />
        public int HiddenRendererCount => _set != null ? _set.HiddenRendererCount : 0;

        /// <inheritdoc />
        public int BuildCount { get; private set; }

        /// <inheritdoc />
        public int ReleaseCount { get; private set; }

        /// <inheritdoc />
        public bool CompanionSkippedBecauseAway => _set != null && _set.CompanionSkippedBecauseAway;

        /// <summary>いま抱えている代理の集合（テスト用。無ければ null）。</summary>
        public AreaTransitionDisplayProxySet Set => _set;

        /// <inheritdoc />
        public bool TryBegin(AreaRuntimeBundle departure)
        {
            if (IsActive || departure == null)
            {
                // <b>二重に立てない。</b> 立て直すと、前の代理が隠した実 Renderer の預かりが失われ、
                // 遷移が終わっても主人公が見えないままになる。
                return false;
            }

            departure.TryResolve(out PlayerRoot player);
            departure.TryResolve(out CompanionActor companion);

            // <b>Actor が居なくても集合は作る。</b> 最小構成（Actor を載せない Scene）でも
            // 到着側を隠す・畳むという後段が同じ形で通るようにしておく。
            _set = new AreaTransitionDisplayProxySet();
            _set.Build(player, companion);
            BuildCount++;
            return true;
        }

        /// <inheritdoc />
        public void HideArrivals(AreaRuntimeBundle destination)
        {
            if (!IsActive || destination == null)
            {
                return;
            }

            destination.TryResolve(out PlayerRoot player);
            destination.TryResolve(out CompanionActor companion);
            _set.HideArrivals(player, companion);
        }

        /// <inheritdoc />
        public void SetRoute(
            Vector3 playerFrom, Vector3 playerTo, Vector3 companionFrom, Vector3 companionTo)
        {
            if (IsActive)
            {
                _set.SetRoute(playerFrom, playerTo, companionFrom, companionTo);
            }
        }

        /// <inheritdoc />
        public void SetProgress(float eased)
        {
            if (IsActive)
            {
                _set.SetProgress(eased);
            }
        }

        /// <inheritdoc />
        public void TickDisplayClock(float unscaledDeltaTime)
        {
            if (IsActive)
            {
                _set.TickDisplayClock(unscaledDeltaTime);
            }
        }

        /// <inheritdoc />
        public void Release()
        {
            if (_set == null)
            {
                return;
            }

            bool wasActive = !_set.IsReleased;
            _set.Release();
            _set = null;

            if (wasActive)
            {
                ReleaseCount++;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 二つ目は作らせない（常駐 Rig と同じ規律）。
                Destroy(this);
                return;
            }

            Instance = this;
            AreaTransitionDisplayProvider.TrySetCurrent(this, this);
        }

        private void OnDestroy()
        {
            // <b>畳んでから外す。</b> 隠した実 Renderer を預かったまま消えると、
            // その Actor は二度と描かれない。
            Release();

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            AreaTransitionDisplayProvider.ReleaseIfOwner(this);
        }
    }
}
