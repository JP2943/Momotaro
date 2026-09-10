using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-FIX F02c：<b>どの状態でどの行動を始めてよいか</b>（v1.0 §8.3 の許可表）を固定する。
    ///
    /// F02b で入れた所有権は「誰が強いか」しか言えない。防御は戦闘より強い持ち主なので、
    /// 所有権だけでは「振っている最中に自動ガードを始めない」を表現できず、実際に
    /// <see cref="CompanionDefenseController"/> は <c>CanDefend</c>（倒れている・ひるみ・退場だけを見る）しか
    /// 見ていなかった。<b>攻撃判定中でも構えを始められる状態だった</b>のがこの工程で直す欠陥である。
    ///
    /// 表そのもの（純粋関数）と、表が実際に駆動へ効いていること（実物同士を繋いだ検証）を分けて置く。
    /// 表だけ緑でも、駆動が表を通っていなければ実機は何も変わらない（CLAUDE.md の「繋ぎ目」）。
    /// </summary>
    public sealed class CompanionActionRuleTests
    {
        private const float GuardCooldown = 3f;
        private const float EvadeCooldown = 4f;
        private const float GuardianRange = 3f;
        private const float GuardianCooldown = 6f;
        private const int PlayerMaxHp = 100;
        private const int CompanionMaxHp = 80;

        private const float Startup = 0.2f;
        private const float Active = 0.5f;
        private const float Recovery = 0.3f;
        private const float AttackCooldown = 1.5f;
        private const float UseRange = 2f;

        private readonly List<Object> _spawned = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            // 静的な供給元は前のテストから持ち越される（PlayMode で実際に踏んだ事故）。毎回外す。
            CompanionActivityProvider.Current = null;
            GameModeProvider.Current = null;
            PerceptionTargetRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
            CompanionActivityProvider.Current = null;
            GameModeProvider.Current = null;
            PerceptionTargetRegistry.Clear();
        }

        // ---- 補助 ----

        private static void SetPrivateField(object target, string field, object value)
        {
            System.Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        /// <summary>危険を任意に出せる観測（入力ではなく「観測結果」を差し替える）。</summary>
        private sealed class FakeDanger : IEnemyDangerSense
        {
            public bool HasDanger { get; set; }
            public bool Unblockable { get; set; }

            /// <summary>危険源→自分の進行方向。既定は「前方（+Z）から来る」。</summary>
            public Vector3 Incoming { get; set; } = Vector3.back;

            public EnemyDangerStimulus Sense(Vector3 selfPosition, Vector3 selfForward, int selfDamageableId)
            {
                return HasDanger
                    ? new EnemyDangerStimulus(selfPosition - Incoming * 2f, Incoming, Unblockable)
                    : EnemyDangerStimulus.None;
            }
        }

        private sealed class FakeAttacker : MonoBehaviour, ICombatActor
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;
        }

        /// <summary>敵役（狙われる側・攻撃対象）。既存の契約だけを実装し、敵 AI は用いない。</summary>
        private sealed class FakeEnemy : MonoBehaviour, ICombatActor, IDamageable, IThreatTarget
        {
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward { get; set; } = Vector3.back;
            public int DamageableId => GetInstanceID();
            public int ActorId => GetInstanceID();
            public Vector3 Position => transform.position;
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat => 0f;
            public float AcquiredThreatMultiplier => 1f;

            public void ReceiveHit(in HitInfo hit)
            {
            }
        }

        private CompanionData MakeData(AttackData attack = null)
        {
            var data = ScriptableObject.CreateInstance<CompanionData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", CompanionMaxHp);
            SetPrivateField(data, "_defense", 0f);
            SetPrivateField(data, "_canGuard", true);
            SetPrivateField(data, "_canEvade", true);
            SetPrivateField(data, "_guardCooldownSeconds", GuardCooldown);
            SetPrivateField(data, "_evadeCooldownSeconds", EvadeCooldown);
            SetPrivateField(data, "_guardianRange", GuardianRange);
            SetPrivateField(data, "_guardianCooldownSeconds", GuardianCooldown);
            if (attack != null)
            {
                SetPrivateField(data, "_attackPower", 60f);
                SetPrivateField(data, "_basicAttack", attack);
            }

            return data;
        }

        private AttackData MakeAttack()
        {
            var attack = ScriptableObject.CreateInstance<AttackData>();
            _spawned.Add(attack);
            SetPrivateField(attack, "_useRange", UseRange);
            SetPrivateField(attack, "_useAngle", 90f);
            SetPrivateField(attack, "_cooldownSeconds", AttackCooldown);
            SetPrivateField(attack, "_startupSeconds", Startup);
            SetPrivateField(attack, "_activeSeconds", Active);
            SetPrivateField(attack, "_recoverySeconds", Recovery);
            SetPrivateField(attack, "_hpMultiplier", 0.8f);
            return attack;
        }

        // ================================================================
        // 1. 表そのもの（純粋関数）
        // ================================================================

        /// <summary>
        /// 表より優先される規則：場に居ない・倒れている・復帰待ち・ひるんでいる・イベント中は何も始めない。
        /// 守護（庇う）も例外にしない。倒れた仲間が主人公を庇う体勢に入ってはいけない。
        /// </summary>
        [Test]
        public void OverridingStates_DenyEveryAction()
        {
            var states = new[]
            {
                CompanionState.Away, CompanionState.Down, CompanionState.Recovering,
                CompanionState.Stagger, CompanionState.Event,
            };

            var kinds = new[]
            {
                CompanionActionKind.AutoGuard, CompanionActionKind.AutoEvade, CompanionActionKind.AutoAttack,
                CompanionActionKind.GuardianTransfer, CompanionActionKind.Investigate,
            };

            foreach (CompanionState state in states)
            {
                foreach (CompanionActionKind kind in kinds)
                {
                    Assert.AreEqual(CompanionActionVerdict.Denied, CompanionActionRules.Evaluate(kind, state),
                        state + " の間に " + kind + " を始めてはいけない。");
                }
            }
        }

        /// <summary>
        /// <b>この工程で増える実質的な禁止</b>：判定中（AttackActive）に構え・回避を始めない。
        /// 振り切る前に構えへ化けると、当たるはずの一撃が消える。
        /// 予兆・後隙からなら中断して構えてよい（危険を見てから止まれるのはその 2 段だけ）。
        /// </summary>
        [Test]
        public void DefensiveActions_CannotInterruptTheActiveFrames()
        {
            Assert.AreEqual(CompanionActionVerdict.Denied,
                CompanionActionRules.Evaluate(CompanionActionKind.AutoGuard, CompanionState.AttackActive),
                "判定中に構えを始めない。");
            Assert.AreEqual(CompanionActionVerdict.Denied,
                CompanionActionRules.Evaluate(CompanionActionKind.AutoEvade, CompanionState.AttackActive),
                "判定中に回避を始めない。");

            Assert.AreEqual(CompanionActionVerdict.Interrupt,
                CompanionActionRules.Evaluate(CompanionActionKind.AutoGuard, CompanionState.AttackPrepare),
                "予兆なら止めて構えてよい。");
            Assert.AreEqual(CompanionActionVerdict.Interrupt,
                CompanionActionRules.Evaluate(CompanionActionKind.AutoEvade, CompanionState.AttackRecovery),
                "後隙なら止めて避けてよい。");
        }

        /// <summary>自動攻撃は平常時（Idle／Follow／Chase）からだけ。防御中・守護中・ワープ中には始めない。</summary>
        [Test]
        public void AutoAttack_StartsOnlyFromNeutralStates()
        {
            Assert.AreEqual(CompanionActionVerdict.Allowed,
                CompanionActionRules.Evaluate(CompanionActionKind.AutoAttack, CompanionState.Chase));
            Assert.AreEqual(CompanionActionVerdict.Allowed,
                CompanionActionRules.Evaluate(CompanionActionKind.AutoAttack, CompanionState.Follow));

            foreach (CompanionState blocked in new[]
            {
                CompanionState.Guard, CompanionState.Evade, CompanionState.Protect, CompanionState.Warp,
                CompanionState.AttackPrepare, CompanionState.AttackActive, CompanionState.AttackRecovery,
            })
            {
                Assert.AreEqual(CompanionActionVerdict.Denied,
                    CompanionActionRules.Evaluate(CompanionActionKind.AutoAttack, blocked),
                    blocked + " から攻撃を始めない。");
            }
        }

        /// <summary>
        /// <b>レビュー §5：探索中の行動競合表。</b>F02c では <see cref="CompanionState.Investigate"/> が
        /// まだ無く書けなかった行。調査中は、戦う・構える・避ける・庇うのいずれも<b>中断</b>として通る。
        /// 「許可」ではなく「中断」で通すのは、調査が途中で止められたことを状態の履歴に残すため。
        /// </summary>
        [Test]
        public void InvestigationTable_EveryCombatActionInterrupts()
        {
            foreach (CompanionActionKind kind in new[]
            {
                CompanionActionKind.AutoAttack, CompanionActionKind.AutoGuard,
                CompanionActionKind.AutoEvade, CompanionActionKind.GuardianTransfer,
            })
            {
                Assert.AreEqual(CompanionActionVerdict.Interrupt,
                    CompanionActionRules.Evaluate(kind, CompanionState.Investigate),
                    kind + " は調査を中断して始められる（敵が出たら調べている場合ではない）。");
            }

            Assert.AreEqual(CompanionActionVerdict.Allowed,
                CompanionActionRules.Evaluate(CompanionActionKind.Investigate, CompanionState.Investigate),
                "同じ調査の継続は許可（始め直しではない）。");
        }

        /// <summary>
        /// 探索は平常時からしか始めない。攻撃・防御・守護・ワープの最中に「調べに行く」ことはない。
        /// 倒れている・ひるんでいるときも同じ（表より優先される規則）。
        /// </summary>
        [Test]
        public void Investigate_StartsOnlyFromNeutralStates()
        {
            Assert.AreEqual(CompanionActionVerdict.Allowed,
                CompanionActionRules.Evaluate(CompanionActionKind.Investigate, CompanionState.Follow));
            Assert.AreEqual(CompanionActionVerdict.Allowed,
                CompanionActionRules.Evaluate(CompanionActionKind.Investigate, CompanionState.Idle));
            Assert.AreEqual(CompanionActionVerdict.Allowed,
                CompanionActionRules.Evaluate(CompanionActionKind.Investigate, CompanionState.Chase),
                "対象を見失った直後は Chase が 1 フレーム残る。ここで禁じると次の行動へ移れなくなる。");

            foreach (CompanionState blocked in new[]
            {
                CompanionState.AttackPrepare, CompanionState.AttackActive, CompanionState.AttackRecovery,
                CompanionState.Guard, CompanionState.Evade, CompanionState.Protect, CompanionState.Warp,
                CompanionState.Down, CompanionState.Stagger, CompanionState.Away,
                CompanionState.Recovering, CompanionState.Event,
            })
            {
                Assert.AreEqual(CompanionActionVerdict.Denied,
                    CompanionActionRules.Evaluate(CompanionActionKind.Investigate, blocked),
                    blocked + " から調べに行き始めない。");
            }
        }

        /// <summary>
        /// 探索は追従より強く、戦闘より弱い。順位が逆だと、調べに行くそばから隊列へ引き戻されるか、
        /// 敵が出ても調べ続けることになる。
        /// </summary>
        [Test]
        public void InvestigateRanksBetweenFollowAndChase()
        {
            Assert.Greater(
                CompanionStatePriority.Rank(CompanionState.Investigate),
                CompanionStatePriority.Rank(CompanionState.Follow),
                "追従より強い（調べに行くあいだ引き戻されない）。");
            Assert.Less(
                CompanionStatePriority.Rank(CompanionState.Investigate),
                CompanionStatePriority.Rank(CompanionState.Chase),
                "戦闘より弱い（敵が出たら中断する）。");

            Assert.Greater(
                (int)CompanionActionOwner.Investigate, (int)CompanionActionOwner.Follow,
                "行動の持ち主としても追従より強い。");
            Assert.Less(
                (int)CompanionActionOwner.Investigate, (int)CompanionActionOwner.Combat,
                "行動の持ち主としては戦闘より弱い。");
            Assert.Greater(
                (int)CompanionMovementOwner.Investigate, (int)CompanionMovementOwner.Follow,
                "移動の持ち主としても追従より強い。");
            Assert.Less(
                (int)CompanionMovementOwner.Investigate, (int)CompanionMovementOwner.Combat,
                "移動の持ち主としては戦闘より弱い。");
        }

        /// <summary>
        /// 守護は攻撃をどの段でも中断してよい（庇うほうが優先）。ただし回避中だけは不可。
        /// 回避は動作全体で 1 行動であり、途中で庇いに化けない。
        /// </summary>
        [Test]
        public void GuardianTransfer_InterruptsAttacksButNotEvade()
        {
            Assert.AreEqual(CompanionActionVerdict.Interrupt,
                CompanionActionRules.Evaluate(CompanionActionKind.GuardianTransfer, CompanionState.AttackActive));
            Assert.AreEqual(CompanionActionVerdict.Interrupt,
                CompanionActionRules.Evaluate(CompanionActionKind.GuardianTransfer, CompanionState.Guard));
            Assert.AreEqual(CompanionActionVerdict.Denied,
                CompanionActionRules.Evaluate(CompanionActionKind.GuardianTransfer, CompanionState.Evade),
                "回避を途中で庇いへ差し替えない。");
        }

        // ================================================================
        // 2. 調停役が表を通していること
        // ================================================================

        /// <summary>表が禁じた要求は、状態も所有権も動かさずに落ちる（黙って通ってはいけない）。</summary>
        [Test]
        public void Arbiter_DeniedAction_ChangesNeitherStateNorOwnership()
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData());
            actor.ResetState(CompanionState.AttackActive);

            CompanionStateArbiter states = go.GetComponent<CompanionStateArbiter>();
            Assert.IsNotNull(states, "CompanionActor は RequireComponent で調停役を連れてくる。");
            states.Bind(actor);
            states.ResetArbitration();

            bool taken = states.TryStartAction(
                CompanionActionOwner.Defense, CompanionActionKind.AutoGuard, CompanionState.Guard,
                CompanionStateChangeReason.DefensiveAction, out CompanionActionHandle handle);

            Assert.IsFalse(taken, "表が禁じたので受理しない。");
            Assert.IsFalse(handle.IsValid, "券も配らない。");
            Assert.AreEqual(CompanionState.AttackActive, actor.State, "状態は動かさない。");
            Assert.AreEqual(CompanionActionOwner.None, states.CurrentOwner, "所有権も動かさない。");
            Assert.AreEqual(1, states.DeniedByRuleCount, "表による拒否として数える（順位による拒否と区別する）。");
            Assert.AreEqual(CompanionActionVerdict.Denied, states.LastVerdict);
        }

        // ================================================================
        // 3. 実物の駆動同士を繋いだ検証
        // ================================================================

        private sealed class DefenseRig
        {
            public GameObject Root;
            public CompanionActor Actor;
            public CompanionStateArbiter States;
            public CompanionDefenseController Defense;
            public CompanionMovementArbiter Movement;
            public CompanionMotor Motor;
            public FakeDanger Danger;
        }

        private DefenseRig MakeDefenseRig(AttackData attack = null)
        {
            var go = new GameObject("Inumaru");
            _spawned.Add(go);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData(attack));
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.forward);

            var motor = go.AddComponent<CompanionMotor>(); // Rigidbody と移動調停役は RequireComponent で付く。
            var defense = go.AddComponent<CompanionDefenseController>();
            defense.Bind(actor);

            var danger = new FakeDanger();
            defense.SetDangerSense(danger);

            return new DefenseRig
            {
                Root = go,
                Actor = actor,
                States = go.GetComponent<CompanionStateArbiter>(),
                Defense = defense,
                Movement = go.GetComponent<CompanionMovementArbiter>(),
                Motor = motor,
                Danger = danger,
            };
        }

        /// <summary>
        /// <b>実物の戦闘駆動が振っている最中に、実物の防御駆動が構えを始めないこと。</b>
        /// 状態を手で置くのではなく、索敵 → 接近 → 攻撃と実際に進めてから防御を 1 Tick 回す。
        /// これがこの工程で直した欠陥そのもの（従来は <c>CanDefend</c> しか見ておらず、構えが通っていた）。
        /// </summary>
        [Test]
        public void RealAttackActive_DoesNotStartGuard()
        {
            AttackData attack = MakeAttack();
            DefenseRig rig = MakeDefenseRig(attack);

            var tracker = rig.Root.AddComponent<CompanionTargetTracker>();
            tracker.Bind(rig.Actor);
            var combat = rig.Root.AddComponent<CompanionCombatController>();
            combat.Bind(rig.Actor, rig.Motor, tracker);
            InvokePrivate(combat, "OnEnable");

            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 1f);
            PerceptionTargetRegistry.Register(enemyGo.AddComponent<FakeEnemy>());

            tracker.TickTargeting();
            combat.TickCombat(0f);
            Assert.IsTrue(combat.IsAttacking, "前提：攻撃が始まっている。");
            combat.TickCombat(Startup);
            Assert.AreEqual(CompanionState.AttackActive, rig.Actor.State, "前提：判定中。");

            rig.Danger.HasDanger = true;
            rig.Defense.TickDefense(0.05f);

            Assert.IsFalse(rig.Defense.IsGuarding, "振っている最中に構えを始めない（当たるはずの一撃を消さない）。");
            Assert.AreEqual(CompanionState.AttackActive, rig.Actor.State, "状態も奪われない。");
            Assert.IsTrue(rig.Defense.Guard.IsReady,
                "始められなかったのだからガードのクールダウンも消費しない（表の判断は能力より前に置く）。");
            Assert.AreEqual(1, rig.States.DeniedByRuleCount, "表が拒んだことが記録に残る。");
        }

        /// <summary>
        /// 構えが終わったら<b>自分で追従へ戻す</b>。表は Guard からの攻撃も次の構えも禁じるので、
        /// ここで戻さないと仲間はその場で固まる。「危険が去ったのに何もしなくなる」は実機で最も分かりにくい壊れ方。
        /// </summary>
        [Test]
        public void GuardEnds_ReturnsToFollow_SoTheCompanionCanActAgain()
        {
            DefenseRig rig = MakeDefenseRig();
            rig.Danger.HasDanger = true;
            rig.Defense.TickDefense(0.1f);
            Assert.AreEqual(CompanionState.Guard, rig.Actor.State, "前提：構えている。");
            Assert.AreEqual(CompanionActionOwner.Defense, rig.States.CurrentOwner, "前提：防御が行動を握っている。");

            rig.Danger.HasDanger = false;
            rig.Defense.TickDefense(0.1f);

            Assert.IsFalse(rig.Defense.IsGuarding, "構えを解く。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "行動を終えて追従へ戻る。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner, "行動の所有権を返す。");
            Assert.AreEqual(CompanionMovementOwner.None, rig.Movement.Owner, "移動の所有権も返す。");
            Assert.AreEqual(CompanionActionVerdict.Allowed,
                CompanionActionRules.Evaluate(CompanionActionKind.AutoAttack, rig.Actor.State),
                "戻った先から次の行動を始められる（固まらない）。");
        }

        /// <summary>
        /// 回避は<b>動作全体</b>で 1 行動。無敵が切れただけでは行動を返さず、モーションが終わってから追従へ戻す。
        /// </summary>
        [Test]
        public void Evade_HoldsTheActionForTheWholeMotion()
        {
            DefenseRig rig = MakeDefenseRig();
            rig.Danger.HasDanger = true;
            rig.Danger.Unblockable = true;
            rig.Defense.TickDefense(0.05f);
            Assert.AreEqual(CompanionState.Evade, rig.Actor.State, "前提：回避中。");

            // 危険が去っても、回避モーションが終わるまでは行動を手放さない。
            rig.Danger.HasDanger = false;
            rig.Defense.TickDefense(0.05f);
            Assert.AreEqual(CompanionState.Evade, rig.Actor.State, "途中で別の行動へ移らない。");
            Assert.AreEqual(CompanionActionOwner.Defense, rig.States.CurrentOwner);

            rig.Defense.TickDefense(EnemyEvadeAbility.DefaultInvulnerableSeconds);

            Assert.IsFalse(rig.Defense.IsEvading, "モーションが終わった。");
            Assert.AreEqual(CompanionState.Follow, rig.Actor.State, "終わってから追従へ戻る。");
            Assert.AreEqual(CompanionActionOwner.None, rig.States.CurrentOwner);
        }

        /// <summary>
        /// <b>構えている向きを追従が書き換えないこと。</b>ガードの成否は <c>Forward</c> と命中方向の角度で決まるため、
        /// 主人公の向きに合わせて回されると、構えているのに素通りする。
        /// 実物の移動調停役を通し、追従の意図が退けられることで固定する。
        /// </summary>
        [Test]
        public void GuardFacing_IsNotOverwrittenByFollow()
        {
            DefenseRig rig = MakeDefenseRig();

            var leaderGo = new GameObject("Player");
            _spawned.Add(leaderGo);
            leaderGo.transform.position = new Vector3(0f, 0f, -3f);
            leaderGo.transform.forward = Vector3.right; // 主人公は横を向いている。

            var follow = rig.Root.AddComponent<CompanionFollowController>();
            follow.Bind(leaderGo.transform, rig.Actor, rig.Motor);
            InvokePrivate(follow, "OnEnable");

            // 危険は +X 側から来る（危険源→自分の進行方向は -X）。構えは危険源を向く＝+X。
            rig.Danger.Incoming = Vector3.left;
            rig.Danger.HasDanger = true;
            rig.Defense.TickDefense(0.05f);

            Assert.AreEqual(CompanionState.Guard, rig.Actor.State, "前提：構えている。");
            Assert.AreEqual(CompanionMovementOwner.Defense, rig.Movement.Owner, "防御が移動と向きを握る。");
            Assert.Greater(Vector3.Dot(rig.Actor.Forward, Vector3.right), 0.99f, "危険源の方を向いて構える。");

            // 追従が同じフレームに歩こうとしても、向きは奪われない。
            follow.TickFollow(0.05f);

            Assert.Greater(Vector3.Dot(rig.Actor.Forward, Vector3.right), 0.99f,
                "追従が主人公の向きで上書きしない（上書きされるとガード弧が外れて素通りする）。");
            Assert.AreEqual(CompanionMovementOwner.Defense, rig.Movement.Owner, "所有権も取られない。");
        }

        /// <summary>
        /// 中断でもクールダウンは始まる。始めないと、小突かれるたびに攻撃をやり直せてしまい、
        /// ひるまされている間だけ手数が増えるという逆さまな挙動になる。秒数は開始時 Snapshot のもの。
        /// </summary>
        [Test]
        public void CancelledAttack_StartsTheSnapshotCooldown()
        {
            AttackData attack = MakeAttack();
            DefenseRig rig = MakeDefenseRig(attack);

            var tracker = rig.Root.AddComponent<CompanionTargetTracker>();
            tracker.Bind(rig.Actor);
            var combat = rig.Root.AddComponent<CompanionCombatController>();
            combat.Bind(rig.Actor, rig.Motor, tracker);
            InvokePrivate(combat, "OnEnable");

            var enemyGo = new GameObject("Enemy");
            _spawned.Add(enemyGo);
            enemyGo.transform.position = new Vector3(0f, 0f, 1f);
            PerceptionTargetRegistry.Register(enemyGo.AddComponent<FakeEnemy>());

            tracker.TickTargeting();
            combat.TickCombat(0f);
            Assert.IsTrue(combat.IsAttacking, "前提：攻撃中。");
            Assert.AreEqual(0f, combat.CooldownRemaining, 1e-4f, "前提：まだクールダウンは走っていない。");

            // 攻撃中に Data を書き換えても、中断で使われるのは開始時に確定した値。
            SetPrivateField(attack, "_cooldownSeconds", AttackCooldown * 10f);
            combat.CancelAttack();

            Assert.IsFalse(combat.IsAttacking, "中断した。");
            Assert.AreEqual(AttackCooldown, combat.CooldownRemaining, 1e-3f,
                "中断でも通常クールダウンを開始する（中断が手数の救済にならない）。");
        }

        // ---- 守護：庇いがひるみ・ダウンを上書きしない ----

        private PlayerVitalsHolder MakePlayer(Vector3 position)
        {
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.transform.position = position;

            var holder = go.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetPrivateField(data, "_maxHp", PlayerMaxHp);
            SetPrivateField(data, "_defense", 0f);
            SetPrivateField(holder, "_data", data);
            return holder;
        }

        /// <summary>
        /// <b>庇った一撃で自分がひるんだ場合、Protect で上書きしない。</b>
        ///
        /// 転送は「受け口へ渡す → 成立を通知する」の順で起こる。渡した時点でひるみ・ダウンが確定しているので、
        /// あとから来る成立通知が状態を書き戻すと、ひるんでいるのに庇う体勢という嘘の状態になる。
        /// スタブではなく実物の <see cref="PlayerVitalsHolder"/>・<see cref="CompanionHitReceiver"/>・
        /// <see cref="CompanionGuardianController"/> を繋いで、実際の順序で確かめる。
        /// </summary>
        [Test]
        public void Protect_DoesNotOverwriteStaggerCausedByTheTransferredHit()
        {
            PlayerVitalsHolder player = MakePlayer(Vector3.zero);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = new Vector3(0f, 0f, 1f);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData());
            actor.ResetState(CompanionState.Follow);
            actor.SetFacing(Vector3.back);

            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);

            var guardian = go.AddComponent<CompanionGuardianController>();
            guardian.Bind(actor, receiver, player.transform);
            InvokePrivate(guardian, "OnEnable");

            var attacker = new GameObject("Enemy").AddComponent<FakeAttacker>();
            _spawned.Add(attacker.gameObject);
            attacker.transform.position = new Vector3(0f, 0f, -2f);

            // ひるみ耐性（既定 40）を超える一撃。庇った犬丸はひるむ。
            var hit = new HitInfo(
                attacker, player, Vector3.forward, Vector3.zero,
                new HitDamage(20f, 0f, 60f),
                guardable: false, justGuardable: false,
                hitId: HitId.Single(4201));

            player.ReceiveHit(hit);

            Assert.AreEqual(1, guardian.TransferCount, "前提：肩代わりは成立している。");
            Assert.AreEqual(PlayerMaxHp, player.Vitals.Health.Current, "前提：主人公は削れていない。");
            Assert.AreEqual(CompanionState.Stagger, actor.State,
                "庇った一撃でひるんだのだから、状態はひるみのまま。Protect で上書きしない。");
        }

        /// <summary>ひるんでいる間は新しい肩代わりも引き受けない（表の「表より優先される規則」が守護にも効く）。</summary>
        [Test]
        public void Staggered_DoesNotTakeOverANewHit()
        {
            PlayerVitalsHolder player = MakePlayer(Vector3.zero);

            var go = new GameObject("Inumaru");
            _spawned.Add(go);
            go.transform.position = new Vector3(0f, 0f, 1f);

            var actor = go.AddComponent<CompanionActor>();
            actor.SetData(MakeData());
            actor.ResetState(CompanionState.Follow);

            var receiver = go.AddComponent<CompanionHitReceiver>();
            receiver.Bind(actor);
            var guardian = go.AddComponent<CompanionGuardianController>();
            guardian.Bind(actor, receiver, player.transform);
            InvokePrivate(guardian, "OnEnable");

            actor.ForceHitState(CompanionState.Stagger, CompanionStateChangeReason.Staggered);

            var attacker = new GameObject("Enemy").AddComponent<FakeAttacker>();
            _spawned.Add(attacker.gameObject);

            var hit = new HitInfo(
                attacker, player, Vector3.forward, Vector3.zero,
                new HitDamage(20f, 0f, 0f),
                guardable: false, justGuardable: false,
                hitId: HitId.Single(4202));

            player.ReceiveHit(hit);

            Assert.AreEqual(0, guardian.TransferCount, "ひるんでいる間は庇わない。");
            Assert.AreEqual(PlayerMaxHp - 20, player.Vitals.Health.Current,
                "肩代わりが成立しないぶんは主人公が通常どおり受ける（命中が消えることはない）。");
        }
    }
}
