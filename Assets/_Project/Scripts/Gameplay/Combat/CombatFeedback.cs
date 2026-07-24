using System.Collections.Generic;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中結果に対する仮のフィードバック指定（Phase2 P2-11。仕様書 §10.2 / §900）。VFX・SE は完成実体ではなく ID（データ参照）で
    /// 接続し、ヒットストップは秒数の「要求」として持つ。Presentation 層が本 Cue を受けて実際の演出へ接続する（Gameplay 非依存）。
    /// </summary>
    public readonly struct CombatFeedbackCue
    {
        /// <summary>VFX 参照 ID（空文字は VFX なし）。</summary>
        public string VfxId { get; }

        /// <summary>SE 参照 ID（空文字は SE なし）。</summary>
        public string SeId { get; }

        /// <summary>ヒットストップ要求秒（0 は要求なし）。</summary>
        public float HitStopSeconds { get; }

        public CombatFeedbackCue(string vfxId, string seId, float hitStopSeconds)
        {
            VfxId = vfxId ?? string.Empty;
            SeId = seId ?? string.Empty;
            HitStopSeconds = hitStopSeconds < 0f ? 0f : hitStopSeconds;
        }

        /// <summary>何もしない Cue。</summary>
        public static CombatFeedbackCue None => new CombatFeedbackCue(string.Empty, string.Empty, 0f);
    }

    /// <summary>
    /// 命中結果種別（<see cref="HitResultKind"/>）から仮フィードバック Cue を解決する純粋マップ（Phase2 P2-11）。ID は仮の固定値で、
    /// 完成 VFX/SE 実体・Accessibility 全項目は対象外。数値・ID は後で Data 化できるが、本 Phase では定数表とする。
    /// </summary>
    public static class CombatFeedbackMap
    {
        /// <summary>種別に対応する仮 Cue を返す。未知種別は <see cref="CombatFeedbackCue.None"/>。</summary>
        public static CombatFeedbackCue Resolve(HitResultKind kind)
        {
            switch (kind)
            {
                case HitResultKind.Damage:
                    return new CombatFeedbackCue("VFX_Hit_Normal", "SE_Hit_Normal", 0.05f);
                case HitResultKind.Guard:
                    return new CombatFeedbackCue("VFX_Guard", "SE_Guard", 0.03f);
                case HitResultKind.JustGuard:
                    // JG はヒットストップで通常ガードと区別（強め）。仕様書 §10.2。
                    return new CombatFeedbackCue("VFX_JustGuard", "SE_JustGuard", 0.09f);
                case HitResultKind.Evade:
                    return new CombatFeedbackCue(string.Empty, "SE_Evade", 0f);
                default:
                    return CombatFeedbackCue.None; // Rejected 等
            }
        }
    }

    /// <summary>フィードバック配信イベント（命中結果＋解決済み Cue）。Phase2 P2-11。</summary>
    public readonly struct CombatFeedbackEvent
    {
        /// <summary>元の命中結果。</summary>
        public HitResult Result { get; }

        /// <summary>解決済みの仮 Cue。</summary>
        public CombatFeedbackCue Cue { get; }

        public CombatFeedbackEvent(in HitResult result, in CombatFeedbackCue cue)
        {
            Result = result;
            Cue = cue;
        }
    }

    /// <summary>フィードバック配信を購読する契約（VFX・SE・HitStop などの Presentation）。Phase2 P2-11。</summary>
    public interface ICombatFeedbackListener
    {
        /// <summary>フィードバックイベントを受け取る。</summary>
        void OnCombatFeedback(in CombatFeedbackEvent feedback);
    }

    /// <summary>
    /// フィードバックイベントの通知チャネル（Phase2 P2-11。<see cref="HitResultChannel"/> と同方針）。通知はスナップショット走査で行い、
    /// 通知中の購読変更で例外を出さない。static な万能マネージャは作らずインスタンス所有。
    /// </summary>
    public sealed class CombatFeedbackChannel
    {
        private readonly List<ICombatFeedbackListener> _listeners = new List<ICombatFeedbackListener>();

        /// <summary>購読者数（診断・テスト用）。</summary>
        public int ListenerCount => _listeners.Count;

        /// <summary>購読者を追加する（null・重複は無視）。</summary>
        public void AddListener(ICombatFeedbackListener listener)
        {
            if (listener == null || _listeners.Contains(listener))
            {
                return;
            }

            _listeners.Add(listener);
        }

        /// <summary>購読を解除する。</summary>
        public void RemoveListener(ICombatFeedbackListener listener)
        {
            if (listener != null)
            {
                _listeners.Remove(listener);
            }
        }

        /// <summary>イベントを全購読者へ配信する（スナップショット走査）。</summary>
        public void Publish(in CombatFeedbackEvent feedback)
        {
            if (_listeners.Count == 0)
            {
                return;
            }

            ICombatFeedbackListener[] snapshot = _listeners.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i].OnCombatFeedback(feedback);
            }
        }
    }
}
