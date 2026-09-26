using System;
using System.Collections.Generic;

namespace Momotaro.Data.World
{
    /// <summary>
    /// 接続レコードの規則（P5.5 仕様書 §3.1／§10.1）。
    ///
    /// <b>Data の検査と Editor の検査で同じ規則を使う。</b> 2 か所に書くと必ず食い違い、
    /// 「Asset 検査は通るが Scene 検査で落ちる」ような、直し方の分からない不合格が出る。
    /// UnityEngine に依存しないので EditMode で決定的に検証できる。
    /// </summary>
    public static class AreaConnectionRules
    {
        /// <summary>向きの軸。東西は X、南北は Z（§3.2）。</summary>
        public enum Axis
        {
            None = 0,
            X = 1,
            Z = 2,
        }

        /// <summary>その向きが使う軸。</summary>
        public static Axis AxisOf(AreaConnectionDirection direction)
        {
            switch (direction)
            {
                case AreaConnectionDirection.East:
                case AreaConnectionDirection.West:
                    return Axis.X;
                case AreaConnectionDirection.North:
                case AreaConnectionDirection.South:
                    return Axis.Z;
                default:
                    return Axis.None;
            }
        }

        /// <summary>向きの反転。逆接続はこれと一致していなければならない。</summary>
        public static AreaConnectionDirection Opposite(AreaConnectionDirection direction)
        {
            switch (direction)
            {
                case AreaConnectionDirection.East: return AreaConnectionDirection.West;
                case AreaConnectionDirection.West: return AreaConnectionDirection.East;
                case AreaConnectionDirection.North: return AreaConnectionDirection.South;
                case AreaConnectionDirection.South: return AreaConnectionDirection.North;
                default: return AreaConnectionDirection.None;
            }
        }

        /// <summary>1 件の接続として成立しているか（§10.1）。</summary>
        public static void Validate(AreaConnectionDefinition c, Action<string> error, string owner)
        {
            if (c == null || error == null)
            {
                return;
            }

            string who = owner + ": 接続 '" + c.ConnectionId.Value + "'";

            if (!c.FromAreaId.IsValid || !c.ToAreaId.IsValid)
            {
                error(who + " の出発／到着エリアが不正です。");
            }

            if (!c.ExitId.IsValid || !c.EntryId.IsValid)
            {
                error(who + " の出入口／入口が不正です。");
            }

            if (c.FromAreaId.Equals(c.ToAreaId))
            {
                error(who + " が同じエリアを指しています（自己接続は作らない）。");
            }

            // <b>Slide は向きが要る。</b> 向き無しでスライドさせると、カメラをどちらへ送るかが決まらない。
            if (c.Style == AreaTransitionStyle.Slide && c.Direction == AreaConnectionDirection.None)
            {
                error(who + " は Slide ですが向きが未指定です（§3.1）。");
            }

            if (c.Style == AreaTransitionStyle.Slide)
            {
                if (c.SlideDuration < MinAllowedDuration || c.SlideDuration > MaxAllowedDuration)
                {
                    error(who + " の SlideDuration が範囲外です: " + c.SlideDuration.ToString("0.###")
                        + "（" + MinAllowedDuration.ToString("0.##") + "〜"
                        + MaxAllowedDuration.ToString("0.##") + " 秒）。");
                }

                if (!c.ReverseConnectionId.IsValid)
                {
                    error(who + " は Slide ですが逆接続 ID がありません（往復の整合を検査できない）。");
                }
            }

            if (c.PreloadDistance <= 0f)
            {
                error(who + " の PreloadDistance が 0 以下です。");
            }
        }

        /// <summary>
        /// 逆接続の対が整合しているか（§10.1「逆接続の方向反転」）。
        ///
        /// 見るのは 3 つ：相手が居ること、相手の逆参照が自分へ戻ること、向きが反転していること。
        /// エリアの入れ替わり（A→B の逆は B→A）もここで見る。
        /// </summary>
        public static void ValidateReversePairs(
            IReadOnlyList<AreaConnectionDefinition> connections, Action<string> error, string owner)
        {
            if (connections == null || error == null)
            {
                return;
            }

            var byId = new Dictionary<string, AreaConnectionDefinition>();
            foreach (AreaConnectionDefinition c in connections)
            {
                if (c != null && c.ConnectionId.IsValid)
                {
                    byId[c.ConnectionId.Value] = c;
                }
            }

            foreach (AreaConnectionDefinition c in connections)
            {
                if (c == null || !c.ReverseConnectionId.IsValid)
                {
                    continue;
                }

                string who = owner + ": 接続 '" + c.ConnectionId.Value + "'";
                if (!byId.TryGetValue(c.ReverseConnectionId.Value, out AreaConnectionDefinition rev))
                {
                    error(who + " の逆接続 '" + c.ReverseConnectionId.Value + "' が見つかりません。");
                    continue;
                }

                if (!rev.ReverseConnectionId.Equals(c.ConnectionId))
                {
                    error(who + " と '" + rev.ConnectionId.Value + "' の逆参照が対になっていません。");
                }

                if (!rev.FromAreaId.Equals(c.ToAreaId) || !rev.ToAreaId.Equals(c.FromAreaId))
                {
                    error(who + " の逆接続がエリアの対になっていません（"
                        + c.FromAreaId.Value + "→" + c.ToAreaId.Value + " の逆が "
                        + rev.FromAreaId.Value + "→" + rev.ToAreaId.Value + "）。");
                }

                if (c.Direction != AreaConnectionDirection.None
                    && rev.Direction != Opposite(c.Direction))
                {
                    error(who + " の向き " + c.Direction + " に対し、逆接続が "
                        + rev.Direction + " です（" + Opposite(c.Direction) + " であるべき）。");
                }
            }
        }

        /// <summary>SlideDuration の下限（Data 検査で使う実効値）。</summary>
        public const float MinAllowedDuration = AreaConnectionDefinition.MinSlideDuration;

        /// <summary>SlideDuration の上限。</summary>
        public const float MaxAllowedDuration = AreaConnectionDefinition.MaxSlideDuration;
    }
}
