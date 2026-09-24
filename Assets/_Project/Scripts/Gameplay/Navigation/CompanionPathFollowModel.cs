using Momotaro.Gameplay.Companion;
using UnityEngine;

namespace Momotaro.Gameplay.Navigation
{
    /// <summary>長距離追従で、いま何をすべきか（P5-05。仕様書 v1.1 §10.1）。</summary>
    public enum PathFollowDecision
    {
        /// <summary>経路は要らない。既存の直線追従に任せる（近い・遮られていない）。</summary>
        Direct = 0,

        /// <summary>経路の角へ移動する。</summary>
        MoveToCorner = 1,

        /// <summary>
        /// 使える経路がまだ無い。<b>動かずに待つ。</b>
        /// 再探索の回数が残っている間はここ。届かないと決めつけて壁へ突っ込ませない。
        /// </summary>
        Waiting = 2,

        /// <summary>経路で届かない。<b>ここから先は既存のワープ資格が判断する</b>（§10.1）。</summary>
        Failed = 3,
    }

    /// <summary>経路追従の設定（§10.1 の受入用初期値）。</summary>
    public readonly struct PathFollowSettings
    {
        /// <summary>
        /// 直線が通るときに、既存追従で足りるとみなす距離（§10.1 の 1 行目「直線で通れる近距離」）。
        ///
        /// <b>これ単独で経路を省く理由にはならない。</b> 薄い壁を挟んだ 1m は、開けた 10m より通れない。
        /// 距離だけで遮蔽を無視すると、壁の向こうの隊列位置へ直進し続けて張り付く。
        /// </summary>
        public float DirectDistance { get; }

        /// <summary>再探索の最短間隔（秒）。§10.1 の初期値は 0.5。</summary>
        public float RequeryInterval { get; }

        /// <summary>目標がこれ以上動いたら、間隔を待たずに再探索する。§10.1 の初期値は 1。</summary>
        public float TargetMoveThreshold { get; }

        /// <summary>角へ到達したとみなす距離。</summary>
        public float CornerArriveDistance { get; }

        /// <summary>停滞とみなすまでの秒数。§10.1 の初期値は 3。</summary>
        public float StallSeconds { get; }

        /// <summary>停滞したときに再探索してよい回数。§10.1 の初期値は 2。</summary>
        public int MaxRetries { get; }

        /// <summary>前進しているとみなす最小の距離変化（m）。</summary>
        public float ProgressEpsilon { get; }

        public PathFollowSettings(
            float directDistance, float requeryInterval, float targetMoveThreshold,
            float cornerArriveDistance, float stallSeconds, int maxRetries, float progressEpsilon)
        {
            DirectDistance = directDistance;
            RequeryInterval = requeryInterval;
            TargetMoveThreshold = targetMoveThreshold;
            CornerArriveDistance = cornerArriveDistance;
            StallSeconds = stallSeconds;
            MaxRetries = maxRetries;
            ProgressEpsilon = progressEpsilon;
        }

        /// <summary>§10.1 の受入用初期値。</summary>
        public static PathFollowSettings Default =>
            new PathFollowSettings(2.5f, 0.5f, 1f, 0.6f, 3f, 2, 0.02f);
    }

    /// <summary>1 Tick ぶんの入力。</summary>
    public readonly struct PathFollowInput
    {
        public PathFollowInput(Vector3 selfPosition, Vector3 targetPosition, bool straightLineClear)
        {
            SelfPosition = selfPosition;
            TargetPosition = targetPosition;
            StraightLineClear = straightLineClear;
        }

        /// <summary>仲間の現在位置。</summary>
        public Vector3 SelfPosition { get; }

        /// <summary>向かう先（隊列位置）。</summary>
        public Vector3 TargetPosition { get; }

        /// <summary>目標まで直線で通れるか（遮蔽判定の結果）。</summary>
        public bool StraightLineClear { get; }
    }

    /// <summary>
    /// 長距離追従の経路判断（P5-05。仕様書 v1.1 §10.1）。純粋 C# で、時間は <see cref="Tick"/> に外部注入する。
    ///
    /// <b>経路は「次の移動目標」を出すだけ。</b> 位置・速度・向きの実書込みは
    /// <c>CompanionMovementArbiter</c> → <c>CompanionMotor</c> のまま変えない。
    ///
    /// <b>停滞の判定はここ 1 か所に置く。</b> §10.1 は「新たな停止・停滞判定を既存 FollowModel と
    /// 二重に競合させない。単一の失敗判定の入力へ統合する」と決めている。
    /// 経路追従中は角へ向かって歩くので、<b>隊列位置までの距離は縮まらないことがある</b>。
    /// 既存 FollowModel の停滞判定をそのまま走らせると、迂回しているだけなのに
    /// 「近づけていない」と数えられてワープしてしまう。だから経路追従中は既存の停滞判定を止め、
    /// こちらが「経路でも届かない」と判断したときだけ、既存のワープ資格へ渡す。
    ///
    /// <b>再探索は 2 回まで。</b> それを超えたら <see cref="PathFollowDecision.Failed"/> にする。
    /// 無制限に探し直すと、届かない場所を延々と計算し続けて何も起きない。
    /// </summary>
    public sealed class CompanionPathFollowModel
    {
        private PathQueryResult _path = PathQueryResult.Pending();
        private Vector3 _queriedTarget;
        private float _sinceQuery;
        private float _stallSeconds;
        private float _previousDistance = float.MaxValue;
        private int _cornerIndex;
        private bool _hasPath;
        private bool _worldChanged;

