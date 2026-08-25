using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 主人公のステップ（回避）開始に同期して SE を鳴らす Presenter（Phase3.5 P3.5-09。ステップ SE）。<see cref="IStepObserver"/> の
    /// ステップ中フラグの立ち上がり（false→true）を検出し、専用 <see cref="CombatSePlayer"/> でステップ SE を 1 回鳴らす。
    ///
    /// 攻撃スイング SE・ヒット音とは別系統の主人公アクション SE。命中の有無に依存しない（回避成立の有無に関わらずステップ動作で鳴る）。
    /// Gameplay ロジックには一切干渉しない（読み取りのみ）。SE 未割当でも無音・無例外で継続する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStepSePresenter : MonoBehaviour
    {
        [Tooltip("ステップ SE の再生器（専用スロット・音量）。")]
        [SerializeField] private CombatSePlayer _se;

        [Tooltip("観測元。未指定なら Scene 内の PlayerStateController を低頻度で自動探索する。")]
        [SerializeField] private PlayerStateController _player;

        [Tooltip("ステップ SE の鍵（CombatSePlayer のスロット seId と一致）。")]
        [SerializeField] private string _stepSeId = "SE_Player_Step";

        [Tooltip("未 Bind の間の自動探索間隔（秒）。毎フレーム FindObjects しないためのスロットル。")]
        [SerializeField] private float _autoLocateInterval = 0.5f;

        private IStepObserver _source;
        private bool _wasStepping;
        private float _locateTimer;

        /// <summary>ステップ SE 再生器（Scene 構築・テストが設定）。</summary>
        public CombatSePlayer Se { get => _se; set => _se = value; }

        /// <summary>ステップ SE を発火した回数（テスト・診断用）。</summary>
        public int PlayCount { get; private set; }

        /// <summary>観測元を注入する（Scene 構築・テスト。読み取りのみ）。</summary>
        public void Bind(IStepObserver source)
        {
            _source = source;
        }

        private void Awake()
        {
            if (_player != null)
            {
                _source = _player;
            }
        }

        private void OnDisable()
        {
            _wasStepping = false; // 再有効化・Scene 再読込後に前回状態を持ち越さない（誤発火防止）。
        }

        private void Update()
        {
            if (_source == null)
            {
                _locateTimer += Time.unscaledDeltaTime;
                if (_locateTimer >= _autoLocateInterval)
                {
                    _locateTimer = 0f;
                    _player = FindFirstObjectByType<PlayerStateController>();
                    if (_player != null)
                    {
                        _source = _player;
                    }
                }
            }

            Tick();
        }

        /// <summary>ステップ中フラグの立ち上がりで SE を 1 回鳴らす（Update から、またはテストが決定的に呼ぶ）。</summary>
        public void Tick()
        {
            bool stepping = _source != null && _source.IsStepping;

            if (stepping && !_wasStepping && !string.IsNullOrEmpty(_stepSeId))
            {
                PlayCount++;
                _se?.Play(_stepSeId); // 未割当（_se・Clip 未設定）でも無音・無例外。
            }

            _wasStepping = stepping;
        }
    }
}
