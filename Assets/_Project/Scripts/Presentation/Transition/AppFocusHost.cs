using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Presentation.Transition
{
    /// <summary>
    /// アプリの前面判定を<b>常駐で</b>持つ担当（P5.5 仕様書 §7.1。工程 P55-10f）。
    ///
    /// Unity は前面／背面の切り替えを <c>OnApplicationFocus</c> と <c>OnApplicationPause</c> で
    /// 知らせる。どちらも <b>MonoBehaviour にしか来ない</b>ので、常駐 Rig と同じ物体に住まわせて
    /// <see cref="AppFocusProvider"/> へ差す（Camera・表示代理・待機表示と同じ扱い）。
    ///
    /// <b>初期値は「前面」。</b> 起動直後に通知は来ないので、既定を非フォーカスにすると
    /// 最初のスライドが進まない。
    ///
    /// <b>Editor では通知を聞かない</b>（下の <c>UNITY_EDITOR</c>）。理由は入力と同じで、
    /// <b>無人の自動実行では Game View にフォーカスが無いのが普通</b>だからである
    /// （<c>PlayModeInputFocusFixture</c> が実キー検査について同じ問題に対処している）。
    /// 表示時計まで止めると、PlayMode の全件が「触っていないのに一斉に止まる」。
    /// 規則そのもの——非フォーカスでは表示時間を進めない——は、
    /// 窓口を差し替えて<b>決定的に</b>見る（§11 の P19）。ビルドでは実際の通知に従う。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppFocusHost : MonoBehaviour, IAppFocusSource
    {
        /// <summary>いま生きている常駐（無ければ null。テスト用）。</summary>
        public static AppFocusHost Instance { get; private set; }

        /// <inheritdoc />
        public bool IsFocused { get; private set; } = true;

        /// <summary>前面を失った回数（診断・テスト用）。</summary>
        public int LostCount { get; private set; }

        /// <summary>前面へ戻った回数（診断・テスト用）。</summary>
        public int RegainedCount { get; private set; }

        /// <summary>
        /// Editor の通知を聞いているか（診断用）。Editor では false——上の理由による。
        /// </summary>
        public static bool ListensToEngineNotifications =>
#if UNITY_EDITOR
            false;
#else
            true;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 二つ目は作らせない（常駐 Rig と同じ規律）。
                Destroy(this);
                return;
            }

            Instance = this;
            AppFocusProvider.TrySetCurrent(this, this);
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            AppFocusProvider.ReleaseIfOwner(this);
        }

#if !UNITY_EDITOR
        private void OnApplicationFocus(bool focused) => Apply(focused);

        private void OnApplicationPause(bool paused) => Apply(!paused);
#endif

        /// <summary>
        /// 前面かどうかを差し替える（受入用。§11 の P19）。
        ///
        /// Editor では Engine の通知を聞かないので、非フォーカスを作る口がこれしか無い。
        /// ビルドでも同じ道を通るので、<b>検査だけの別経路にはならない</b>。
        /// </summary>
        public void Apply(bool focused)
        {
            if (IsFocused == focused)
            {
                return;
            }

            IsFocused = focused;
            if (focused)
            {
                RegainedCount++;
            }
            else
            {
                LostCount++;
            }
        }
    }
}
