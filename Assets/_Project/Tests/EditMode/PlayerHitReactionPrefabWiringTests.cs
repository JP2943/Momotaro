using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-01 受入修正：Player Prefab（PF_Player_Momotaro）へ <see cref="PlayerHitReaction"/> が接続され、
    /// 実ゲームで <see cref="IPlayerHurtReaction"/> が解決できることを検出する回帰テスト。コード・テストは追加済みでも
    /// Prefab へ未接続だと Hurt/被弾後無敵が発生しないため、接続漏れを EditMode で自動検出する。
    /// </summary>
    public sealed class PlayerHitReactionPrefabWiringTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";

        private static GameObject LoadPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, "PF_Player_Momotaro が見つかりません（" + PrefabPath + "）。");
            return prefab;
        }

        [Test]
        public void Prefab_Loads()
        {
            Assert.IsNotNull(LoadPrefab());
        }

        [Test]
        public void Root_HasExactlyOnePlayerHitReaction()
        {
            GameObject prefab = LoadPrefab();
            Assert.AreEqual(1, prefab.GetComponents<PlayerHitReaction>().Length, "PlayerHitReaction はルートに 1 つだけ。");
            Assert.AreEqual(1, prefab.GetComponentsInChildren<PlayerHitReaction>(true).Length,
                "PlayerHitReaction は Prefab 全体でも 1 つだけ（重複配置なし）。");
        }

        [Test]
        public void Root_HasVitalsHolderAndStateController()
        {
            GameObject prefab = LoadPrefab();
            Assert.IsNotNull(prefab.GetComponent<PlayerVitalsHolder>(), "ルートに PlayerVitalsHolder が必要。");
            Assert.IsNotNull(prefab.GetComponent<PlayerStateController>(), "ルートに PlayerStateController が必要。");
        }

        [Test]
        public void HurtReaction_ResolvesFromRoot_AsInterface()
        {
            GameObject prefab = LoadPrefab();
            // Prefab Asset はシーン非在籍で activeInHierarchy=false のため、includeInactive:true で解決する
            // （実ゲームでは Player は在シーンで active のため既定解決で足りる。ここは接続の有無だけを検証する）。
            var reaction = prefab.GetComponentInParent<IPlayerHurtReaction>(true);
            Assert.IsNotNull(reaction, "ルートから IPlayerHurtReaction を解決できる（実ゲームの解決経路と同じ）。");
        }

        [Test]
        public void HurtReaction_ConfiguredDurations()
        {
            var reaction = LoadPrefab().GetComponent<PlayerHitReaction>();
            Assert.IsNotNull(reaction, "ルートに PlayerHitReaction。");
            Assert.AreEqual(0.30f, reaction.HurtSeconds, 1e-4f, "Hurt 硬直は 0.30 秒。");
            Assert.AreEqual(0.50f, reaction.PostHitInvincibleSeconds, 1e-4f, "被弾後無敵は 0.50 秒。");
        }

        [Test]
        public void Prefab_HasNoMissingScripts()
        {
            GameObject prefab = LoadPrefab();
            foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
            {
                Assert.IsNotNull(c, "Missing Script が含まれています（コンポーネント参照が壊れています）。");
            }
        }
    }
}
