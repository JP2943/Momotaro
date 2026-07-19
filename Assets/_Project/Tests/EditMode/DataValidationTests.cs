using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data;
using Momotaro.Data.Characters;
using Momotaro.Data.Combat;
using Momotaro.Data.Events;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class DataValidationTests
    {
        // GameDataAsset の private フィールドをテストから設定するヘルパー。
        private static void SetId(GameDataAsset asset, string idValue)
        {
            FieldInfo field = typeof(GameDataAsset).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(asset, new StableId(idValue));
        }

        private static void SetDisplayName(GameDataAsset asset, string displayName)
        {
            FieldInfo field = typeof(GameDataAsset).GetField("_displayName", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(asset, displayName);
        }

        [Test]
        public void ValidAsset_ProducesNoErrors()
        {
            var data = ScriptableObject.CreateInstance<PlayerData>();
            SetId(data, "player_momotaro");
            SetDisplayName(data, "桃太郎");

            var report = new DataValidationReport();
            data.Validate(report);

            Assert.IsFalse(report.HasErrors, "有効なデータはエラーを出さないべき: "
                + string.Join(", ", report.Errors));
            Object.DestroyImmediate(data);
        }

        [Test]
        public void EmptyId_ProducesError()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            SetId(data, string.Empty);

            var report = new DataValidationReport();
            data.Validate(report);

            Assert.IsTrue(report.HasErrors);
            Object.DestroyImmediate(data);
        }

        [Test]
        public void InvalidIdFormat_ProducesError()
        {
            var data = ScriptableObject.CreateInstance<AttackData>();
            SetId(data, "Invalid ID");

            var report = new DataValidationReport();
            data.Validate(report);

            Assert.IsTrue(report.HasErrors);
            Object.DestroyImmediate(data);
        }

        [Test]
        public void Encounter_WithNoEnemies_ProducesError()
        {
            var data = ScriptableObject.CreateInstance<EncounterData>();
            SetId(data, "encounter_vs_first_battle");
            SetDisplayName(data, "最初の戦い");

            var report = new DataValidationReport();
            data.Validate(report);

            Assert.IsTrue(report.HasErrors, "敵IDが空の Encounter はエラーになるべき");
            Object.DestroyImmediate(data);
        }
    }
}
