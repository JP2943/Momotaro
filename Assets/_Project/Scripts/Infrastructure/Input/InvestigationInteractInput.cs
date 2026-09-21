using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// 探索の入力仲介（P4-07B。v1.0 §4.2・§12「既存 Interact を 1 回消費、Gameplay へ InputSystem 型を漏らさない」）。
    ///
    /// 主人公入力（<see cref="PlayerInputProvider"/>）がラッチした Interact の押下エッジを、Scene の探索の調停役
    /// （<see cref="InvestigationCoordinator"/>）へ<b>押下 1 回につき依頼 1 回</b>として渡す。押しっぱなしで連続実行しない
    /// （エッジのラッチが 1 回ぶんしか無い）。
    ///
    /// 対象が無い押下は<b>実行せずに捨てる</b>（古い押下を次の対象へ持ち越さない）。「対象が無いときは既存操作を維持する」は、
    /// この仲介が Step 等の他の入力に触れないこと＝Interact 以外を消費しないことで守る。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InvestigationInteractInput : MonoBehaviour
    {
        [Tooltip("探索の調停役（Scene 構築時に注入。Find* で探さない）。")]
        [SerializeField] private InvestigationCoordinator _coordinator;

        private IInteractInput _override;

        /// <summary>依頼を出した回数（テスト・診断用）。</summary>
        public int RequestCount { get; private set; }

        /// <summary>対象が無く捨てた押下の回数（テスト・診断用）。</summary>
        public int DiscardedCount { get; private set; }

        /// <summary>配線された調停役（Scene 検査用）。</summary>
        public InvestigationCoordinator Coordinator => _coordinator;

        /// <summary>調停役を注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(InvestigationCoordinator coordinator)
        {
            if (coordinator != null)
            {
                _coordinator = coordinator;
            }
        }

        /// <summary>入力源を差し替える（テスト。null で <see cref="PlayerInputProvider"/> へ戻す）。</summary>
        public void SetInput(IInteractInput input)
        {
            _override = input;
        }

        /// <summary>1 フレームぶんの仲介（Update から呼ばれるが、テストは決定的に直接呼べる）。依頼を出したら true。</summary>
        public bool TickInput()
        {
            IInteractInput input = _override ?? PlayerInputProvider.Current as IInteractInput;
            if (input == null || !input.InteractPressed)
            {
                return false;
            }

            if (_coordinator == null)
            {
                input.DiscardInteractPressed();
                DiscardedCount++;
                return false;
            }

            // 対象が定まらない押下（範囲内に地点が無い・未配線・入力が閉じている）は実行せずに捨てる。
            // 地点が文脈にある拒否（未加入・調査済み・到達不能・実行中）は 1 回の依頼として通し、理由通知を出させる。
            InvestigationRejectReason peek = _coordinator.Peek(out IInvestigationPoint point);
            if (point == null && peek != InvestigationRejectReason.None)
            {
                input.DiscardInteractPressed();
                DiscardedCount++;
                return false;
            }

            if (!input.ConsumeInteractPressed())
            {
                return false;
            }

            RequestCount++;
            _coordinator.TryRequest();
            return true;
        }

        private void Update()
        {
            TickInput();
        }
    }
}
