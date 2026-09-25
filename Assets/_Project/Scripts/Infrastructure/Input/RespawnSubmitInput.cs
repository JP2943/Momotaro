using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// 再開操作（UI/Submit）を死亡再開の実行役へ仲介する（P5-08。仕様書 v1.1 §9.1 手順 3）。
    ///
    /// <b>押下を溜めない。</b> 再開待ちでないときに来た押下はその場で捨てる。
    /// 溜めておくと、戦闘中や到着直後に押した Submit が「次に死んだ瞬間の再開」になり、
    /// 死亡画面が一瞬で飛ぶ。§9.1 末尾の「旧 Submit のエッジ・ラッチを破棄する」はこれのこと。
    ///
    /// 実行の判断（受理は 1 回・周期の更新は要求 ID につき 1 回）は
    /// <see cref="CampaignRespawnRunner"/> と Session の調停役が持ち、ここは読み取りだけを行う。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RespawnSubmitInput : MonoBehaviour
    {
        [Tooltip("死亡再開の実行役。")]
        [SerializeField] private CampaignRespawnRunner _runner;

        private IRespawnSubmitInput _input;
        private bool _inputInjected;

        /// <summary>仲介先が配線されているか（Scene 検査・診断用）。</summary>
        public bool IsWired => _runner != null;

        /// <summary>
        /// 実際の配線先（常駐側が「Scene 側の受付が使えるか」を見るために読む。R9 の指摘）。
        /// <b>居るかどうかではなく、何に繋がっているか</b>を見ないと、
        /// 受付はあるのに繋ぎ先が死んでいる状態を取りこぼす。
        /// </summary>
        public CampaignRespawnRunner BoundRunner => _runner;

        /// <summary>所有権が無くて何もしなかった回数（診断・テスト用）。</summary>
        public int YieldedCount { get; private set; }

        /// <summary>再開を要求した回数（診断・テスト用）。</summary>
        public int SubmitCount { get; private set; }

        /// <summary>再開待ちでないために捨てた押下の数（診断・テスト用）。</summary>
        public int DiscardedCount { get; private set; }

        /// <summary>直近の判定（診断・テスト用）。</summary>
        public RespawnDecision LastDecision { get; private set; }

        /// <summary>仲介先を配線する（Builder・テストが呼ぶ）。</summary>
        public void Bind(CampaignRespawnRunner runner)
        {
            _runner = runner;
        }

        /// <summary>入力源を差し替える（テスト用）。</summary>
        public void SetInput(IRespawnSubmitInput input)
        {
            _input = input;
            _inputInjected = input != null;
        }

        private void Update()
        {
            TickInput();
        }

        /// <summary>1 フレーム分の仲介を行う（テストは Update を待たずに呼べる）。</summary>
        public bool TickInput()
        {
            // 再開に使ったボタンが離れたかを、通知ではなく<b>実デバイス</b>で確かめる（§9.1 末尾）。
            // Button 型の Action は押しっぱなしのまま Map を開き直しても何も通知しないので、
            // これが無いと解放待ちが解けず、到着後の最初の Interact を飲み込む（実際に踏んだ）。
            InputReleaseGateProvider.Current?.PollHeldControls();

            // <b>所有権が常駐側にあるなら、消費も破棄もしない</b>（GPT レビュー R9 の指摘）。
            //
            // ここを「消費しない」だけにすると足りない。下の枝は実行役が居ないときに押下を
            // <b>捨てる</b>ので、Scene 側が先に Update された順序では、常駐側が読む前に
            // 押下が消える。両方の実行順で成立させるには、捨てる側も所有権に従う。
            if (RespawnSubmitOwnerProvider.ResidentOwnsSubmit)
            {
                YieldedCount++;
                return false;
            }

            IRespawnSubmitInput input = ResolveInput();
            if (input == null)
            {
                return false;
            }

            if (_runner == null || !_runner.IsAwaitingRespawn)
            {
                if (input.SubmitPressed)
                {
                    input.DiscardSubmitPressed();
                    DiscardedCount++;
                }

                return false;
            }

            if (!input.ConsumeSubmitPressed())
            {
                return false;
            }

            SubmitCount++;
            LastDecision = _runner.RequestRespawn();
            return LastDecision.Accepted;
        }

        private IRespawnSubmitInput ResolveInput()
        {
            if (_inputInjected)
            {
                return _input;
            }

            return RespawnSubmitProvider.Current;
        }
    }
}
