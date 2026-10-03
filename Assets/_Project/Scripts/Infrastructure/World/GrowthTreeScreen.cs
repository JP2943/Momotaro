using System.Collections.Generic;
using System.Globalization;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// 成長の木の仮 UI（P6B 04。仕様 §10）。三方向を列に並べ、9 ノードの接続・取得済み・取得可能・条件不足を区別して見せる。
    /// 選択したノードの効果・費用・前提・取得後の能力値、使用可能徳と払い戻し権利を常に表示する。
    ///
    /// <b>判定は持たない。</b> 取得・払い戻しの可否は <see cref="ShrineProcedures.CanPurchase"/>／<see cref="ShrineProcedures.CanRefund"/>
    /// （処理側の再検証と同じ判定）を表示に使い、実行は所有者（<see cref="CampaignShrineService"/>）へ返す。
    ///
    /// 確認の既定は<b>「いいえ」</b>：決定を連打しても、取得 → 次の押下で払い戻し確認 → 払い戻し、と進まない。
    /// </summary>
    public sealed class GrowthTreeScreen
    {
        private const float StickThreshold = 0.6f;

        private int _column;
        private int _row;
        private int _ignoreUntilFrame = -1;
        private Vector2 _previousStick;

        /// <summary>確認の種類。</summary>
        public enum Pending
        {
            None = 0,
            Acquire = 1,
            Refund = 2,
        }

        /// <summary>入力 1 フレームの結果（所有者が実行する）。</summary>
        public enum Command
        {
            None = 0,
            Acquire = 1,
            Refund = 2,
            Close = 3,
        }

        /// <summary>選択中の列（0..列数-1）。</summary>
        public int Column => _column;

        /// <summary>選択中の段（0..）。</summary>
        public int Row => _row;

        /// <summary>確認中の操作。</summary>
        public Pending Confirming { get; private set; }

        /// <summary>確認の選択（true＝はい）。既定はいいえ。</summary>
        public bool ConfirmYes { get; private set; }

        /// <summary>直近の理由表示。</summary>
        public string Notice { get; set; } = string.Empty;

        /// <summary>開き直す（選択を左上へ。開いたフレームの押下は使わない）。</summary>
        public void Reset()
        {
            _column = 0;
            _row = 0;
            Confirming = Pending.None;
            ConfirmYes = false;
            Notice = string.Empty;
            IgnoreThisFrame();
        }

        /// <summary>このフレームの押下を無視する（実行の直後に同じ押下で次へ進まない）。</summary>
        public void IgnoreThisFrame() => _ignoreUntilFrame = Time.frameCount;

        /// <summary>選ぶ（テスト・マウス用）。</summary>
        public void Select(int column, int row)
        {
            _column = column < 0 ? 0 : column;
            _row = row < 0 ? 0 : row;
            Confirming = Pending.None;
        }

        /// <summary>選択中のノード（無ければ空）。</summary>
        public StableId SelectedId(Grid grid) => grid.At(_column, _row);

        /// <summary>
        /// 確認を開く（決定）。取得済みなら払い戻し、未取得なら取得の確認。成り立たない操作は理由だけ表示して開かない。
        /// </summary>
        public void BeginConfirm(Grid grid, GameSessionState session, CampaignCatalog campaign)
        {
            StableId id = SelectedId(grid);
            if (id.IsEmpty || session == null || campaign == null)
            {
                return;
            }

            if (session.Progress.HasGrowth(id))
            {
                GrowthRefundResult r = ShrineProcedures.CanRefund(session, campaign, id);
                if (r != GrowthRefundResult.Refunded)
                {
                    Notice = RefundReason(r, campaign, session, id);
                    return;
                }

                Confirming = Pending.Refund;
            }
            else
            {
                GrowthPurchaseResult r = ShrineProcedures.CanPurchase(session, campaign, id);
                if (r != GrowthPurchaseResult.Purchased)
                {
                    Notice = PurchaseReason(r, campaign, session, id);
                    return;
                }

                Confirming = Pending.Acquire;
            }

            ConfirmYes = false;
            Notice = string.Empty;
        }

        /// <summary>確認を閉じる（何も変えない）。</summary>
        public void CancelConfirm()
        {
            Confirming = Pending.None;
            ConfirmYes = false;
        }

        /// <summary>
        /// 1 フレームの入力を読む（パッド全台・矢印キー・Enter・Esc）。確定した操作を返す。
        /// </summary>
        public Command Poll(Grid grid, GameSessionState session, CampaignCatalog campaign)
        {
            bool left = false, right = false, up = false, down = false, confirm = false, back = false;
            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                Gamepad g = Gamepad.all[i];
                left |= g.dpad.left.wasPressedThisFrame;
                right |= g.dpad.right.wasPressedThisFrame;
                up |= g.dpad.up.wasPressedThisFrame;
                down |= g.dpad.down.wasPressedThisFrame;
                confirm |= g.buttonSouth.wasPressedThisFrame;
                back |= g.buttonEast.wasPressedThisFrame;
            }

            Vector2 stick = Gamepad.current != null ? Gamepad.current.leftStick.ReadValue() : Vector2.zero;
            left |= stick.x < -StickThreshold && _previousStick.x >= -StickThreshold;
            right |= stick.x > StickThreshold && _previousStick.x <= StickThreshold;
            up |= stick.y > StickThreshold && _previousStick.y <= StickThreshold;
            down |= stick.y < -StickThreshold && _previousStick.y >= -StickThreshold;
            _previousStick = stick;

            Keyboard k = Keyboard.current;
            if (k != null)
            {
                left |= k.leftArrowKey.wasPressedThisFrame;
                right |= k.rightArrowKey.wasPressedThisFrame;
                up |= k.upArrowKey.wasPressedThisFrame;
                down |= k.downArrowKey.wasPressedThisFrame;
                confirm |= k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
                back |= k.escapeKey.wasPressedThisFrame;
            }

            if (Time.frameCount <= _ignoreUntilFrame)
            {
                return Command.None;
            }

            return Apply(grid, session, campaign, left, right, up, down, confirm, back);
        }

        /// <summary>入力の解釈（テストから直接呼べる）。</summary>
        public Command Apply(Grid grid, GameSessionState session, CampaignCatalog campaign,
            bool left, bool right, bool up, bool down, bool confirm, bool back)
        {
            if (Confirming != Pending.None)
            {
                if (left || right || up || down)
                {
                    ConfirmYes = !ConfirmYes;
                }

                if (back)
                {
                    CancelConfirm();
                    return Command.None;
                }

                if (confirm)
                {
                    Pending pending = Confirming;
                    bool yes = ConfirmYes;
                    CancelConfirm();
                    IgnoreThisFrame();
                    if (!yes)
                    {
                        return Command.None;
                    }

                    return pending == Pending.Acquire ? Command.Acquire : Command.Refund;
                }

                return Command.None;
            }

            if (back)
            {
                return Command.Close;
            }

            if (left)
            {
                _column = (_column - 1 + grid.Columns) % grid.Columns;
            }
            else if (right)
            {
                _column = (_column + 1) % grid.Columns;
            }
            else if (up)
            {
                _row = (_row - 1 + grid.Rows) % grid.Rows;
            }
            else if (down)
            {
                _row = (_row + 1) % grid.Rows;
            }

            if (left || right || up || down)
            {
                Notice = string.Empty;
            }

            if (confirm)
            {
                BeginConfirm(grid, session, campaign);
            }

            return Command.None;
        }

        // ---------------------------------------------------------------- 並び

        /// <summary>
        /// ノードの並び（列＝方向、段＝前提の深さ）。<b>保存形式には持たない</b>（表示順・座標を保存へ固定しない。仕様 §3）。
        /// 列は「前提なしのノード」から始まる鎖ごと、段はその中の深さ。
        /// </summary>
        public sealed class Grid
        {
            private readonly List<List<StableId>> _columns = new List<List<StableId>>();

            public int Columns => _columns.Count == 0 ? 1 : _columns.Count;

            public int Rows { get; private set; } = 1;

            public StableId At(int column, int row) =>
                column >= 0 && column < _columns.Count && row >= 0 && row < _columns[column].Count
                    ? _columns[column][row]
                    : default;

            public IReadOnlyList<StableId> Column(int column) => _columns[column];

            public static Grid From(CampaignCatalog campaign)
            {
                var grid = new Grid();
                if (campaign == null)
                {
                    return grid;
                }

                var placed = new HashSet<string>();
                foreach (GrowthInfo root in campaign.GrowthNodes)
                {
                    if (root.Prerequisites.Count != 0 || placed.Contains(root.GrowthId.Value))
                    {
                        continue;
                    }

                    var chain = new List<StableId> { root.GrowthId };
                    placed.Add(root.GrowthId.Value);
                    StableId tail = root.GrowthId;
                    bool extended = true;
                    while (extended)
                    {
                        extended = false;
                        foreach (GrowthInfo g in campaign.GrowthNodes)
                        {
                            if (!placed.Contains(g.GrowthId.Value) && g.Prerequisites.Count == 1
                                && g.Prerequisites[0].Equals(tail))
                            {
                                chain.Add(g.GrowthId);
                                placed.Add(g.GrowthId.Value);
                                tail = g.GrowthId;
                                extended = true;
                                break;
                            }
                        }
                    }

                    grid._columns.Add(chain);
                    if (chain.Count > grid.Rows)
                    {
                        grid.Rows = chain.Count;
                    }
                }

                // 鎖にならなかったノード（分岐・複数前提）は最後の列へまとめる（P6B の 9 ノードには無い）。
                var rest = new List<StableId>();
                foreach (GrowthInfo g in campaign.GrowthNodes)
                {
                    if (!placed.Contains(g.GrowthId.Value))
                    {
                        rest.Add(g.GrowthId);
                    }
                }

                if (rest.Count > 0)
                {
                    grid._columns.Add(rest);
                    if (rest.Count > grid.Rows)
                    {
                        grid.Rows = rest.Count;
                    }
                }

                return grid;
            }
        }

        // ---------------------------------------------------------------- 文言

        /// <summary>ノードの状態の札。</summary>
        public static string StateLabel(GameSessionState session, CampaignCatalog campaign, StableId id)
        {
            if (session.Progress.HasGrowth(id))
            {
                return "取得済み";
            }

            GrowthPurchaseResult r = ShrineProcedures.CanPurchase(session, campaign, id);
            switch (r)
            {
                case GrowthPurchaseResult.Purchased:
                    return "取得可能";
                case GrowthPurchaseResult.InsufficientVirtue:
                    return "徳不足";
                default:
                    return "条件不足";
            }
        }

        public static string PurchaseReason(GrowthPurchaseResult r, CampaignCatalog campaign, GameSessionState session,
            StableId id)
        {
            campaign.TryGetGrowth(id, out GrowthInfo g);
            switch (r)
            {
                case GrowthPurchaseResult.InsufficientVirtue:
                    return "徳が足りません（必要 " + g.Cost + "／所持 " + session.Progress.AvailableVirtue + "）";
                case GrowthPurchaseResult.PrerequisiteNotMet:
                    return "前提が未取得です：" + PrerequisiteNames(campaign, g);
                case GrowthPurchaseResult.AlreadyAcquired:
                    return "取得済みです";
                case GrowthPurchaseResult.NotAllowedHere:
                    return "ここでは取得できません";
                default:
                    return "取得できません（" + r + "）";
            }
        }

        public static string RefundReason(GrowthRefundResult r, CampaignCatalog campaign, GameSessionState session,
            StableId id)
        {
            switch (r)
            {
                case GrowthRefundResult.NoRights:
                    return "払い戻し権利がありません";
                case GrowthRefundResult.HasDependents:
                    return "先に、このノードを前提にしている取得済みノードを払い戻してください";
                case GrowthRefundResult.NotAcquired:
                    return "未取得です";
                case GrowthRefundResult.NotAllowedHere:
                    return "ここでは払い戻せません";
                default:
                    return "払い戻せません（" + r + "）";
            }
        }

        public static string PrerequisiteNames(CampaignCatalog campaign, GrowthInfo g)
        {
            if (g.Prerequisites.Count == 0)
            {
                return "なし";
            }

            var names = new List<string>();
            for (int i = 0; i < g.Prerequisites.Count; i++)
            {
                names.Add(campaign.TryGetGrowth(g.Prerequisites[i], out GrowthInfo p) && !string.IsNullOrEmpty(p.DisplayName)
                    ? p.DisplayName
                    : g.Prerequisites[i].Value);
            }

            return string.Join("・", names);
        }

        /// <summary>能力値の 1 行（基礎値と効果から）。</summary>
        public static string Stats(GrowthEffects e, int maxHp, int maxStamina, int heal, int capacity)
        {
            return "最大HP " + maxHp + "　刀×" + e.AttackHpMultiplier.ToString("0.00", CultureInfo.InvariantCulture)
                + "　スタミナ " + maxStamina + "　体幹×" + e.NormalPoiseMultiplier.ToString("0.00", CultureInfo.InvariantCulture)
                + "　回復 " + heal + "　最大数 " + capacity;
        }
    }
}
