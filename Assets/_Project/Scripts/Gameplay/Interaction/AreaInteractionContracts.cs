using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// Interact の押下 1 回で選ばれうる対象の契約（P5-04。仕様書 v1.1 §7.1）。
    ///
    /// <b>対象は入力を読まない。</b> 扉・レバー・犬丸調査はどれもこの形で登録し、
    /// 押下の消費と選択は <see cref="AreaInteractionController"/> が 1 か所で行う
    /// （§7.1「各対象が独自に InputSystem を読む方式は禁止」）。
    ///
    /// <b>「実行できるか」は対象が持つ。</b> 選択の規則（エリア・Floor・距離・遮蔽）は窓口が見るが、
    /// 調査済み・未加入・ロック中といった対象の内部条件は対象しか知らない。
    /// 内部条件で断られても<b>同じ押下を次点へ流さない</b>のが §7.1 の要点なので、
    /// 断りも <see cref="AreaInteractionOutcome"/> として返して押下を使い切る。
    /// </summary>
    public interface IAreaInteractable
    {
        /// <summary>配置された対象固有の StableId（同距離のときの順位もこれで決める）。</summary>
        StableId InteractableId { get; }

        /// <summary>この対象が属するエリア。別エリアの対象は候補にしない。</summary>
        StableId AreaId { get; }

        /// <summary>この対象が属する Floor。P5 は 0 のみ（§3.1）。</summary>
        int FloorId { get; }

        /// <summary>距離・遮蔽判定の基準点（§7.1 の InteractionAnchor）。</summary>
        Vector3 InteractionAnchor { get; }

        /// <summary>
        /// この対象固有の受付距離。0 以下なら窓口の既定値を使う。
        /// <b>窓口の既定値より短い場合は短い方を使う</b>（§7.1）。長くはできない。
        /// </summary>
        float InteractionRadius { get; }

        /// <summary>有効で配線が揃っているか（Disable・破棄・設定欠落なら false）。</summary>
        bool IsAvailable { get; }

        /// <summary>仮 UI 用の短文（「調べる」「開ける」等）。</summary>
        string Prompt { get; }

        /// <summary>押下を実行する。断る場合も<b>押下は使い切る</b>（次点へ流さない）。</summary>
        AreaInteractionOutcome Interact();
    }

    /// <summary>Interact の実行結果（§7.1）。断りも結果であり、押下は使い切られている。</summary>
    public readonly struct AreaInteractionOutcome
    {
        private AreaInteractionOutcome(bool handled, string message)
        {
            Handled = handled;
            Message = message ?? string.Empty;
        }

        /// <summary>対象が要求を受け付けたか。false は「断った」であって「対象が無い」ではない。</summary>
        public bool Handled { get; }

        /// <summary>仮 UI 用の短文（理由・結果）。</summary>
        public string Message { get; }

        /// <summary>受け付けた。</summary>
        public static AreaInteractionOutcome Accepted(string message = null) =>
            new AreaInteractionOutcome(true, message);

        /// <summary>対象の内部条件で断った（押下は使い切る。次点へ流さない）。</summary>
        public static AreaInteractionOutcome Refused(string message) =>
            new AreaInteractionOutcome(false, message);
    }

    /// <summary>候補が決まらなかった理由（§7.1。表示と診断のため）。</summary>
    public enum AreaInteractionRejection
    {
        /// <summary>候補が決まった。</summary>
        None = 0,

        /// <summary>窓口の配線が足りない（Context・主人公の基準点が無い）。Validator で検出する対象。</summary>
        NotWired = 1,

        /// <summary>AreaReady でない（初期化前・遷移中）。</summary>
        AreaNotReady = 2,

        /// <summary>探索モードではない（戦闘・Pause・会話・Loading）。</summary>
        WrongMode = 3,

        /// <summary>範囲内・同 Floor・遮蔽なしの対象が無い。押下は捨てる。</summary>
        NoTargetInRange = 4,

        /// <summary>
        /// この区画の戦闘が始まっている（§8.2 手順 3 の Starting 〜 §8.4 の Resolving）。
        ///
        /// <b>GameMode だけでは閉じられない。</b> 手順 3 で Starting を確定してから手順 6 で Combat へ
        /// 変えるまでの間、モードはまだ Exploration である。その間に走る探索の撤収通知（手順 4）から
        /// Interact が再入できてしまうと、§8.3 の「戦闘開始を先に確定し、移動／Interact を拒否する」が破れる。
        /// </summary>
        EncounterStarting = 5,
    }

    /// <summary>
    /// Scene 内の Interact 対象の登録簿（<c>Find*</c> を使わずに集めるため。
    /// 索敵の <c>PerceptionTargetRegistry</c>・調査の <c>InvestigationPointRegistry</c> と同じ形）。
    ///
    /// 選択の規則は持たない。規則は純粋な <see cref="AreaInteractionSelector"/> が持つ。
    /// </summary>
    public static class AreaInteractableRegistry
    {
        private static readonly List<IAreaInteractable> _items = new List<IAreaInteractable>();

        /// <summary>登録数（診断・テスト用）。</summary>
        public static int Count => _items.Count;

        public static void Register(IAreaInteractable item)
        {
            if (item != null && !_items.Contains(item))
            {
                _items.Add(item);
            }
        }

        public static void Unregister(IAreaInteractable item) => _items.Remove(item);

        /// <summary>片付ける（Scene 遷移・テストの後始末）。</summary>
        public static void Clear() => _items.Clear();

        /// <summary>登録中の対象を使い回しバッファへ写す（呼び出し側は前回の中身をあてにしない）。</summary>
        public static void CopyTo(List<IAreaInteractable> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i] != null)
                {
                    buffer.Add(_items[i]);
                }
            }
        }
    }
}
