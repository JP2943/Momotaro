using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Data;
using Momotaro.Data.World;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P5.5-E01：接続の解決・逆方向・不正 ID・軸不一致・既存 Data の Fade 既定（P5.5 仕様書 §11 の E01）。
    ///
    /// <b>P5.5 の土台はここ。</b> 接続が引けない・逆が対になっていない・向きが無い、といった不整合を
    /// 実行時まで持ち越すと、スライドの途中でカメラの行き先が決まらない形で表に出る。
    /// Data の検査と Editor の検査が同じ規則（<see cref="AreaConnectionRules"/>）を使うことも、ここで固定する。
    /// </summary>
    public sealed class P55ConnectionContractTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        private static AreaConnectionDefinition Make(
            string id, string from, string exit, string to, string entry,
            AreaTransitionStyle style, AreaConnectionDirection dir, string reverse,
            float duration = AreaConnectionDefinition.DefaultSlideDuration)
        {
            var c = new AreaConnectionDefinition();
            c.Configure(new StableId(id), new StableId(from), new StableId(exit),
                new StableId(to), new StableId(entry), style, dir, new StableId(reverse), duration);
            return c;
        }

        private AreaConnectionData MakeData(params AreaConnectionDefinition[] connections)
        {
            var data = ScriptableObject.CreateInstance<AreaConnectionData>();
            data.name = "SO_AreaConnections_Test";
            _spawned.Add(data);
            data.SetConnections(connections);
            return data;
        }

        /// <summary>東西の往復が引け、逆方向が対になっている。</summary>
        [Test]
        public void EastWestPair_ResolvesBothWaysAndIsMutuallyReversed()
        {
            AreaConnectionData data = MakeData(
                Make("conn_a_east", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.East, "conn_b_west"),
                Make("conn_b_west", "area_p55_b", "exit_b_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.West, "conn_a_east"));

            Assert.IsTrue(AreaConnectionCatalog.TryBuild(data, out AreaConnectionCatalog catalog, out List<string> errors),
                "整合した接続からはカタログを作れる: " + string.Join(" / ", errors));
            Assert.AreEqual(2, catalog.Count);

            Assert.IsTrue(catalog.TryGetFromExit(new StableId("area_p55_a"), new StableId("exit_a_east"),
                out AreaConnectionSnapshot east), "出発エリアと出入口から引ける。");
            Assert.AreEqual("area_p55_b", east.ToAreaId.Value);
            Assert.AreEqual("entry_b_west", east.EntryId.Value);
            Assert.AreEqual(AreaConnectionDirection.East, east.Direction);
            Assert.IsTrue(east.IsSlide);
            Assert.AreEqual(AreaConnectionRules.Axis.X, east.Axis, "東西は X 軸（§3.2）。");

            Assert.IsTrue(catalog.TryGetReverse(east.ConnectionId, out AreaConnectionSnapshot back),
                "逆方向を引ける。");
            Assert.AreEqual("conn_b_west", back.ConnectionId.Value);
            Assert.AreEqual("area_p55_a", back.ToAreaId.Value, "逆はエリアの対になる。");
            Assert.AreEqual(AreaConnectionDirection.West, back.Direction, "向きは反転する。");
        }

        /// <summary>南北は Z 軸として解決される。</summary>
        [Test]
        public void NorthSouthPair_UsesTheZAxis()
        {
            AreaConnectionData data = MakeData(
                Make("conn_n", "area_p55_s", "exit_s_north", "area_p55_n", "entry_n_south",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.North, "conn_s"),
                Make("conn_s", "area_p55_n", "exit_n_south", "area_p55_s", "entry_s_north",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.South, "conn_n"));

            Assert.IsTrue(AreaConnectionCatalog.TryBuild(data, out AreaConnectionCatalog catalog, out _));
            Assert.IsTrue(catalog.TryGet(new StableId("conn_n"), out AreaConnectionSnapshot north));
            Assert.AreEqual(AreaConnectionRules.Axis.Z, north.Axis, "南北は Z 軸（§3.2）。");
            Assert.AreEqual(AreaConnectionDirection.South, AreaConnectionRules.Opposite(north.Direction));
        }

        /// <summary>知らない ID・存在しない出入口では引けない（黙って既定値を返さない）。</summary>
        [Test]
        public void UnknownIdsResolveToNothing()
        {
            AreaConnectionData data = MakeData(
                Make("conn_a_east", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.East, "conn_b_west"),
                Make("conn_b_west", "area_p55_b", "exit_b_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.West, "conn_a_east"));

            Assert.IsTrue(AreaConnectionCatalog.TryBuild(data, out AreaConnectionCatalog catalog, out _));

            Assert.IsFalse(catalog.TryGet(new StableId("conn_does_not_exist"), out _), "知らない接続 ID。");
            Assert.IsFalse(catalog.TryGet(default, out _), "空の ID。");
            Assert.IsFalse(catalog.TryGetFromExit(new StableId("area_p55_a"), new StableId("exit_unknown"), out _),
                "そのエリアに無い出入口。");
            Assert.IsFalse(catalog.TryGetFromExit(new StableId("area_unknown"), new StableId("exit_a_east"), out _),
                "別エリアの出入口 ID を流用しても引けない。");
        }

        /// <summary><b>向きの無い Slide はカタログを作らせない。</b>カメラの行き先が決まらない。</summary>
        [Test]
        public void SlideWithoutDirection_IsRefused()
        {
            AreaConnectionData data = MakeData(
                Make("conn_bad", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.None, "conn_back"),
                Make("conn_back", "area_p55_b", "exit_b_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.West, "conn_bad"));

            Assert.IsFalse(AreaConnectionCatalog.TryBuild(data, out _, out List<string> errors));
            CollectionAssert.IsNotEmpty(errors);
            StringAssert.Contains("向きが未指定", string.Join(" / ", errors));
        }

        /// <summary>逆接続の向きが反転していなければ不合格（§10.1）。</summary>
        [Test]
        public void ReverseWithWrongDirection_IsRefused()
        {
            AreaConnectionData data = MakeData(
                Make("conn_a_east", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.East, "conn_b_bad"),
                // 逆なのに North。X 軸と Z 軸で食い違う。
                Make("conn_b_bad", "area_p55_b", "exit_b_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.North, "conn_a_east"));

            Assert.IsFalse(AreaConnectionCatalog.TryBuild(data, out _, out List<string> errors));
            StringAssert.Contains("West であるべき", string.Join(" / ", errors));
        }

        /// <summary>逆接続がエリアの対になっていなければ不合格。</summary>
        [Test]
        public void ReverseBetweenWrongAreas_IsRefused()
        {
            AreaConnectionData data = MakeData(
                Make("conn_a_east", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.East, "conn_c_west"),
                Make("conn_c_west", "area_p55_c", "exit_c_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.West, "conn_a_east"));

            Assert.IsFalse(AreaConnectionCatalog.TryBuild(data, out _, out List<string> errors));
            StringAssert.Contains("エリアの対になっていません", string.Join(" / ", errors));
        }

        /// <summary>逆接続が片側しか指していなければ不合格。</summary>
        [Test]
        public void OneSidedReverse_IsRefused()
        {
            AreaConnectionData data = MakeData(
                Make("conn_a_east", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.East, "conn_b_west"),
                Make("conn_b_west", "area_p55_b", "exit_b_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.West, "conn_other"));

            Assert.IsFalse(AreaConnectionCatalog.TryBuild(data, out _, out List<string> errors));
            string joined = string.Join(" / ", errors);
            Assert.IsTrue(joined.Contains("見つかりません") || joined.Contains("対になっていません"), joined);
        }

        /// <summary>
        /// <b>既存 Data の既定は Fade。</b> 向き無し・逆接続無しでも成立する（§3.1
        /// 「既存未指定は Fade にして既存 Data の挙動を保持」）。
        /// ここが Slide 既定になると、P5 の扉が黙ってスライドに化ける。
        /// </summary>
        [Test]
        public void FadeIsTheDefault_AndNeedsNoDirectionOrReverse()
        {
            var plain = new AreaConnectionDefinition();
            Assert.AreEqual(AreaTransitionStyle.Fade, plain.Style, "既定は Fade。");
            Assert.AreEqual(AreaConnectionDirection.None, plain.Direction, "既定は向き無し。");

            AreaConnectionData data = MakeData(
                Make("conn_door", "area_p55_a", "door_a_house", "area_p55_house", "entry_house",
                    AreaTransitionStyle.Fade, AreaConnectionDirection.None, string.Empty));

            Assert.IsTrue(AreaConnectionCatalog.TryBuild(data, out AreaConnectionCatalog catalog, out List<string> errors),
                "Fade は向き・逆接続が無くても成立する: " + string.Join(" / ", errors));
            Assert.IsTrue(catalog.TryGet(new StableId("conn_door"), out AreaConnectionSnapshot door));
            Assert.IsFalse(door.IsSlide, "扉は暗転のまま（§0）。");
        }

        /// <summary>SlideDuration が調整範囲を外れたら不合格（§3.1）。</summary>
        [Test]
        public void SlideDurationOutsideTheTuningRange_IsRefused()
        {
            AreaConnectionData data = MakeData(
                Make("conn_a_east", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.East, "conn_b_west", duration: 1.5f),
                Make("conn_b_west", "area_p55_b", "exit_b_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.West, "conn_a_east"));

            Assert.IsFalse(AreaConnectionCatalog.TryBuild(data, out _, out List<string> errors));
            StringAssert.Contains("SlideDuration", string.Join(" / ", errors));
        }

        /// <summary>自己接続は作らせない。</summary>
        [Test]
        public void SelfConnection_IsRefused()
        {
            AreaConnectionData data = MakeData(
                Make("conn_self", "area_p55_a", "exit_a_east", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Fade, AreaConnectionDirection.None, string.Empty));

            Assert.IsFalse(AreaConnectionCatalog.TryBuild(data, out _, out List<string> errors));
            StringAssert.Contains("同じエリア", string.Join(" / ", errors));
        }

        /// <summary>Data 側の検査も同じ規則で落ちる（Asset 検査と実行時で食い違わない）。</summary>
        [Test]
        public void DataValidation_UsesTheSameRules()
        {
            AreaConnectionData data = MakeData(
                Make("conn_bad", "area_p55_a", "exit_a_east", "area_p55_b", "entry_b_west",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.None, "conn_back"),
                Make("conn_back", "area_p55_b", "exit_b_west", "area_p55_a", "entry_a_east",
                    AreaTransitionStyle.Slide, AreaConnectionDirection.West, "conn_bad"));

            var report = new DataValidationReport();
            data.Validate(report);

            Assert.IsTrue(report.HasErrors, "Data 検査でも同じ不整合で落ちる。");
            StringAssert.Contains("向きが未指定", string.Join(" / ", report.Errors));
        }

        /// <summary>同じ AreaId でも読込世代が違えば別の実体として扱う（§4.3）。</summary>
        [Test]
        public void AreaInstanceHandle_DistinguishesReloadsOfTheSameArea()
        {
            var first = new AreaInstanceHandle(new StableId("area_p55_a"), 1);
            var second = new AreaInstanceHandle(new StableId("area_p55_a"), 2);

            Assert.AreNotEqual(first, second, "同じ AreaId でも世代が違えば別の実体。");
            Assert.AreEqual(first, new AreaInstanceHandle(new StableId("area_p55_a"), 1));
            Assert.IsTrue(first.IsValid);
            Assert.IsFalse(AreaInstanceHandle.None.IsValid);
            Assert.IsFalse(new AreaInstanceHandle(new StableId("area_p55_a"), 0).IsValid, "世代 0 は無効。");
        }
    }
}
