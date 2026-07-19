using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Data;
using Momotaro.Data.Characters;
using Momotaro.Editor.Validation;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class ProjectDataValidatorTests
    {
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

        private readonly List<Object> _created = new List<Object>();

        private PlayerData NewPlayer(string id, string display)
        {
            var p = ScriptableObject.CreateInstance<PlayerData>();
            p.name = id; // Asset 名の代わり
            SetId(p, id);
            SetDisplayName(p, display);
            _created.Add(p);
            return p;
        }

        [SetUp]
        public void SetUp()
        {
            // 重複検出時の GameLog.Error を捨て、Test Runner が LogError で失敗しないようにする。
            GameLog.SetSink(new TestLogSink());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created)
            {
                Object.DestroyImmediate(o);
            }

            _created.Clear();
            GameLog.SetSink(null);
        }

        [Test]
        public void Validate_AllValidUniqueAssets_HasNoErrors()
        {
            var assets = new List<GameDataAsset>
            {
                NewPlayer("player_momotaro", "桃太郎"),
                NewPlayer("player_alt", "分身"),
            };

            DataValidationReport report = ProjectDataValidator.Validate(assets);

            Assert.IsFalse(report.HasErrors, "正常な Sample はエラーなし: " + string.Join(", ", report.Errors));
        }

        [Test]
        public void Validate_InvalidIdAsset_IsDetected()
        {
            var assets = new List<GameDataAsset> { NewPlayer("Invalid ID", "不正") };

            DataValidationReport report = ProjectDataValidator.Validate(assets);

            Assert.IsTrue(report.HasErrors, "不正な Stable ID を検出するべき");
        }

        [Test]
        public void Validate_DuplicateStableIds_AreDetected()
        {
            var assets = new List<GameDataAsset>
            {
                NewPlayer("player_momotaro", "桃太郎A"),
                NewPlayer("player_momotaro", "桃太郎B"),
            };

            DataValidationReport report = ProjectDataValidator.Validate(assets);

            Assert.IsTrue(report.HasErrors, "ID 重複を検出するべき");
            bool hasDuplicateMessage = false;
            foreach (string e in report.Errors)
            {
                if (e.Contains("Duplicate"))
                {
                    hasDuplicateMessage = true;
                    break;
                }
            }

            Assert.IsTrue(hasDuplicateMessage, "重複メッセージが含まれるべき");
        }
    }
}
