using System;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    public sealed class PlayerVitalsTests
    {
        private static PlayerData MakeData(int maxHp, int maxStamina)
        {
            var data = ScriptableObject.CreateInstance<PlayerData>();
            // CharacterData._maxHp（基底）と PlayerData._maxStamina を設定。
            typeof(CharacterData).GetField("_maxHp", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(data, maxHp);
            typeof(PlayerData).GetField("_maxStamina", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(data, maxStamina);
            return data;
        }

        [Test]
        public void FromData_GeneratesVitalsAtMax()
        {
            var data = MakeData(120, 80);
            var vitals = PlayerVitals.FromData(data);

            Assert.AreEqual(120, vitals.Health.Max);
            Assert.AreEqual(120, vitals.Health.Current);
            Assert.AreEqual(80, vitals.Stamina.Max);
            Assert.AreEqual(80, vitals.Stamina.Current);

            UnityEngine.Object.DestroyImmediate(data);
        }

        [Test]
        public void FromData_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PlayerVitals.FromData(null));
        }

        [Test]
        public void MutatingVitals_DoesNotModifySourceData()
        {
            var data = MakeData(100, 100);
            var vitals = PlayerVitals.FromData(data);

            vitals.Health.Change(-40);
            vitals.Stamina.SetMax(10);

            Assert.AreEqual(60, vitals.Health.Current);
            Assert.AreEqual(100, data.MaxHp, "SO 原本の MaxHp は不変");
            Assert.AreEqual(100, data.MaxStamina, "SO 原本の MaxStamina は不変");

            UnityEngine.Object.DestroyImmediate(data);
        }
    }
}
