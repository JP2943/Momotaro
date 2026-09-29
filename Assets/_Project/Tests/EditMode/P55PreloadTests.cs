using System.Collections.Generic;
using Momotaro.Core.Identification;
using Momotaro.Gameplay.Session;
using NUnit.Framework;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 隣 Area の先読み（P5.5 仕様書 §4.1／§5／§11 の E04）。
    ///
    /// 固めるのは 4 つ——<b>同一先の再利用</b>、<b>候補切替</b>、<b>最大 2 Area</b>、
    /// <b>ロードの直列化</b>。どれも「プレイヤーが出入口の間を行き来する」だけで踏む道で、
    /// 外すと二重ロードか、撤去されない Area の積み上がりになる。
    ///
    /// Scene API の向こう側は差し替えるので、ここは決定的に回る。
    /// 実 Scene での先読みは PlayMode（<c>P55PreloadPlayTests</c>）で見る。
    /// </summary>
    public sealed class P55PreloadTests
    {
        private static readonly StableId AreaB = new StableId("area_b");
        private static readonly StableId AreaC = new StableId("area_c");
        private const string PathB = "Assets/Scenes/B.unity";
        private const string PathC = "Assets/Scenes/C.unity";

        private AreaResidencyLedger _ledger;
        private FakeHost _host;
        private AreaPreloader _preloader;
        private readonly object _owner = new object();

        [SetUp]
        public void SetUp()
        {
            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
            _ledger = new AreaResidencyLedger();
            _host = new FakeHost();
            _preloader = new AreaPreloader(_ledger, _host, _owner);
        }

        [TearDown]
        public void TearDown()
        {
            AreaStagingRequest.ResetForTests();
            AreaBundleDirectory.ClearForTests();
            CurrentAreaProvider.ClearForTests();
        }

        // ---------------------------------------------------------------- 同一先再利用

        [Test]
        public void RequestingTheSameDestinationTwice_DoesNotLoadItAgain()
        {
            Assert.IsTrue(_preloader.Request(AreaB, PathB));
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase);
            Assert.AreEqual(1, _host.LoadCount);

            // 読込中に同じ先を頼み直しても読み直さない。
            Assert.IsTrue(_preloader.Request(AreaB, PathB));
            Assert.AreEqual(1, _host.LoadCount, "読込中の同一先は読み直さない。");

            _host.CompleteLoad(1234);
            _preloader.Poll();
            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase);
            Assert.AreEqual(1234, _preloader.StagedSceneHandle);

            // 読み終わってから同じ先を頼み直しても読み直さない。
            Assert.IsTrue(_preloader.Request(AreaB, PathB));
            _preloader.Poll();
            Assert.AreEqual(1, _host.LoadCount, "Staged の同一先も読み直さない。");
            Assert.AreEqual(0, _host.UnloadCount, "撤去も起きない。");
            Assert.AreEqual(2, _preloader.ReusedCount);
        }

        [Test]
        public void TheStagedAreaIsAdmittedToTheLedgerAsStaged()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1234);
            _preloader.Poll();

            Assert.AreEqual(1, _ledger.ResidentCount);
            Assert.AreEqual(AreaActivationPhase.Staged, _ledger.PhaseOf(_preloader.StagedArea));
            Assert.IsFalse(_ledger.IsSceneOperationInFlight, "読み終わったら Scene 操作は閉じる。");
            Assert.IsTrue(_ledger.SatisfiesResidencyRules(out string _));
        }

        // ---------------------------------------------------------------- 遷移への引き渡し

        /// <summary>
        /// 遷移が引き取ったら、<b>先読みはもうその Area の面倒を見ない</b>（§6.2 手順 4）。
        ///
        /// 引き取りを忘れると、次に別の候補を望んだ瞬間に<b>いま遊んでいる Area を unload しにかかる</b>。
        /// </summary>
        [Test]
        public void HandingTheStagedAreaToTheTransition_KeepsTheSceneAndTheResidencySlot()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1234);
            _preloader.Poll();
            AreaInstanceHandle staged = _preloader.StagedArea;

            Assert.IsTrue(_preloader.TryHandOffStaged(AreaB, out AreaInstanceHandle handed, out int scene));
            Assert.AreEqual(staged, handed, "引き渡すのは Staged だった実体そのもの。");
            Assert.AreEqual(1234, scene, "Scene handle も一緒に渡す。");

            Assert.AreEqual(0, _host.UnloadCount, "撤去はしない（実 Scene は載ったまま）。");
            Assert.AreEqual(1, _ledger.ResidentCount,
                "在留枠は埋まったまま（空きがあると誤認して 3 枚目を読まない）。");
            Assert.AreEqual(AreaPreloadPhase.Idle, _preloader.Phase, "先読みは手ぶらへ戻る。");
            Assert.IsFalse(_preloader.StagedArea.IsValid, "預かりは残らない。");
            Assert.AreEqual(1, _preloader.HandedOffCount);

            // 引き渡したあとに別の候補を望んでも、渡した Area は撤去されない。
            _preloader.Request(AreaC, PathC);
            Assert.AreEqual(0, _host.UnloadCount, "遊んでいる Area を unload しにかからない。");
            Assert.AreEqual(2, _host.LoadCount, "新しい候補は普通に読む。");
        }

        /// <summary>
        /// <b>宛先違いは渡さない</b>（§6.2 手順 1「Area／Scene 世代を検証し」）。
        /// 「いま Staged なもの」を無条件に渡す形だと、行き先と違う Area を活動させられる。
        /// </summary>
        [Test]
        public void HandingOff_RefusesWhenTheNamedAreaIsNotTheStagedOne()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1234);
            _preloader.Poll();

            Assert.IsFalse(_preloader.TryHandOffStaged(AreaC, out AreaInstanceHandle handed, out int scene));
            Assert.IsFalse(handed.IsValid);
            Assert.AreEqual(0, scene);
            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase, "預かりはそのまま。");
            Assert.AreEqual(1234, _preloader.StagedSceneHandle);
            Assert.AreEqual(0, _preloader.HandedOffCount);
        }

        /// <summary>読込中・手ぶらのときは渡せない（まだ誰も引き取れるものを持っていない）。</summary>
        [Test]
        public void HandingOff_RefusesBeforeTheDestinationIsStaged()
        {
            Assert.IsFalse(_preloader.TryHandOffStaged(AreaB, out _, out _), "手ぶらでは渡せない。");

            _preloader.Request(AreaB, PathB);
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase);
            Assert.IsFalse(_preloader.TryHandOffStaged(AreaB, out _, out _), "読込中は渡せない。");
            Assert.AreEqual(0, _preloader.HandedOffCount);
        }

        // ---------------------------------------------------------------- 候補切替

        [Test]
        public void SwitchingTheCandidate_RetiresTheOldOneBeforeLoadingTheNew()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1111);
            _preloader.Poll();
            AreaInstanceHandle staledB = _preloader.StagedArea;

            // 望む先を言い換えるだけ。撤去と読込の順序は呼び出し側が組まない。
            _preloader.Request(AreaC, PathC);

            Assert.AreEqual(AreaPreloadPhase.Releasing, _preloader.Phase,
                "先に撤去する（2 枚載せたまま 3 枚目を読まない）。");
            Assert.AreEqual(1, _host.UnloadCount);
            Assert.AreEqual(1111, _host.LastUnloadHandle, "撤去するのは前の Scene。");
            Assert.AreEqual(1, _host.LoadCount, "まだ新しい方は読んでいない。");

            _host.CompleteUnload();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "撤去が終わってから読む。");
            Assert.AreEqual(2, _host.LoadCount);
            Assert.AreEqual(AreaActivationPhase.Unloaded, _ledger.PhaseOf(staledB),
                "前の実体は台帳から消える。");

            _host.CompleteLoad(2222);
            _preloader.Poll();
            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase);
            Assert.AreEqual(AreaC, _preloader.StagedArea.AreaId);
            Assert.AreEqual(1, _ledger.ResidentCount, "積み上がらない。");
            Assert.AreEqual(1, _preloader.SwitchedCount);
        }

        [Test]
        public void SwitchingWhileStillLoading_TakesEffectWhenTheLoadFinishes()
        {
            _preloader.Request(AreaB, PathB);
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase);

            // 読込中に望む先が変わる（プレイヤーが別の出入口へ向き直した）。
            // <b>走っている読込は止められない</b>（Unity の非同期ロードはキャンセル不可）。
            // だから読み終わってから切り替える。
            _preloader.Request(AreaC, PathC);
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "走っている読込は止めない。");
            Assert.AreEqual(1, _host.LoadCount);

            _host.CompleteLoad(1111);
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Releasing, _preloader.Phase,
                "読み終わった B を撤去してから C を読む。");
            _host.CompleteUnload();
            _preloader.Poll();
            _host.CompleteLoad(2222);
            _preloader.Poll();

            Assert.AreEqual(AreaC, _preloader.StagedArea.AreaId);
            Assert.AreEqual(1, _ledger.ResidentCount);
        }

        [Test]
        public void ClearingTheRequest_RetiresTheStagedArea()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1111);
            _preloader.Poll();

            _preloader.ClearRequest();
            Assert.AreEqual(AreaPreloadPhase.Releasing, _preloader.Phase);
            _host.CompleteUnload();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Idle, _preloader.Phase);
            Assert.AreEqual(0, _ledger.ResidentCount, "撤去したら台帳からも消える。");
            Assert.AreEqual(0, _host.LoadCount - 1, "読み直しはしない。");
        }

        // ---------------------------------------------------------------- 最大 2 Area

        [Test]
        public void AtCapacity_ThePreloadIsRefusedInsteadOfPilingUp()
        {
            // 活動中 1 ＋ 遷移で読んだ 1 ＝ 上限。
            AreaInstanceHandle active = _ledger.NextHandle(new StableId("area_a"));
            Assert.IsTrue(_ledger.TryAdmitStaged(active));
            Assert.IsTrue(_ledger.TrySetPhase(active, AreaActivationPhase.Active));
            AreaInstanceHandle second = _ledger.NextHandle(new StableId("area_d"));
            Assert.IsTrue(_ledger.TryAdmitStaged(second));
            Assert.AreEqual(AreaResidencyLedger.MaxResidentAreas, _ledger.ResidentCount);

            _preloader.Request(AreaB, PathB);

            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase);
            Assert.AreEqual(AreaPreloadRejection.AtCapacity, _preloader.LastRejection);
            Assert.AreEqual(0, _host.LoadCount, "3 枚目は読まない。");
            Assert.AreEqual(AreaResidencyLedger.MaxResidentAreas, _ledger.ResidentCount,
                "断ったぶんは台帳に残さない。");
            Assert.IsFalse(_ledger.IsSceneOperationInFlight, "断っても Scene 操作を掴んだままにしない。");
            Assert.IsFalse(AreaStagingRequest.IsRequested, "先読み要求も残さない。");

            // 空きができても<b>自動では再試行しない</b>（§5）。
            Assert.IsTrue(_ledger.Remove(second));
            _preloader.Poll();
            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase, "自動では読み直さない。");
            Assert.AreEqual(0, _host.LoadCount);

            // 新しい遷移操作で許可されたときに初めて読む。
            Assert.IsTrue(_preloader.ArmRetry());
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "許可されれば読み直せる。");
        }

        // ---------------------------------------------------------------- 直列化

        [Test]
        public void WhileAnotherSceneOperationRuns_ThePreloadWaitsInsteadOfFailing()
        {
            Assert.IsTrue(_ledger.TryBeginSceneOperation(), "前提：遷移が Scene 操作を掴んでいる。");

            _preloader.Request(AreaB, PathB);

            // <b>失敗にしない。</b> 遷移中の読込と先読みがかち合うのは正常で、
            // 失敗扱いにすると「遷移のたびに先読みが諦める」ことになる。
            Assert.AreEqual(AreaPreloadPhase.Idle, _preloader.Phase);
            Assert.AreEqual(0, _host.LoadCount, "重ねて読まない（§5 の直列化）。");
            Assert.AreEqual(1, _ledger.BlockedSceneOperationCount);
            Assert.AreEqual(0, _preloader.RefusedCount, "断りとして数えない。");

            _ledger.EndSceneOperation();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "空いたら読み始める。");
            Assert.AreEqual(1, _host.LoadCount);
        }

        // ---------------------------------------------------------------- 失敗

        [Test]
        public void AFailedLoad_LeavesNoStagingRequestAndDoesNotRetryByItself()
        {
            _preloader.Request(AreaB, PathB);
            Assert.IsTrue(AreaStagingRequest.IsRequested, "前提：活動ゲートへ申し入れている。");

            _host.FailLoad();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase);
            Assert.IsNotEmpty(_preloader.FailureReason);

            // 残すと、次に直開きした Scene が閉じたまま起動する（＝何も動かないゲーム）。
            Assert.IsFalse(AreaStagingRequest.IsRequested, "要求を残さない。");
            Assert.AreEqual(0, _ledger.ResidentCount, "台帳にも残さない。");
            Assert.IsFalse(_ledger.IsSceneOperationInFlight);

            // <b>自動では再試行しない</b>（仕様書 §5「自動で毎フレーム再試行しない」）。
            //
            // 壊れた Scene パスのような恒久的な失敗を目の前にすると、
            // 毎フレームロードを発行し続けて出発側の進行まで巻き込む。
            for (int i = 0; i < 5; i++)
            {
                _preloader.Poll();
            }

            Assert.AreEqual(1, _host.LoadCount, "何度 Poll しても読み直さない。");
            Assert.AreEqual(AreaB, _preloader.DesiredArea, "望みは残る（外から許可されたときに読み直せる）。");
            Assert.Greater(_preloader.SuppressedRetryCount, 0);

            // 同じ先を言い直しても再試行にはならない。
            Assert.IsTrue(_preloader.Request(AreaB, PathB));
            Assert.AreEqual(1, _host.LoadCount, "同じ先の言い直しは再試行ではない。");

            // <b>新しい遷移操作で一度だけ</b>再試行できる。
            Assert.IsTrue(_preloader.ArmRetry());
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase);
            Assert.AreEqual(2, _host.LoadCount, "許可 1 回で 1 回だけ読み直す。");

            _host.FailLoad();
            _preloader.Poll();
            _preloader.Poll();
            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase);
            Assert.AreEqual(2, _host.LoadCount, "再び失敗したらまた止まる。");
        }

        [Test]
        public void SwitchingToAnotherCandidate_ClearsAPreviousLoadFailure()
        {
            _preloader.Request(AreaB, PathB);
            _host.FailLoad();
            _preloader.Poll();
            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase);

            // 別の候補は「再試行」でなく「新しい先読み」なので、前の読込失敗は持ち越さない。
            // （読込失敗では実 Scene が何も残っていないので、先に片付けるものがない。）
            Assert.IsTrue(_preloader.Request(AreaC, PathC));
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase);
            Assert.AreEqual(2, _host.LoadCount);
        }

        [Test]
        public void AFailedUnload_KeepsTheLedgerSlotAndTheHandle_WhileTheSceneIsStillLoaded()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1111);
            _preloader.Poll();
            AreaInstanceHandle staged = _preloader.StagedArea;

            _preloader.ClearRequest();
            _host.FailUnloadAndKeepScene();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.ReleaseFailed, _preloader.Phase);

            // <b>枕を返してはいけない。</b> 実際には A ＋ B が載っているのに
            // 「空きあり」と見なして C を読むと、実 Scene が 3 枚になる。
            // handle を消すと B をもう一度撤去する手も失う（GPT レビュー R10 の指摘 2）。
            Assert.AreEqual(1, _ledger.ResidentCount, "台帳の枕は持ったままにする。");
            Assert.AreEqual(staged, _preloader.StagedArea, "実体ハンドルを失わない。");
            Assert.AreEqual(1111, _preloader.StagedSceneHandle, "Scene handle も失わない。");
            Assert.IsFalse(_ledger.IsSceneOperationInFlight);

            // 新しいロードは禁止。自動で再試行もしない。
            Assert.IsTrue(_preloader.Request(AreaC, PathC));
            for (int i = 0; i < 5; i++)
            {
                _preloader.Poll();
            }

            Assert.AreEqual(1, _host.LoadCount, "撤去が終わるまで新しい Area を読まない。");
            Assert.AreEqual(1, _host.UnloadCount, "自動では撤去もやり直さない。");

            // 再試行は<b>残った Scene の撤去</b>を狭う。
            Assert.IsTrue(_preloader.ArmRetry());
            Assert.AreEqual(AreaPreloadPhase.Releasing, _preloader.Phase);
            Assert.AreEqual(2, _host.UnloadCount);
            Assert.AreEqual(1111, _host.LastUnloadHandle, "撤去するのは残っている Scene。");

            _host.CompleteUnload();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "片付いてから次を読む。");
            Assert.AreEqual(2, _host.LoadCount);
        }

        [Test]
        public void AFailedUnloadNotification_FreesTheSlotWhenTheSceneIsActuallyGone()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1111);
            _preloader.Poll();

            _preloader.ClearRequest();
            _host.FailUnloadButSceneIsGone();
            _preloader.Poll();

            // 実 Scene の不在を確かめられたのだから、枕は返す。
            // 返さないと在留上限を食い続けて、以降の先読みが一切通らなくなる（§9.1）。
            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase,
                "撤去失敗としては扱わない（Scene は消えている）。");
            Assert.AreEqual(0, _ledger.ResidentCount, "枕は返す。");
            Assert.AreEqual(0, _preloader.StagedSceneHandle);
            Assert.IsFalse(_ledger.IsSceneOperationInFlight);
        }

        [Test]
        public void AnInvalidRequest_IsRefusedWithoutTouchingAnything()
        {
            Assert.IsFalse(_preloader.Request(default, PathB));
            Assert.IsFalse(_preloader.Request(AreaB, null));
            Assert.IsFalse(_preloader.Request(AreaB, string.Empty));

            Assert.AreEqual(AreaPreloadPhase.Idle, _preloader.Phase);
            Assert.AreEqual(0, _host.LoadCount);
            Assert.AreEqual(3, _preloader.RefusedCount);
            Assert.AreEqual(AreaPreloadRejection.InvalidRequest, _preloader.LastRejection);
            Assert.IsFalse(AreaStagingRequest.IsRequested);
        }

        // ---------------------------------------------------------------- 再試行の許可と直列化

        /// <summary>
        /// 直列化待ちで<b>再試行の許可を失わない</b>（読込失敗側。GPT レビュー R11 の指摘 1）。
        ///
        /// 許可を先に消費してから直列化で弾かれると、状態は Failed のまま
        /// <b>許可だけが消える</b>。他の操作が終わっても再試行されず、
        /// ユーザーにもう一度操作を要求することになる。
        /// </summary>
        [Test]
        public void RetryPermission_SurvivesASerializationWait_ForALoad()
        {
            _preloader.Request(AreaB, PathB);
            _host.FailLoad();
            _preloader.Poll();
            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase);
            Assert.AreEqual(1, _host.LoadCount);

            // 遷移が Scene 操作を掴んでいる間に再試行を許可する。
            Assert.IsTrue(_ledger.TryBeginSceneOperation());
            Assert.IsTrue(_preloader.ArmRetry());
            Assert.AreEqual(AreaPreloadPhase.Failed, _preloader.Phase, "直列化待ちなのでまだ始まらない。");
            Assert.AreEqual(1, _host.LoadCount);

            // 何度 Poll しても始まらないが、<b>許可は残っている</b>。
            for (int i = 0; i < 3; i++)
            {
                _preloader.Poll();
            }

            Assert.AreEqual(1, _host.LoadCount);

            _ledger.EndSceneOperation();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "空いたところで 1 回だけ読む。");
            Assert.AreEqual(2, _host.LoadCount);

            // 許可は使い切っている。
            _host.FailLoad();
            _preloader.Poll();
            for (int i = 0; i < 3; i++)
            {
                _preloader.Poll();
            }

            Assert.AreEqual(2, _host.LoadCount, "許可は 1 回分しかない。");
        }

        /// <summary>直列化待ちで再試行の許可を失わない（<b>撤去失敗側</b>）。</summary>
        [Test]
        public void RetryPermission_SurvivesASerializationWait_ForARelease()
        {
            _preloader.Request(AreaB, PathB);
            _host.CompleteLoad(1111);
            _preloader.Poll();

            _preloader.ClearRequest();
            _host.FailUnloadAndKeepScene();
            _preloader.Poll();
            Assert.AreEqual(AreaPreloadPhase.ReleaseFailed, _preloader.Phase);
            Assert.AreEqual(1, _host.UnloadCount);

            Assert.IsTrue(_ledger.TryBeginSceneOperation());
            Assert.IsTrue(_preloader.ArmRetry());
            Assert.AreEqual(AreaPreloadPhase.ReleaseFailed, _preloader.Phase);
            Assert.AreEqual(1, _host.UnloadCount, "直列化待ちなのでまだ始まらない。");

            for (int i = 0; i < 3; i++)
            {
                _preloader.Poll();
            }

            Assert.AreEqual(1, _host.UnloadCount);

            _ledger.EndSceneOperation();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Releasing, _preloader.Phase, "空いたところで 1 回だけ撤去する。");
            Assert.AreEqual(2, _host.UnloadCount);
            Assert.AreEqual(1111, _host.LastUnloadHandle);
        }

        // ---------------------------------------------------------------- 旧 Area の預かり（工程 P55-10c）

        /// <summary>
        /// <b>預かった Area は、Poll を回しても手放さない</b>（§6.2 手順 11。裁定 2）。
        ///
        /// <see cref="AreaPreloader.TryAdoptRetained"/> は望みも一緒に立てる。立てないと
        /// 直後の <see cref="AreaPreloader.Poll"/> が「望まれていない Staged」と見て
        /// <b>その場で撤去しにかかる</b>——預けた意味が消える。
        ///
        /// <b>実 Scene の検査（PlayMode）ではこれを捕まえられなかった。</b> 到着した主人公は
        /// 出入口のすぐ内側に立っているので、§5 の距離による先読みが<b>同じフレームに</b>
        /// 同じ先を望み直す。望みを立てない実装でも、外から立て直されて素通りする
        /// （欠陥注入 92 で実測した）。規則そのものはここで見る。
        /// </summary>
        [Test]
        public void AdoptingARetainedArea_KeepsItStagedAcrossPolls()
        {
            AreaInstanceHandle handle = AdmitActive(AreaB);
            _host.SceneStillLoaded = true;

            Assert.IsTrue(_preloader.TryAdoptRetained(handle, 4242, PathB), "預かれる。");
            Assert.AreEqual(1, _preloader.AdoptedCount, "数えている。");

            for (int i = 0; i < 3; i++)
            {
                _preloader.Poll();
            }

            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase, "預かったまま。");
            Assert.AreEqual(4242, _preloader.StagedSceneHandle, "同じ Scene を預かっている。");
            Assert.AreEqual(0, _host.UnloadCount, "撤去しにかかっていない。");
            Assert.AreEqual(0, _host.LoadCount, "読み直してもいない。");
        }

        /// <summary>
        /// 預かった Area は<b>在留枠を返さない</b>（契約表「在留上限」）。
        /// Active から非活動へ移すだけで、Area の数は変わらない。
        /// </summary>
        [Test]
        public void TheAdoptedArea_StaysInTheLedgerAsNonActive()
        {
            AreaInstanceHandle handle = AdmitActive(AreaB);
            _host.SceneStillLoaded = true;
            Assert.AreEqual(1, _ledger.ResidentCount, "前提：台帳に 1 つある。");
            Assert.AreEqual(AreaActivationPhase.Active, _ledger.PhaseOf(handle), "前提：活動中。");

            Assert.IsTrue(_preloader.TryAdoptRetained(handle, 4242, PathB));

            Assert.AreEqual(1, _ledger.ResidentCount, "枠は返さない（実 Scene は載ったまま）。");
            Assert.AreEqual(AreaActivationPhase.Staged, _ledger.PhaseOf(handle),
                "台帳の上では非活動（Active は到着側だけ）。");
        }

        /// <summary>
        /// <b>即時の逆移動</b>：同じ先を望まれたら、預かったものをそのまま返す（契約表）。
        /// <b>読み直さない</b>——ここが「毎回 unload しない」の見返りである。
        /// </summary>
        [Test]
        public void RequestingTheAdoptedArea_ReusesItWithoutLoading()
        {
            AreaInstanceHandle handle = AdmitActive(AreaB);
            _host.SceneStillLoaded = true;
            Assert.IsTrue(_preloader.TryAdoptRetained(handle, 4242, PathB));

            Assert.IsTrue(_preloader.Request(AreaB, PathB), "同じ先を望める。");

            Assert.AreEqual(AreaPreloadPhase.Staged, _preloader.Phase, "預かったまま返せる。");
            Assert.AreEqual(0, _host.LoadCount, "読み直していない。");
            Assert.AreEqual(0, _host.UnloadCount, "撤去もしていない。");

            Assert.IsTrue(
                _preloader.TryHandOffStaged(AreaB, out AreaInstanceHandle back, out int sceneHandle),
                "遷移へ引き渡せる。");
            Assert.AreEqual(handle, back, "<b>同じ実体</b>が返る（世代も同じ）。");
            Assert.AreEqual(4242, sceneHandle, "同じ Scene が返る。");
        }

        /// <summary>
        /// <b>別候補への切替は、預かったものを解放してから</b>（契約表）。
        /// 先に読むと実 Scene が 3 枚になる。
        /// </summary>
        [Test]
        public void RequestingADifferentArea_ReleasesTheAdoptedOneFirst()
        {
            AreaInstanceHandle handle = AdmitActive(AreaB);
            _host.SceneStillLoaded = true;
            Assert.IsTrue(_preloader.TryAdoptRetained(handle, 4242, PathB));

            Assert.IsTrue(_preloader.Request(AreaC, PathC), "別の先を望める。");

            Assert.AreEqual(AreaPreloadPhase.Releasing, _preloader.Phase, "先に解放へ入る。");
            Assert.AreEqual(1, _host.UnloadCount, "解放を 1 回だけ頼んだ。");
            Assert.AreEqual(4242, _host.LastUnloadHandle, "解放するのは預かっていた Scene。");
            Assert.AreEqual(0, _host.LoadCount, "<b>まだ読んでいない</b>（解放の完了が先）。");

            _host.CompleteUnload();
            _preloader.Poll();

            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "解放が終わってから読む。");
            Assert.AreEqual(1, _host.LoadCount, "読込は 1 回だけ。");
        }

        /// <summary>すでに何か掴んでいるなら預からない（枠は 1 つしかない）。</summary>
        [Test]
        public void AdoptingWhileAlreadyHoldingSomething_IsRefused()
        {
            Assert.IsTrue(_preloader.Request(AreaC, PathC), "前提：先読みを始める。");
            Assert.AreEqual(AreaPreloadPhase.Loading, _preloader.Phase, "前提：読込中。");

            AreaInstanceHandle handle = AdmitActive(AreaB);
            Assert.IsFalse(_preloader.TryAdoptRetained(handle, 4242, PathB),
                "掴んでいるものを上書きしない（撤去する手段を失う）。");
            Assert.AreEqual(0, _preloader.AdoptedCount, "預かっていない。");
        }

        /// <summary>読み直しのパスが無ければ預からない（望みを立てられない）。</summary>
        [Test]
        public void AdoptingWithoutAScenePath_IsRefused()
        {
            AreaInstanceHandle handle = AdmitActive(AreaB);

            Assert.IsFalse(_preloader.TryAdoptRetained(handle, 4242, null), "パスが無い。");
            Assert.IsFalse(_preloader.TryAdoptRetained(handle, 0, PathB), "Scene が無い。");
            Assert.IsFalse(_preloader.TryAdoptRetained(default, 4242, PathB), "実体が無い。");
            Assert.AreEqual(0, _preloader.AdoptedCount, "どれも預かっていない。");
        }

        /// <summary>台帳へ「活動中の Area」として 1 つ入れる（遷移の出発側に相当）。</summary>
        private AreaInstanceHandle AdmitActive(StableId areaId)
        {
            AreaInstanceHandle handle = _ledger.NextHandle(areaId);
            Assert.IsTrue(_ledger.TryAdmitStaged(handle), "台帳へ入れられる。");
            Assert.IsTrue(_ledger.TrySetPhase(handle, AreaActivationPhase.Active), "活動中にできる。");
            return handle;
        }

        // ---------------------------------------------------------------- 差し替え

        /// <summary>
        /// Scene API の代わり。<b>本番と同じ口を通す</b>（テスト専用の別経路を作らない。
        /// P5 の <c>IAreaSceneLoader</c> と同じ考え方）。
        /// </summary>
        private sealed class FakeHost : IAreaSceneHost
        {
            private readonly List<ManualOperation> _pending = new List<ManualOperation>();

            public int LoadCount { get; private set; }
            public int UnloadCount { get; private set; }
            public int LastUnloadHandle { get; private set; }

            /// <summary>
            /// 実 Scene がまだ載っているか。<b>撤去の成否とは別に持つ</b>——
            /// 撤去が失敗しても Scene が消えていることはあるし、逆もある。
            /// </summary>
            public bool SceneStillLoaded { get; set; }

            public bool IsLoaded(int sceneHandle) => sceneHandle != 0 && SceneStillLoaded;

            public IAreaSceneOperation LoadAdditive(string scenePath)
            {
                LoadCount++;
                var op = new ManualOperation();
                _pending.Add(op);
                return op;
            }

            public IAreaSceneOperation Unload(int sceneHandle)
            {
                UnloadCount++;
                LastUnloadHandle = sceneHandle;
                var op = new ManualOperation();
                _pending.Add(op);
                return op;
            }

            public void CompleteLoad(int sceneHandle)
            {
                SceneStillLoaded = true;
                Finish(sceneHandle, error: false);
            }

            public void FailLoad() => Finish(0, error: true);

            public void CompleteUnload()
            {
                SceneStillLoaded = false;
                Finish(0, error: false);
            }

            /// <summary>撤去が失敗し、<b>Scene はまだ載っている</b>。</summary>
            public void FailUnloadAndKeepScene()
            {
                SceneStillLoaded = true;
                Finish(0, error: true);
            }

            /// <summary>撤去の通知だけが失敗し、<b>Scene は消えている</b>。</summary>
            public void FailUnloadButSceneIsGone()
            {
                SceneStillLoaded = false;
                Finish(0, error: true);
            }

            private void Finish(int sceneHandle, bool error)
            {
                Assert.Greater(_pending.Count, 0, "終わらせる操作がありません。");
                ManualOperation op = _pending[_pending.Count - 1];
                _pending.RemoveAt(_pending.Count - 1);
                op.Finish(sceneHandle, error);
            }

            private sealed class ManualOperation : IAreaSceneOperation
            {
                public bool IsDone { get; private set; }
                public bool HasError { get; private set; }
                public int SceneHandle { get; private set; }

                public void Finish(int sceneHandle, bool error)
                {
                    SceneHandle = sceneHandle;
                    HasError = error;
                    IsDone = true;
                }
            }
        }
    }
}
