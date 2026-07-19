using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// シーン進入時に指定の <see cref="GameMode"/> を要求する軽量コンポーネント（Phase1）。
    /// 例えば VS_Field へ置いて Exploration を要求すると、Bootstrap 経由でも直開きでも
    /// Player が操作可能になる。
    ///
    /// 常駐サービスの生成タイミングに依存しないよう、<see cref="GameModeProvider"/> が利用可能に
    /// なるまで待ってから一度だけ適用する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplaySceneMode : MonoBehaviour
    {
        [Tooltip("このシーンで要求するモード。")]
        [SerializeField] private GameMode _mode = GameMode.Exploration;

        private bool _applied;

        private void OnEnable()
        {
            _applied = false;
            TryApply();
        }

        private void Update()
        {
            if (!_applied)
            {
                TryApply();
            }
        }

        private void TryApply()
        {
            IGameModeService service = GameModeProvider.Current;
            if (service == null)
            {
                return;
            }

            service.ChangeMode(_mode);
            _applied = true;
        }
    }
}
