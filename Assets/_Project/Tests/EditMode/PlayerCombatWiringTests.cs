using System.Reflection;
using Momotaro.Core.Identification;
using Momotaro.Data;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-12 統合受入（項目 L / 戦闘 loop 準備）：PF_Player_Momotaro に戦闘 loop が実際に配線されていることを
    /// 検査する。移動・ステップ・必殺技・コンボ・攻撃側ステータス・Vitals の各 Data 参照が割り当て済みで、
    /// かつ参照先 Data の Stable ID が有効であることを確認する（未割り当てなら実行時に戦闘が成立しない回帰を検出）。
    /// なお GuardData（JG パラメータ）は未割当時に既定値へフォールバックする設計のため必須配線から除外する。
    /// </summary>
    public sealed class PlayerCombatWiringTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "PF_Player_Momotaro が見つかりません（" + PrefabPath + "）。");
            return prefab;
        }

        private static Object GetSerializedRef(object component, string fieldName)
        {
            System.Type ty = component.GetType();
            FieldInfo f = null;
            while (ty != null && f == null)
            {
                f = ty.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                ty = ty.BaseType;
            }

            Assert.IsNotNull(f, "フィールドが見つかりません: " + fieldName);
            return f.GetValue(component) as Object;
        }

        private static void AssertWired(object component, string fieldName)
        {
            Object value = GetSerializedRef(component, fieldName);
            Assert.IsTrue(value != null, fieldName + " が割り当てられていません（戦闘 loop が成立しません）。");

            // 参照先が Data Asset なら Stable ID の書式も検証する。
            if (value is GameDataAsset data)
            {
                Assert.IsTrue(
                    StableIdFormat.IsValid(data.Id.Value),
                    fieldName + " の参照先 Stable ID が不正: '" + data.Id.Value + "' (" + data.name + ")");
            }
        }

        [Test]
        public void Prefab_PlayerStateController_HasAllCombatData()
        {
            GameObject prefab = LoadPrefab();
            var controller = prefab.GetComponentInChildren<PlayerStateController>(true);
            Assert.IsNotNull(controller, "PlayerStateController が Prefab に存在しません。");

            AssertWired(controller, "_movement");
            AssertWired(controller, "_stepData");
            AssertWired(controller, "_specialData");
            AssertWired(controller, "_attackCombo");
            AssertWired(controller, "_attackerStats");
        }

        [Test]
        public void Prefab_PlayerVitalsHolder_HasPlayerData()
        {
            GameObject prefab = LoadPrefab();
            var vitals = prefab.GetComponentInChildren<PlayerVitalsHolder>(true);
            Assert.IsNotNull(vitals, "PlayerVitalsHolder が Prefab に存在しません。");

            AssertWired(vitals, "_data");
        }
    }
}
