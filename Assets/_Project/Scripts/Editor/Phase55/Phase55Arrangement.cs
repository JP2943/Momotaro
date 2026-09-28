using System;
using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Editor.Phase5;
using UnityEngine;

namespace Momotaro.Editor.Phase55
{
    /// <summary>
    /// P5.5 の「配置」1 つぶん（工程 P55-05c）。
    ///
    /// <b>生成も検査も配置を受け取る形にする。</b> 東西配置を作ったときは、生成器だけ
    /// 設定で切り替えて、<b>検査は東西の定数を直に読んでいた</b>。その形のままでは、
    /// 南北の配置を足しても検査が付いてこない——「作ったが見ていない」になる。
    ///
    /// ここが持つのは<b>検査が要る事実だけ</b>である。座標の正本は
    /// <c>Phase55WorldLayout</c>／<c>Phase55NorthSouthLayout</c> にあり、
    /// 検査の期待値は生成された Scene から採る（<c>Phase55WorldValidator</c> の方針）。
    /// </summary>
    public sealed class Phase55Arrangement
    {
        /// <summary>結果表示に使う呼び名（"東西" など）。</summary>
        public string Label { get; set; }

        /// <summary>A（出発側）の接続口の面。接続軸はここから決まる。</summary>
        public Phase5SeamSide ASide { get; set; }

        /// <summary>この接続が走る軸（面から決まる）。</summary>
        public Phase5SeamAxis Axis => Phase5SeamSides.AxisOf(ASide);

        /// <summary>A から B へ進む向き（接続軸の符号。+1 か −1）。</summary>
        public float Forward => Along(Phase5SeamSides.OutwardOf(ASide));

        /// <summary>A 側の Scene。</summary>
        public string AreaAScenePath { get; set; }

        /// <summary>B 側の Scene。</summary>
        public string AreaBScenePath { get; set; }

        /// <summary>この配置のカタログ。</summary>
        public string CatalogDataPath { get; set; }

        /// <summary>この配置の接続一覧。</summary>
        public string ConnectionDataPath { get; set; }

        /// <summary>A 側の AreaId。</summary>
        public StableId AreaAId { get; set; }

        /// <summary>B 側の AreaId。</summary>
        public StableId AreaBId { get; set; }

        /// <summary>接続一覧の安定 ID。</summary>
        public StableId ConnectionsId { get; set; }

        /// <summary>接続一覧の表示名。</summary>
        public string ConnectionsDisplayName { get; set; }

        /// <summary>A → B の出入口。</summary>
        public StableId ExitFromA { get; set; }

        /// <summary>B → A の出入口。</summary>
        public StableId ExitFromB { get; set; }

        /// <summary>A の「B から戻る」入口。</summary>
        public StableId EntryInA { get; set; }

        /// <summary>B の「A から来る」入口。</summary>
        public StableId EntryInB { get; set; }

        /// <summary>A → B の接続。</summary>
        public StableId ConnectionAToB { get; set; }

        /// <summary>B → A の接続。</summary>
        public StableId ConnectionBToA { get; set; }

        /// <summary>A → B の向き（Data に焼く値）。</summary>
        public AreaConnectionDirection ForwardDirection { get; set; }

        /// <summary>B → A の向き。</summary>
        public AreaConnectionDirection BackwardDirection { get; set; }

        /// <summary>接続通路の中心（<b>直交軸</b>の世界座標）。</summary>
        public float PassageCenter { get; set; }

        /// <summary>接続通路の幅（直交軸）。</summary>
        public float PassageWidth { get; set; }

        /// <summary>Builder へ渡す設定を作る。</summary>
        public Func<Phase5BuildTargets> Targets { get; set; }

        // ---- 軸の読み替え（検査がここだけを通る）----

        /// <summary>接続軸の成分。</summary>
        public float Along(Vector3 v) => Axis == Phase5SeamAxis.X ? v.x : v.z;

        /// <summary>直交軸の成分。</summary>
        public float Across(Vector3 v) => Axis == Phase5SeamAxis.X ? v.z : v.x;

        /// <summary>Bounds の接続軸方向の最小・最大。</summary>
        public float AlongMin(Bounds b) => Along(b.min);

        /// <summary>同・最大。</summary>
        public float AlongMax(Bounds b) => Along(b.max);

        /// <summary>Bounds の直交軸方向の最小・最大。</summary>
        public float AcrossMin(Bounds b) => Across(b.min);

        /// <summary>同・最大。</summary>
        public float AcrossMax(Bounds b) => Across(b.max);

        /// <summary>A から見て「進む先」の端（東西なら max.x、南北なら max.z。逆向きなら min）。</summary>
        public float ForwardEdge(Bounds b) => Forward > 0f ? AlongMax(b) : AlongMin(b);

