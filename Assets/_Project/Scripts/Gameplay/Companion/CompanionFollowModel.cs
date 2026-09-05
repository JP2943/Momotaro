using UnityEngine;

namespace Momotaro.Gameplay.Companion
{
    /// <summary>
    /// 追従・隊列・ワープの判断（P4-02）。純粋 C# で、時間は <see cref="Tick"/> に外部注入する（EditMode で決定的に検証できる）。
    /// Transform も Physics も触らず、「隊列位置はどこか」「移動すべきか・止まるべきか・ワープすべきか」だけを返す。
    ///
    /// ワープは 2 つの理由で要求する。
    /// <list type="number">
    /// <item><description><b>距離超過</b>：隊列位置から <see cref="CompanionFollowSettings.WarpDistance"/> 以上離れた
    /// （Scene 遷移・エリア移動・置き去り）。</description></item>
    /// <item><description><b>経路失敗</b>：移動しているのに隊列位置へ近づけない状態が
    /// <see cref="CompanionFollowSettings.StuckSeconds"/> 続いた（壁・段差・地形の谷に嵌まった）。</description></item>
    /// </list>
    ///
    /// 停止・再開は別のしきい値で判定し（<see cref="CompanionFollowSettings.StopDistance"/> ／
    /// <see cref="CompanionFollowSettings.ResumeDistance"/>）、境目での往復を防ぐ。
    /// ワープ要求は「要求」であり、実行するかは呼び出し側（Motor）が決める。実行されなければ次 Tick でも要求し続ける。
    /// </summary>
    public sealed class CompanionFollowModel
    {
        private float _previousDistance = float.MaxValue;

        /// <summary>直近の判断。</summary>
        public CompanionFollowDecision Decision { get; private set; } = CompanionFollowDecision.Hold;

        /// <summary>直近に算出した隊列位置（World）。</summary>
        public Vector3 SlotPosition { get; private set; }

        /// <summary>隊列位置までの水平距離（m）。</summary>
        public float DistanceToSlot { get; private set; }

        /// <summary>近づけないまま経過した秒数（経路失敗の判定用。テスト・診断）。</summary>
        public float StuckSeconds { get; private set; }

        /// <summary>これまでに発したワープ要求の回数（テスト・診断）。</summary>
        public int WarpRequests { get; private set; }

        /// <summary>1 Tick 進めて判断を返す。<paramref name="deltaTime"/> の負値は 0 として扱う。</summary>
        public CompanionFollowDecision Tick(in CompanionFollowInput input, in CompanionFollowSettings settings,
            float deltaTime)
        {
            SlotPosition = FormationSlot.Resolve(input.LeaderPosition, input.LeaderForward, input.SlotIndex, settings.Spacing);
            DistanceToSlot = FormationSlot.HorizontalDistance(input.SelfPosition, SlotPosition);

            // 距離超過：移動での復帰をあきらめる（Scene 遷移・置き去り）。
            if (settings.WarpDistance > 0f && DistanceToSlot >= settings.WarpDistance)
            {
                return RequestWarp();
            }

            // 経路失敗：移動中に限り、近づけていない時間を積む。停止中は積まない（止まっているのは正常）。
            if (Decision == CompanionFollowDecision.Move)
            {
                float step = deltaTime < 0f ? 0f : deltaTime;
                float progress = _previousDistance - DistanceToSlot;
                StuckSeconds = progress < settings.StuckProgressEpsilon ? StuckSeconds + step : 0f;

                if (settings.StuckSeconds > 0f && StuckSeconds >= settings.StuckSeconds)
                {
                    return RequestWarp();
                }
            }
            else
            {
                StuckSeconds = 0f;
            }

            _previousDistance = DistanceToSlot;

            // 停止・再開はしきい値を分けて往復を防ぐ。
            Decision = Decision == CompanionFollowDecision.Move
                ? (DistanceToSlot <= settings.StopDistance ? CompanionFollowDecision.Hold : CompanionFollowDecision.Move)
                : (DistanceToSlot >= settings.ResumeDistance ? CompanionFollowDecision.Move : CompanionFollowDecision.Hold);

            return Decision;
        }

        /// <summary>
        /// 判断状態を初期化する（加入・退場・Scene 離脱・Down からの復帰）。隊列位置とワープ要求数は保持しない。
        /// 次の <see cref="Tick"/> は「停止中」から評価を始める。
        /// </summary>
        public void Reset()
        {
            Decision = CompanionFollowDecision.Hold;
            StuckSeconds = 0f;
            _previousDistance = float.MaxValue;
        }

        private CompanionFollowDecision RequestWarp()
        {
            Decision = CompanionFollowDecision.Warp;
            StuckSeconds = 0f;
            _previousDistance = float.MaxValue; // ワープ後は前回距離を引き継がない。
            WarpRequests++;
            return Decision;
        }
    }
}
