using System.Collections.Generic;

namespace Momotaro.Gameplay.Enemy.Threat
{
    /// <summary>
    /// 敵のヘイト（脅威）テーブル（Phase3 P3-06。§7）。対象ごとに「基礎ヘイト＋獲得ヘイト」を保持し、行動加算（§7.1）・
    /// 減衰（§7.2）・再評価と対象選択（§7.2）を決定的に行う純粋クラス。時間は <paramref name="dt"/> 注入で進み、Time／物理／
    /// 乱数・レジストリに依存しない（EditMode で再現可能）。MonoBehaviour ドライバ（<see cref="EnemyThreatTracker"/>）が観測可能な
    /// 戦闘結果を加算へ変換し、候補（在圏の敵対 <see cref="IThreatTarget"/>）を毎フレーム渡す。範囲外・離脱は候補から外れることで
    /// 「無効」となり即時切替に至る（§7.2）。基礎ヘイトは減衰の対象外で、有効対象の下限として維持される。
    /// </summary>
    public sealed class EnemyThreatTable
    {
        /// <summary>対象なしを表す ActorId。</summary>
        public const int NoTarget = 0;

        private sealed class Entry
        {
            public float Acquired;
            public float TimeSinceGain;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private ThreatSettings _settings;
        private int _currentTargetId = NoTarget;
        private float _reevaluateTimer;

        /// <summary>設定を指定して生成する。</summary>
        public EnemyThreatTable(ThreatSettings settings)
        {
            _settings = settings;
        }

        /// <summary>現在の設定を差し替える（Inspector 変更の反映用）。蓄積・選択状態は保持する。</summary>
        public void Configure(ThreatSettings settings) => _settings = settings;

        /// <summary>現在選択中の対象 ActorId（未選択は <see cref="NoTarget"/>）。読み取り専用（Debug／Phase 4）。</summary>
        public int CurrentTargetId => _currentTargetId;

        /// <summary>次回再評価までの残り秒（Debug）。</summary>
        public float TimeToReevaluate
        {
            get
            {
                float r = _settings.ReevaluateInterval - _reevaluateTimer;
                return r > 0f ? r : 0f;
            }
        }

        /// <summary>脅威を追跡している対象数（テスト用）。</summary>
        public int TrackedCount => _entries.Count;

        /// <summary>
        /// 行動由来のヘイトを加算する（§7.1）。加算量＝ <c>WeightFor(source) × amount × 対象の獲得倍率</c>。
        /// HP／体幹ダメージは <paramref name="amount"/> に実ダメージ量を渡し、ひるみ／JG は <paramref name="amount"/>=1（既定）。
        /// 加算で減衰待ち時間をリセットする（§7.2「最後の獲得から 3 秒後に減衰」）。
        /// </summary>
        public void AddThreat(IThreatTarget target, ThreatSource source, float amount = 1f)
        {
            if (target == null)
            {
                return;
            }

            float raw = _settings.WeightFor(source) * amount;
            AddAcquired(target.ActorId, raw * target.AcquiredThreatMultiplier);
        }

        /// <summary>
        /// 獲得ヘイトを直接加算する（Phase 4 の型付き Threat Event 等の汎用入口）。対象倍率は呼び出し側で適用済みとする。
        /// 負値・0 は無視する。
        /// </summary>
        public void AddAcquired(int actorId, float acquiredAmount)
        {
            if (actorId == NoTarget || acquiredAmount <= 0f)
            {
                return;
            }

            if (!_entries.TryGetValue(actorId, out Entry e))
            {
                e = new Entry();
                _entries.Add(actorId, e);
            }

            e.Acquired += acquiredAmount;
            e.TimeSinceGain = 0f;
        }

        /// <summary>対象の獲得ヘイト（基礎ヘイトを含まない。テスト／Debug 用）。</summary>
        public float GetAcquired(int actorId)
        {
            return _entries.TryGetValue(actorId, out Entry e) ? e.Acquired : 0f;
        }

        /// <summary>
        /// 対象の現在脅威＝ <c>基礎ヘイト＋獲得ヘイト</c>。無効（非活動／Down）なら 0（§7.2）。
        /// </summary>
        public float GetThreat(IThreatTarget target)
        {
            if (target == null || !IsEligible(target))
            {
                return 0f;
            }

            return target.BaseThreat + GetAcquired(target.ActorId);
        }

