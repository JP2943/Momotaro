using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// 敵の認識統合（Phase3 P3-02。§4）。視覚（角度・距離・LOS）で <see cref="PerceptionState"/> を進め、被弾は即時 Alert、
    /// 音・警戒共有で Suspicious 化して最終確認位置を調査する。認識結果は <see cref="EnemyActor.RequestState"/> 経由で
    /// Idle／Suspicious／Alert に反映する（追跡移動・攻撃は P3-03/04 対象外）。LOS は毎フレーム一斉実行せず更新間隔をずらし、
    /// Pause／会話中は評価しない。物理 LOS は <see cref="ILineOfSightProbe"/> 越しでテスト時は Fake を注入できる。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(EnemyActor))]
    public sealed class EnemyPerception : MonoBehaviour, INoiseListener, IHitResultListener
    {
        [Tooltip("認識評価（LOS 含む）の更新間隔（秒）。毎フレーム一斉実行せず負荷を平準化する。")]
        [SerializeField] private float _evaluateInterval = 0.15f;

        [Tooltip("LOS レイの目線高さ（m）。")]
        [SerializeField] private float _eyeHeight = 1.0f;

        private EnemyActor _actor;
        private PerceptionState _state;
        private PerceptionSettings _settings;
        private ILineOfSightProbe _probe;
        private readonly HashSet<int> _processedNoise = new HashSet<int>();
        private float _nextEvalTime;
        private float _lastEvalTime;
        private bool _ready;

        /// <summary>現在の認識段階。</summary>
        public PerceptionPhase Phase => _state != null ? _state.Phase : PerceptionPhase.Unaware;

        /// <summary>最終確認位置を持つか。</summary>
        public bool HasLastKnownPosition => _state != null && _state.HasLastKnownPosition;

        /// <summary>最終確認位置（調査先）。</summary>
        public Vector3 LastKnownPosition => _state != null ? _state.LastKnownPosition : transform.position;

        private void Awake()
        {
            _actor = GetComponent<EnemyActor>();
            EnsureReady();
        }

        private void OnEnable()
        {
            EnsureReady();
            NoiseBus.Channel.AddListener(this);
            if (_actor != null)
            {
                _actor.Results.AddListener(this);
            }
        }

        private void OnDisable()
        {
            NoiseBus.Channel.RemoveListener(this);
            if (_actor != null)
            {
                _actor.Results.RemoveListener(this);
            }
        }

        private void EnsureReady()
        {
            if (_ready)
            {
                return;
            }

            if (_actor == null)
            {
                _actor = GetComponent<EnemyActor>();
            }

            if (_actor == null || _actor.Archetype == null)
            {
                return; // Data 未設定（テストで後から注入する場合など）。
            }

            _settings = PerceptionSettings.From(_actor.Archetype);
            _state = new PerceptionState(_settings);
            if (_probe == null)
            {
                _probe = new PhysicsLineOfSightProbe(_eyeHeight);
            }

            // インスタンスごとに初回評価時刻をずらして一斉 LOS を避ける。
            _nextEvalTime = Time.time + (GetInstanceID() & 7) * 0.01f;
            _lastEvalTime = Time.time;
            _ready = true;
        }

        /// <summary>視線プローブを差し替える（テストで Fake を注入する）。</summary>
        public void SetLineOfSightProbe(ILineOfSightProbe probe)
        {
            _probe = probe;
        }

        private void Update()
        {
            if (!IsGameplayActive())
            {
                return; // Pause／会話／ローディング中は認識タイマーを進めない。
            }

            EnsureReady();
            if (!_ready)
            {
                return;
            }

            if (Time.time < _nextEvalTime)
            {
                return;
            }

            float dt = Mathf.Max(0f, Time.time - _lastEvalTime);
            _lastEvalTime = Time.time;
            _nextEvalTime = Time.time + Mathf.Max(0.02f, _evaluateInterval);
            EvaluateOnce(dt);
        }

        /// <summary>
        /// 認識を 1 評価分進める（視覚→状態→反映）。Update から間隔ごとに呼ばれるほか、テストが決定的に駆動できる。
        /// <paramref name="deltaTime"/> は前回評価からの経過秒。
        /// </summary>
        public void EvaluateOnce(float deltaTime)
        {
            EnsureReady();
            if (!_ready)
            {
                return;
            }

            Vector3 selfPos = _actor.WorldPosition;
            if (PerceptionTargetRegistry.TryGetNearestHostile(selfPos, _actor.Faction, out IPerceptionTarget target))
            {
                Vector3 targetPos = target.Position;
                bool hasLos = _probe == null || _probe.HasLineOfSight(selfPos, targetPos);
                bool sensed = VisionCheck.CanSense(selfPos, _actor.Forward, targetPos, _settings, _state.IsAlert, hasLos);
                if (_state.ObserveSight(sensed, targetPos, deltaTime))
                {
                    EmitAlertVoice();
                }
            }
            else
            {
                _state.ObserveSight(false, selfPos, deltaTime);
            }

            ReflectAiState();
        }

        /// <inheritdoc />
        public void OnNoise(in NoiseStimulus stimulus)
        {
            EnsureReady();
            if (!_ready)
            {
                return;
            }

            if (stimulus.SourceActorId == _actor.DamageableId)
            {
                return; // 自分の音（自分の警戒声を含む）は無視。
            }

            if (!_processedNoise.Add(stimulus.StimulusId))
            {
                return; // 同一刺激は重複処理しない。
            }

            float dist = VisionCheck.PlanarDistance(_actor.WorldPosition, stimulus.Position);
            if (dist > stimulus.Radius)
            {
                return; // 到達半径外。
            }

            if (stimulus.Kind == NoiseKind.EnemyAlertVoice)
            {
                if (stimulus.ShareGeneration >= 1)
                {
                    return; // 共有連鎖は最大 1 回。二次共有は処理しない。
                }

                _state.NotifyAlertShared(stimulus.Position); // 直接視認まで Alert にはしない（§4.3）。再共有もしない。
            }
            else
            {
                _state.NotifyNoiseHeard(stimulus.Position);
            }

            ReflectAiState();
        }

        /// <inheritdoc />
        public void OnHitResult(in HitResult result)
        {
            EnsureReady();
            if (!_ready || !ReferenceEquals(result.Target, _actor))
            {
                return; // 自分の被弾のみ。
            }

            bool hasAttacker = result.Attacker != null;
            Vector3 attackerPos = hasAttacker ? result.Attacker.WorldPosition : _actor.WorldPosition;
            if (_state.NotifyHit(hasAttacker, attackerPos)) // 被弾は視線に関係なく即時 Alert。
            {
                EmitAlertVoice();
            }

            ReflectAiState();
        }

        private void EmitAlertVoice()
        {
            // 直接視認・被弾で Alert 化したときのみ発行（共有経由では発行せず連鎖を止める）。
            NoiseChannel channel = NoiseBus.Channel;
            Vector3 pos = _state.HasLastKnownPosition ? _state.LastKnownPosition : _actor.WorldPosition;
            channel.Publish(new NoiseStimulus(
                channel.NextStimulusId(), _actor.DamageableId, pos, NoiseCatalog.AlertShareRadius, Time.time,
                NoiseKind.EnemyAlertVoice, shareGeneration: 0));
        }

        private void ReflectAiState()
        {
            EnemyState current = _actor.State;
            if (EnemyStatePriority.IsForcedByHit(current))
            {
                return; // 被弾由来（Down/Stunned/Stagger）は認識で上書きしない。
            }

            EnemyState want;
            EnemyStateChangeReason reason;
            switch (_state.Phase)
            {
                case PerceptionPhase.Alert:
                    want = EnemyState.Alert;
                    reason = EnemyStateChangeReason.PerceivedTarget;
                    break;
                case PerceptionPhase.Suspicious:
                    want = EnemyState.Suspicious;
                    reason = EnemyStateChangeReason.SuspiciousStimulus;
                    break;
                default:
                    want = EnemyState.Idle;
                    reason = EnemyStateChangeReason.LostTarget;
                    break;
            }

            if (current != want)
            {
                _actor.RequestState(want, reason);
            }
        }

        /// <summary>認識と音履歴を初期化する（戦闘終了・検証の再試行用）。</summary>
        public void ResetPerception()
        {
            _state?.Reset();
            _processedNoise.Clear();
        }

        private static bool IsGameplayActive()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return true; // 未初期化（単体テスト等）は評価を許可する。
            }

            GameMode m = modes.Current;
            return m == GameMode.Exploration || m == GameMode.Combat;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            EnemyActor actor = _actor != null ? _actor : GetComponent<EnemyActor>();
            if (actor == null || actor.Archetype == null)
            {
                return;
            }

            PerceptionSettings s = PerceptionSettings.From(actor.Archetype);
            Vector3 origin = actor.WorldPosition + Vector3.up * _eyeHeight;
            Vector3 fwd = actor.Forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = Vector3.forward;
            }

            fwd.Normalize();

            // 視野コーン（通常/警戒距離）と背後近接圏、最終確認位置。
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            float half = s.ViewAngleDegrees * 0.5f;
            Vector3 left = Quaternion.AngleAxis(-half, Vector3.up) * fwd;
            Vector3 right = Quaternion.AngleAxis(half, Vector3.up) * fwd;
            Gizmos.DrawLine(origin, origin + left * s.ViewDistance);
            Gizmos.DrawLine(origin, origin + right * s.ViewDistance);
            Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(actor.WorldPosition, s.AlertViewDistance);
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.6f);
            Gizmos.DrawWireSphere(actor.WorldPosition, s.BackAwarenessRadius);

            if (_state != null && _state.HasLastKnownPosition)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireCube(_state.LastKnownPosition + Vector3.up * 0.5f, Vector3.one * 0.4f);
            }
        }
#endif
    }
}
