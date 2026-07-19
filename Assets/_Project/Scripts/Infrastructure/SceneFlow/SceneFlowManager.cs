using System.Collections;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.View;
using Momotaro.Infrastructure.Bootstrap;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Infrastructure.SceneFlow
{
    /// <summary>
    /// Scene 遷移を担う常駐コンポーネント（仕様書 11.7 / 15.7）。フェード → 非同期 Load → フェード復帰の
    /// 手順で遷移し、遷移中の追加要求は <see cref="TransitionGuard"/> で安全に無視する。
    /// フェード描画は <see cref="IScreenFader"/> に委譲し、既定は非描画の <see cref="NullScreenFader"/>。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneFlowManager : MonoBehaviour, ISceneFlow, IGameService
    {
        [SerializeField] private float _fadeDuration = 0.3f;

        private readonly TransitionGuard _guard = new TransitionGuard();
        private IScreenFader _fader = new NullScreenFader();

        /// <inheritdoc />
        public string ServiceName => "SceneFlow";

        /// <summary>遷移が進行中か。</summary>
        public bool IsTransitioning => _guard.IsActive;

        /// <summary>フェード描画の実装を差し替える。null なら非描画実装に戻す。</summary>
        public void SetFader(IScreenFader fader)
        {
            _fader = fader ?? new NullScreenFader();
        }

        /// <inheritdoc />
        public ServiceInitResult Initialize()
        {
            return ServiceInitResult.Ok("Scene flow ready.");
        }

        /// <inheritdoc />
        public void LoadLauncher()
        {
            LoadScene(SceneNames.Launcher, useLoadingScreen: false);
        }

        /// <summary>
        /// 指定 Scene へ遷移する。遷移中に呼ばれた場合は無視する。
        /// </summary>
        /// <param name="targetScene">遷移先 Scene 名。</param>
        /// <param name="useLoadingScreen">true なら Loading Scene を挟む。</param>
        /// <returns>遷移を開始した場合 true。無視された場合 false。</returns>
        public bool LoadScene(string targetScene, bool useLoadingScreen = true)
        {
            if (string.IsNullOrEmpty(targetScene))
            {
                GameLog.Error(LogCategory.Scene, "LoadScene called with empty target.");
                return false;
            }

            if (!_guard.TryBegin())
            {
                GameLog.WarningOnce(LogCategory.Scene, "scene_transition_busy",
                    "Scene transition already in progress; request ignored.", scene: targetScene);
                return false;
            }

            StartCoroutine(TransitionRoutine(targetScene, useLoadingScreen));
            return true;
        }

        private IEnumerator TransitionRoutine(string targetScene, bool useLoadingScreen)
        {
            GameLog.Info(LogCategory.Scene, "Transition begin.", scene: targetScene);
            yield return Fade(1f);

            if (useLoadingScreen)
            {
                yield return SceneManager.LoadSceneAsync(SceneNames.Loading, LoadSceneMode.Single);
            }

            AsyncOperation op = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            while (op != null && !op.isDone)
            {
                yield return null;
            }

            yield return Fade(0f);
            _guard.End();
            GameLog.Info(LogCategory.Scene, "Transition complete.", scene: targetScene);
        }

        private IEnumerator Fade(float targetAlpha)
        {
            if (_fadeDuration <= 0f)
            {
                _fader.Alpha = targetAlpha;
                yield break;
            }

            float start = _fader.Alpha;
            float elapsed = 0f;
            while (elapsed < _fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                _fader.Alpha = Mathf.Lerp(start, targetAlpha, elapsed / _fadeDuration);
                yield return null;
            }

            _fader.Alpha = targetAlpha;
        }
    }
}