        /// <summary>直近の判断。</summary>
        public PathFollowDecision Decision { get; private set; } = PathFollowDecision.Direct;

        /// <summary>次に向かう角（<see cref="PathFollowDecision.MoveToCorner"/> のときだけ意味を持つ）。</summary>
        public Vector3 NextCorner { get; private set; }

        /// <summary>直近の経路の状態（診断・テスト用）。</summary>
        public PathQueryStatus Status => _path.Status;

        /// <summary>経路を問い合わせた回数（診断・テスト用）。</summary>
        public int QueryCount { get; private set; }

        /// <summary>停滞で探し直した回数（診断・テスト用）。<b>設定の上限を超えてはいけない</b>。</summary>
        public int RetryCount { get; private set; }

        /// <summary>前進できないまま経過した秒数（診断・テスト用）。</summary>
        public float StallSeconds => _stallSeconds;

        /// <summary>経路追従中か（既存 FollowModel の停滞判定を止める条件）。</summary>
        public bool IsPathActive => Decision == PathFollowDecision.MoveToCorner;

        /// <summary>
        /// 直近の Direct が「直線が通る<b>近距離</b>」だったか（診断・テスト用）。
        /// 直線が通る遠距離も Direct にはなるので、§10.1 の 1 行目そのものを見分けるために持つ。
        /// </summary>
        public bool LastDirectWasNear { get; private set; }

        /// <summary>
        /// 世界の通行状態が変わったことを伝える（門の開通など。§10.1）。
        /// 次の Tick で、間隔を待たずに探し直す。
        /// </summary>
        public void NotifyWorldChanged()
        {
            _worldChanged = true;

            // 世界が変わったのだから、前の「届かなかった」は古い判断。探し直す余地を戻す。
            // これが無いと、門を開けても仲間が閉まっていた頃の結論のまま動かない。
            RetryCount = 0;
        }

        /// <summary>1 Tick 進めて判断を返す。</summary>
        public PathFollowDecision Tick(
            in PathFollowInput input, in PathFollowSettings settings, IPathProvider provider, float deltaTime)
        {
            float step = deltaTime < 0f ? 0f : deltaTime;
            _sinceQuery += step;

            float distance = FormationSlot.HorizontalDistance(input.SelfPosition, input.TargetPosition);

            // 直線で通れるなら経路は要らない（§10.1 の 1 行目「直線で通れる近距離は既存追従を使える」）。
            //
            // <b>「近い」だけでは足りない。</b> 以前は距離が近ければ遮蔽を見ずに直線追従へ倒していたが、
            // それだと薄い壁を挟んで 2.5m 以内に居るときも直進し続け、壁に張り付いたまま動かない。
            // §10.1 の 2 行目は「壁に遮られた追従は経路の Corner へ移動要求を出す」であって、
            // 距離による例外を置いていない（GPT レビュー R4 の指摘 3）。
            //
            // 距離は「通れるときに、どこまでを既存追従で済ませてよいか」の目安として残す。
            if (input.StraightLineClear)
            {
                ResetPath();
                Decision = PathFollowDecision.Direct;
                LastDirectWasNear = distance <= settings.DirectDistance;
                return Decision;
            }

            // 供給元が無い＝経路追従を止めて診断する（§10.1。勝手に直線で突っ込まない）。
            if (provider == null)
            {
                _path = PathQueryResult.Invalid();
                Decision = PathFollowDecision.Failed;
                return Decision;
            }

            bool queried = false;
            if (ShouldQuery(input, settings))
            {
                Query(input, provider);
                queried = true;
            }

            if (!_hasPath || !_path.IsUsable)
            {
                // Partial も Invalid もここへ来る（Partial を成功扱いにしない。§10.1）。
                return NoUsablePath(settings, queried);
            }

            AdvanceCorner(input, settings);

            if (_cornerIndex >= _path.CornerCount)
            {
                // 角を使い切った＝目的地に着いている。あとは既存の直線追従でよい。
                ResetPath();
                Decision = PathFollowDecision.Direct;
                return Decision;
            }

            // 停滞は<b>目的地までの距離</b>で測る。角までの距離で測ると、
            // 経路を引き直すたびに基準が入れ替わって計測が 0 へ戻り、
            // 再探索の間隔（0.5 秒）より長い停滞（3 秒）を一度も検出できない（実際に踏んだ）。
            // 目的地までの距離は経路を引き直しても変わらないので、素直に積める。
            float toTarget = FormationSlot.HorizontalDistance(input.SelfPosition, input.TargetPosition);
            _stallSeconds = _previousDistance - toTarget < settings.ProgressEpsilon ? _stallSeconds + step : 0f;
            _previousDistance = toTarget;

            if (settings.StallSeconds > 0f && _stallSeconds >= settings.StallSeconds)
            {
                // 角へ近づけない。探し直す余地があるなら、次の Tick で探し直す。
                _stallSeconds = 0f;
                if (RetryCount >= settings.MaxRetries)
                {
                    Decision = PathFollowDecision.Failed;
                    return Decision;
                }

                RetryCount++;
                _sinceQuery = float.MaxValue; // 間隔を待たずに探し直す。
                Decision = PathFollowDecision.Waiting;
                return Decision;
            }

            NextCorner = _path.Corners[_cornerIndex];
            Decision = PathFollowDecision.MoveToCorner;
            return Decision;
        }

