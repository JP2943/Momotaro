using Momotaro.Core.Identification;
using Momotaro.Data.World;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P5.5 の E02（受理の優先関係と再入拒否）・E03（世代照合）・E06（Commit だけが確定）・
    /// E08（通知と後始末の分離）・E04 の在留規則（P5.5 仕様書 §11）。
    ///
    /// <b>純粋ロジックだけを見る。</b> Scene・カメラ・表示代理は Infrastructure／Presentation の担当で、
    /// ここが守るのは「受け付けてよいか」「今の世代か」「成功を確定してよいか」の 3 つ。
    /// </summary>
    public sealed class P55SlideCoordinatorTests
    {
        /// <summary>受付条件を任意に作れる供給元。</summary>
        private sealed class Conditions : IAreaTransitionConditions
        {
            public bool IsAreaReady { get; set; } = true;
            public GameMode Mode { get; set; } = GameMode.Exploration;
            public bool IsPlayerAlive { get; set; } = true;
            public bool IsPlayerBusy { get; set; }
            public bool IsEncounterActive { get; set; }
        }

        private static AreaConnectionSnapshot EastConnection()
        {
            var def = new AreaConnectionDefinition();
            def.Configure(new StableId("conn_a_east"), new StableId("area_p55_a"), new StableId("exit_a_east"),
                new StableId("area_p55_b"), new StableId("entry_b_west"),
                AreaTransitionStyle.Slide, AreaConnectionDirection.East, new StableId("conn_b_west"));

            var data = UnityEngine.ScriptableObject.CreateInstance<AreaConnectionData>();
            data.name = "SO_Conn_Test";
            var rev = new AreaConnectionDefinition();
            rev.Configure(new StableId("conn_b_west"), new StableId("area_p55_b"), new StableId("exit_b_west"),
                new StableId("area_p55_a"), new StableId("entry_a_east"),
                AreaTransitionStyle.Slide, AreaConnectionDirection.West, new StableId("conn_a_east"));
            data.SetConnections(new[] { def, rev });

            Assert.IsTrue(AreaConnectionCatalog.TryBuild(data, out AreaConnectionCatalog catalog, out _));
            Assert.IsTrue(catalog.TryGet(new StableId("conn_a_east"), out AreaConnectionSnapshot snapshot));
            UnityEngine.Object.DestroyImmediate(data);
            return snapshot;
        }

        // ------------------------------------------------------------------ E02

        /// <summary>E02：死亡・戦闘開始・Pause・行動中・未 Ready では受理しない（§6.1）。</summary>
        [Test]
        public void Admission_KeepsThePhase5PriorityOrder()
        {
            var c = new AreaSlideCoordinator();
            AreaConnectionSnapshot conn = EastConnection();

            Assert.AreEqual(AreaTransitionRejection.NotReady,
                c.TryRequest(conn, new Conditions { IsAreaReady = false }).Rejection, "未 AreaReady。");

            Assert.AreEqual(AreaTransitionRejection.WrongMode,
                c.TryRequest(conn, new Conditions { Mode = GameMode.Paused }).Rejection, "Paused。");

            Assert.AreEqual(AreaTransitionRejection.PlayerDefeated,
                c.TryRequest(conn, new Conditions { IsPlayerAlive = false }).Rejection, "死亡。");

            // <b>戦闘開始が行動中より先。</b> 両方立っていても EncounterActive を返す（§8.3 の競合表）。
            Assert.AreEqual(AreaTransitionRejection.EncounterActive,
                c.TryRequest(conn, new Conditions { IsEncounterActive = true, IsPlayerBusy = true }).Rejection,
                "戦闘開始が優先。");

            Assert.AreEqual(AreaTransitionRejection.PlayerBusy,
                c.TryRequest(conn, new Conditions { IsPlayerBusy = true }).Rejection, "行動中。");

            Assert.AreEqual(0, c.AcceptedCount, "どれも受理していない。");
            Assert.AreEqual(AreaSlideTransactionPhase.Idle, c.Phase, "拒否は段階を変えない。");
        }

        /// <summary>E02：受理済みの最中は再入を断る。</summary>
        [Test]
        public void SecondRequestWhileTransitioning_IsRefused()
        {
            var c = new AreaSlideCoordinator();
            AreaConnectionSnapshot conn = EastConnection();
            var ok = new Conditions();

            AreaTransitionDecision first = c.TryRequest(conn, ok);
            Assert.IsTrue(first.Accepted);

            AreaTransitionDecision second = c.TryRequest(conn, ok);
            Assert.IsFalse(second.Accepted);
            Assert.AreEqual(AreaTransitionRejection.AlreadyTransitioning, second.Rejection);
            Assert.AreEqual(1, c.AcceptedCount, "受理は 1 回だけ。");
        }

        /// <summary>
        /// E02：<b>先読み中であることは拒否理由にしない</b>（§6.1 末尾）。
        /// 調停役は先読みを知らない——知っていると「近づくと動けない」を作ってしまう。
        /// </summary>
        [Test]
        public void Coordinator_DoesNotKnowAboutPreloading()
        {
            var c = new AreaSlideCoordinator();
            Assert.IsTrue(c.TryRequest(EastConnection(), new Conditions()).Accepted,
                "受付条件が揃っていれば、先読みの有無に関わらず受理する。");
        }

        /// <summary>解決できない接続では受理しない（世代も進めない）。</summary>
        [Test]
        public void InvalidConnection_IsRefusedWithoutAdvancingTheGeneration()
        {
            var c = new AreaSlideCoordinator();
            AreaTransitionDecision d = c.TryRequest(default, new Conditions());

            Assert.IsFalse(d.Accepted);
            Assert.AreEqual(AreaTransitionRejection.UnknownDestination, d.Rejection);
            Assert.AreEqual(0, c.CurrentTransitionId, "受理前の不正 Data で State を変えない（§6.2 手順 1）。");
        }

        // ------------------------------------------------------------------ E03

        /// <summary>E03：古い世代の通知は現行へ作用しない。</summary>
        [Test]
        public void StaleNotifications_DoNotAffectTheCurrentTransition()
        {
            var c = new AreaSlideCoordinator();
            AreaConnectionSnapshot conn = EastConnection();
            var ok = new Conditions();

            int first = c.TryRequest(conn, ok).TransitionId;
            Assert.IsTrue(c.NotifyPreparing(first));
            Assert.IsTrue(c.TryBeginRollback(first));
            Assert.IsTrue(c.NotifyRolledBack(first));

            int second = c.TryRequest(conn, ok).TransitionId;
            Assert.AreNotEqual(first, second, "世代は進む。");

            int stale = c.StaleNotificationCount;
            Assert.IsFalse(c.NotifyPreparing(first), "古い世代の準備通知は通らない。");
            Assert.IsFalse(c.TryCommit(first), "古い世代の確定は通らない。");
            Assert.IsFalse(c.NotifyFailed(first), "古い世代の失敗も通らない。");
            Assert.Greater(c.StaleNotificationCount, stale, "無視した数を数えている。");
            Assert.AreEqual(AreaSlideTransactionPhase.Accepted, c.Phase, "現行の段階は動いていない。");
        }

        /// <summary>E03：段階を飛ばす通知は通らない（準備前に確定できない）。</summary>
        [Test]
        public void OutOfOrderNotifications_AreRefused()
        {
            var c = new AreaSlideCoordinator();
            int id = c.TryRequest(EastConnection(), new Conditions()).TransitionId;

            Assert.IsFalse(c.TryCommit(id), "受理直後に確定はできない。");
            Assert.IsFalse(c.NotifySlideStarted(id), "準備前にスライドは始められない。");
            Assert.IsFalse(c.NotifyPrepared(id), "Preparing を経ずに Ready にはならない。");
            Assert.AreEqual(AreaSlideTransactionPhase.Accepted, c.Phase);
        }

        // ------------------------------------------------------------------ E06

        /// <summary>
        /// E06：<b>Commit だけが成功を確定し、1 回しか通らない</b>。
        /// Prepared までは「まだ着いていない」。
        /// </summary>
        [Test]
        public void OnlyCommitConfirmsArrival_AndOnlyOnce()
        {
            var c = new AreaSlideCoordinator();
            int id = c.TryRequest(EastConnection(), new Conditions()).TransitionId;

            Assert.IsTrue(c.NotifyPreparing(id));
            Assert.IsTrue(c.NotifyPrepared(id));
            Assert.AreEqual(0, c.CommittedCount, "Prepared では確定していない（§4.1）。");

            Assert.IsTrue(c.NotifySlideStarted(id));
            Assert.IsTrue(c.TryCommit(id), "スライドが終われば確定できる。");
            Assert.AreEqual(1, c.CommittedCount);
            Assert.AreEqual(AreaSlideTransactionPhase.Committed, c.Phase);

            Assert.IsFalse(c.TryCommit(id), "二度目の確定は通らない。");
            Assert.AreEqual(1, c.CommittedCount, "確定は 1 回だけ。");
        }

        /// <summary>E06：確定後は Rollback で取り消せない（§8「成功を取り消さない」）。</summary>
        [Test]
        public void CommittedTransition_CannotBeRolledBack()
        {
            var c = new AreaSlideCoordinator();
            int id = c.TryRequest(EastConnection(), new Conditions()).TransitionId;
            c.NotifyPreparing(id);
            c.NotifyPrepared(id);
            c.NotifySlideStarted(id);
            Assert.IsTrue(c.TryCommit(id));

            Assert.IsFalse(c.TryBeginRollback(id), "確定済みは戻さない。");
            Assert.IsFalse(c.NotifyFailed(id), "確定済みを失敗にしない。");
            Assert.AreEqual(AreaSlideTransactionPhase.Committed, c.Phase);
        }

        // ------------------------------------------------------------------ E08

        /// <summary>
        /// E08：到着通知は<b>確定とは別に、1 回だけ</b>取り出す（§6.2 手順 10）。
        /// 通知の中から次の遷移を要求でき、旧後始末がそれを消さない。
        /// </summary>
        [Test]
        public void ArrivalAnnouncement_IsSeparateFromCommitAndFiresOnce()
        {
            var c = new AreaSlideCoordinator();
            AreaConnectionSnapshot conn = EastConnection();
            var ok = new Conditions();

            int id = c.TryRequest(conn, ok).TransitionId;
            c.NotifyPreparing(id);
            c.NotifyPrepared(id);
            c.NotifySlideStarted(id);
            Assert.IsTrue(c.TryCommit(id));

            Assert.IsTrue(c.TryConsumeArrivalAnnouncement(id), "確定後に 1 回だけ通知を出せる。");
            Assert.IsFalse(c.TryConsumeArrivalAnnouncement(id), "二度は出さない。");

            // 通知の中から次を要求する。
            Assert.IsTrue(c.Release(id), "後始末を終えて排他を解く。");
            AreaTransitionDecision next = c.TryRequest(conn, ok);
            Assert.IsTrue(next.Accepted, "通知の中から次の遷移を要求できる。");
            Assert.AreNotEqual(id, next.TransitionId);

            // 旧世代の後始末が新しい要求を消さない。
            Assert.IsFalse(c.Release(id), "旧世代の Release は通らない。");
            Assert.AreEqual(AreaSlideTransactionPhase.Accepted, c.Phase, "新しい要求は生きている。");
        }

        /// <summary>確定していない世代の通知は出せない。</summary>
        [Test]
        public void AnnouncementBeforeCommit_IsRefused()
        {
            var c = new AreaSlideCoordinator();
            int id = c.TryRequest(EastConnection(), new Conditions()).TransitionId;
            c.NotifyPreparing(id);

            Assert.IsFalse(c.TryConsumeArrivalAnnouncement(id), "確定前に到着を通知しない（§6.2）。");
        }

        /// <summary>失敗しても次の要求は受けられる（詰まらせない）。</summary>
        [Test]
        public void AfterFailure_TheNextRequestIsStillAccepted()
        {
            var c = new AreaSlideCoordinator();
            AreaConnectionSnapshot conn = EastConnection();
            var ok = new Conditions();

            int id = c.TryRequest(conn, ok).TransitionId;
            c.NotifyPreparing(id);
            Assert.IsTrue(c.NotifyFailed(id));
            Assert.AreEqual(AreaSlideTransactionPhase.Failed, c.Phase);
            Assert.IsFalse(c.IsTransitioning, "戻れなくても次の操作は塞がない。");

            Assert.IsTrue(c.TryRequest(conn, ok).Accepted);
        }

        // ------------------------------------------------------------------ E04（在留規則）

        /// <summary>E04：Active は最大 1、読込済みは最大 2（§9.1）。</summary>
        [Test]
        public void Residency_AllowsOneActiveAndTwoResidents()
        {
            var ledger = new AreaResidencyLedger();
            AreaInstanceHandle a = ledger.NextHandle(new StableId("area_p55_a"));
            AreaInstanceHandle b = ledger.NextHandle(new StableId("area_p55_b"));
            AreaInstanceHandle c = ledger.NextHandle(new StableId("area_p55_c"));

            Assert.IsTrue(ledger.TryAdmitStaged(a));
            Assert.IsTrue(ledger.TrySetPhase(a, AreaActivationPhase.Active));
            Assert.IsTrue(ledger.TryAdmitStaged(b), "先読みで 2 つ目まで載る。");

            Assert.IsFalse(ledger.TryAdmitStaged(c), "3 つ目は載せない（§1.2）。");
            Assert.AreEqual(1, ledger.BlockedAdmissionCount);
            Assert.AreEqual(2, ledger.ResidentCount);

            Assert.IsFalse(ledger.TrySetPhase(b, AreaActivationPhase.Active),
                "2 つ目を活動させない（境界越しの索敵・Interact が起きる）。");
            Assert.AreEqual(a, ledger.ActiveArea);

            Assert.IsTrue(ledger.SatisfiesResidencyRules(out string violation), violation);
        }

        /// <summary>E04：交代は「旧を降ろしてから新を上げる」順で成立する。</summary>
        [Test]
        public void Residency_HandsOverByLoweringTheOldOneFirst()
        {
            var ledger = new AreaResidencyLedger();
            AreaInstanceHandle a = ledger.NextHandle(new StableId("area_p55_a"));
            AreaInstanceHandle b = ledger.NextHandle(new StableId("area_p55_b"));

            ledger.TryAdmitStaged(a);
            ledger.TrySetPhase(a, AreaActivationPhase.Active);
            ledger.TryAdmitStaged(b);
            ledger.TrySetPhase(b, AreaActivationPhase.Prepared);

            Assert.IsTrue(ledger.TrySetPhase(a, AreaActivationPhase.Suspended), "先に旧を降ろす。");
            Assert.IsTrue(ledger.TrySetPhase(b, AreaActivationPhase.Active), "そのあと新を上げる。");
            Assert.AreEqual(b, ledger.ActiveArea);

            Assert.IsTrue(ledger.TrySetPhase(a, AreaActivationPhase.Retiring));
            Assert.IsTrue(ledger.Remove(a), "撤去すると台帳から外れる。");
            Assert.AreEqual(1, ledger.ResidentCount);
        }

        /// <summary>E04：Scene 操作は直列化する（重ねない）。</summary>
        [Test]
        public void Residency_SerializesSceneOperations()
        {
            var ledger = new AreaResidencyLedger();

            Assert.IsTrue(ledger.TryBeginSceneOperation(), "1 つ目は始められる。");
            Assert.IsFalse(ledger.TryBeginSceneOperation(), "走っている間は重ねない（§5 末尾）。");
            Assert.AreEqual(1, ledger.BlockedSceneOperationCount);

            ledger.EndSceneOperation();
            Assert.IsTrue(ledger.TryBeginSceneOperation(), "終端したら次を始められる。");
        }

        /// <summary>E03／E04：同じ AreaId を往復しても、前の実体を現行と取り違えない。</summary>
        [Test]
        public void Residency_DistinguishesReloadsOfTheSameArea()
        {
            var ledger = new AreaResidencyLedger();
            var areaA = new StableId("area_p55_a");

            AreaInstanceHandle first = ledger.NextHandle(areaA);
            ledger.TryAdmitStaged(first);
            ledger.TrySetPhase(first, AreaActivationPhase.Active);
            ledger.TrySetPhase(first, AreaActivationPhase.Retiring);
            ledger.Remove(first);

            AreaInstanceHandle second = ledger.NextHandle(areaA);
            Assert.IsTrue(ledger.TryAdmitStaged(second));

            Assert.AreNotEqual(first, second, "同じ AreaId でも別の実体。");
            Assert.AreEqual(AreaActivationPhase.Unloaded, ledger.PhaseOf(first),
                "撤去済みの実体は現行として引けない。");
            Assert.AreEqual(AreaActivationPhase.Staged, ledger.PhaseOf(second));
            Assert.IsFalse(ledger.TrySetPhase(first, AreaActivationPhase.Active),
                "撤去済みの実体を活動させない。");
        }
    }
}
