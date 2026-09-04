using Momotaro.Data;
using Momotaro.Data.Characters;
using Momotaro.Data.Progression;
using NUnit.Framework;
using UnityEditor;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-00：撃破報酬アセット（<c>SO_Reward_Enemy_*</c>）の実体と、敵アーキタイプへの割当を検証する。
    /// 仕様値（安定 ID・徳量・GrantOnce=false・ItemId 空）と、試遊 Scene が用いる 3 種の Prefab が参照する
    /// アーキタイプ（近接／遠距離／強敵）に正しい Reward が割り当たっていることを回帰として固定する。
    /// </summary>
    public sealed class RewardDataAssetTests
    {
        private const string RewardDir = "Assets/_Project/Data/Progression/";
        private const string EnemyDir = "Assets/_Project/Data/Enemies/";

        private static RewardData LoadReward(string fileName)
        {
            var data = AssetDatabase.LoadAssetAtPath<RewardData>(RewardDir + fileName + ".asset");
            Assert.IsNotNull(data, "報酬アセットが見つかりません: " + RewardDir + fileName + ".asset");
            return data;
        }

        private static EnemyArchetypeData LoadEnemy(string fileName)
        {
            var data = AssetDatabase.LoadAssetAtPath<EnemyArchetypeData>(EnemyDir + fileName + ".asset");
            Assert.IsNotNull(data, "敵アーキタイプが見つかりません: " + EnemyDir + fileName + ".asset");
            return data;
        }

        [TestCase("SO_Reward_Enemy_Melee", "reward_enemy_melee", 10)]
        [TestCase("SO_Reward_Enemy_Ranged", "reward_enemy_ranged", 12)]
        [TestCase("SO_Reward_Enemy_Elite", "reward_enemy_elite", 40)]
        public void RewardAsset_MatchesSpec(string fileName, string stableId, int virtue)
        {
            RewardData data = LoadReward(fileName);

            Assert.AreEqual(stableId, data.Id.Value, "安定 ID（保存・参照の正本）。");
            Assert.AreEqual(virtue, data.VirtueAmount, "徳量（P4-00 試遊調整値）。");
            Assert.IsFalse(data.GrantOnce, "一般敵報酬は撃破ごとに累積するため GrantOnce=false。");
            Assert.IsTrue(data.ItemId.IsEmpty, "P4-00 ではアイテム付与を扱わないため ItemId は空。");
        }

        [TestCase("SO_Reward_Enemy_Melee")]
        [TestCase("SO_Reward_Enemy_Ranged")]
        [TestCase("SO_Reward_Enemy_Elite")]
        public void RewardAsset_PassesDataValidation(string fileName)
        {
            RewardData data = LoadReward(fileName);
            var report = new DataValidationReport();

            data.Validate(report);

            Assert.IsFalse(report.HasErrors, "Data 検証エラー:\n- " + string.Join("\n- ", report.Errors));
        }

        [TestCase("SO_Enemy_Melee_Prototype", "reward_enemy_melee")]
        [TestCase("SO_Enemy_Ranged_Prototype", "reward_enemy_ranged")]
        [TestCase("SO_Enemy_Elite_Prototype", "reward_enemy_elite")]
        public void EnemyArchetype_HasReward(string enemyFile, string expectedRewardId)
        {
            EnemyArchetypeData enemy = LoadEnemy(enemyFile);

            Assert.IsNotNull(enemy.Reward, enemyFile + " に報酬 Data が未割当です（撃破しても徳が入りません）。");
            Assert.AreEqual(expectedRewardId, enemy.Reward.Id.Value);
        }
    }
}
