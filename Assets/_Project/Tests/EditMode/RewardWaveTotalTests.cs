using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Data.Progression;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Gameplay.Progression;
using Momotaro.Gameplay.Scenes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P4-00 受入：試遊 4 Wave を全滅させたときの徳の合計を、実アセットを通した端から端まで（Session の撃破通知 →
    /// <see cref="CombatRewardCollector"/> → <see cref="PlayerProgressHolder"/>）で固定する。編成は §8.2 Table7 の
    /// W1 近接1／W2 遠距離1／W3 近接2＋遠距離1／W4 強敵1（＝近接3・遠距離2・強敵1）で、期待値は 3×10＋2×12＋1×40＝94。
    ///
    /// 個々の付与規則（GrantOnce・重複・負値・上限）は <c>PlayerProgressStateTests</c>／<c>CombatRewardCollectorTests</c> が
    /// 担保する。本テストは「実アセットの値」と「Wave 編成」を掛け合わせた合計の回帰に絞る。アセットの徳量を変更した場合は、
    /// 期待値 94 と併せてここが落ちるため、仕様変更なのか事故なのかを切り分けられる。
    /// </summary>
    public sealed class RewardWaveTotalTests
    {
        private const string RewardDir = "Assets/_Project/Data/Progression/";

        // 試遊 Scene の Wave 編成（§8.2 Table7）を役割ごとの総数へ畳んだもの。
        private const int MeleeCount = 3;
        private const int RangedCount = 2;
        private const int EliteCount = 1;

        private const int ExpectedTotalVirtue = 94;

        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private sealed class FakeEnemy : IEnemyDefeatSource
        {
            public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();
            public int DamageableId { get; set; }
            public bool IsDefeated { get; set; }
            public RewardData Reward { get; set; }
            public EnemyRole Role { get; set; }

            public void Kill()
            {
                Defeats.Publish(new EnemyDefeatedEvent(DamageableId,
                    new EnemyRewardRequest(DamageableId, Role, Reward, Vector3.zero)));
            }
        }

        private static void InvokePrivate(object target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(m, "method not found: " + method);
            m.Invoke(target, null);
        }

        private static RewardData LoadReward(string fileName)
        {
            var data = AssetDatabase.LoadAssetAtPath<RewardData>(RewardDir + fileName + ".asset");
            Assert.IsNotNull(data, "報酬アセットが見つかりません: " + RewardDir + fileName + ".asset");
            return data;
        }

        private T Make<T>(string name) where T : Component
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        [Test]
        public void FourWaveClear_GrantsExpectedTotalVirtue()
        {
            RewardData melee = LoadReward("SO_Reward_Enemy_Melee");
            RewardData ranged = LoadReward("SO_Reward_Enemy_Ranged");
            RewardData elite = LoadReward("SO_Reward_Enemy_Elite");

            int expected = (MeleeCount * melee.VirtueAmount)
                + (RangedCount * ranged.VirtueAmount)
                + (EliteCount * elite.VirtueAmount);
            Assert.AreEqual(ExpectedTotalVirtue, expected,
                "報酬アセットの徳量が仕様（近接10／遠距離12／強敵40）から変わっています。仕様変更なら本テストの期待値も更新すること。");

            CombatSessionController session = Make<CombatSessionController>("Session");
            PlayerProgressHolder progress = Make<PlayerProgressHolder>("PlayerProgress");
            CombatRewardCollector collector = Make<CombatRewardCollector>("RewardCollector");
            collector.Bind(session, progress);
            InvokePrivate(collector, "OnEnable"); // 二重購読は内部フラグで抑止されるため明示呼び出しは安全。

            var enemies = new List<FakeEnemy>();
            int nextId = 1;
            foreach ((RewardData reward, EnemyRole role, int count) in new[]
            {
                (melee, EnemyRole.Melee, MeleeCount),
                (ranged, EnemyRole.Ranged, RangedCount),
                (elite, EnemyRole.Elite, EliteCount),
            })
            {
                for (int i = 0; i < count; i++)
                {
                    var enemy = new FakeEnemy { DamageableId = nextId++, Reward = reward, Role = role };
                    session.RegisterEnemy(enemy);
                    enemies.Add(enemy);
                }
            }

            Assert.AreEqual(MeleeCount + RangedCount + EliteCount, session.AliveEnemyCount, "前提：6 体登録。");

            bool allCleared = false;
            session.AllEnemiesDefeated += () => allCleared = true;

            foreach (FakeEnemy enemy in enemies)
            {
                enemy.Kill();
            }

            Assert.AreEqual(ExpectedTotalVirtue, progress.Virtue, "4 Wave 全滅で得られる徳の合計。");
            Assert.AreEqual(enemies.Count, collector.GrantedCount, "6 体すべてで付与が成立する。");
            Assert.AreEqual(0, collector.AlreadyGrantedCount,
                "一般敵報酬は GrantOnce=false のため、同じ Reward を共有していても重複扱いにならない。");
            Assert.AreEqual(0, collector.NoRewardCount);
            Assert.AreEqual(0, progress.GrantedRewardCount, "GrantOnce=false は付与済み記録に残さない。");
            Assert.IsTrue(allCleared, "全滅通知（Victory 判定の入力）も発火する。");
            Assert.AreEqual(0, session.AliveEnemyCount);
        }

        [Test]
        public void ResetProgress_ReturnsTotalToZero()
        {
            RewardData melee = LoadReward("SO_Reward_Enemy_Melee");

            CombatSessionController session = Make<CombatSessionController>("Session");
            PlayerProgressHolder progress = Make<PlayerProgressHolder>("PlayerProgress");
            CombatRewardCollector collector = Make<CombatRewardCollector>("RewardCollector");
            collector.Bind(session, progress);
            InvokePrivate(collector, "OnEnable");

            var enemy = new FakeEnemy { DamageableId = 1, Reward = melee, Role = EnemyRole.Melee };
            session.RegisterEnemy(enemy);
            enemy.Kill();
            Assert.AreEqual(melee.VirtueAmount, progress.Virtue);

            // Retry は Scene 再読込で Holder ごと破棄されるため実機では自動的に 0 へ戻る（PlayMode の
            // CombatTrialReloadPlayTests が実 Scene で検証）。ここでは同じ初期状態へ明示的に戻せることを確認する。
            progress.ResetProgress();

            Assert.AreEqual(0, progress.Virtue);
            Assert.AreEqual(0, progress.GrantedRewardCount);
        }
    }
}
