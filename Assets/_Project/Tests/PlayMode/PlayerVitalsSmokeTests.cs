using System.Collections;
using System.Reflection;
using Momotaro.Core.Logging;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Player;
using Momotaro.Infrastructure.Bootstrap;
using Momotaro.Infrastructure.SceneFlow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// SCN_VS_Field の Player が Runtime Vitals を最大値で保持し、実行中に SO 原本を
    /// 変更しないことを確認する Smoke Test（Phase1 P1-10 受入）。
    /// PlayerVitalsHolder / SO_Player_Momotaro が未配置・未割当の場合は失敗し、整備漏れを検出する。
    /// </summary>
    public sealed class PlayerVitalsSmokeTests
    {
        private sealed class NullLogSink : ILogSink
        {
            public void Write(LogLevel level, string message) { }
        }

        [SetUp]
        public void SetUp()
        {
            GameLog.SetSink(new NullLogSink());
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.SetSink(null);
        }

        [UnityTest]
        public IEnumerator VsField_PlayerVitals_StartFull_AndDoNotMutateAsset()
        {
            yield return SceneManager.LoadSceneAsync(SceneNames.VsField, LoadSceneMode.Single);

            // テスト実行文脈では RuntimeInitializeOnLoadMethod が発火しないため、常駐を明示的に用意する。
            if (!BootstrapRoot.HasInstance)
            {
                new GameObject("[TestBootstrapRoot]").AddComponent<BootstrapRoot>();
            }

            // Awake で Vitals が生成されるまで待つ。
            PlayerVitalsHolder holder = null;
            float timeout = 8f;
            while (timeout > 0f)
            {
                holder = Object.FindFirstObjectByType<PlayerVitalsHolder>();
                if (holder != null && holder.Vitals != null)
                {
                    break;
                }

                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            Assert.IsNotNull(holder, "SCN_VS_Field の Player に PlayerVitalsHolder が必要です。");
            Assert.IsNotNull(holder.Vitals,
                "PlayerVitals が生成されていません（PlayerData（SO_Player_Momotaro）の割当を確認してください）。");

            // 割り当てられた SO 原本を取得する。
            FieldInfo dataField = typeof(PlayerVitalsHolder)
                .GetField("_data", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(dataField, "PlayerVitalsHolder._data が見つかりません（実装が変更された可能性）。");

            var data = dataField.GetValue(holder) as PlayerData;
            Assert.IsNotNull(data,
                "PlayerVitalsHolder に PlayerData（SO_Player_Momotaro）が割り当てられていません。");

            int assetMaxHp = data.MaxHp;
            int assetMaxStamina = data.MaxStamina;

            // HP・スタミナは最大値から開始する。
            Assert.AreEqual(assetMaxHp, holder.Vitals.Health.Max, "HP Max が SO 原本と一致しません。");
            Assert.AreEqual(assetMaxHp, holder.Vitals.Health.Current, "HP は最大値から開始する必要があります。");
            Assert.AreEqual(assetMaxStamina, holder.Vitals.Stamina.Max, "Stamina Max が SO 原本と一致しません。");
            Assert.AreEqual(assetMaxStamina, holder.Vitals.Stamina.Current,
                "Stamina は最大値から開始する必要があります。");

            // 数フレーム進めても SO 原本（ScriptableObject）が変更されないこと。
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            Assert.AreEqual(assetMaxHp, data.MaxHp, "実行中に SO 原本の MaxHp が変更されました。");
            Assert.AreEqual(assetMaxStamina, data.MaxStamina, "実行中に SO 原本の MaxStamina が変更されました。");
        }
    }
}
