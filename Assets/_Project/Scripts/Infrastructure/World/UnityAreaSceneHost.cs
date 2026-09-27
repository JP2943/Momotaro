using Momotaro.Gameplay.Session;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// Unity の追加読込・撤去を、先読みが観測できる形に包む（P5.5 §2／§5）。
    ///
    /// <b>P5 の <see cref="UnitySceneLoader"/> は残す。</b> あちらは <c>Single</c> 読込の口で、
    /// P5 の遷移はそのまま通る。ここは P5.5 の追加読込専用。
    ///
    /// <b>読んだ Scene を path ではなく handle で返す。</b> 往復すると同じ path の Scene が
    /// 2 度読まれるため、path で引くと前のインスタンスを掴みうる（§4.3）。
    /// </summary>
    public sealed class UnityAreaSceneHost : IAreaSceneHost
    {
        /// <inheritdoc />
        public IAreaSceneOperation LoadAdditive(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
            {
                return new FailedOperation();
            }

            try
            {
                AsyncOperation operation = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
                return operation == null
                    ? (IAreaSceneOperation)new FailedOperation()
                    : new AdditiveLoad(operation, scenePath);
            }
            catch (System.Exception e)
            {
                // 開始に失敗しても例外を投げない（失敗も同じ経路で観測する。§6.3）。
                Debug.LogWarning("[P5.5] 追加読込の開始に失敗しました: " + scenePath + " / " + e.Message);
                return new FailedOperation();
            }
        }

        /// <inheritdoc />
        public IAreaSceneOperation Unload(int sceneHandle)
        {
            if (!TryFindScene(sceneHandle, out Scene scene))
            {
                return new FailedOperation();
            }

            try
            {
                AsyncOperation operation = SceneManager.UnloadSceneAsync(scene);
                return operation == null
                    ? (IAreaSceneOperation)new FailedOperation()
                    : new SimpleOperation(operation);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[P5.5] 撤去の開始に失敗しました: handle=" + sceneHandle + " / " + e.Message);
                return new FailedOperation();
            }
        }

        /// <summary>handle から読み込まれている Scene を引く。</summary>
        private static bool TryFindScene(int sceneHandle, out Scene found)
        {
            found = default;
            if (sceneHandle == 0)
            {
                return false;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.handle == sceneHandle)
                {
                    found = s;
                    return true;
                }
            }

            return false;
        }

        /// <summary>開始できなかった操作（即エラー）。</summary>
        private sealed class FailedOperation : IAreaSceneOperation
        {
            public bool IsDone => true;
            public bool HasError => true;
            public int SceneHandle => 0;
        }

        /// <summary>Scene を特定する必要がない操作（撤去）。</summary>
        private sealed class SimpleOperation : IAreaSceneOperation
        {
            private readonly AsyncOperation _operation;

            public SimpleOperation(AsyncOperation operation)
            {
                _operation = operation;
            }

            public bool IsDone => _operation == null || _operation.isDone;
            public bool HasError => _operation == null;
            public int SceneHandle => 0;
        }

        /// <summary>
        /// 追加読込。<b>読み込まれた Scene を通知で拾う</b>。
        ///
        /// <c>GetSceneByPath</c> で引くと、同じ path の前のインスタンスを掴みうる。
        /// 通知なら「いま読まれたもの」が確実に取れる。Scene 操作は台帳が直列化しているので、
        /// 同じ path の読込が同時に 2 本走ることはない。
        /// </summary>
        private sealed class AdditiveLoad : IAreaSceneOperation
        {
            private readonly AsyncOperation _operation;
            private readonly string _scenePath;
            private bool _attached;
            private int _sceneHandle;

            public AdditiveLoad(AsyncOperation operation, string scenePath)
            {
                _operation = operation;
                _scenePath = scenePath;
                SceneManager.sceneLoaded += OnSceneLoaded;
                _attached = true;
            }

            public bool IsDone
            {
                get
                {
                    bool done = _operation == null || _operation.isDone;
                    if (done)
                    {
                        // 購読の解除忘れを残さない：終わった時点で必ず外す。
                        Detach();
                    }

                    return done;
                }
            }

            public bool HasError => _operation == null;

            public int SceneHandle => _sceneHandle;

            private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                if (_sceneHandle != 0 || mode != LoadSceneMode.Additive || scene.path != _scenePath)
                {
                    return;
                }

                _sceneHandle = scene.handle;
                Detach();
            }

            private void Detach()
            {
                if (_attached)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    _attached = false;
                }
            }
        }
    }
}
