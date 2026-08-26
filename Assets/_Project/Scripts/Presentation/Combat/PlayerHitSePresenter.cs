using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Diagnostics;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 主人公の攻撃が「当たった瞬間」に段別のヒット音を鳴らす Presenter（Phase3.5 P3.5-08B/09。ヒット音）。命中フィードバック
    /// （<see cref="CombatFeedbackDispatcher.Feedback"/>）を購読し、<see cref="HitResultKind.Damage"/> かつ攻撃者が主人公
    /// （<see cref="CombatFaction.Player"/>）の結果に対して、その時点の攻撃段（<see cref="IAttackSwingSource.SwingStage"/>）で
    /// ヒット SE を選ぶ。通常 1・2 段目は共通、3 段目と必殺技は別音（Scene 構築側で割り当て）。
    ///
    /// スイング SE（振り）とは別で、こちらは接触（Damage 成立）に同期する。同一 Swing（<see cref="HitId"/>）の重複再生を禁止し、
    /// 複数体命中でも 1 回だけ鳴らす。敵→主人公の被弾（攻撃者が敵）では鳴らさない。ヒット結果 SE（Guard/JG）とは別系統で専用
    /// <see cref="CombatSePlayer"/> を持つ。Gameplay 非干渉（読み取りのみ）。SE 未割当でも無音・無例外で継続する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHitSePresenter : MonoBehaviour, ICombatFeedbackListener
    {
        [Tooltip("ヒット SE の再生器（専用スロット・音量）。")]
        [SerializeField] private CombatSePlayer _se;

        [Header("段別ヒット SE の鍵（CombatSePlayer のスロット seId と一致）")]
        [Tooltip("通常攻撃 1・2 段目のヒット SE（共通）。")]
        [SerializeField] private string _stage12SeId = "SE_Player_Hit1";

        [Tooltip("通常攻撃 3 段目・必殺技のヒット SE（共通）。")]
        [SerializeField] private string _stage3SpecialSeId = "SE_Player_Hit2";

        [Tooltip("配信元（Dispatcher）を再取得する間隔（秒）。未購読の間だけ探索する。")]
        [SerializeField] private float _refreshInterval = 1f;

        private CombatFeedbackChannel _channel;
        private float _nextRefresh;
        private HitId _lastHitId;
        private bool _hasLast;

        /// <summary>ヒット SE 再生器（Scene 構築・テストが設定）。</summary>
        public CombatSePlayer Se { get => _se; set => _se = value; }

        /// <summary>直近に鳴らしたヒット SE 鍵（テスト・診断用。未発火なら null）。</summary>
        public string LastSeId { get; private set; }

        /// <summary>ヒット SE を発火した回数（テスト・診断用）。</summary>
        public int PlayCount { get; private set; }

        /// <summary>購読チャネルを差し替える（テスト・Scene 構築）。重複購読しない。</summary>
        public void Bind(CombatFeedbackChannel channel)
        {
            if (ReferenceEquals(channel, _channel))
            {
                return;
            }

            if (_channel != null)
            {
                _channel.RemoveListener(this);
            }

            _channel = channel;
            if (_channel != null)
            {
                _channel.AddListener(this);
            }
        }

        /// <summary>Scene 内の <see cref="CombatFeedbackDispatcher"/> を探して購読し直す。</summary>
        public void Rescan()
        {
            CombatFeedbackDispatcher dispatcher = FindFirstObjectByType<CombatFeedbackDispatcher>();
            Bind(dispatcher != null ? dispatcher.Feedback : null);
        }

        private void OnEnable()
        {
            Rescan();
        }

        private void OnDisable()
        {
            if (_channel != null)
            {
                _channel.RemoveListener(this);
                _channel = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, _refreshInterval);
                if (_channel == null)
                {
                    Rescan();
                }
            }
        }

        /// <inheritdoc />
        public void OnCombatFeedback(in CombatFeedbackEvent feedback)
        {
            HitResult r = feedback.Result;
            if (r.Kind != HitResultKind.Damage)
            {
                return;
            }

            // 主人公の攻撃が当たった時のみ（敵→主人公の被弾では鳴らさない）。
            if (!(r.Attacker is ICombatActor actor) || actor.Faction != CombatFaction.Player)
            {
                return;
            }

            // 同一 Swing（HitId）は 1 回だけ（複数体命中でも重複再生しない）。
            if (_hasLast && r.HitId == _lastHitId)
            {
                return;
            }

            _lastHitId = r.HitId;
            _hasLast = true;

            int stage = (r.Attacker as IAttackSwingSource)?.SwingStage ?? 0;
            string seId = SeIdFor(stage);
            if (!string.IsNullOrEmpty(seId))
            {
                LastSeId = seId;
                PlayCount++;
                _se?.Play(seId); // 未割当（_se・Clip 未設定）でも無音・無例外。
            }
        }

        /// <summary>攻撃段に対応するヒット SE 鍵。1・2 段目＝共通、3 段目・必殺技＝共通。それ以外（敵段・非攻撃）は null。</summary>
        private string SeIdFor(int stage)
        {
            switch (stage)
            {
                case 1:
                case 2:
                    return _stage12SeId;
                case 3:
                case AttackSwing.SpecialStage:
                    return _stage3SpecialSeId;
                default:
                    return null;
            }
        }
    }
}
