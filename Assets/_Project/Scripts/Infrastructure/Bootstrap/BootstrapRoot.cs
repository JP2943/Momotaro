using System;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Player;
using Momotaro.Infrastructure.Input;
using Momotaro.Infrastructure.SceneFlow;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Momotaro.Infrastructure.Bootstrap
{
    /// <summary>
    /// 常駐システムのルート。仕様書 11.7 / 15.7 に基づき、起動時に常駐サービスを既定順で
    /// 初期化し、成功時に Launcher へ遷移する。多重生成を防止し、初期化失敗をログに残す。
    ///
    /// Phase 0 では巨大な単一 GameManager へ集約せず、<see cref="ServiceRegistry"/> による
    /// 明示的な初期化枠のみを用意する。個々のサービス実装は後続タスクで追加する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BootstrapRoot : MonoBehaviour
    {
        private static BootstrapRoot _instance;

        /// <summary>唯一の実体。未生成なら null。</summary>
        public static BootstrapRoot Instance => _instance;

        /// <summary>実体が生成済みか。</summary>
        public static bool HasInstance => _instance != null;

        [Header("Input")]
        [Tooltip("常駐 InputService が使用する Action Asset（IA_Momotaro）。未設定時は project-wide actions を試す。")]
        [SerializeField] private InputActionAsset _inputActions;

        private readonly ServiceRegistry _registry = new ServiceRegistry();
        private ISceneFlow _sceneFlow;
        private InputBootService _inputBootService;
        private GameModeBootService _gameModeBootService;

        /// <summary>Player 向け入力（P1-02）。未初期化・アセット未設定時は null。</summary>
        public IPlayerInput PlayerInput => _inputBootService?.PlayerInput;

        /// <summary>初期化が完了し Launcher 遷移可能となったか（テスト・診断用）。</summary>
        public bool BootstrapSucceeded { get; private set; }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // 直開き補助や Scene 重複により二重生成された場合、後発を破棄する。
                GameLog.Warning(LogCategory.Boot, "Duplicate BootstrapRoot detected; destroying the newcomer.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            RunBootstrap();
        }

        private void OnDestroy()
        {
            // Input のイベント購読（InputSystem.onEvent / GameMode リスナー）を確実に解除する。
            _inputBootService?.Dispose();
            _inputBootService = null;

            // GameMode 提供点の注入を解除する。
            _gameModeBootService?.Dispose();
            _gameModeBootService = null;

            if (_instance == this)
            {
                _instance = null;
            }
        }

        /// <summary>
        /// 既定サービスを登録し、初期化を実行して結果に応じ Launcher へ遷移する。
        /// </summary>
        private void RunBootstrap()
        {
            GameLog.Info(LogCategory.Boot, "Bootstrap starting.");
            BuildRegistry();

            bool ok = _registry.InitializeAll();
            BootstrapSucceeded = ok;

            if (!ok)
            {
                GameLog.Error(LogCategory.Boot, "Bootstrap aborted due to a critical service failure. Staying in Bootstrap scene.");
                return;
            }

            // Bootstrap Scene から起動した通常経路のときのみ Launcher へ自動遷移する。
            // 直開き補助で他 Scene に生成された場合は、サービス初期化のみ行い遷移しない。
            if (SceneManager.GetActiveScene().name == SceneNames.Bootstrap)
            {
                GameLog.Info(LogCategory.Boot, "Bootstrap complete. Requesting Launcher.");
                _sceneFlow?.LoadLauncher();
            }
            else
            {
                GameLog.Info(LogCategory.Boot, "Bootstrap complete outside Bootstrap scene; skipping auto-navigation.");
            }
        }

        /// <summary>
        /// Phase 0 の既定サービス構成を登録する。後続タスクで実サービスを差し込む。
        /// 初期化順はこの登録順で決まる。
        /// </summary>
        private void BuildRegistry()
        {
            // GameMode を先に用意し、以降のサービス・Scene Flow・Input が参照できるようにする。
            var gameMode = new GameModeBootService();
            _gameModeBootService = gameMode;
            _registry.Register(gameMode);

            // Scene Flow は常駐ルートのコンポーネントとして生成し、DontDestroyOnLoad を共有する。
            var sceneFlow = gameObject.AddComponent<SceneFlowManager>();
            _sceneFlow = sceneFlow;
            _registry.Register(sceneFlow);

            // Input を常駐サービスとして接続し、GameMode 変更で Action Map を切り替える。
            var input = new InputBootService(_inputActions, gameMode.Modes);
            _inputBootService = input;
            _registry.Register(input);
        }
    }
}
