using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Diagnostics;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 命中フィードバック（<see cref="CombatFeedbackEvent"/>）を受け、手応え演出（ヒットストップ・被弾点滅・カメラ揺れ・仮 SE）へ
    /// 振り分ける調停役（Phase3.5 P3.5-05B）。<see cref="CombatFeedbackDispatcher.Feedback"/> を購読し、種別ごとに各効果を起動する。
    ///
    /// ジャストガードは Cue のヒットストップが長く（§10.2）、加えて点滅色・カメラ揺れを強調して「弾いた」感を出す。回避・棄却は SE のみ。
    /// 各効果は表示専用のサブ Presenter に委譲し、本体は配線と種別分岐に徹する。Disable・Scene 再読込で購読を解除・再取得する。
    /// Gameplay ロジックには一切干渉しない（読み取りのみ）。サブ効果が未割当でも例外なく継続する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatFeedbackPresenter : MonoBehaviour, ICombatFeedbackListener
    {
        [Header("サブ効果（未割当でも安全）")]
        [SerializeField] private HitStopController _hitStop;
        [SerializeField] private HitFlashPresenter _flash;
        [SerializeField] private CameraShakePresenter _shake;
        [SerializeField] private CombatSePlayer _se;

        [Header("種別ごとの色・強さ")]
        [Tooltip("通常ダメージの点滅色。")]
        [SerializeField] private Color _damageFlash = Color.white;

        [Tooltip("通常ガードの点滅色（控えめ）。")]
        [SerializeField] private Color _guardFlash = new Color(0.8f, 0.85f, 1f, 1f);

        [Tooltip("ジャストガードの点滅色（強調）。")]
        [SerializeField] private Color _justGuardFlash = new Color(1f, 0.95f, 0.5f, 1f);

        [Tooltip("ジャスト回避の点滅色（P3.5-09。JG と区別する寒色の閃き）。")]
        [SerializeField] private Color _justEvadeFlash = new Color(0.55f, 0.95f, 1f, 1f);

        [Tooltip("通常ダメージのカメラ揺れ幅。")]
        [SerializeField] private float _damageShake = 0.12f;

        [Tooltip("ジャストガードのカメラ揺れ幅（強調）。")]
        [SerializeField] private float _justGuardShake = 0.22f;

        [Tooltip("ジャスト回避のカメラ揺れ幅（P3.5-09。手応えを出しつつ JG より控えめ）。")]
        [SerializeField] private float _justEvadeShake = 0.16f;

        [Tooltip("カメラ揺れの長さ（秒）。")]
        [SerializeField] private float _shakeSeconds = 0.12f;

        [Tooltip("配信元（Dispatcher）を再取得する間隔（秒）。")]
        [SerializeField] private float _refreshInterval = 1f;

        private CombatFeedbackChannel _channel;
        private float _nextRefresh;

        /// <summary>ヒットストップ制御（Scene 構築 P3.5-06・テストが設定）。</summary>
        public HitStopController HitStop { get => _hitStop; set => _hitStop = value; }

        /// <summary>被弾点滅（Scene 構築 P3.5-06・テストが設定）。</summary>
        public HitFlashPresenter Flash { get => _flash; set => _flash = value; }

        /// <summary>カメラ揺れ（Scene 構築 P3.5-06・テストが設定）。</summary>
        public CameraShakePresenter CameraShake { get => _shake; set => _shake = value; }

        /// <summary>仮 SE 再生（Scene 構築 P3.5-06・テストが設定）。</summary>
        public CombatSePlayer Se { get => _se; set => _se = value; }

        /// <summary>購読チャネルを差し替える（テスト・Scene 構築）。</summary>
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
                    Rescan(); // 未購読の間だけ探索（Dispatcher 生成待ち・Scene 再読込）。
                }
            }
        }

        /// <inheritdoc />
        public void OnCombatFeedback(in CombatFeedbackEvent feedback)
        {
            HitResult r = feedback.Result;
            CombatFeedbackCue cue = feedback.Cue;

            // ヒットストップと SE は種別に依らず Cue に従う（Evade/Rejected は Cue が空なので無処理）。
            if (_hitStop != null && cue.HitStopSeconds > 0f)
            {
                _hitStop.Request(cue.HitStopSeconds);
            }

            if (_se != null)
            {
                _se.Play(cue.SeId);
            }

            switch (r.Kind)
            {
                case HitResultKind.Damage:
                    if (_flash != null) _flash.Trigger(r.Target, _damageFlash);
                    if (_shake != null) _shake.Shake(_damageShake, _shakeSeconds);
                    break;

                case HitResultKind.Guard:
                    if (_flash != null) _flash.Trigger(r.Target, _guardFlash);
                    break;

                case HitResultKind.JustGuard:
                    if (_flash != null) _flash.Trigger(r.Target, _justGuardFlash);
                    if (_shake != null) _shake.Shake(_justGuardShake, _shakeSeconds); // 強調。
                    break;

                case HitResultKind.JustEvade:
                    // ジャスト回避（P3.5-09）：回避成功の中でも「弾き回避」。点滅＋控えめのカメラ揺れで手応えを出す（ヒットストップ・SE は Cue で処理済み）。
                    if (_flash != null) _flash.Trigger(r.Target, _justEvadeFlash);
                    if (_shake != null) _shake.Shake(_justEvadeShake, _shakeSeconds);
                    break;

                default:
                    break; // Evade / Rejected は点滅・揺れなし（SE は上で処理済み）。
            }
        }
    }
}
