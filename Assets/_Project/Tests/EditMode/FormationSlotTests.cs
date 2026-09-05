using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-02：隊列位置の解決（<see cref="FormationSlot"/>）を検証する。主人公の後方 V 字配置、主人公の向きに追従して
    /// 隊列も回ること、前方不定・番号や間隔の異常値でも破綻しないことを固定する。純粋関数のため決定的に検証できる。
    /// </summary>
    public sealed class FormationSlotTests
    {
        private const float Spacing = 2f;
        private static readonly Vector3 Leader = new Vector3(10f, 1.5f, -4f);

        private static void AssertPosition(Vector3 actual, float x, float y, float z, string message)
        {
            Assert.AreEqual(x, actual.x, 1e-4f, message + " (x)");
            Assert.AreEqual(y, actual.y, 1e-4f, message + " (y)");
            Assert.AreEqual(z, actual.z, 1e-4f, message + " (z)");
        }

        [Test]
        public void Slot0_IsBehindLeftOfLeader()
        {
            Vector3 p = FormationSlot.Resolve(Leader, Vector3.forward, 0, Spacing);

            // 前方 +Z のとき、後方は -Z、左は -X。
            AssertPosition(p, Leader.x - (0.6f * Spacing), Leader.y, Leader.z - Spacing, "0 番は後方やや左");
        }

        [Test]
        public void Slot1_IsBehindRightOfLeader()
        {
            Vector3 p = FormationSlot.Resolve(Leader, Vector3.forward, 1, Spacing);

            AssertPosition(p, Leader.x + (0.6f * Spacing), Leader.y, Leader.z - Spacing, "1 番は後方やや右");
        }

        [Test]
        public void Slot2_IsFurtherBehind_OnCenterLine()
        {
            Vector3 p = FormationSlot.Resolve(Leader, Vector3.forward, 2, Spacing);

            AssertPosition(p, Leader.x, Leader.y, Leader.z - (1.8f * Spacing), "2 番はさらに後方の中央");
        }

        [Test]
        public void Slots_RotateWithLeaderForward()
        {
            // 前方 +X のとき、後方は -X、右は -Z。
            Vector3 p = FormationSlot.Resolve(Leader, Vector3.right, 1, Spacing);

            AssertPosition(p, Leader.x - Spacing, Leader.y, Leader.z - (0.6f * Spacing), "隊列は主人公の向きに追従して回る");
        }

        [Test]
        public void LeaderForward_IsFlattenedToXZ()
        {
            // 高さ成分は無視して XZ で正規化する（斜面や見下ろしでも隊列が伸び縮みしない）。
            Vector3 flat = FormationSlot.Resolve(Leader, Vector3.forward, 0, Spacing);
            Vector3 tilted = FormationSlot.Resolve(Leader, new Vector3(0f, 5f, 3f), 0, Spacing);

            AssertPosition(tilted, flat.x, flat.y, flat.z, "Y 成分は隊列位置に影響しない");
        }

        [Test]
        public void ZeroForward_FallsBackToWorldForward()
        {
            Vector3 zero = FormationSlot.Resolve(Leader, Vector3.zero, 0, Spacing);
            Vector3 forward = FormationSlot.Resolve(Leader, Vector3.forward, 0, Spacing);

            AssertPosition(zero, forward.x, forward.y, forward.z, "前方不定なら +Z とみなす（例外にしない）");
        }

        [Test]
        public void SlotHeight_MatchesLeader()
        {
            Vector3 p = FormationSlot.Resolve(Leader, Vector3.forward, 0, Spacing);

            Assert.AreEqual(Leader.y, p.y, 1e-4f, "高さは主人公に合わせる（接地は Motor の責務）。");
        }

        [Test]
        public void NegativeSlotIndex_IsTreatedAsZero()
        {
            Vector3 negative = FormationSlot.Resolve(Leader, Vector3.forward, -3, Spacing);
            Vector3 zero = FormationSlot.Resolve(Leader, Vector3.forward, 0, Spacing);

            AssertPosition(negative, zero.x, zero.y, zero.z, "負の番号は 0 番として扱う");
        }

        [Test]
        public void IndexBeyondLayout_RepeatsFurtherBehind()
        {
            Vector3 first = FormationSlot.Resolve(Leader, Vector3.forward, 0, Spacing);
            Vector3 fourth = FormationSlot.Resolve(Leader, Vector3.forward, 3, Spacing);

            Assert.AreEqual(3, FormationSlot.LayoutCount, "並びの定義は 3 体ぶん（犬・猿・雉）。");
            Assert.AreEqual(first.x, fourth.x, 1e-4f, "4 体目は 0 番と同じ横位置。");
            Assert.AreEqual(first.z - Spacing, fourth.z, 1e-4f, "4 体目は 1 周ぶん後方へ下がる。");
        }

        [Test]
        public void NegativeSpacing_CollapsesToLeaderPosition()
        {
            Vector3 p = FormationSlot.Resolve(Leader, Vector3.forward, 1, -5f);

            AssertPosition(p, Leader.x, Leader.y, Leader.z, "間隔の負値は 0 として扱う（設定ミスで飛んでいかない）");
        }

        [Test]
        public void HorizontalDistance_IgnoresHeight()
        {
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(3f, 100f, 4f);

            Assert.AreEqual(5f, FormationSlot.HorizontalDistance(a, b), 1e-4f, "高さの差は距離に含めない。");
        }
    }
}
