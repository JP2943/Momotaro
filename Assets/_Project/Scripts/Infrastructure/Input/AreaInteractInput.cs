using Momotaro.Gameplay.Interaction;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Infrastructure.Input
{
    /// <summary>
    /// P5 の Interact 入力仲介（P5-04。仕様書 v1.1 §7.1「入力消費は 1 か所」）。
    ///
    /// 主人公入力がラッチした Interact の押下エッジを、Area の単一選択窓口へ
    /// <b>押下 1 回につき実行 1 回</b>として渡す。扉・レバー・調査はすべてこの 1 本を通る。
    ///
    /// <b>P4 の <see cref="InvestigationInteractInput"/> とは同時に動かさない</b>（§7.1）。
    /// あちらは調査だけを見て最近傍を選び直すので、両方が生きていると
    /// 同じ押下が 2 回消費されるか、窓口が選んだ対象と別の地点へ依頼が飛ぶ。
    /// P5 Scene にはこちらだけを置き、Validator が旧仲介の不在を検査する。
    ///
    /// <b>対象が無い押下は捨てる</b>（§7.1「対象がなければ押下を捨てる」）。
    /// 古い押下を次のフレームや次の対象へ持ち越さない。
    /// 対象が内部条件で断った場合も押下は使い切る（次点へ流さない）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaInteractInput : MonoBehaviour
    {
        [Tooltip("Area の単一選択窓口（Scene 構築時に注入。Find* で探さない）。")]
        [SerializeField] private AreaInteractionController _controller;

        private IInteractInput _override;

        /// <summary>実行まで通した押下の回数（テスト・診断用）。断られた分も含む。</summary>
        public int InteractCount { get; private set; }

        /// <summary>対象が無く捨てた押下の回数（テスト・診断用）。</summary>
        public int DiscardedCount { get; private set; }

        /// <summary>配線された窓口（Scene 検査用）。</summary>
        public AreaInteractionController Controller => _controller;

        /// <summary>直近の実行結果（表示・診断用）。</summary>
        public AreaInteractionOutcome LastOutcome { get; private set; }

        /// <summary>窓口を注入する（Scene 構築・テスト。null は無視）。</summary>
        public void Bind(AreaInteractionController controller)
        {
            if (controller != null)
            {
                _controller = controller;
            }
        }

        /// <summary>入力源を差し替える（テスト。null で <see cref="PlayerInputProvider"/> へ戻す）。</summary>
        public void SetInput(IInteractInput input)
        {
            _override = input;
        }

        /// <summary>
        /// 1 フレームぶんの仲介。実行まで通したら true。
        /// <c>Update</c> から呼ばれるが、テストは決定的に直接呼べる（`CLAUDE.md` の時間注入と同じ形）。
        /// </summary>
        public bool TickInput()
        {
            IInteractInput input = _override ?? PlayerInputProvider.Current as IInteractInput;
            if (input == null || !input.InteractPressed)
            {
                return false;
            }

            if (_controller == null)
            {
                input.DiscardInteractPressed();
                DiscardedCount++;
                return false;
            }

            // 対象が定まらない押下は実行せずに捨てる（§7.1）。
            if (!_controller.Peek(out _, out _))
            {
                input.DiscardInteractPressed();
                DiscardedCount++;
                return false;
            }

            if (!input.ConsumeInteractPressed())
            {
                return false;
            }

            // 消費してから実行する。実行の中で対象が消えていても、押下はもう戻さない
            // （戻すと、同じ押下が次のフレームで別の対象に当たる）。
            if (!_controller.TryInteract(out AreaInteractionOutcome outcome))
            {
                DiscardedCount++;
                LastOutcome = AreaInteractionOutcome.Refused("対象がなくなりました。");
                return false;
            }

            InteractCount++;
            LastOutcome = outcome;
            return true;
        }

        private void Update()
        {
            TickInput();
        }
    }
}
