using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Perception;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03：仲間の対象選択（<see cref="CompanionTargetSelection"/>）を検証する。捕捉・維持（ヒステリシス）・同距離の決定性・
    /// 無効化された対象の手放しを固定する。純粋関数のため Registry も Transform も要らず決定的に検証できる。
    /// </summary>
    public sealed class CompanionTargetSelectionTests
    {
        private sealed class FakeTarget : IPerceptionTarget
        {
            public int ActorId { get; set; }
            public CombatFaction Faction { get; set; } = CombatFaction.Enemy;
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;

            public FakeTarget(int id, float z)
            {
                ActorId = id;
                Position = new Vector3(0f, 0f, z);
            }
        }

        private const float Acquire = 8f;
        private const float Lose = 12f;

        private static bool Select(IReadOnlyList<IPerceptionTarget> candidates, IPerceptionTarget current,
            out IPerceptionTarget selected, float acquire = Acquire, float lose = Lose)
        {
            return CompanionTargetSelection.TrySelect(candidates, Vector3.zero, current, acquire, lose, out selected);
        }

        // ---- 捕捉 ----

        [Test]
        public void NoCandidates_SelectsNothing()
        {
            Assert.IsFalse(Select(new List<IPerceptionTarget>(), null, out IPerceptionTarget selected));
            Assert.IsNull(selected);
        }

        [Test]
        public void NullCandidates_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => Select(null, null, out IPerceptionTarget _));
        }

        [Test]
        public void SelectsNearestWithinAcquireRange()
        {
            var far = new FakeTarget(1, 6f);
            var near = new FakeTarget(2, 3f);

            Assert.IsTrue(Select(new List<IPerceptionTarget> { far, near }, null, out IPerceptionTarget selected));
            Assert.AreSame(near, selected, "捕捉距離の内側で最も近い敵を選ぶ。");
        }

        [Test]
        public void IgnoresCandidatesBeyondAcquireRange()
        {
            var beyond = new FakeTarget(1, Acquire + 0.1f);

            Assert.IsFalse(Select(new List<IPerceptionTarget> { beyond }, null, out IPerceptionTarget _),
                "捕捉距離の外は新規に狙わない。");
        }

        [Test]
        public void AcquiresExactlyAtRange()
        {
            var atRange = new FakeTarget(1, Acquire);

            Assert.IsTrue(Select(new List<IPerceptionTarget> { atRange }, null, out IPerceptionTarget selected),
                "捕捉距離と一致は捕捉できる。");
            Assert.AreSame(atRange, selected);
        }

        [Test]
        public void ZeroAcquireRange_MeansUnlimited()
        {
            var veryFar = new FakeTarget(1, 500f);

            Assert.IsTrue(Select(new List<IPerceptionTarget> { veryFar }, null, out IPerceptionTarget _,
                acquire: 0f, lose: 0f), "0 は無制限として扱う。");
        }

        [Test]
        public void IgnoresInactiveCandidates()
        {
            var dead = new FakeTarget(1, 2f) { IsActive = false };
            var alive = new FakeTarget(2, 5f);

            Assert.IsTrue(Select(new List<IPerceptionTarget> { dead, alive }, null, out IPerceptionTarget selected));
            Assert.AreSame(alive, selected, "撃破・退場した候補は近くても選ばない。");
        }

        // ---- 維持（ヒステリシス） ----

        [Test]
        public void KeepsCurrentTarget_EvenIfCloserAppears()
        {
            var current = new FakeTarget(1, 7f);
            var closer = new FakeTarget(2, 1f);

            Assert.IsTrue(Select(new List<IPerceptionTarget> { current, closer }, current, out IPerceptionTarget selected));
            Assert.AreSame(current, selected, "維持できる間は乗り換えない（毎フレーム対象が入れ替わらない）。");
        }

        [Test]
        public void KeepsCurrentTarget_BetweenAcquireAndLoseRange()
        {
            var current = new FakeTarget(1, 10f); // 捕捉距離の外・見失い距離の内。

            Assert.IsTrue(Select(new List<IPerceptionTarget> { current }, current, out IPerceptionTarget selected));
            Assert.AreSame(current, selected, "捕捉できない距離でも、見失うまでは狙い続ける。");
        }

        [Test]
        public void DropsCurrentTarget_BeyondLoseRange()
        {
            var current = new FakeTarget(1, Lose + 0.1f);

            Assert.IsFalse(Select(new List<IPerceptionTarget> { current }, current, out IPerceptionTarget _),
                "見失い距離を超えたら手放す。");
        }

        [Test]
        public void DropsCurrentTarget_WhenItBecomesInactive()
        {
            var current = new FakeTarget(1, 3f) { IsActive = false };
            var other = new FakeTarget(2, 5f);

            Assert.IsTrue(Select(new List<IPerceptionTarget> { current, other }, current, out IPerceptionTarget selected));
            Assert.AreSame(other, selected, "撃破された対象は手放して次を捕捉する。");
        }

        [Test]
        public void DropsCurrentTarget_WhenNoLongerACandidate()
        {
            var current = new FakeTarget(1, 3f);
            var other = new FakeTarget(2, 5f);

            // current が候補一覧から消えた（範囲外・登録解除）。
            Assert.IsTrue(Select(new List<IPerceptionTarget> { other }, current, out IPerceptionTarget selected));
            Assert.AreSame(other, selected);
        }

        // ---- 決定性 ----

        [Test]
        public void EqualDistance_PrefersSmallerActorId()
        {
            var a = new FakeTarget(9, 4f);
            var b = new FakeTarget(3, 4f);

            Select(new List<IPerceptionTarget> { a, b }, null, out IPerceptionTarget first);
            Select(new List<IPerceptionTarget> { b, a }, null, out IPerceptionTarget second);

            Assert.AreSame(b, first, "同距離は Actor ID の小さい方。");
            Assert.AreSame(b, second, "候補の並び順が変わっても結果は同じ。");
        }

        [Test]
        public void IsUsable_RejectsNullAndInactive()
        {
            Assert.IsFalse(CompanionTargetSelection.IsUsable(null));
            Assert.IsFalse(CompanionTargetSelection.IsUsable(new FakeTarget(1, 0f) { IsActive = false }));
            Assert.IsTrue(CompanionTargetSelection.IsUsable(new FakeTarget(1, 0f)));
        }
    }
}
