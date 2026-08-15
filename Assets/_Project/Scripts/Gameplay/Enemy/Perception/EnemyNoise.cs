using System.Collections.Generic;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>
    /// 音刺激の種別（Phase3 §4.2 / Table 8）。Player の行動から発行され、敵が聴覚で受信する。将来の乱舞・術は拡張点のみ。
    /// </summary>
    public enum NoiseKind
    {
        /// <summary>通常移動（半径なし／極小）。</summary>
        Movement = 0,

        /// <summary>ステップ（半径 3.0）。</summary>
        Step = 1,

        /// <summary>通常攻撃（半径 4.0）。</summary>
        Attack = 2,

        /// <summary>必殺技チャージ（半径 3.0）。</summary>
        SpecialCharge = 3,

        /// <summary>必殺技発動（半径 8.0）。</summary>
        SpecialActivate = 4,

        /// <summary>敵の警戒声（半径 6.0。共有用。壁越しに届く）。</summary>
        EnemyAlertVoice = 5,

        /// <summary>将来の乱舞・術（半径 5.0。拡張点。Phase 3 では発行しない）。</summary>
        FlurryOrArt = 6,
    }

    /// <summary>
    /// 音刺激（Phase3 §4.2）。Stimulus ID・発生元・発生位置・半径・発生時刻・種別・共有世代を持つ。受信者は発生地点を知るが、
    /// 遮蔽後の主人公現在位置は取得しない（本刺激の Position は「発生した地点」）。同一 Stimulus ID を重複処理しない。
    /// </summary>
    public readonly struct NoiseStimulus
    {
        /// <summary>刺激の一意 ID（重複処理防止キー）。</summary>
        public int StimulusId { get; }
        /// <summary>発生元の Actor 同定 ID。</summary>
        public int SourceActorId { get; }
        /// <summary>発生位置（音が鳴った地点）。</summary>
        public Vector3 Position { get; }
        /// <summary>到達半径（m）。</summary>
        public float Radius { get; }
        /// <summary>発生時刻（Game Time 秒）。</summary>
        public float TimeStamp { get; }
        /// <summary>種別。</summary>
        public NoiseKind Kind { get; }
        /// <summary>共有世代（0=一次。警戒声の連鎖を最大 1 回に制限する）。</summary>
        public int ShareGeneration { get; }

        public NoiseStimulus(int stimulusId, int sourceActorId, Vector3 position, float radius, float timeStamp,
            NoiseKind kind, int shareGeneration)
        {
            StimulusId = stimulusId;
            SourceActorId = sourceActorId;
            Position = position;
            Radius = radius;
            TimeStamp = timeStamp;
            Kind = kind;
            ShareGeneration = shareGeneration;
        }
    }

    /// <summary>音刺激の受信契約。</summary>
    public interface INoiseListener
    {
        /// <summary>音刺激を受信したときに呼ばれる。半径・距離・重複判定は受信側で行う。</summary>
        void OnNoise(in NoiseStimulus stimulus);
    }

    /// <summary>
    /// 音刺激の配信チャネル（HitResultChannel と同系統。発火中の増減に安全なスナップショット反復）。Stimulus ID の
    /// 採番も担う（<see cref="NextStimulusId"/>）。Gameplay 内で完結し、Presentation／Input へ依存しない。
    /// </summary>
    public sealed class NoiseChannel
    {
        private readonly List<INoiseListener> _listeners = new List<INoiseListener>();
        private int _nextId = 1;

        /// <summary>購読者数。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>刺激 ID を採番する（発行の直前に取得する）。</summary>
        public int NextStimulusId() => _nextId++;

        /// <summary>購読を追加する（重複登録はしない）。</summary>
        public void AddListener(INoiseListener listener)
        {
            if (listener != null && !_listeners.Contains(listener))
            {
                _listeners.Add(listener);
            }
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(INoiseListener listener) => _listeners.Remove(listener);

        /// <summary>全購読者へ刺激を配信する。</summary>
        public void Publish(in NoiseStimulus stimulus)
        {
            int count = _listeners.Count;
            if (count == 0)
            {
                return;
            }

            var snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnNoise(stimulus);
            }
        }
    }

    /// <summary>
    /// 音刺激の提供点（Phase3 §4.2）。GameModeProvider / PlayerInputProvider と同じ静的プロバイダ方式で、
    /// Find* を使わず Player（発行）と敵（受信）を疎結合につなぐ。テストは <see cref="Reset"/> で初期化できる。
    /// </summary>
    public static class NoiseBus
    {
        /// <summary>共有の音チャネル。</summary>
        public static NoiseChannel Channel { get; private set; } = new NoiseChannel();

        /// <summary>チャネルを差し替える（テスト・再初期化用）。null なら新規生成。</summary>
        public static void Reset(NoiseChannel channel = null)
        {
            Channel = channel ?? new NoiseChannel();
        }
    }

    /// <summary>
    /// 音種別ごとの到達半径（Phase3 Table 8）と警戒共有半径（§4.3）。Phase 3 の試作定数（読み合い調整用。将来 Data 化可）。
    /// </summary>
    public static class NoiseCatalog
    {
        /// <summary>警戒声の共有半径（§4.3）。</summary>
        public const float AlertShareRadius = 6.0f;

        /// <summary>種別ごとの到達半径（m）。Movement は 0（極小）。</summary>
        public static float Radius(NoiseKind kind)
        {
            switch (kind)
            {
                case NoiseKind.Step: return 3.0f;
                case NoiseKind.Attack: return 4.0f;
                case NoiseKind.SpecialCharge: return 3.0f;
                case NoiseKind.SpecialActivate: return 8.0f;
                case NoiseKind.EnemyAlertVoice: return AlertShareRadius;
                case NoiseKind.FlurryOrArt: return 5.0f;
                default: return 0f; // Movement
            }
        }
    }
}