        /// <summary>A から見て「戻る先」の端。</summary>
        public float BackwardEdge(Bounds b) => Forward > 0f ? AlongMin(b) : AlongMax(b);

        /// <summary>接続軸・直交軸の値から XZ の点を作る（Y は 0）。</summary>
        public Vector3 Point(float along, float across) => Axis == Phase5SeamAxis.X
            ? new Vector3(along, 0f, across)
            : new Vector3(across, 0f, along);

        /// <summary>接続軸の呼び名（エラー文に出す）。</summary>
        public string AlongName => Axis == Phase5SeamAxis.X ? "x" : "z";

        /// <summary>直交軸の呼び名。</summary>
        public string AcrossName => Axis == Phase5SeamAxis.X ? "z" : "x";

        /// <summary>見える半分の広がりのうち、接続軸のぶん。</summary>
        public float AlongHalf(Vector2 half) => Axis == Phase5SeamAxis.X ? half.x : half.y;

        /// <summary>同・直交軸のぶん。</summary>
        public float AcrossHalf(Vector2 half) => Axis == Phase5SeamAxis.X ? half.y : half.x;
    }

    /// <summary>この repo にある P5.5 の配置の一覧（§11 の P01／P02／P03）。</summary>
    public static class Phase55Arrangements
    {
        /// <summary>東西配置（A の東に B。工程 P55-03c）。</summary>
        public static Phase55Arrangement EastWest() => new Phase55Arrangement
        {
            Label = "東西",
            ASide = Phase5SeamSide.East,
            AreaAScenePath = Phase55WorldIds.AreaAScenePath,
            AreaBScenePath = Phase55WorldIds.AreaBScenePath,
            CatalogDataPath = Phase55WorldIds.CatalogDataPath,
            ConnectionDataPath = Phase55WorldIds.ConnectionDataPath,
            AreaAId = Phase55WorldIds.AreaA,
            AreaBId = Phase55WorldIds.AreaB,
            ConnectionsId = Phase55WorldIds.Connections,
            ConnectionsDisplayName = "P5.5 エリア接続（東西）",
            ExitFromA = Phase55WorldIds.ExitAEast,
            ExitFromB = Phase55WorldIds.ExitBWest,
            EntryInA = Phase5AreaIds.AreaAFromB,
            EntryInB = Phase5AreaIds.AreaBFromA,
            ConnectionAToB = Phase55WorldIds.ConnectionAToB,
            ConnectionBToA = Phase55WorldIds.ConnectionBToA,
            ForwardDirection = AreaConnectionDirection.East,
            BackwardDirection = AreaConnectionDirection.West,
            PassageCenter = Phase55WorldLayout.SeamZ,
            PassageWidth = Phase55WorldLayout.PassageWidth,
            Targets = Phase55WorldLayout.Targets,
        };

        /// <summary>南北配置（S の北に N。工程 P55-05c。§11 の P03）。</summary>
        public static Phase55Arrangement NorthSouth() => new Phase55Arrangement
        {
            Label = "南北",
            ASide = Phase5SeamSide.North,
            AreaAScenePath = Phase55NorthSouthIds.AreaSScenePath,
            AreaBScenePath = Phase55NorthSouthIds.AreaNScenePath,
            CatalogDataPath = Phase55NorthSouthIds.CatalogDataPath,
            ConnectionDataPath = Phase55NorthSouthIds.ConnectionDataPath,
            AreaAId = Phase55NorthSouthIds.AreaS,
            AreaBId = Phase55NorthSouthIds.AreaN,
            ConnectionsId = Phase55NorthSouthIds.Connections,
            ConnectionsDisplayName = "P5.5 エリア接続（南北）",
            ExitFromA = Phase55NorthSouthIds.ExitSNorth,
            ExitFromB = Phase55NorthSouthIds.ExitNSouth,
            EntryInA = Phase5AreaIds.AreaAFromB,
            EntryInB = Phase5AreaIds.AreaBFromA,
            ConnectionAToB = Phase55NorthSouthIds.ConnectionSToN,
            ConnectionBToA = Phase55NorthSouthIds.ConnectionNToS,
            ForwardDirection = AreaConnectionDirection.North,
            BackwardDirection = AreaConnectionDirection.South,
            PassageCenter = Phase55NorthSouthLayout.SeamX,
            PassageWidth = Phase55NorthSouthLayout.PassageWidth,
            Targets = Phase55NorthSouthLayout.Targets,
        };

        /// <summary>全配置。生成も検査もこの一覧をなめる。</summary>
        public static IReadOnlyList<Phase55Arrangement> All() =>
            new List<Phase55Arrangement> { EastWest(), NorthSouth() };
    }
}
