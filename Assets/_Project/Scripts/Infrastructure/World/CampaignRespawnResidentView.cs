using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// 死亡再開の表示と再試行受付を<b>常駐側から肩代わりする</b>（§9.1 末尾。GPT レビュー R8 の指摘 1）。
    ///
    /// <b>なぜ常駐に要るのか。</b> Scene 側の受付（<c>RespawnSubmitInput</c>）と表示
    /// （<c>CampaignRespawnView</c>）は、どちらも Scene の <see cref="CampaignRespawnRunner"/> を
    /// 前提にしている。Runner が無ければ入力は捨てられ、表示も出ない。
    /// 死亡再開では旧 Scene を破棄してから読むので、<b>到着先で Runner が欠落・利用不能になると
    /// Session は Failed でも、再試行の表示も操作経路も無くなる</b>——遷移役を常駐で公開しても、
    /// それを呼ぶ側が居ない状態になっていた。しかも死亡再開の失敗は
    /// <c>FailRespawnInsteadOfRecovery</c> により終端失敗にしないので、Error 表示も出ない。
    ///
    /// <b>常駐に置く理由は <see cref="AreaTransitionFailureView"/> と同じ。</b>
    /// 失敗したときは Scene 側が壊れているのが普通なので、Scene に置いた表示では出せない。
    /// IMGUI なので Canvas もフォント資産も要らず、Gameplay 時計を止めたままでも描画・入力が生きている。
    ///
    /// <b>二重処理はしない。</b> Scene 側の経路が使えるうちは何も出さず、入力にも触らない
    /// （<see cref="ShouldTakeOver"/>）。押下は 1 つの受け口だけが消費する。
    ///
    /// 正式な見た目は P10b。ここでの契約は <see cref="IsShowing"/>／<see cref="Message"/>／
    /// <see cref="TryRequestRespawn"/> の 3 つ。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampaignRespawnResidentView : MonoBehaviour
    {
        private AreaTransitionService _service;

        /// <summary>肩代わりして受理した回数（診断・テスト用）。</summary>
        public int TakeoverSubmitCount { get; private set; }

        /// <summary>直近の判定（診断・テスト用）。</summary>
        public RespawnDecision LastDecision { get; private set; }

        /// <summary>遷移サービスを差す（常駐の組み立て時に 1 度だけ）。</summary>
        public void Bind(AreaTransitionService service)
        {
            _service = service;
        }

        /// <summary>
        /// Scene 側の経路が使えないので、常駐が肩代わりすべきか。
        ///
        /// <b>Runner が居るだけでは足りない。</b> 居ても Session を解決できなければ
        /// <c>IsAwaitingRespawn</c> が false を返し、表示も受付も動かない。
        /// 受け口（<c>RespawnSubmitInput</c>）が配線されているかまで見る。
        /// </summary>
        public bool ShouldTakeOver
        {
            get
            {
                CampaignRespawnCoordinator respawn = GameSessionProvider.Current?.Respawn;
                if (respawn == null || !respawn.IsAwaitingRespawn)
                {
                    return false;
                }

                var runner = Object.FindFirstObjectByType<CampaignRespawnRunner>();
                if (runner == null || !runner.IsAwaitingRespawn)
                {
                    return true;
                }

                var submit = Object.FindFirstObjectByType<Momotaro.Infrastructure.Input.RespawnSubmitInput>();
                return submit == null || !submit.IsWired;
            }
        }

        /// <summary>いま常駐側で出しているか。</summary>
        public bool IsShowing => !string.IsNullOrEmpty(Message);

        /// <summary>出している短文（出していなければ空）。Scene 側が生きているうちは空。</summary>
        public string Message
        {
            get
            {
                if (!ShouldTakeOver)
                {
                    return string.Empty;
                }

                CampaignRespawnCoordinator respawn = GameSessionProvider.Current?.Respawn;
                return respawn == null ? string.Empty : CampaignRespawnLabels.ForPhase(respawn.Phase);
            }
        }

        /// <summary>
        /// 肩代わりして再開を要求する（手順は <see cref="CampaignRespawnRequestProcedure"/> が持つ）。
        /// Scene 側が生きているときは何もしない。
        /// </summary>
        public RespawnDecision TryRequestRespawn()
        {
            if (!ShouldTakeOver)
            {
                return RespawnDecision.Reject(RespawnRejection.NotDead);
            }

            GameSessionState session = GameSessionProvider.Current;
            CampaignRespawnRequestProcedure.Outcome outcome = CampaignRespawnRequestProcedure.Execute(
                session, session?.Respawn, TryResolveEntry, CampaignRespawnTravelProvider.Current);

            LastDecision = outcome.Decision;
            return outcome.Decision;
        }

        private bool TryResolveEntry(out AreaEntryInfo entry)
        {
            if (_service != null)
            {
                return _service.TryGetRespawnEntry(out entry);
            }

            entry = default;
            return false;
        }

        private void Update()
        {
            if (!ShouldTakeOver)
            {
                return;
            }

            // 押下は 1 つの受け口だけが消費する。Scene 側が死んでいるこの場合だけここが取る。
            IRespawnSubmitInput input = RespawnSubmitProvider.Current;
            if (input == null || !input.ConsumeSubmitPressed())
            {
                return;
            }

            TakeoverSubmitCount++;
            TryRequestRespawn();
        }

        private void OnGUI()
        {
            string message = Message;
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
            };
            style.normal.textColor = Color.white;

            float w = 520f;
            float h = 60f;
            var rect = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.45f, w, h);
            GUI.Label(rect, message, style);
        }
    }
}
