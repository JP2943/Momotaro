using Momotaro.Data;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Editor.Phase4;
using Momotaro.Gameplay.Companion;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-03：<b>出荷される実アセット</b>（犬丸 Data・通常攻撃 Data・Prefab）が戦闘できる形に配線されているかを検証する。
    ///
    /// 合成データの単体テストが全部緑でも、実アセットの参照が 1 本切れていれば犬丸は敵へ近づきも攻撃もしない
    /// （<see cref="CompanionAttackSettings.HasAttack"/> が false になり、判断は常に Idle へ落ちる）。
    /// 実機で「近づくが攻撃しない」を目視で追いかける前に、その配線を機械で確かめられるようにする。
    /// </summary>
    public sealed class CompanionCombatWiringTests
    {
        private static CompanionData LoadData()
        {
            var data = AssetDatabase.LoadAssetAtPath<CompanionData>(Phase4CompanionBuilder.InumaruDataPath);
            Assert.IsNotNull(data, "犬丸 Data が読めない: " + Phase4CompanionBuilder.InumaruDataPath);
            return data;
        }

        [Test]
        public void InumaruData_HasBasicAttackWired()
        {
            CompanionData data = LoadData();

            Assert.IsNotNull(data.BasicAttack,
                "犬丸の通常攻撃 Data が未配線。これが null だと索敵はしても接近・攻撃をしない（判断が Idle になる）。");
            Assert.Greater(data.AttackPower, 0f,
                "攻撃力が 0 だと当たっても HP が減らず、獲得ヘイトも増えない（敵が犬丸へ振り向かない）。");
        }

        [Test]
        public void InumaruAttackAsset_ExistsAtExpectedPath()
        {
            var attack = AssetDatabase.LoadAssetAtPath<AttackData>(Phase4CompanionBuilder.InumaruAttackDataPath);

            Assert.IsNotNull(attack, "通常攻撃 Data が読めない: " + Phase4CompanionBuilder.InumaruAttackDataPath);
            Assert.AreSame(attack, LoadData().BasicAttack, "Data が参照しているのは同じアセット。");
        }

        [Test]
        public void InumaruAttack_ProducesUsableSettings()
        {
            CompanionAttackSettings settings = CompanionAttackSettings.From(LoadData().BasicAttack);

            Assert.IsTrue(settings.HasAttack, "判定時間と射程がある（これが false だと敵へ寄っていかない）。");
            Assert.Greater(settings.TotalSeconds, 0f, "長さゼロの攻撃は開始できない。");
            Assert.Less(settings.AttackStartDistance, settings.UseRange,
                "攻撃を始める距離は、判定が届く距離より内側であること。"
                + "同じにすると、予兆のあいだに対象が離れた時点で必ず空振りする（P4-03 受入で実際に起きた）。");
        }

        [Test]
        public void InumaruAttack_IsReachableWithinTargetingRange()
        {
            CompanionData data = LoadData();

            Assert.LessOrEqual(data.BasicAttack.UseRange, data.TargetAcquireRange,
                "捕捉できない距離の相手を攻撃できる設定は間合いが破綻する。");
        }

        [Test]
        public void InumaruData_PassesValidation()
        {
            var report = new DataValidationReport();

            LoadData().Validate(report);

            Assert.IsFalse(report.HasErrors, "実アセットの検証エラー:\n- " + string.Join("\n- ", report.Errors));
        }

        [Test]
        public void InumaruPrefab_HasCombatComponentsWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Phase4CompanionBuilder.InumaruPrefabPath);
            if (prefab == null)
            {
                Assert.Ignore("犬丸 Prefab が未生成のためスキップ（Momotaro / Phase 4 / Generate Inumaru Prefab）。");
            }

            var combat = prefab.GetComponent<CompanionCombatController>();
            Assert.IsNotNull(combat,
                "Prefab に戦闘駆動が付いていない。Scene のインスタンスも Prefab から受け継ぐため、"
                + "未生成のままだと犬丸は追従しかしない。");

            var actor = prefab.GetComponent<CompanionActor>();
            Assert.IsNotNull(actor, "仲間 Actor が付く。");
            Assert.IsNotNull(actor.Data, "Actor に Data が配線される（null だと攻撃設定が取れない）。");
            Assert.IsNotNull(actor.Data.BasicAttack, "Prefab が参照する Data 側にも通常攻撃が配線されている。");
            Assert.IsNotNull(prefab.GetComponent<CompanionTargetTracker>(), "索敵が付く。");
            Assert.IsNotNull(prefab.GetComponent<CompanionMotor>(), "移動実行が付く。");
        }
    }
}
