using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Data.World;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Combat.Guardian;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P6C 01〜08・11・13（ジャスト回避と反撃強化。P6C 仕様 §3〜§5・§8）。
    ///
    /// <b>本物の主人公部品</b>（PlayerStateController・PlayerVitalsHolder・PlayerHitReaction・PlayerMotor）に<b>出荷 Data</b>
    /// （SO_Step_Momotaro・SO_Player_AttackCombo・SO_Player_Momotaro・SO_Special_Momotaro）を繋ぎ、入力は既存の入力状態の押下エッジから流す。
    /// 敵の命中は<b>出荷の敵攻撃 Data から既存の生成経路</b>（<see cref="EnemyHitFactory.Build"/>）で作り、主人公の被弾入口へ渡す
    /// （敵の Update が主人公の Tick の外で命中を出す順序を再現する）。主人公の攻撃は実際の Hitbox（OverlapBox）で的の Collider に当てる。
    /// 実 Scene の実敵・実入力の経路は PlayMode <c>P6CWorldPlayTests</c> が別に確かめる。
    /// </summary>
    public sealed class P6CJustEvadeTests
    {
        private const string StepPath = "Assets/_Project/Data/Combat/SO_Step_Momotaro.asset";
        private const string ComboPath = "Assets/_Project/Data/Combat/SO_Player_AttackCombo.asset";
        private const string PlayerPath = "Assets/_Project/Data/Player/SO_Player_Momotaro.asset";
        private const string SpecialPath = "Assets/_Project/Data/Combat/SO_Special_Momotaro.asset";
        private const string UnblockablePath = "Assets/_Project/Data/Enemies/SO_EnemyAttack_Elite_Unblockable.asset";
        private const string MeleePath = "Assets/_Project/Data/Enemies/SO_EnemyAttack_Melee_Normal.asset";
        private const string ShotPath = "Assets/_Project/Data/Enemies/SO_EnemyAttack_Ranged_Shot.asset";
        private const string P6BCatalogPath = "Assets/_Project/Data/Tests/Phase6B/SO_AreaCatalog_P6B.asset";

        private readonly List<Object> _spawned = new List<Object>();
        private int _hitSeq;
        private int _rigSeq;

        [SetUp]
        public void SetUp()
        {
            GameModeProvider.Current = null;
            GameplayClockProvider.Current = null;
            KibidangoUseProvider.Current = null;
            PlayerInputProvider.Current = null;
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
            PlayerInputProvider.Current = null;
            GameplayClockProvider.Current = null;
            KibidangoUseProvider.Current = null;
        }

        // ================================================================ P6C 01・02：成功条件と境界

        /// <summary>
        /// 出荷 Data の値：受付終端 0.12・倍率 1.5・有効 2.0、無敵 [0.05, 0.20)。重なり [0.05, 0.12) の 0.07 秒だけが成功区間。
        /// Validator は受付終端が無敵開始より後・無敵終了以下、有限、時間 &gt; 0、倍率 ≥ 1 を要求する。
        /// </summary>
        [Test]
        public void Data_ShippedValues_AndValidatorBounds()
        {
            var step = AssetDatabase.LoadAssetAtPath<StepData>(StepPath);
            Assert.IsNotNull(step);
            Assert.AreEqual(0.12f, step.JustEvadeWindowSeconds, 1e-6f);
            Assert.AreEqual(1.5f, step.JustEvadeCounterHpMultiplier, 1e-6f);
            Assert.AreEqual(2.0f, step.JustEvadeCounterSeconds, 1e-6f);
            Assert.AreEqual(0.05f, step.InvincibleStartSeconds, 1e-6f);
            Assert.AreEqual(0.20f, step.InvincibleEndSeconds, 1e-6f);
            var report = new DataValidationReport();
            step.Validate(report);
            Assert.IsFalse(report.HasErrors, string.Join(", ", report.Errors));

            Assert.IsTrue(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.12f, 1.5f, 2f, out _));
            Assert.IsTrue(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.20f, 1f, 0.1f, out _), "終端＝無敵終了・倍率 1 は可。");
            Assert.IsFalse(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.05f, 1.5f, 2f, out _), "終端＝無敵開始は不可（重なり 0）。");
            Assert.IsFalse(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.04f, 1.5f, 2f, out _), "無敵開始前をジャスト扱いにしない。");
            Assert.IsFalse(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.21f, 1.5f, 2f, out _), "通常無敵を広げない。");
            Assert.IsFalse(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.12f, 0.99f, 2f, out _), "倍率は 1 以上。");
            Assert.IsFalse(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.12f, 1.5f, 0f, out _), "時間は正。");
            Assert.IsFalse(StepData.TryValidateJustEvade(0.05f, 0.20f, float.NaN, 1.5f, 2f, out _), "有限値。");
            Assert.IsFalse(StepData.TryValidateJustEvade(0.05f, 0.20f, 0.12f, float.PositiveInfinity, 2f, out _));

            // Asset 側でも同じ規則（壊れた設定はエラー）。
            StepData broken = Object.Instantiate(step);
            _spawned.Add(broken);
            SetPrivate(broken, "_justEvadeWindowSeconds", 0.03f);
            var report2 = new DataValidationReport();
            broken.Validate(report2);
            Assert.IsTrue(report2.HasErrors);
        }

        /// <summary>
        /// 境界：無敵開始（含む）・受付終端（含まない）・無敵終端（含まない）。フレームの刻みを変えても（1 回で到達・1/64・1/32・0.001 の
        /// ヒットストップ相当）同じ Gameplay 経過で同じ判定になる。成功は 1 回の通知・反撃強化の付与・体幹反射なし。
        /// </summary>
        [Test]
        public void Boundaries_ConsistentAcrossDeltaTimes()
        {
            // (到達の仕方, 期待)。経過＝刻み×回数。
            var cases = new (float dt, int frames, HitResultKind expected, string label)[]
            {
                (0.0499f, 1, HitResultKind.Damage, "0.0499：無敵前"),
                (0.05f, 1, HitResultKind.JustEvade, "0.05：無敵開始（含む）"),
                (0.1199f, 1, HitResultKind.JustEvade, "0.1199：受付終端の直前"),
                (0.12f, 1, HitResultKind.Evade, "0.12：受付終端（含まない）→ 通常回避"),
                (0.1999f, 1, HitResultKind.Evade, "0.1999：無敵の終わり直前"),
                (0.20f, 1, HitResultKind.Damage, "0.20：無敵終端（含まない）"),
                (1f / 64f, 3, HitResultKind.Damage, "1/64×3=0.047"),
                (1f / 64f, 4, HitResultKind.JustEvade, "1/64×4=0.0625"),
                (1f / 64f, 7, HitResultKind.JustEvade, "1/64×7=0.109"),
                (1f / 64f, 8, HitResultKind.Evade, "1/64×8=0.125"),
                (1f / 64f, 13, HitResultKind.Damage, "1/64×13=0.203"),
                (1f / 32f, 1, HitResultKind.Damage, "1/32×1=0.031"),
                (1f / 32f, 2, HitResultKind.JustEvade, "1/32×2=0.0625"),
                (1f / 32f, 4, HitResultKind.Evade, "1/32×4=0.125"),
                (1f / 32f, 7, HitResultKind.Damage, "1/32×7=0.219"),
                (0.001f, 60, HitResultKind.JustEvade, "0.001×60≈0.06（ヒットストップの細かい刻み）"),
                (0.001f, 125, HitResultKind.Evade, "0.001×125≈0.125"),
            };

            foreach ((float dt, int frames, HitResultKind expected, string label) in cases)
            {
                Rig r = NewRig();
                BeginStep(r);
                for (int i = 0; i < frames; i++)
                {
                    Frame(r, dt);
                }

                Assert.IsTrue(r.Player.IsStepping, label + "：前提（ステップ中）。");
                var attacker = NewAttacker(r);
                r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, attacker));
                Assert.AreEqual(expected, r.Results.Last, label + "（経過 " + r.Player.StepElapsed + "）");
                bool success = expected == HitResultKind.JustEvade;
                Assert.AreEqual(success ? 1 : 0, r.Player.JustEvadeSuccessCount, label + "：成功回数。");
                Assert.AreEqual(success, r.Player.HasJustEvadeCounter, label + "：強化の付与。");
                Assert.AreEqual(0, attacker.Received, label + "：攻撃者へ体幹反射しない。");
                Assert.AreEqual(-1f, attacker.FlinchSeconds, label + "：強制ひるみしない。");
            }
        }

        /// <summary>
        /// 報酬が出ないもの：ステップしていない、敵の攻撃でない接触（環境・反射）、ステップ回避不可の攻撃、
        /// 被弾後無敵だけで避けた命中（ステップ中でも被弾後無敵が先に評価される）。Pause・遷移凍結中は経過が進まない。
        /// </summary>
        [Test]
        public void NoReward_NotStepping_NonEnemy_NotSteppable_PostHitInvincibleOnly_AndClockStops()
        {
            // ステップしていない。
            Rig r = NewRig();
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.Damage, r.Results.Last);
            Assert.AreEqual(0, r.Player.JustEvadeSuccessCount);

            // 敵の攻撃でない接触（環境など。命中に敵の印が無い）。
            r = NewRig();
            BeginStep(r);
            Frame(r, 0.06f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, null).AsEnemyAttack(false));
            Assert.AreEqual(HitResultKind.Evade, r.Results.Last, "無敵で避けるが通常回避。");
            Assert.AreEqual(0, r.Player.JustEvadeSuccessCount);
            // 同じステップでまだ成功できる（環境接触で受付を閉じていない）。
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.JustEvade, r.Results.Last);

            // ステップ回避不可の敵攻撃（無敵を貫通）。
            r = NewRig();
            EnemyAttackData notSteppable = CloneAttack(MeleePath, steppable: false);
            BeginStep(r);
            Frame(r, 0.06f);
            r.Vitals.ReceiveHit(EnemyHit(r, notSteppable, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.Damage, r.Results.Last);
            Assert.AreEqual(0, r.Player.JustEvadeSuccessCount);

            // 被弾後無敵だけで避けた命中（ステップしていない）。
            r = NewRig();
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.Damage, r.Results.Last);
            RunUntil(r, () => !r.Reaction.IsHurt, 2f);
            Assert.IsTrue(r.Reaction.IsPostHitInvincible, "前提：被弾後無敵の残り。");
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.Evade, r.Results.Last);
            // 被弾後無敵の残りでステップし、受付中に命中 → 被弾後無敵が先（既存の優先関係）なので成功しない。
            BeginStep(r);
            Frame(r, 0.06f);
            Assert.IsTrue(r.Reaction.IsPostHitInvincible && r.Player.CanJustEvade, "前提：両方の無敵が重なる。");
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.Evade, r.Results.Last);
            Assert.AreEqual(0, r.Player.JustEvadeSuccessCount, "被弾後無敵だけで避けた命中からは成功しない。");

            // 遷移凍結（Gameplay 時計）中は経過が進まない。Pause（入力が閉じる）はステップ自体を止める（既存）。
            r = NewRig();
            BeginStep(r);
            Frame(r, 0.03f);
            GameplayClockProvider.Current = new FrozenClock();
            Frame(r, 0.5f);
            GameplayClockProvider.Current = null;
            Assert.AreEqual(0.03f, r.Player.StepElapsed, 1e-5f, "凍結中は進まない。");
            Frame(r, 0.03f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.JustEvade, r.Results.Last, "凍結前後の合計 0.06 で成功。");
        }

        // ================================================================ P6C 03：1 ステップ 1 回・重複なし

        /// <summary>
        /// 同じ命中（同じ HitId）を複数 Collider・複数フレーム・再入で受けても成功は 1 回。同じステップの別の命中も 2 回目は通常回避。
        /// 攻撃側の既存の重複排除（<see cref="MultiHitTracker"/>：1 攻撃 1 対象 1 回）は同じ主人公の別 Collider を同じ対象として扱う。
        /// 別ステップでは別の命中に再び成功でき、強化は 1 回分のまま残時間だけ更新される。
        /// </summary>
        [Test]
        public void OncePerStep_NoDuplicateFromSameHitCollidersFramesOrReentry()
        {
            Rig r = NewRig();
            var attacker = NewAttacker(r);
            HitInfo hitA = EnemyHit(r, MeleePath, attacker);

            // 攻撃側の重複排除：同じ一撃 × 同じ主人公（Collider が複数でも IDamageable は 1 つ）は 1 回しか届かない。
            var tracker = new MultiHitTracker();
            Assert.IsTrue(tracker.TryRegisterHit(hitA.HitId, r.Vitals));
            Assert.IsFalse(tracker.TryRegisterHit(hitA.HitId, r.Vitals), "同じ一撃の 2 つ目の Collider・次のフレーム。");

            BeginStep(r);
            Frame(r, 0.06f);
            r.Vitals.ReceiveHit(hitA);
            Assert.AreEqual(HitResultKind.JustEvade, r.Results.Last);
            r.Vitals.ReceiveHit(hitA); // 同じ命中の再入
            Frame(r, 0.02f);
            r.Vitals.ReceiveHit(hitA); // 次のフレームにもう一度
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, attacker)); // 同じステップの別の命中
            Assert.AreEqual(1, r.Player.JustEvadeSuccessCount, "1 ステップ 1 回。");
            Assert.AreEqual(1, r.Results.Count(HitResultKind.JustEvade), "成功の通知は 1 回。");
            Assert.AreEqual(3, r.Results.Count(HitResultKind.Evade), "残りは無敵による通常回避（ダメージなし）。");
            Assert.AreEqual(r.Vitals.MaxHp, r.Vitals.CurrentHp);
            float firstRemaining = r.Player.JustEvadeCounterRemaining;

            // 別のステップでは別の命中に成功できる。強化は 1 回分のまま、残時間を 2.0 へ更新。
            RunUntil(r, () => !r.Player.IsStepping, 1f);
            Frame(r, 0.3f);
            Assert.Less(r.Player.JustEvadeCounterRemaining, firstRemaining);
            BeginStep(r);
            Frame(r, 0.06f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, attacker));
            Assert.AreEqual(2, r.Player.JustEvadeSuccessCount);
            Assert.AreEqual(2.0f, r.Player.JustEvadeCounterRemaining, 1e-5f, "残時間を有効時間へ更新。");
            Assert.IsTrue(r.Player.HasJustEvadeCounter);
        }

        // ================================================================ P6C 04：多段・複数敵・飛び道具

        /// <summary>
        /// 成功しても後続の命中は無条件に無効にならない：無敵の残る間の命中は通常回避、無敵が切れた後の命中は被弾。
        /// 複数の敵の同時攻撃も報酬は最初の 1 回だけ。飛び道具（射手が退場して攻撃者が無い矢）も成功対象で、
        /// 射手への反射・ひるみ・弾の反射はしない。体幹反射・強制ひるみは近接にも飛び道具にも無い。
        /// </summary>
        [Test]
        public void MultiHitMultiEnemyProjectile_OnlyIFramesAvoid_NoFreeInvalidation()
        {
            Rig r = NewRig();
            var a1 = NewAttacker(r);
            var a2 = NewAttacker(r);
            BeginStep(r);
            Frame(r, 0.06f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, a1)); // 1 体目
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, a2)); // 同じフレームの 2 体目
            Assert.AreEqual(1, r.Player.JustEvadeSuccessCount, "複数の敵でも報酬は 1 回。");
            Frame(r, 0.08f); // 経過 0.14：無敵中・受付外
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, a1)); // 多段の 2 発目（別の命中）
            Assert.AreEqual(HitResultKind.Evade, r.Results.Last, "無敵の範囲は回避。");
            Frame(r, 0.08f); // 経過 0.22：無敵の外
            int hpBefore = r.Vitals.CurrentHp;
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, a2)); // 多段の 3 発目
            Assert.AreEqual(HitResultKind.Damage, r.Results.Last, "成功しても後続は無料で無効にならない。");
            Assert.Less(r.Vitals.CurrentHp, hpBefore);
            Assert.AreEqual(0, a1.Received + a2.Received, "体幹反射なし。");
            Assert.AreEqual(-1f, a1.FlinchSeconds);
            Assert.AreEqual(-1f, a2.FlinchSeconds);

            // 飛び道具（射手が退場：攻撃者 null）。
            Rig p = NewRig();
            BeginStep(p);
            Frame(p, 0.07f);
            HitInfo arrow = EnemyHit(p, ShotPath, null);
            Assert.IsTrue(arrow.Reaction.IsProjectile && arrow.IsEnemyAttack && arrow.Attacker == null, "前提：退場した射手の矢。");
            p.Vitals.ReceiveHit(arrow);
            Assert.AreEqual(HitResultKind.JustEvade, p.Results.Last, "飛び道具も成功対象。");
            Assert.IsTrue(p.Player.HasJustEvadeCounter);

            // 生きている射手でも反射・ひるみなし。
            Rig q = NewRig();
            var archer = NewAttacker(q);
            BeginStep(q);
            Frame(q, 0.07f);
            q.Vitals.ReceiveHit(EnemyHit(q, ShotPath, archer));
            Assert.AreEqual(HitResultKind.JustEvade, q.Results.Last);
            Assert.AreEqual(0, archer.Received);
            Assert.AreEqual(-1f, archer.FlinchSeconds);
        }

        // ================================================================ P6C 05：倍率・時間・蓄積なし・通常攻撃だけ

        /// <summary>
        /// 1.5 倍・2.0 秒・別成功で時間更新・蓄積なし。強化を持ったまま必殺・ガード・通常の回避をしても消費されず（必殺は強化されない）、
        /// 時間内の次の通常攻撃の一段だけが 1.5 倍。2.0 秒ちょうどで満了（同時は強化なし）。
        /// </summary>
        [Test]
        public void Counter_OnePointFive_TwoSeconds_NoStack_OnlyNextNormalStage()
        {
            Rig r = NewRig();
            Target t = NewTarget(r, Vector3.back * 0.8f);
            Succeed(r);
            Assert.AreEqual(1.5f, r.Player.JustEvadeCounterMultiplier, 1e-6f);
            Assert.AreEqual(2.0f, r.Player.JustEvadeCounterRemaining, 1e-5f);
            RunUntil(r, () => !r.Player.IsStepping, 1f);

            // ガード（押して離す）・通常の回避（命中なし）では消費しない。残時間は通常どおり減る。
            r.Input.SetGuard(true);
            Frame(r, 0.05f);
            r.Input.SetGuard(false);
            Frame(r, 0.02f);
            BeginStep(r);
            RunUntil(r, () => !r.Player.IsStepping, 1f);
            Assert.IsTrue(r.Player.HasJustEvadeCounter, "ガード・回避で消費しない。");
            Assert.Less(r.Player.JustEvadeCounterRemaining, 1.5f);
            Assert.AreEqual(0, r.Player.JustEvadeCounterConsumeCount);

            // 別の成功で残時間を 2.0 へ更新（蓄積しない：権利は 1 回分のまま）。
            Succeed(r);
            Assert.AreEqual(2.0f, r.Player.JustEvadeCounterRemaining, 1e-5f);
            RunUntil(r, () => !r.Player.IsStepping, 1f);

            // 必殺（溜め 1.0 → 発動）：強化されず、消費もしない。
            r.Input.SetSpecialAttack(true);
            RunUntil(r, () => r.Player.IsSpecialCharged, 2f);
            r.Input.SetSpecialAttack(false);
            RunUntil(r, () => t.Hits.Count > 0, 1f);
            Assert.AreEqual(1, t.Hits.Count, "必殺が当たる。");
            Assert.AreEqual(100f * 7f * 0.1f, t.Hits[0].Hp, 1e-3f, "必殺は強化されない（7.0 倍 × 攻撃力 100 × 0.1）。");
            Assert.IsTrue(r.Player.HasJustEvadeCounter, "必殺で消費しない。");
            Assert.Greater(r.Player.JustEvadeCounterRemaining, 0f);
            RunUntil(r, () => !r.Player.IsSwingHitboxActive, 1f); // 必殺の判定（出し切り）が終わって後隙へ

            // 時間内の次の通常攻撃（必殺の後隙を攻撃で打ち切る）。
            int hits = t.Hits.Count;
            Tap(r, v => r.Input.SetAttack(v));
            Assert.AreEqual(1, r.Player.JustEvadeCounterConsumeCount, "段の開始で消費。");
            Assert.IsTrue(r.Player.IsCurrentSwingCountered);
            RunUntil(r, () => t.Hits.Count > hits, 1f);
            Assert.AreEqual(15f, t.Hits[hits].Hp, 1e-4f, "通常 1 段目 10 × 1.5。");
            Assert.AreEqual(8f, t.Hits[hits].Poise, 1e-4f, "体幹は強化しない。");
            Assert.AreEqual(20f, t.Hits[hits].Flinch, 1e-4f, "ひるませ値は強化しない。");
            RunUntil(r, () => r.Player.Current == PlayerState.Idle, 2f);
            Assert.IsFalse(r.Player.IsCurrentSwingCountered, "攻撃の終了で倍率を解放。");

            // 次の攻撃は通常。
            hits = t.Hits.Count;
            Tap(r, v => r.Input.SetAttack(v));
            RunUntil(r, () => t.Hits.Count > hits, 1f);
            Assert.AreEqual(10f, t.Hits[hits].Hp, 1e-4f, "持ち越さない。");
            RunUntil(r, () => r.Player.Current == PlayerState.Idle, 2f);

            // 満了：2.0 秒ちょうどで消える。同時（満了の Tick）に開始した段は強化なし。刻みは 2 進で割り切れる値。
            Succeed(r);
            Frame(r, 1.0f);
            Assert.IsTrue(r.Player.HasJustEvadeCounter);
            Frame(r, 0.75f);
            Assert.IsTrue(r.Player.HasJustEvadeCounter, "1.75 秒はまだ有効。");
            Assert.AreEqual(0.25f, r.Player.JustEvadeCounterRemaining, 1e-6f);
            r.Input.SetAttack(true);
            Frame(r, 0.25f); // 2.0 秒到達と同じ Tick で段が開始
            r.Input.SetAttack(false);
            Assert.AreEqual(PlayerState.Attack, r.Player.Current);
            Assert.IsFalse(r.Player.IsCurrentSwingCountered, "満了と同時は強化なし。");
            Assert.AreEqual(1, r.Player.JustEvadeCounterExpireCount);
        }

        // ================================================================ P6C 06：消費の時点

        /// <summary>
        /// 開始が受理された時に消費。拒否された入力（Pause 中の押下）では残る。ステップ中に押した攻撃は予約されるだけで、
        /// 段が実際に始まるまで消費しない。空振り・被弾で中断しても戻さない。満了前に開始した段は、命中が満了後でも強化を保つ。
        /// </summary>
        [Test]
        public void Consume_OnAcceptedStart_RejectedKeeps_NoRefund_LateHitKeepsBoost()
        {
            // 拒否：Pause（入力が閉じる）中の押下は捨てられる。
            Rig r = NewRig();
            Succeed(r);
            r.Input.SetActive(false);
            Tap(r, v => r.Input.SetAttack(v));
            r.Input.SetActive(true);
            Frame(r, 0.02f);
            Assert.AreEqual(0, r.Player.JustEvadeCounterConsumeCount, "拒否された入力では消費しない。");
            Assert.IsTrue(r.Player.HasJustEvadeCounter);

            // 予約：ステップ中に押した攻撃は、ステップ終了で段が始まった時に消費。
            RunUntil(r, () => !r.Player.IsStepping, 1f);
            BeginStep(r);
            RunUntil(r, () => r.Player.StepElapsed >= 0.2f, 1f); // 先行入力の保持（0.3 秒）がステップ終了まで届く時点で押す
            Tap(r, v => r.Input.SetAttack(v));
            Assert.IsTrue(r.Player.IsStepping, "前提：まだステップ中。");
            Assert.AreEqual(0, r.Player.JustEvadeCounterConsumeCount, "予約時には消費しない。");
            RunUntil(r, () => r.Player.Current == PlayerState.Attack, 1f);
            Assert.AreEqual(1, r.Player.JustEvadeCounterConsumeCount, "段の開始で消費。");

            // 空振り（的なし）：消費は戻らない。
            RunUntil(r, () => r.Player.Current == PlayerState.Idle, 2f);
            Assert.IsFalse(r.Player.HasJustEvadeCounter, "空振りでも返還なし。");

            // 被弾で中断：消費は戻らない。
            Succeed(r);
            RunUntil(r, () => !r.Player.IsStepping, 1f);
            Tap(r, v => r.Input.SetAttack(v));
            Assert.IsTrue(r.Player.IsCurrentSwingCountered);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Frame(r, 0.02f);
            Assert.AreEqual(PlayerState.Hurt, r.Player.Current);
            Assert.IsFalse(r.Player.HasJustEvadeCounter, "被弾で取り消されても返還なし。");
            Assert.IsFalse(r.Player.IsCurrentSwingCountered, "中断で段の倍率は解放。");

            // 満了前に開始 → 命中は満了後でも強化。
            Rig s = NewRig();
            Target t = NewTarget(s, Vector3.back * 0.8f);
            Succeed(s);
            Frame(s, 1.95f);
            Tap(s, v => s.Input.SetAttack(v)); // 1.95 + 0.02 で開始（満了前）
            Assert.IsTrue(s.Player.IsCurrentSwingCountered);
            RunUntil(s, () => t.Hits.Count > 0, 1f);
            Assert.IsFalse(s.Player.HasJustEvadeCounter);
            Assert.AreEqual(15f, t.Hits[0].Hp, 1e-4f, "命中が満了後（2.0 秒超）でもその段の強化を保つ。");
        }

        // ================================================================ P6C 07：一段の複数対象・次段・再利用

        /// <summary>
        /// 一振りで 2 体に当てると 2 体とも 1.5 倍。同じ段で同じ対象には 1 回だけ（既存の重複排除）。
        /// 連続入力の 2 段目は通常（1.1 倍のまま）。次の攻撃でも同じ Hitbox を再利用するが倍率は残らない。
        /// </summary>
        [Test]
        public void OneStage_AllTargetsBoosted_NextStageAndReusedHitboxNormal()
        {
            Rig r = NewRig();
            Target t1 = NewTarget(r, new Vector3(-0.3f, 0f, -0.8f));
            Target t2 = NewTarget(r, new Vector3(0.3f, 0f, -0.8f));
            Succeed(r);
            RunUntil(r, () => !r.Player.IsStepping, 1f);

            Tap(r, v => r.Input.SetAttack(v));
            RunUntil(r, () => t1.Hits.Count > 0 && t2.Hits.Count > 0, 1f);
            Run(r, 0.06f); // 判定の残り（同じ対象に二度当たらない）
            Assert.AreEqual(1, t1.Hits.Count, "同じ段・同じ対象は 1 回。");
            Assert.AreEqual(1, t2.Hits.Count);
            Assert.AreEqual(15f, t1.Hits[0].Hp, 1e-4f);
            Assert.AreEqual(15f, t2.Hits[0].Hp, 1e-4f, "一段の有効対象すべてを同倍率で強化。");
            Assert.AreEqual(2, r.Player.CounterBoostedHitCount);

            // 2 段目（連続入力）は通常。
            Tap(r, v => r.Input.SetAttack(v));
            RunUntil(r, () => t1.Hits.Count > 1, 1f);
            Assert.AreEqual(11f, t1.Hits[1].Hp, 1e-4f, "2 段目 10 × 1.1、強化なし。");
            Assert.AreEqual(1, r.Player.JustEvadeCounterConsumeCount);
            RunUntil(r, () => r.Player.Current == PlayerState.Idle, 3f);

            // 次の攻撃（同じ Hitbox の再利用）も通常。
            int n = t1.Hits.Count;
            Tap(r, v => r.Input.SetAttack(v));
            RunUntil(r, () => t1.Hits.Count > n, 1f);
            Assert.AreEqual(10f, t1.Hits[n].Hp, 1e-4f);
        }

        // ================================================================ P6C 08：成長との積・防御・スタン・丸め

        /// <summary>
        /// 適用順：攻撃側寄与＝攻撃力 × 技倍率 × 0.1 × （成長倍率 × 反撃倍率）× 背後 → 対象側で 防御補正 × スタン倍率 → 四捨五入。
        /// 成長 1.20 × 反撃 1.5 ＝ 1.80（寄与 18）。例：防御 10 の普通敵へ 18×100/110＝16.36→16（強化なし 12→10.9→11。比は 1.45 で
        /// 厳密な 1.5 倍ではない）。スタン 1.5 の精鋭（防御 25）へ 18×0.8×1.5＝21.6→22（強化なし 12×0.8×1.5＝14.4→14）。
        /// 体幹・ひるみは強化しない（成長の体幹倍率だけ）。
        /// </summary>
        [Test]
        public void Damage_ProductWithGrowth_DefenseStunRounding_PoiseFlinchUnaffected()
        {
            Rig r = NewRig();
            r.Player.SetGrowthMultipliers(1.2f, 1.1f);
            Target t = NewTarget(r, Vector3.back * 0.8f);

            Tap(r, v => r.Input.SetAttack(v));
            RunUntil(r, () => t.Hits.Count > 0, 1f);
            RunUntil(r, () => r.Player.Current == PlayerState.Idle, 2f);
            Assert.AreEqual(12f, t.Hits[0].Hp, 1e-4f, "強化なし：10 × 1.2。");

            Succeed(r);
            RunUntil(r, () => !r.Player.IsStepping, 1f);
            Tap(r, v => r.Input.SetAttack(v));
            RunUntil(r, () => t.Hits.Count > 1, 1f);
            HitRecord boosted = t.Hits[1];
            Assert.AreEqual(18f, boosted.Hp, 1e-4f, "10 × 1.2 × 1.5 ＝ 18（両補正の積 1.80）。");
            Assert.AreEqual(t.Hits[0].Poise, boosted.Poise, 1e-4f, "体幹は成長の倍率だけ（反撃では増えない）。");
            Assert.AreEqual(8f * 1.1f, boosted.Poise, 1e-4f);
            Assert.AreEqual(t.Hits[0].Flinch, boosted.Flinch, 1e-4f, "ひるみは増えない。");

            // 対象側の既存計算（防御・スタン・丸め）の例。
            Assert.AreEqual(16, HpDamageCalculator.ResolveFinal(boosted.Hp, 10f));
            Assert.AreEqual(11, HpDamageCalculator.ResolveFinal(t.Hits[0].Hp, 10f));
            Assert.AreEqual(22, HpDamageCalculator.ResolveFinal(boosted.Hp, 25f, 1f, 1.5f));
            Assert.AreEqual(14, HpDamageCalculator.ResolveFinal(t.Hits[0].Hp, 25f, 1f, 1.5f));
        }

        // ================================================================ P6C 09：通常ガード不可のジャスガ（出荷 Data・既存の生成経路）

        /// <summary>
        /// 精鋭のガード不能（出荷 Data・既存の生成経路）：通常ガードでは受け止められず被弾、ジャスガは成立して体幹反射。
        /// 正面以外・ガードブレイク中・受付外は既存どおり JG 不成立。JG の受付・消費 0・体幹反射の量は通常攻撃と同じ規則。
        /// </summary>
        [Test]
        public void Unblockable_NormalGuardFails_JustGuardSucceeds_ExistingConditionsKept()
        {
            var data = AssetDatabase.LoadAssetAtPath<EnemyAttackData>(UnblockablePath);
            Assert.IsFalse(data.Guardable);
            Assert.IsTrue(data.JustGuardable);

            // ジャスガ（押した直後の受付中）：成立、HP・スタミナ消費 0、体幹反射は Data の値。
            Rig r = NewRig();
            var attacker = NewAttacker(r);
            r.Input.SetGuard(true);
            Frame(r, 0.02f);
            Assert.IsTrue(r.Player.CanJustGuard);
            int st0 = r.Vitals.Vitals.Stamina.Current;
            r.Vitals.ReceiveHit(EnemyHit(r, data, attacker, -r.Player.GuardForward));
            Assert.AreEqual(HitResultKind.JustGuard, r.Results.Last);
            Assert.AreEqual(r.Vitals.MaxHp, r.Vitals.CurrentHp);
            Assert.AreEqual(st0, r.Vitals.Vitals.Stamina.Current, "消費 0。");
            Assert.AreEqual(1, attacker.Received);
            Assert.AreEqual(data.JustGuardPoiseReturn, attacker.LastPoise, 1e-4f, "体幹反射は既存規則（Data の値）。");

            // 通常ガード（受付を過ぎて構えたまま）：受け止められず被弾。
            r = NewRig();
            r.Input.SetGuard(true);
            Run(r, 0.5f);
            Assert.IsFalse(r.Player.CanJustGuard);
            Assert.IsTrue(r.Player.IsGuarding);
            r.Vitals.ReceiveHit(EnemyHit(r, data, NewAttacker(r), -r.Player.GuardForward));
            Assert.AreEqual(HitResultKind.Damage, r.Results.Last, "通常ガードでは受けられない。");

            // 背後からは JG でも成立しない（正面規則を維持）。
            r = NewRig();
            r.Input.SetGuard(true);
            Frame(r, 0.02f);
            r.Vitals.ReceiveHit(EnemyHit(r, data, NewAttacker(r), r.Player.GuardForward));
            Assert.AreEqual(HitResultKind.Damage, r.Results.Last, "背後は防げない。");

            // 比較：通常攻撃の JG と同じ受付（同じ入力で成立）。
            r = NewRig();
            r.Input.SetGuard(true);
            Frame(r, 0.02f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r), -r.Player.GuardForward));
            Assert.AreEqual(HitResultKind.JustGuard, r.Results.Last);
        }

        // ================================================================ P6C 10：可否の保持（Snapshot → 命中）

        /// <summary>防御可否は Data → Snapshot → 敵の命中（近接・飛び道具）で上書きされずに保たれる。敵の命中には敵の印が付く。</summary>
        [Test]
        public void DefenseFlags_PreservedThroughSnapshotAndHitFactory()
        {
            foreach (string path in new[] { UnblockablePath, MeleePath, ShotPath })
            {
                var data = AssetDatabase.LoadAssetAtPath<EnemyAttackData>(path);
                EnemyAttackSnapshot snap = EnemyAttackSnapshot.From(data);
                Assert.AreEqual(data.Guardable, snap.Guardable, path);
                Assert.AreEqual(data.JustGuardable, snap.JustGuardable, path);
                Assert.AreEqual(data.Steppable, snap.Steppable, path);
                HitInfo hit = EnemyHitFactory.Build(snap, 10f, null, null, Vector3.back, Vector3.zero, HitId.Single(1));
                Assert.AreEqual(data.Guardable, hit.Guardable, path);
                Assert.AreEqual(data.JustGuardable, hit.JustGuardable, path + "：分類を理由に上書きしない。");
                Assert.AreEqual(data.Steppable, hit.Steppable, path);
                Assert.IsTrue(hit.IsEnemyAttack, path);
                Assert.AreEqual(data.AttackClass == EnemyAttackClass.Projectile, hit.Reaction.IsProjectile, path);
            }

            var ub = AssetDatabase.LoadAssetAtPath<EnemyAttackData>(UnblockablePath);
            Assert.AreEqual(EnemyAttackDefenseNotice.UnblockableJustGuardable, EnemyAttackDefenseNotice.Describe(ub));
        }

        // ================================================================ P6C 11：守護・被弾後無敵・同フレーム死亡

        /// <summary>
        /// 回避で主人公に届かなかった一撃は犬丸へ転送しない（成功・通常回避とも）。成功の通知は 1 回で、二重の反応はない。
        /// 同じフレームで成功の後に別の攻撃（ステップ回避不可）で死亡すると、死亡が優先して強化は残らない。
        /// </summary>
        [Test]
        public void Priority_NoGuardianTransfer_NoDoubleReaction_DeathClearsSameFrame()
        {
            Rig r = NewRig();
            var guardian = new FakeGuardian();
            r.Vitals.SetGuardianResolver(new FakeResolver(guardian));
            BeginStep(r);
            Frame(r, 0.06f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Frame(r, 0.04f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(0, guardian.Received, "回避した一撃は守護へ転送しない。");
            Assert.AreEqual(1, r.Results.Count(HitResultKind.JustEvade));
            Assert.AreEqual(1, r.Results.Count(HitResultKind.Evade));
            Assert.AreEqual(2, r.Results.Total, "結果は命中ごとに 1 回。");

            // 同フレーム：成功 → 致死（ステップ回避不可）。
            Rig d = NewRig();
            BeginStep(d);
            Frame(d, 0.06f);
            d.Vitals.ReceiveHit(EnemyHit(d, MeleePath, NewAttacker(d)));
            Assert.IsTrue(d.Player.HasJustEvadeCounter);
            d.Vitals.ReceiveHit(EnemyHit(d, CloneAttack(MeleePath, steppable: false), NewAttacker(d), null, 100000f));
            Assert.IsTrue(d.Vitals.IsDefeated);
            Frame(d, 0.02f);
            Assert.IsFalse(d.Player.HasJustEvadeCounter, "死亡を優先して強化を残さない。");
            d.Player.NotifyJustEvadeSuccess();
            Assert.IsFalse(d.Player.HasJustEvadeCounter, "死亡後は付与しない。");
            Assert.IsFalse(d.Player.CanJustEvade);
        }

        // ================================================================ P6C 12（EditMode 部分）：消去の口

        /// <summary>入場・死亡再開・休息（共通の中立化）で消える。消去は付与回数の記録を変えない。</summary>
        [Test]
        public void Clear_OnAreaEntryAndRespawnReset()
        {
            Rig r = NewRig();
            Succeed(r);
            r.Player.ResetForAreaEntry();
            Assert.IsFalse(r.Player.HasJustEvadeCounter, "入場（通常移動・旅立ち）で消す。");
            Assert.AreEqual(0f, r.Player.JustEvadeCounterRemaining);

            RunUntil(r, () => true, 0.02f);
            Succeed(r);
            r.Player.ResetForCampaignRespawn();
            Assert.IsFalse(r.Player.HasJustEvadeCounter, "死亡再開・休息（成長・払い戻しを含む）で消す。");
            Assert.AreEqual(2, r.Player.JustEvadeSuccessCount);
        }

        // ================================================================ P6C 13：きびだんご

        /// <summary>
        /// 強化を持っていても使用中は回避・ガードできず、使用は中断できない。有効な被弾で使用は中断（P6B の契約）。
        /// 強化は使用で消費されず、時計どおり減る。ステップの受付状態が使用開始へ残って被弾を消すことはない。
        /// 成長の効果は強化の付与・消去で変わらない。
        /// </summary>
        [Test]
        public void Kibidango_NoStepOrGuardDuringUse_HitInterrupts_CounterUntouched_GrowthKept()
        {
            var catalogData = AssetDatabase.LoadAssetAtPath<AreaCatalogData>(P6BCatalogPath);
            Assert.IsNotNull(catalogData, P6BCatalogPath);
            Assert.IsTrue(AreaCatalog.TryBuild(catalogData, out AreaCatalog catalog, out IReadOnlyList<string> errors),
                string.Join("/", errors));
            Assert.IsTrue(catalog.Campaign.TryGetShrine(new StableId("shrine_p6_a"), out ShrineInfo shrine));
            var session = new GameSessionState(EncounterClearPolicy.Permanent);
            Assert.IsTrue(session.InitializeNewAdventure("adv_p6c", shrine, 3, 3));

            Rig r = NewRig();
            r.Player.ItemUseOverride = new CampaignKibidangoUse(() => session, () => catalog.Campaign);
            r.Player.SetGrowthMultipliers(1.2f, 1.1f);
            Succeed(r);
            RunUntil(r, () => !r.Player.IsStepping, 1f);
            r.Vitals.Vitals.Health.SetCurrent(40);

            Tap(r, v => r.Input.SetUseItem(v));
            Assert.IsTrue(r.Player.IsUsingItem, "拒否=" + r.Player.LastItemUseRejection);
            float remaining = r.Player.JustEvadeCounterRemaining;
            Tap(r, v => r.Input.SetStep(v));
            r.Input.SetGuard(true);
            Frame(r, 0.05f);
            Assert.IsFalse(r.Player.IsStepping, "使用中は回避しない。");
            Assert.IsFalse(r.Player.IsGuarding, "使用中はガードしない。");
            Assert.IsTrue(r.Player.IsUsingItem, "強化を持っていても使用は中断しない。");
            Assert.Less(r.Player.JustEvadeCounterRemaining, remaining, "強化は時計どおり減る。");
            Assert.IsTrue(r.Player.HasJustEvadeCounter, "使用で消費しない。");
            r.Input.SetGuard(false);

            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(HitResultKind.Damage, r.Results.Last, "ステップの受付が残って被弾を消さない。");
            Frame(r, 0.02f);
            Assert.IsFalse(r.Player.IsUsingItem, "有効な被弾で中断。");
            Assert.AreEqual(0, r.Player.ItemUseCommitCount);
            Assert.AreEqual(3, session.Kibidango);
            Assert.IsTrue(r.Player.HasJustEvadeCounter, "通常の被弾では強化を消さない。");
            Assert.AreEqual(1.2f, r.Player.GrowthAttackHpMultiplier, 1e-6f);
            r.Player.ClearJustEvadeCounter();
            Assert.AreEqual(1.2f, r.Player.GrowthAttackHpMultiplier, 1e-6f, "強化の消去で成長は変わらない。");
            Assert.AreEqual(1.1f, r.Player.GrowthNormalPoiseMultiplier, 1e-6f);
        }

        // ================================================================ 補助

        private sealed class Rig
        {
            public GameObject Go;
            public PlayerStateController Player;
            public PlayerVitalsHolder Vitals;
            public PlayerHitReaction Reaction;
            public PlayerInputState Input;
            public ResultRecorder Results;
        }

        private sealed class ResultRecorder : IHitResultListener
        {
            private readonly List<HitResultKind> _kinds = new List<HitResultKind>();
            public void OnHitResult(in HitResult result) => _kinds.Add(result.Kind);
            public HitResultKind Last => _kinds.Count > 0 ? _kinds[_kinds.Count - 1] : (HitResultKind)(-1);
            public int Total => _kinds.Count;

            public int Count(HitResultKind kind)
            {
                int n = 0;
                foreach (HitResultKind k in _kinds)
                {
                    if (k == kind)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        private Rig NewRig()
        {
            var go = new GameObject("P6C_Player_" + _rigSeq);
            go.transform.position = new Vector3(_rigSeq * 50f, 0f, 0f); // リグ同士の Hitbox が重ならない位置へ
            _rigSeq++;
            _spawned.Add(go);
            var r = new Rig { Go = go };
            r.Vitals = go.AddComponent<PlayerVitalsHolder>();
            SetPrivate(r.Vitals, "_data", AssetDatabase.LoadAssetAtPath<PlayerData>(PlayerPath));
            r.Reaction = go.AddComponent<PlayerHitReaction>();
            var motor = go.AddComponent<PlayerMotor>();
            r.Player = go.AddComponent<PlayerStateController>();
            SetPrivate(r.Player, "_motor", motor);
            SetPrivate(r.Player, "_stepData", AssetDatabase.LoadAssetAtPath<StepData>(StepPath));
            SetPrivate(r.Player, "_attackCombo", AssetDatabase.LoadAssetAtPath<PlayerAttackComboData>(ComboPath));
            SetPrivate(r.Player, "_attackerStats", AssetDatabase.LoadAssetAtPath<PlayerData>(PlayerPath));
            SetPrivate(r.Player, "_specialData", AssetDatabase.LoadAssetAtPath<SpecialAttackData>(SpecialPath));
            r.Input = new PlayerInputState();
            PlayerInputProvider.Current = r.Input;
            r.Results = new ResultRecorder();
            r.Vitals.Results.AddListener(r.Results);
            Frame(r, 0.02f); // 入力を掴ませる
            PlayerInputProvider.Current = null;
            return r;
        }

        private void BeginStep(Rig r)
        {
            r.Input.SetMove(Vector2.right);
            r.Input.SetStep(true);
            Frame(r, 0.02f);
            r.Input.SetStep(false);
            r.Input.SetMove(Vector2.zero);
            Assert.IsTrue(r.Player.IsStepping, "ステップを開始する。");
            Assert.AreEqual(0f, r.Player.StepElapsed, 1e-6f, "開始フレームの経過は 0。");
        }

        /// <summary>ジャスト回避を 1 回成功させる（経過 0.06 で敵の命中）。</summary>
        private void Succeed(Rig r)
        {
            int before = r.Player.JustEvadeSuccessCount;
            if (r.Player.IsStepping)
            {
                RunUntil(r, () => !r.Player.IsStepping, 1f);
            }

            BeginStep(r);
            Frame(r, 0.06f);
            r.Vitals.ReceiveHit(EnemyHit(r, MeleePath, NewAttacker(r)));
            Assert.AreEqual(before + 1, r.Player.JustEvadeSuccessCount, "ジャスト回避が成功する。");
        }

        private void Tap(Rig r, Action<bool> set)
        {
            set(true);
            Frame(r, 0.02f);
            set(false);
        }

        private static void Frame(Rig r, float dt)
        {
            r.Reaction.Tick(dt);
            r.Player.Tick(dt);
            r.Player.ResolveItemUseCommit();
        }

        private static void Run(Rig r, float seconds)
        {
            for (float t = 0f; t < seconds - 1e-5f; t += 0.02f)
            {
                Frame(r, 0.02f);
            }
        }

        private static void RunUntil(Rig r, Func<bool> done, float maxSeconds)
        {
            for (float t = 0f; t < maxSeconds && !done(); t += 0.02f)
            {
                Frame(r, 0.02f);
            }
        }

        private HitInfo EnemyHit(Rig r, string attackPath, ICombatActor attacker, Vector3? direction = null, float power = 30f)
        {
            return EnemyHit(r, AssetDatabase.LoadAssetAtPath<EnemyAttackData>(attackPath), attacker, direction, power);
        }

        private HitInfo EnemyHit(Rig r, EnemyAttackData data, ICombatActor attacker, Vector3? direction = null, float power = 30f)
        {
            Assert.IsNotNull(data);
            Vector3 dir = direction ?? -r.Player.GuardForward;
            return EnemyHitFactory.Build(EnemyAttackSnapshot.From(data), power, attacker, r.Vitals, dir,
                r.Go.transform.position, HitId.Single(++_hitSeq + 9000));
        }

        private EnemyAttackData CloneAttack(string path, bool steppable)
        {
            var data = Object.Instantiate(AssetDatabase.LoadAssetAtPath<EnemyAttackData>(path));
            _spawned.Add(data);
            SetPrivate(data, "_steppable", steppable);
            return data;
        }

        private RecordingAttacker NewAttacker(Rig r)
        {
            var go = new GameObject("Attacker");
            go.transform.position = r.Go.transform.position + Vector3.back * 1.5f;
            _spawned.Add(go);
            return go.AddComponent<RecordingAttacker>();
        }

        private Target NewTarget(Rig r, Vector3 offset)
        {
            var go = new GameObject("Target");
            go.transform.position = r.Go.transform.position + offset + Vector3.up * 0.5f;
            _spawned.Add(go);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(0.3f, 0.6f, 0.3f);
            Physics.SyncTransforms();
            return go.AddComponent<Target>();
        }

        private struct HitRecord
        {
            public float Hp;
            public float Poise;
            public float Flinch;
        }

        private sealed class Target : MonoBehaviour, IDamageable, ICombatActor
        {
            public readonly List<HitRecord> Hits = new List<HitRecord>();
            public int DamageableId => GetInstanceID();
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;

            public void ReceiveHit(in HitInfo hit)
            {
                Hits.Add(new HitRecord { Hp = hit.Damage.Hp, Poise = hit.Damage.Poise, Flinch = hit.Damage.Flinch });
            }
        }

        private sealed class RecordingAttacker : MonoBehaviour, IDamageable, ICombatActor, IForcedFlinchReceiver
        {
            public int Received;
            public float LastPoise;
            public float FlinchSeconds = -1f;
            public int DamageableId => GetInstanceID();
            public CombatFaction Faction => CombatFaction.Enemy;
            public int FloorId => 0;
            public Vector3 WorldPosition => transform.position;
            public Vector3 Forward => Vector3.forward;

            public void ReceiveHit(in HitInfo hit)
            {
                Received++;
                LastPoise = hit.Damage.Poise;
            }

            public void ForceFlinch(float seconds) => FlinchSeconds = seconds;
        }

        private sealed class FrozenClock : IGameplayClockSource
        {
            public bool IsFrozen => true;
        }

        private sealed class FakeGuardian : IGuardianReceiver
        {
            public int Received;
            public int DamageableId => 77002;
            public Vector3 WorldPosition => Vector3.zero;
            public bool CanTakeOver => true;
            public void ReceiveHit(in HitInfo hit) => Received++;

            public bool TryReceiveTransferredHit(in HitInfo hit)
            {
                Received++;
                return true;
            }
        }

        private sealed class FakeResolver : IGuardianResolver
        {
            private readonly IGuardianReceiver _guardian;

            public FakeResolver(IGuardianReceiver guardian) => _guardian = guardian;

            public bool TryResolveGuardian(in HitInfo hit, out IGuardianReceiver guardian)
            {
                guardian = _guardian;
                return true;
            }

            public void NotifyTransferred(in HitInfo transferred, IGuardianReceiver guardian)
            {
            }
        }

        private static void SetPrivate(object target, string field, object value)
        {
            Type t = target.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f != null)
                {
                    f.SetValue(target, value);
                    return;
                }

                t = t.BaseType;
            }

            Assert.Fail("field not found: " + field);
        }
    }
}