        /// <summary>判断を初期化する（加入・退場・Scene 離脱・探索や戦闘から戻ったとき）。</summary>
        public void Reset()
        {
            ResetPath();
            Decision = PathFollowDecision.Direct;
            RetryCount = 0;
            _worldChanged = false;
        }

        private bool ShouldQuery(in PathFollowInput input, in PathFollowSettings settings)
        {
            // 門の開通など。間隔を待たない（§10.1）。ここは上限より先に見る：
            // 世界が変わったなら、前に届かなかったことは理由にならない。
            if (_worldChanged)
            {
                return true;
            }

            // 上限まで探して届かなかった。世界が変わるまでは探し直さない
            // （届かない場所を毎フレーム計算し続けない）。
            if (RetryCount >= settings.MaxRetries && Decision == PathFollowDecision.Failed)
            {
                return false;
            }

            if (!_hasPath || _path.Status == PathQueryStatus.Pending)
            {
                return true;
            }

            // 目標が大きく動いた。間隔を待たない（§10.1）。
            if (FormationSlot.HorizontalDistance(_queriedTarget, input.TargetPosition) >= settings.TargetMoveThreshold)
            {
                return true;
            }

            return _sinceQuery >= settings.RequeryInterval;
        }

        private void Query(in PathFollowInput input, IPathProvider provider)
        {
            _path = provider.Query(input.SelfPosition, input.TargetPosition);
            _queriedTarget = input.TargetPosition;
            _sinceQuery = 0f;
            _cornerIndex = 0;
            _hasPath = true;
            _worldChanged = false;
            QueryCount++;
        }

        private void AdvanceCorner(in PathFollowInput input, in PathFollowSettings settings)
        {
            // 供給元は始点を含めて返すことがある。届いた角は読み飛ばす。
            while (_cornerIndex < _path.CornerCount
                   && FormationSlot.HorizontalDistance(input.SelfPosition, _path.Corners[_cornerIndex])
                      <= settings.CornerArriveDistance)
            {
                _cornerIndex++;
            }
        }

        /// <summary>
        /// 使える経路が返らなかった（Partial・Invalid）。
        ///
        /// <b>1 Tick に 1 回しか探さない。</b> 同じフレームで上限まで探し直しても、
        /// 世界は何も変わっていないので同じ答えが返るだけ。間隔（§10.1 の 0.5 秒）を挟んで
        /// 探し直し、それでも駄目なら失敗にする。失敗の先は既存のワープ資格が判断する。
        /// </summary>
        private PathFollowDecision NoUsablePath(in PathFollowSettings settings, bool queried)
        {
            // 最初の 1 回は「再探索」ではない。2 回目以降の空振りを数える。
            if (queried && QueryCount > 1)
            {
                RetryCount++;
            }

            Decision = RetryCount >= settings.MaxRetries
                ? PathFollowDecision.Failed
                : PathFollowDecision.Waiting;
            return Decision;
        }

        private void ResetPath()
        {
            _path = PathQueryResult.Pending();
            _hasPath = false;
            _cornerIndex = 0;
            _stallSeconds = 0f;
            _previousDistance = float.MaxValue;
            _sinceQuery = float.MaxValue; // 次に必要になったら即座に問い合わせる。
        }
    }
}
