using System.Collections.Generic;
using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Gameplay.Companion.Investigation
{
    /// <summary>
    /// 調査依頼を受け付ける地点の契約（P4-07A。v1.0 §10.1）。
    /// 地点は「何を要求し、どこで調べるか」だけを持ち、調査済みかどうかは Scene 単位の記録
    /// （<see cref="IInvestigationRecord"/>）が持つ（地点の Disable／再 Enable で記録が消えないため。§10.3）。
    /// </summary>
    public interface IInvestigationPoint
    {
        /// <summary>配置された地点固有の StableId（Prefab／SO 共有 ID や GetInstanceID で代用しない）。</summary>
        StableId PointId { get; }

        /// <summary>この地点を調べられる仲間の StableId（犬丸なら Data の Id）。役割ではなく加入キャラの同定。</summary>
        StableId RequiredCompanion { get; }

        /// <summary>発見内容の識別子（報酬そのものではない）。</summary>
        StableId DiscoveryId { get; }

        /// <summary>地点の位置（マーカー・距離判定の基準）。</summary>
        Vector3 Position { get; }

        /// <summary>調査位置（ApproachAnchor）。</summary>
        Vector3 ApproachPosition { get; }

        /// <summary>調査時の向き（ゼロなら地点の方を向く）。</summary>
        Vector3 ApproachFacing { get; }

        /// <summary>有効で配線が揃っているか（Disable／破棄・設定欠落なら false）。</summary>
        bool IsAvailable { get; }

        /// <summary>距離・時間の設定（受付時に Snapshot として写す）。</summary>
        InvestigationSettings Settings { get; }

        /// <summary>仮 UI 用の短文：調べられる。</summary>
        string Prompt { get; }

        /// <summary>仮 UI 用の短文：未加入。</summary>
        string MissingCompanionHint { get; }

        /// <summary>仮 UI 用の短文：調査済み。</summary>
        string CompletedText { get; }
    }

    /// <summary>
    /// Scene 内の調査地点の登録簿（<c>Find*</c> を使わずに集めるためのレジストリ。索敵の PerceptionTargetRegistry と同じ形）。
    /// 選択の規則は持たない（<see cref="InvestigationTargetSelector"/> が純粋ロジックとして持つ）。
    /// </summary>
    public static class InvestigationPointRegistry
    {
        private static readonly List<IInvestigationPoint> _points = new List<IInvestigationPoint>();

        public static int Count => _points.Count;

        public static void Register(IInvestigationPoint point)
        {
            if (point != null && !_points.Contains(point))
            {
                _points.Add(point);
            }
        }

        public static void Unregister(IInvestigationPoint point) => _points.Remove(point);

        public static void Clear() => _points.Clear();

        /// <summary>登録中の地点を使い回しバッファへ写す（呼び出し側は前回の中身をあてにしない）。</summary>
        public static void CopyTo(List<IInvestigationPoint> buffer)
        {
            buffer.Clear();
            for (int i = 0; i < _points.Count; i++)
            {
                // 非活動 Area の地点へ犬丸を行かせない（P5.5 §4.3）。
                if (_points[i] != null && Session.AreaScope.IsVisible(_points[i]))
                {
                    buffer.Add(_points[i]);
                }
            }
        }
    }
}
