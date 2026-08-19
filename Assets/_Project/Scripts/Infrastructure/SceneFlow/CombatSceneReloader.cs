using Momotaro.Gameplay.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Infrastructure.SceneFlow
{
    /// <summary>
    /// 試遊 Scene の再読込 Adapter（Phase3.5 P3.5-08。<see cref="ICombatSceneReloader"/> の Infrastructure 実装）。仕様書 §9.2。
    /// Gameplay（Session）は Scene API に直接触れず、本コンポーネント経由でのみ現在 Scene を Async 再読込する。読込対象は
    /// 起動時に捕捉した「現在の Scene」（名前文字列を散在させない）。二重要求は自前フラグと Session の Reloading 状態の二段で防ぐ。
    ///
    /// 再読込開始時に <see cref="Time.timeScale"/> を 1 へ戻し（万一の低速・凍結状態からの確実な復帰）、読込完了で新しい Scene の
    /// <see cref="GameplaySceneMode"/> が Exploration を要求し、HP／Stamina／Special／Wave／敵／UI は新規 Session として Preparing から
    /// 初期化される（Object を個別復元しない）。
    ///
    /// 注意：Runtime 再読込は対象 Scene が Build Settings に含まれる必要がある。未登録時は読込できないため、その旨を一度だけ警告する
    /// （Build Settings の整理は P3.5-10）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatSceneReloader : MonoBehaviour, ICombatSceneReloader
    {
        [SerializeField] private CombatSessionController _session;

        private bool _loading;
        private int _sceneBuildIndex;
        private string _sceneName;

        private void Awake()
        {
            Scene active = SceneManager.GetActiveScene();
            _sceneBuildIndex = active.buildIndex;
            _sceneName = active.name;
        }

        private void OnEnable()
        {
            // Session はこの Adapter 経由でのみ再読込を要求する（Runtime 結線。契約は非 Serialize）。
            if (_session != null)
            {
                _session.SetReloader(this);
            }
        }

        /// <inheritdoc />
        public bool ReloadCurrent()
        {
            if (_loading)
            {
                return false; // 進行中の二重要求は無視（Session 側も Reloading で拒否）。
            }

            bool byIndex = _sceneBuildIndex >= 0;
            if (!byIndex && !CanLoadByName())
            {
                Debug.LogWarning("[CombatSceneReloader] 現在の Scene が Build Settings に未登録のため再読込できません（" + _sceneName
                    + "）。試遊 Scene を Build Settings に追加してください（正式対応は P3.5-10）。", this);
                return false;
            }

            _loading = true;
            Time.timeScale = 1f; // 万一 timeScale が落ちていても、再読込後は通常速度で始める（フリーズからの確実な復帰）。

            if (byIndex)
            {
                SceneManager.LoadSceneAsync(_sceneBuildIndex, LoadSceneMode.Single);
            }
            else
            {
                SceneManager.LoadSceneAsync(_sceneName, LoadSceneMode.Single);
            }

            return true;
        }

        private bool CanLoadByName()
        {
            return !string.IsNullOrEmpty(_sceneName) && Application.CanStreamedLevelBeLoaded(_sceneName);
        }
    }
}
