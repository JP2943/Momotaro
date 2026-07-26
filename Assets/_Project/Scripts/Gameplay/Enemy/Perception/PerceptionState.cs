using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// 認識の純粋状態機（Phase3 §4.1/§4.3）。視覚蓄積（完全認識 0.25 秒）で Unaware→Suspicious→Alert、視認喪失後は
    /// 追跡継続秒（3 秒）で Alert を維持してから Suspicious へ落とし、最終確認位置を保持する。被弾は視線に関係なく即時 Alert、
    /// 音・警戒共有は Suspicious（発生/共有地点を調査）。Game Time を注入して Pause 中は進めない（deltaTime を渡さない）。
    /// Unity 非依存で EditMode 再現可能。
    /// </summary>
    public sealed class PerceptionState
    {
        private readonly PerceptionSettings _settings;
        private float _loseTimer;

        public PerceptionState(PerceptionSettings settings)
        {
            _settings = settings;
        }

        /// <summary>現在の認識段階。</summary>
        public PerceptionPhase Phase { get; private set; } = PerceptionPhase.Unaware;

        /// <summary>最終確認位置を持つか。</summary>
        public bool HasLastKnownPosition { get; private set; }

        /// <summary>最後に確認/共有された対象位置（調査先）。</summary>
        public Vector3 LastKnownPosition { get; private set; }

        /// <summary>視覚の蓄積秒（完全認識判定用）。</summary>
        public float RecognitionAccum { get; private set; }

        /// <summary>Alert 中の視認喪失タイマー（秒）。</summary>
        public float LoseTimer => _loseTimer;

        /// <summary>警戒を「共有経由」で得たか（再共有抑止に用いる）。</summary>
        public bool AlertedByShare { get; private set; }

        /// <summary>Alert 中か。</summary>
        public bool IsAlert => Phase == PerceptionPhase.Alert;

        /// <summary>
        /// 視覚観測を 1 フレーム分進める。<paramref name="sensed"/> は角度・距離・視線をすべて満たすか。
        /// 直接視認で新規に Alert 化したら true（呼び出し側が警戒声を発行する契機）。
        /// </summary>
        public bool ObserveSight(bool sensed, Vector3 targetPos, float deltaTime)
        {
            bool becameAlertDirect = false;

            if (sensed)
            {
                LastKnownPosition = targetPos;
                HasLastKnownPosition = true;
                _loseTimer = 0f;

                if (Phase != PerceptionPhase.Alert)
                {
                    RecognitionAccum += deltaTime;
                    if (RecognitionAccum + 1e-6f >= _settings.FullRecognitionSeconds)
                    {
                        Phase = PerceptionPhase.Alert;
                        AlertedByShare = false;
                        becameAlertDirect = true;
                    }
                    else
                    {
                        Phase = PerceptionPhase.Suspicious; // 蓄積中の短い視認は不審。
                    }
                }
            }
            else
            {
                if (Phase == PerceptionPhase.Alert)
                {
                    // 戦闘開始後は背後移動だけで即時喪失しない：追跡継続秒を経てから不審へ。
                    _loseTimer += deltaTime;
                    if (_loseTimer + 1e-6f >= _settings.LoseSightSeconds)
                    {
                        Phase = PerceptionPhase.Suspicious;
                        RecognitionAccum = 0f;
                        _loseTimer = 0f;
                    }
                }
                else
                {
                    RecognitionAccum = Mathf.Max(0f, RecognitionAccum - deltaTime); // 不審の蓄積は減衰。
                }
            }

            return becameAlertDirect;
        }

        /// <summary>被弾：視線に関係なく即時 Alert。攻撃者位置が既知なら最終確認位置に採用。新規 Alert 化なら true。</summary>
        public bool NotifyHit(bool hasAttackerPosition, Vector3 attackerPosition)
        {
            bool rising = Phase != PerceptionPhase.Alert;
            Phase = PerceptionPhase.Alert;
            RecognitionAccum = _settings.FullRecognitionSeconds;
            _loseTimer = 0f;
            AlertedByShare = false;
            if (hasAttackerPosition)
            {
                LastKnownPosition = attackerPosition;
                HasLastKnownPosition = true;
            }

            return rising;
        }

        /// <summary>音を聞いた：発生地点を最終確認位置にして不審化（既に Alert なら段階は維持し地点のみ更新）。</summary>
        public void NotifyNoiseHeard(Vector3 noisePosition)
        {
            LastKnownPosition = noisePosition;
            HasLastKnownPosition = true;
            if (Phase == PerceptionPhase.Unaware)
            {
                Phase = PerceptionPhase.Suspicious;
            }
        }

        /// <summary>
        /// 警戒共有を受信：共有元の最終確認位置へ向かう（Suspicious）。直接視認が成立するまで正確な現在位置は知らないため
        /// Alert にはしない（§4.3）。共有経由フラグを立て、この敵はさらに通常共有しない（連鎖を止める）。
        /// </summary>
        public void NotifyAlertShared(Vector3 sharedLastKnown)
        {
            LastKnownPosition = sharedLastKnown;
            HasLastKnownPosition = true;
            if (Phase == PerceptionPhase.Unaware)
            {
                Phase = PerceptionPhase.Suspicious;
                AlertedByShare = true;
            }
        }

        /// <summary>初期化（戦闘終了・Reset 用）。</summary>
        public void Reset()
        {
            Phase = PerceptionPhase.Unaware;
            HasLastKnownPosition = false;
            LastKnownPosition = Vector3.zero;
            RecognitionAccum = 0f;
            _loseTimer = 0f;
            AlertedByShare = false;
        }
    }
}
