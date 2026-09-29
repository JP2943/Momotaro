using System.Collections.Generic;
using Momotaro.Core.Identification;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 距離で選んだ先読み候補（§5 の 1 行目）。<b>座標も Scene も持ち込まない</b>——
    /// 測るのは呼び出し側（Infrastructure）の仕事で、ここは「どれを望むか」だけを決める。
    /// </summary>
    public readonly struct AreaPreloadCandidate
    {
        /// <summary>接続 ID。<b>同距離のときの順序</b>はこれの Ordinal 比較で決める（§5）。</summary>
        public StableId ConnectionId { get; }

        /// <summary>先読みする Area。</summary>
        public StableId ToAreaId { get; }

        /// <summary>その Area の Scene パス。</summary>
        public string ScenePath { get; }

        /// <summary>主人公から出入口までの距離。</summary>
        public float Distance { get; }

        /// <summary>この接続の先読み開始距離（Data の <c>PreloadDistance</c>。既定 6）。</summary>
        public float PreloadDistance { get; }

        /// <summary>作る。</summary>
        public AreaPreloadCandidate(
            StableId connectionId, StableId toAreaId, string scenePath,
            float distance, float preloadDistance)
        {
            ConnectionId = connectionId;
            ToAreaId = toAreaId;
            ScenePath = scenePath;
            Distance = distance;
            PreloadDistance = preloadDistance;
        }

        /// <summary>望める候補か（行き先と Scene が揃っているか）。</summary>
        public bool IsValid =>
            ConnectionId.IsValid && ToAreaId.IsValid && !string.IsNullOrEmpty(ScenePath);
    }

    /// <summary>
    /// <b>距離による先読みの選定</b>（P5.5 仕様書 §5 の 1〜2 行目。工程 P55-08b）。
    ///
    /// §5 はこう定める。
    /// <list type="bullet">
    /// <item><description>出入口の 6 units 以内で、活動中 Area の有効な Slide 接続を候補にする。
    /// <b>距離 → ConnectionId の Ordinal 順</b>で一つを選ぶ。</description></item>
    /// <item><description>一度読み始めた先は、<b>同じ Area で活動している間は境界から離れても保持する</b>。
    /// 距離境界でロード／unload を反復しない。</description></item>
    /// <item><description>別候補への切替は<b>距離差 2 units 以上が 0.5 秒継続</b>した場合に限り、
    /// <b>旧先読みの終端・解放後</b>に行う。</description></item>
    /// </list>
    ///
    /// <b>ここは判断だけを持つ。</b> 座標の測定・Scene の読込・活動中 Area の解決は呼び出し側にある。
    /// 分けてあるのは、ヒステリシス（2 units・0.5 秒）が<b>時間の絡む判断</b>で、
    /// 実 Scene を動かさずに刻みを与えて検査したいから——
    /// 実遷移でしか確かめられない形にすると、境界を往復する検査が Scene ロードの束になる。
    ///
    /// <b>「保持」は「望みを言い続ける」こと。</b> 先読み自体の重複排除は
    /// <see cref="AreaPreloader.Request"/> が持つ（同じ先なら読み直さない）ので、
    /// ここは毎フレーム同じ答えを返すだけでよい。
    /// </summary>
    public sealed class AreaProximityPreloadPlanner
    {
        /// <summary>切替に必要な距離差（units。§5）。</summary>
        public const float SwitchMarginUnits = 2f;

        /// <summary>切替に必要な継続時間（秒。§5）。</summary>
        public const float SwitchHoldSeconds = 0.5f;

        private StableId _activeArea;
        private StableId _heldConnection;
        private StableId _heldArea;
        private string _heldPath;
        private StableId _pendingConnection;
        private float _pendingSeconds;
        private StableId _suppressedConnection;

        /// <summary>いま保持している接続（無ければ無効）。</summary>
        public StableId HeldConnectionId => _heldConnection;

        /// <summary>いま保持している行き先（無ければ無効）。</summary>
        public StableId HeldAreaId => _heldArea;

        /// <summary>切替待ちの接続（無ければ無効。診断・テスト用）。</summary>
        public StableId PendingConnectionId => _pendingConnection;

        /// <summary>切替条件が続いている秒数（診断・テスト用）。</summary>
        public float PendingSeconds => _pendingSeconds;

        /// <summary>
        /// <b>自動では選び直さない接続</b>（診断・テスト用。工程 P55-08c。GPT 再修正②）。
        ///
        /// 遷移が先読みを取り下げた（Rollback・時間切れ・Single 読込前の受け渡し）とき、
        /// <b>その場に立っているだけで同じ先を言い直してはいけない</b>。
        /// 言い直すと、遅れて着いた Scene が「また必要な先読み」になって撤去されない
        /// （§8 の 5 行目が永久に起きない）。
        ///
        /// 解けるのは<b>新しい遷移操作</b>のときだけ——§5 の
        /// 「自動で毎フレーム再試行しない。次の新しい遷移操作で一度だけ再試行できる」と同じ規律である。
        /// 別の Area へ移ったときも解ける（前の Area の話ではなくなる）。
        /// </summary>
        public StableId SuppressedConnectionId => _suppressedConnection;

        /// <summary>自動再選択を抑止した回数（診断・テスト用）。</summary>
        public int SuppressedCount { get; private set; }

        /// <summary>新しく先読みを始めた回数（診断・テスト用）。</summary>
        public int StartedCount { get; private set; }

        /// <summary>別候補へ切り替えた回数（診断・テスト用）。</summary>
        public int SwitchedCount { get; private set; }

        /// <summary>距離が離れても保持し続けたフレーム数（診断・テスト用）。</summary>
        public int HeldOutOfRangeCount { get; private set; }

        /// <summary>旧先読みが終端していないので切替を見送った回数（診断・テスト用）。</summary>
        public int SwitchDeferredCount { get; private set; }

        /// <summary>活動中 Area が変わって保持を捨てた回数（診断・テスト用）。</summary>
        public int ResetCount { get; private set; }

        /// <summary>
        /// 1 フレーム進め、<b>望む先</b>を答える。
        ///
        /// <paramref name="canSwitch"/> は<b>旧先読みが終端・解放済みか</b>。
        /// false のあいだ切替の継続時間は数え続けるが、答えは保持中の先のままにする——
        /// 「条件が揃ったのに終端待ちで捨てられる」を避ける。
        /// </summary>
        /// <param name="activeAreaId">いま活動している Area。変われば保持を捨てる。</param>
        /// <param name="candidates">活動中 Area の有効な Slide 接続（距離つき）。</param>
        /// <param name="unscaledDeltaTime">経過時間（unscaled。先読みは Gameplay 時計を見ない）。</param>
        /// <param name="canSwitch">旧先読みが終端・解放済みか。</param>
        /// <param name="areaId">望む Area（返り値が false なら無効）。</param>
        /// <param name="scenePath">望む Scene（返り値が false なら null）。</param>
        /// <returns>望む先があるか。</returns>
        public bool Tick(
            StableId activeAreaId,
            IReadOnlyList<AreaPreloadCandidate> candidates,
            float unscaledDeltaTime,
            bool canSwitch,
            out StableId areaId,
            out string scenePath)
        {
            // <b>Area が変われば保持は無効になる。</b> §5 の「同じ Area で活動している間は保持」の
            // 裏返しで、別の Area へ移ったら前の Area の出入口からの距離には意味が無い。
            if (!_activeArea.Equals(activeAreaId))
            {
                if (_heldConnection.IsValid)
                {
                    ResetCount++;
                }

                // <b>抑止を捨てるのは「別の Area へ移った」ときだけ</b>（工程 P55-08d）。
                //
                // 初回の <see cref="Tick"/> は<b>移動ではない</b>。ここで捨てると、
                // まだ一度も回っていない Area で失敗した遷移の抑止が<b>直後に消える</b>——
                // 抑止は遷移の失敗から名指しで入るので、選定より先に来ることがある。
                if (_activeArea.IsValid)
                {
                    _suppressedConnection = default;
                }

                _activeArea = activeAreaId;
                _heldConnection = default;
                _heldArea = default;
                _heldPath = null;
                ClearPending();
            }

            bool hasBest = TryPickBest(candidates, out AreaPreloadCandidate best);
            bool hasHeld = TryFindHeld(candidates, out AreaPreloadCandidate held);

            if (!_heldConnection.IsValid)
            {
                // まだ何も読んでいない。範囲に入った候補だけが <c>TryPickBest</c> を通っている。
                if (hasBest)
                {
                    Hold(best);
                    StartedCount++;
                    areaId = _heldArea;
                    scenePath = _heldPath;
                    return true;
                }

                ClearPending();
                areaId = default;
                scenePath = null;
                return false;
            }

            // ---- 保持している ----
            //
            // <b>距離で捨てない。</b> 境界から離れても保持する（§5 の 2 行目）。
            // 捨てると、行ったり来たりするだけでロードと unload を反復する。
            if (hasHeld && held.Distance > held.PreloadDistance)
            {
                HeldOutOfRangeCount++;
            }

            if (hasBest && !best.ConnectionId.Equals(_heldConnection))
            {
                float heldDistance = hasHeld ? held.Distance : float.PositiveInfinity;
                bool clearlyCloser = heldDistance - best.Distance >= SwitchMarginUnits;

                if (clearlyCloser)
                {
                    if (!_pendingConnection.Equals(best.ConnectionId))
                    {
                        _pendingConnection = best.ConnectionId;
                        _pendingSeconds = 0f;
                    }

                    _pendingSeconds += unscaledDeltaTime > 0f ? unscaledDeltaTime : 0f;

                    if (_pendingSeconds >= SwitchHoldSeconds)
                    {
                        // <b>旧先読みの終端・解放を待つ</b>（§5 の 3 行目）。
                        // 終端していない操作の上に次の読込を重ねない。
                        if (!canSwitch)
                        {
                            SwitchDeferredCount++;
                        }
                        else
                        {
                            Hold(best);
                            SwitchedCount++;
                            ClearPending();
                        }
                    }
                }
                else
                {
                    ClearPending();
                }
            }
            else
            {
                ClearPending();
            }

            areaId = _heldArea;
            scenePath = _heldPath;
            return true;
        }

        /// <summary>保持を捨てる（遷移が引き取ったとき）。抑止はしない。</summary>
        public void Forget()
        {
            _heldConnection = default;
            _heldArea = default;
            _heldPath = null;
            ClearPending();
        }

        /// <summary>
        /// <b>その接続を自動では選び直さない</b>（工程 P55-08d。GPT 再修正の残件）。
        ///
        /// <b>抑止する相手は呼び出し側が名指しする。</b> 以前は「いま保持している接続」から
        /// 推し測っていたが、<b>保持していない場面がある</b>——遷移が走っている間は
        /// <see cref="Tick"/> が回らないので、手動の再試行から入った遷移では保持が空のままである。
        /// そこで失敗すると誰も抑止されず、次の <see cref="Tick"/> が同じ接続を選び直した。
        ///
        /// <see cref="Forget"/> だけでは足りない。条件（距離）はまだ揃っているので、
        /// <b>その場に立っているだけで次のフレームに言い直す</b>。
        /// </summary>
        public void Suppress(StableId connectionId)
        {
            if (!connectionId.IsValid)
            {
                return;
            }

            _suppressedConnection = connectionId;
            SuppressedCount++;

            if (_heldConnection.Equals(connectionId))
            {
                Forget();
            }
        }

        /// <summary>
        /// いま保持している接続を抑止する（呼び出し側が接続を知らない経路のための保険）。
        ///
        /// <b>こちらだけに頼らない。</b> 保持は遷移中に更新されないので、
        /// 失敗した遷移の接続は <see cref="Suppress"/> で名指しする。
        /// </summary>
        public void SuppressHeld()
        {
            if (_heldConnection.IsValid)
            {
                Suppress(_heldConnection);
            }
        }

        /// <summary>
        /// 抑止を解く。<b>新しい遷移操作だけが解ける</b>（§5 の「次の新しい遷移操作で一度だけ」）。
        /// </summary>
        public void ClearSuppression()
        {
            _suppressedConnection = default;
        }

        private void Hold(in AreaPreloadCandidate candidate)
        {
            _heldConnection = candidate.ConnectionId;
            _heldArea = candidate.ToAreaId;
            _heldPath = candidate.ScenePath;
        }

        private void ClearPending()
        {
            _pendingConnection = default;
            _pendingSeconds = 0f;
        }

        private bool TryFindHeld(
            IReadOnlyList<AreaPreloadCandidate> candidates, out AreaPreloadCandidate held)
        {
            held = default;
            if (candidates == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].ConnectionId.Equals(_heldConnection))
                {
                    held = candidates[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// <b>距離 → ConnectionId の Ordinal 順</b>で一つ選ぶ（§5）。
        ///
        /// 同距離のときに順序を決めておかないと、フレームごとに別の候補を選びうる——
        /// 候補が一つしかない P5.5 実試遊では現れないが、増やした瞬間に
        /// <b>毎フレーム読み直す</b>形になって気付く。
        /// </summary>
        private bool TryPickBest(
            IReadOnlyList<AreaPreloadCandidate> candidates, out AreaPreloadCandidate best)
        {
            best = default;
            bool found = false;
            if (candidates == null)
            {
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                AreaPreloadCandidate c = candidates[i];
                if (!c.IsValid)
                {
                    continue;
                }

                // <b>開始距離は候補ごとに違う。</b> 先に最寄りを選んでから 1 件だけ距離を見ると、
                // 「距離 3・開始距離 2」の<b>まだ範囲外の候補</b>が
                // 「距離 4・開始距離 6」の有効な候補を遮る（GPT 再修正の指摘）。
                // 絞ってから順位付けする。
                if (c.Distance > c.PreloadDistance)
                {
                    continue;
                }

                // 取り下げられた先は、新しい遷移操作まで自動では選び直さない（§5）。
                if (_suppressedConnection.IsValid && c.ConnectionId.Equals(_suppressedConnection))
                {
                    continue;
                }

                if (!found)
                {
                    best = c;
                    found = true;
                    continue;
                }

                if (c.Distance < best.Distance)
                {
                    best = c;
                    continue;
                }

                if (c.Distance <= best.Distance
                    && string.CompareOrdinal(c.ConnectionId.Value, best.ConnectionId.Value) < 0)
                {
                    best = c;
                }
            }

            return found;
        }
    }
}
