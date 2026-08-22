using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 主人公の通常攻撃・必殺技の「振り（swing）」に同期して、刀を振る効果音（スイング SE）を鳴らす Presenter（Phase3.5 P3.5-08C）。
    /// <see cref="IAttackSwingSource.SwingStage"/> の切り替わり（段の出現）を検出し、その段（1〜3 段目・必殺技）に対応する SE を
    /// <see cref="CombatSePlayer"/> 経由で 1 回鳴らす。命中の有無に依存せず「空振りでも」鳴らす（＝振りの音であり、当たった音ではない）。
    ///
    /// タイミング：通常コンボの <c>SwingStage</c> は判定（Active）区間より前の予備動作（Startup）から立つため、判定（＝剣閃 VFX の発生）より
    /// わずかに早く鳴り、振り出しに音が先行する（P3.5-08C 調整。ヒット SE は別途加わる想定）。段番号の変化で検出するため、連続コンボ
    /// （1→2→3 がシームレスに切り替わる場合）も各段で確実に鳴る。必殺技は判定区間で <see cref="AttackSwing.SpecialStage"/> が立つため、
    /// その振り（発動）に同期する。
    ///
    /// ヒット時の手応え SE（通常ダメージ・ガード・ジャストガード）は <see cref="CombatFeedbackPresenter"/> が別系統で担当する。本 Presenter は
    /// 攻撃アクション由来の SE だけを扱い、専用の <see cref="CombatSePlayer"/>（スロット表）を持って役割を分離する。
    /// Gameplay ロジックには一切干渉しない（読み取りのみ）。SE 未割当でも無音・無例外で継続する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAttackSwingSePresenter : MonoBehaviour
    {
        [Tooltip("スイング SE の再生器（段別スロットを持つ。専用インスタンスを割り当てる）。")]
        [SerializeField] private CombatSePlayer _se;

        [Tooltip("観測元。未指定なら Scene 内の PlayerStateController を低頻度で自動探索する。")]
        [SerializeField] private PlayerStateController _player;

        [Header("段別スイング SE の鍵（CombatSePlayer のスロット seId と一致させる）")]
        [SerializeField] private string _stage1SeId = "SE_Player_Attack1";
        [SerializeField] private string _stage2SeId = "SE_Player_Attack2";
        [SerializeField] private string _stage3SeId = "SE_Player_Attack3";
        [SerializeField] private string _specialSeId = "SE_Player_Special";

        [Tooltip("未 Bind の間の自動探索間隔（秒）。毎フレーム FindObjects しないためのスロットル。")]
        [SerializeField] private float _autoLocateInterval = 0.5f;

        private IAttackSwingSource _source;
        private int _lastStage;
        private float _locateTimer;

        /// <summary>スイング SE 再生器（Scene 構築・テストが設定）。</summary>
        public CombatSePlayer Se { get => _se; set => _se = value; }

        /// <summary>直近に鳴らした段別スイング SE の鍵（テスト・診断用。未発火なら null）。</summary>
        public string LastSwingSeId { get; private set; }

        /// <summary>スイング SE を発火した回数（段の出現×主人公段。テスト・診断用）。</summary>
        public int SwingCount { get; private set; }

        /// <summary>観測元を注入する（Scene 構築・テスト。読み取りのみ）。</summary>
        public void Bind(IAttackSwingSource source)
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
            _lastStage = 0; // 再有効化・Scene 再読込後に前回段を持ち越さない（誤発火防止）。
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

        /// <summary>
        /// 段（<see cref="IAttackSwingSource.SwingStage"/>）の変化を検出し、新しい主人公段に対応するスイング SE を 1 回鳴らす
        /// （Update から、またはテストが決定的に呼ぶ）。同じ段が続く間は再発火しない（振り 1 回につき 1 音）。時間引数は不要
        /// （段番号の変化で判定するため）。連続コンボ（0 を挟まず 1→2→3 と変わる場合）も各段で鳴る。
        /// </summary>
        public void Tick()
        {
            int stage = _source != null ? _source.SwingStage : 0;

            if (stage != _lastStage)
            {
                string seId = SeIdFor(stage);
                if (!string.IsNullOrEmpty(seId))
                {
                    LastSwingSeId = seId;
                    SwingCount++;
                    _se?.Play(seId); // 未割当（_se・Clip 未設定）でも無音・無例外。
                }
            }

            _lastStage = stage;
        }

        /// <summary>段（通常 1..3・必殺技）に対応するスイング SE 鍵を返す。敵段・非攻撃(0)は null（発火しない）。</summary>
        private string SeIdFor(int stage)
        {
            switch (stage)
            {
                case 1: return _stage1SeId;
                case 2: return _stage2SeId;
                case 3: return _stage3SeId;
                case AttackSwing.SpecialStage: return _specialSeId;
                default: return null; // 敵段（200 系）・非攻撃時は主人公スイング SE の対象外。
            }
        }
    }
}