        /// <summary>脅威対象として有効か（有効かつ非ダウン）。範囲外・離脱は候補から外れることで無効化する。</summary>
        public static bool IsEligible(IThreatTarget target)
        {
            return target != null && target.IsActive && !target.IsDown;
        }

        /// <summary>
        /// 減衰と再評価タイマを 1 フレーム進め、候補から現在対象を更新して選択中 ActorId を返す（§7.2）。
        /// <paramref name="candidates"/> は在圏の敵対対象（範囲外・離脱は含めない）。<paramref name="attackLocked"/> が true の間
        /// （近接攻撃中）は嗜好による切替（25% 閾値）を行わないが、現在対象が無効化した場合の即時切替は行う。
        /// </summary>
        public int UpdateSelection(IReadOnlyList<IThreatTarget> candidates, float dt, bool attackLocked)
        {
            Decay(dt);
            _reevaluateTimer += dt;

            IThreatTarget current = FindCandidate(candidates, _currentTargetId);
            bool currentInvalid = current == null || !IsEligible(current);

            if (currentInvalid)
            {
                // 対象の Down／離脱／範囲外は即時切替（攻撃中でも、消えた対象は攻撃継続できないため選択を更新する。§7.2）。
                _currentTargetId = SelectBest(candidates);
                _reevaluateTimer = 0f;
                return _currentTargetId;
            }

            if (_reevaluateTimer < _settings.ReevaluateInterval)
            {
                return _currentTargetId; // まだ再評価時刻でない。
            }

            _reevaluateTimer = 0f;

            if (attackLocked)
            {
                return _currentTargetId; // 近接攻撃中は嗜好切替を保留（終了後の再評価で判断。§7.2）。
            }

            int bestId = SelectBest(candidates);
            if (bestId != _currentTargetId && bestId != NoTarget)
            {
                IThreatTarget best = FindCandidate(candidates, bestId);
                float bestThreat = GetThreat(best);
                float currentThreat = GetThreat(current);

                // 新対象が現対象より 25% 以上高い場合だけ切替（§7.2）。同点・僅差は現対象維持（揺れ防止）。
                if (bestThreat >= currentThreat * _settings.SwitchThresholdRatio)
                {
                    _currentTargetId = bestId;
                }
            }

            return _currentTargetId;
        }

        /// <summary>全対象の脅威と選択・再評価タイマを初期化する（戦闘終了／Return 完了。§7.2）。</summary>
        public void Reset()
        {
            _entries.Clear();
            _currentTargetId = NoTarget;
            _reevaluateTimer = 0f;
        }

        private void Decay(float dt)
        {
            if (dt <= 0f)
            {
                return;
            }

            float delay = _settings.DecayDelaySeconds;
            float rate = _settings.DecayRatePerSecond;
            foreach (KeyValuePair<int, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                e.TimeSinceGain += dt;
                if (e.TimeSinceGain < delay || e.Acquired <= 0f)
                {
                    continue;
                }

                // 減衰開始後、獲得ヘイトを毎秒 rate だけ減らす（基礎ヘイトは維持）。dt=1 の 1 ステップで丁度 rate（20%）減。
                e.Acquired -= e.Acquired * rate * dt;
                if (e.Acquired < 0f)
                {
                    e.Acquired = 0f;
                }
            }
        }

        /// <summary>候補中で最も脅威の高い有効対象を返す。同点は ActorId 昇順で固定（§7.2「同点規則を固定」）。</summary>
        private int SelectBest(IReadOnlyList<IThreatTarget> candidates)
        {
            int bestId = NoTarget;
            float bestThreat = 0f;
            if (candidates == null)
            {
                return bestId;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                IThreatTarget t = candidates[i];
                if (t == null || !IsEligible(t))
                {
                    continue;
                }

                float threat = GetThreat(t);
                if (bestId == NoTarget
                    || threat > bestThreat
                    || (threat == bestThreat && t.ActorId < bestId))
                {
                    bestThreat = threat;
                    bestId = t.ActorId;
                }
            }

            return bestId;
        }

        private static IThreatTarget FindCandidate(IReadOnlyList<IThreatTarget> candidates, int actorId)
        {
            if (candidates == null || actorId == NoTarget)
            {
                return null;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                IThreatTarget t = candidates[i];
                if (t != null && t.ActorId == actorId)
                {
                    return t;
                }
            }

            return null;
        }
    }
}
