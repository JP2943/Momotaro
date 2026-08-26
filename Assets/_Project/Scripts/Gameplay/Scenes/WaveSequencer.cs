using System;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 連続ウェーブ進行の純粋な時間・段階モデル（Phase3.5 P3.5-07。仕様書 §8.2 / §8.3）。MonoBehaviour 非依存にし、
    /// 「全滅検出 → 休止入り(1.0s) → Intermission(3.0s) → 次 Wave」の時間境界と最終 Wave 完了を決定的に検証できる
    /// （<see cref="CombatSessionMachine"/> と同方針の純粋クラス）。実際の敵生成・回復・Cleanup・HUD 通知・Session 状態遷移は
    /// <see cref="WaveRunner"/> がイベント購読で行う。
    ///
    /// 段階：NotStarted →(<see cref="Begin"/>)→ Fighting →(全滅=<see cref="NotifyWaveCleared"/>)→ PostClear(1.0s) →
    ///   ・次 Wave あり：Intermission(3.0s) →(次 Wave)→ Fighting …
    ///   ・最終 Wave：Complete（<see cref="AllWavesCleared"/> を 1 回発火。勝利遷移・パネルは P3.5-08）。
    ///
    /// 全滅通知は Fighting 段階でのみ受理し、PostClear/Intermission/Complete 中の遅延通知や別 Wave 由来の通知は無視する
    /// （§「別 Wave の敵イベントが後続 Wave を進めない」）。Pause は呼び出し側が Game Time の <see cref="Tick"/> を止めることで担保する。
    /// </summary>
    public sealed class WaveSequencer
    {
        /// <summary>ウェーブ進行の段階。</summary>
        public enum Phase
        {
            /// <summary>未開始（Begin 前）。</summary>
            NotStarted,
            /// <summary>Wave 戦闘中（敵生存）。</summary>
            Fighting,
            /// <summary>全滅後・休止入りまでの待機（1.0s）。</summary>
            PostClear,
            /// <summary>Wave 間休止（3.0s）。</summary>
            Intermission,
            /// <summary>全 Wave 完了（終端）。</summary>
            Complete,
        }

        private readonly int _waveCount;
        private readonly float _postClearDelay;
        private readonly float _intermissionDelay;

        private Phase _phase = Phase.NotStarted;
        private int _index = -1; // 現在 Wave の 0 始まり添字。未開始は -1。
        private float _timer;

        /// <summary>Wave 総数と時間境界（既定 1.0s / 3.0s。§8.3）を指定して生成する。</summary>
        public WaveSequencer(int waveCount, float postClearDelay = 1.0f, float intermissionDelay = 3.0f)
        {
            _waveCount = waveCount < 0 ? 0 : waveCount;
            _postClearDelay = postClearDelay < 0f ? 0f : postClearDelay;
            _intermissionDelay = intermissionDelay < 0f ? 0f : intermissionDelay;
        }

        /// <summary>現在の段階。</summary>
        public Phase Current => _phase;

        /// <summary>Wave 総数。</summary>
        public int WaveCount => _waveCount;

        /// <summary>現在 Wave 番号（1 始まり）。未開始は 0。</summary>
        public int CurrentWaveNumber => _index < 0 ? 0 : _index + 1;

        /// <summary>全 Wave 完了済みか。</summary>
        public bool IsComplete => _phase == Phase.Complete;

        /// <summary>Wave を engage した瞬間に発火（1 始まり番号）。購読側が敵生成＋Session.StartWave＋HUD 通知を行う。</summary>
        public event Action<int> WaveEngaged;

        /// <summary>Intermission へ入った瞬間に発火。購読側が Session.ToIntermission＋残留 Cleanup＋Player 全回復・中立化を行う。</summary>
        public event Action IntermissionEntered;

        /// <summary>最終 Wave 完了時に一度だけ発火。勝利パネル・入力ロックは P3.5-08 が付与する。</summary>
        public event Action AllWavesCleared;

        /// <summary>最初の Wave を開始する（NotStarted → Fighting）。二重呼び出しは無視。WaveCount=0 なら即 Complete。</summary>
        public void Begin()
        {
            if (_phase != Phase.NotStarted)
            {
                return;
            }

            if (_waveCount <= 0)
            {
                _phase = Phase.Complete;
                AllWavesCleared?.Invoke();
                return;
            }

            _index = 0;
            _timer = 0f;
            _phase = Phase.Fighting;
            WaveEngaged?.Invoke(CurrentWaveNumber);
        }

        /// <summary>現在 Wave の敵が全滅したことを通知する（Fighting 中のみ受理）。他段階・別 Wave 由来の遅延通知は無視する。</summary>
        public void NotifyWaveCleared()
        {
            if (_phase != Phase.Fighting)
            {
                return;
            }

            _phase = Phase.PostClear;
            _timer = 0f;
        }

        /// <summary>時間を進める（Game Time。Pause 中は呼び出し側が停止する）。0 以下の dt は無視する。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            switch (_phase)
            {
                case Phase.PostClear:
                    _timer += deltaTime;
                    if (_timer >= _postClearDelay)
                    {
                        if (_index + 1 >= _waveCount)
                        {
                            _phase = Phase.Complete;
                            AllWavesCleared?.Invoke();
                        }
                        else
                        {
                            _phase = Phase.Intermission;
                            _timer = 0f;
                            IntermissionEntered?.Invoke();
                        }
                    }

                    break;

                case Phase.Intermission:
                    _timer += deltaTime;
                    if (_timer >= _intermissionDelay)
                    {
                        _index++;
                        _timer = 0f;
                        _phase = Phase.Fighting;
                        WaveEngaged?.Invoke(CurrentWaveNumber);
                    }

                    break;
            }
        }
    }
}
