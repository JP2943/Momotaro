using System;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data.Progression;
using Momotaro.Gameplay.Progression;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-00：報酬 Snapshot（<see cref="RewardSnapshot"/>）が原本 <see cref="RewardData"/> から必要な値だけを不変複製すること、
    /// 未設定（null）を「報酬なし」として正常に表現すること、生成後に原本を書き換えても Snapshot が揺れないことを検証する。
    /// </summary>
    public sealed class RewardSnapshotTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object o in _created)
            {
                if (o != null) UnityEngine.Object.DestroyImmediate(o);
            }

            _created.Clear();
        }

        /// <summary>継承階層を遡って private フィールドへ値を設定する（Data 原本は Inspector 専用の private フィールドを持つため）。</summary>
        internal static void SetPrivateField(object target, string field, object value)
        {
            Type t = target.GetType();
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

        internal static RewardData MakeReward(string id, int virtue, bool grantOnce, string itemId = null)
        {
            var data = ScriptableObject.CreateInstance<RewardData>();
            data.name = "SO_Reward_Test";
            SetPrivateField(data, "_id", new StableId(id));
            SetPrivateField(data, "_displayName", "Test Reward");
            SetPrivateField(data, "_virtueAmount", virtue);
            SetPrivateField(data, "_itemId", new StableId(itemId));
            SetPrivateField(data, "_grantOnce", grantOnce);
            return data;
        }

        private RewardData Track(RewardData data)
        {
            _created.Add(data);
            return data;
        }

        [Test]
        public void From_Null_IsNoReward()
        {
            RewardSnapshot snapshot = RewardSnapshot.From(null);

            Assert.IsFalse(snapshot.HasReward, "報酬未設定の敵は正常系として『報酬なし』で表す。");
            Assert.AreEqual(0, snapshot.VirtueAmount);
            Assert.IsTrue(snapshot.RewardId.IsEmpty);
            Assert.IsTrue(snapshot.ItemId.IsEmpty);
        }

        [Test]
        public void None_IsNoReward()
        {
            Assert.IsFalse(RewardSnapshot.None.HasReward);
        }

        [Test]
        public void From_Data_CopiesFields()
        {
            RewardData data = Track(MakeReward("reward_enemy_elite", 40, grantOnce: false, itemId: "item_kibidango"));

            RewardSnapshot snapshot = RewardSnapshot.From(data);

            Assert.IsTrue(snapshot.HasReward);
            Assert.AreEqual("reward_enemy_elite", snapshot.RewardId.Value);
            Assert.AreEqual(40, snapshot.VirtueAmount);
            Assert.AreEqual("item_kibidango", snapshot.ItemId.Value);
            Assert.IsFalse(snapshot.GrantOnce);
        }

        [Test]
        public void From_Data_ClampsNegativeVirtue()
        {
            RewardData data = Track(MakeReward("reward_broken", -10, grantOnce: true));

            RewardSnapshot snapshot = RewardSnapshot.From(data);

            Assert.AreEqual(0, snapshot.VirtueAmount);
            Assert.IsTrue(snapshot.GrantOnce);
        }

        [Test]
        public void Snapshot_IsImmutable_AgainstSourceChanges()
        {
            RewardData data = Track(MakeReward("reward_enemy_melee", 10, grantOnce: false));
            RewardSnapshot snapshot = RewardSnapshot.From(data);

            // 原本を後から書き換えても、生成済み Snapshot は変化しない（付与計算の正本は Snapshot 側）。
            SetPrivateField(data, "_virtueAmount", 999);

            Assert.AreEqual(10, snapshot.VirtueAmount);
            Assert.AreEqual(999, data.VirtueAmount);
        }

        [Test]
        public void Constructor_MarksHasReward()
        {
            var snapshot = new RewardSnapshot(new StableId("reward_x"), 5, new StableId(null), true);

            Assert.IsTrue(snapshot.HasReward);
            Assert.AreEqual(5, snapshot.VirtueAmount);
            Assert.IsTrue(snapshot.ItemId.IsEmpty);
        }
    }
}
