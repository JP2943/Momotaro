using System.Collections.Generic;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 「口火を切った一撃」のヘイト（試遊フィードバック 2026-09-21）。
    ///
    /// 報告された状況は、①敵は未認識 →②犬丸が先に殴る →③なのに敵は主人公へ向かう、というもの。実機計測では
    /// 犬丸が狙われるまで 6 発・8.8 秒かかっていた（主人公の基礎ヘイト 50 は減衰しない下限で、犬丸は 1 発 14.3 しか積めないため）。
    /// そこで「まだ誰とも交戦していない敵に最初の実害を与えた相手」へ 1 戦闘 1 回だけ大きく加算する。
    ///
    /// ここで固定するのは 3 つ。<b>1 戦闘 1 回であること</b>、<b>先制した仲間が実際に狙われること</b>、そして
    /// <b>出荷アセットの数値がその効果を出せる関係にあること</b>（後者が無いと、Data に項目を足しても
    /// Prefab 側が 0 のまま黙って無効になる——実際に Unity は未記載の直列化項目を 0 で読む）。
    /// </summary>
    public sealed class EnemyFirstStrikeThreatTests
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/PF_Player_Momotaro.prefab";
        private const string InumaruDataPath = "Assets/_Project/Data/Companions/SO_Companion_Inumaru.asset";
        private const string EnemyPrefabFolder = "Assets/_Project/Prefabs/Enemies";

        private sealed class FakeThreatTarget : IThreatTarget
        {
            public int ActorId { get; set; }
            public CombatFaction Faction { get; set; } = CombatFaction.Player;
            public Vector3 Position { get; set; }
            public bool IsActive { get; set; } = true;
            public bool IsDown { get; set; }
            public float BaseThreat { get; set; }
            public float AcquiredThreatMultiplier { get; set; } = 1f;
            public float MaxAcquiredThreat { get; set; }
        }

        /// <summary>何もしていない主人公（基礎 50・倍率 1.0）。</summary>
        private static FakeThreatTarget IdlePlayer() => new FakeThreatTarget
        {
            ActorId = 1, Faction = CombatFaction.Player, BaseThreat = 50f, AcquiredThreatMultiplier = 1f,
        };

        /// <summary>犬丸（基礎 0・倍率 1.5）。</summary>
        private static FakeThreatTarget Inumaru() => new FakeThreatTarget
        {
            ActorId = 2, Faction = CombatFaction.Ally, BaseThreat = 0f, AcquiredThreatMultiplier = 1.5f,
            MaxAcquiredThreat = 75f,
        };

        // ================================================================
        // 1 戦闘 1 回
        // ================================================================

        [Test]
        public void FirstStrike_IsGrantedOnce_AndRearmsOnlyAfterTheFightEnds()
        {
            var table = new EnemyThreatTable(ThreatSettings.Default);

            Assert.IsFalse(table.FirstStrikeConsumed, "戦闘前は未使用。");
            Assert.IsTrue(table.TryConsumeFirstStrike(), "最初の一撃だけが口火になる。");
            Assert.IsTrue(table.FirstStrikeConsumed);
            Assert.IsFalse(table.TryConsumeFirstStrike(), "2 発目以降は口火にならない（殴るたびに大加算しない）。");
            Assert.IsFalse(table.TryConsumeFirstStrike());

            table.Reset(); // 撃破・帰還完了（EnemyThreatTracker が呼ぶ）。

            Assert.IsFalse(table.FirstStrikeConsumed, "次の戦闘では再装填される。");
            Assert.IsTrue(table.TryConsumeFirstStrike(), "次の戦闘でも、先制した側が狙われる。");
        }

        /// <summary>
        /// 加算量は由来の重み × 対象の獲得倍率（既存の加算規則と同じ扱い）。犬は 1.5 倍、主人公は等倍。
        /// </summary>
        [Test]
        public void FirstStrike_UsesTheTargetsAcquiredMultiplier()
        {
            var table = new EnemyThreatTable(ThreatSettings.Default);
            FakeThreatTarget dog = Inumaru();
            FakeThreatTarget player = IdlePlayer();

            table.AddThreat(dog, ThreatSource.FirstStrike);
            table.AddThreat(player, ThreatSource.FirstStrike);

            float expectedDog = ThreatSettings.Default.FirstStrikeThreat * 1.5f;
            float expectedPlayer = ThreatSettings.Default.FirstStrikeThreat * 1f;
            Assert.AreEqual(expectedDog, table.GetAcquired(dog.ActorId), 0.01f, "犬は獲得倍率 1.5 が乗る。");
            Assert.AreEqual(expectedPlayer, table.GetAcquired(player.ActorId), 0.01f, "主人公は等倍。");
        }

        // ================================================================
        // 先制した仲間が実際に狙われる（報告された状況そのもの）
        // ================================================================

        /// <summary>
        /// 主人公が何もしていない（基礎 50 のみ）状態で、犬丸が 1 発だけ入れる。
        /// 口火のぶんで切替閾値（現対象 × 1.25）を超え、<b>次の再評価で狙いが犬丸へ移る</b>。
        /// 口火が無い場合（0 のとき）は 1 発では移らないことも同じ条件で確かめ、効いているのが口火だと示す。
        /// </summary>
        [Test]
        public void OneOpeningHitByTheCompanion_TurnsTheEnemyOnIt_WhereasWithoutFirstStrikeItDoesNot()
        {
            FakeThreatTarget player = IdlePlayer();
            FakeThreatTarget dog = Inumaru();
            var candidates = new List<IThreatTarget> { player, dog };

            // 実機計測と同じ 1 発分（HP 5・体幹 6）。
            const float hitHp = 5f;
            const float hitPoise = 6f;

            var withFirstStrike = new EnemyThreatTable(ThreatSettings.Default);
            withFirstStrike.UpdateSelection(candidates, 0f, attackLocked: false); // 交戦前：基礎の高い主人公が選ばれる。
            Assert.AreEqual(player.ActorId, withFirstStrike.CurrentTargetId, "前提：交戦前の狙いは主人公。");

            Assert.IsTrue(withFirstStrike.TryConsumeFirstStrike());
            withFirstStrike.AddThreat(dog, ThreatSource.FirstStrike);
            withFirstStrike.AddThreat(dog, ThreatSource.HpDamage, hitHp);
            withFirstStrike.AddThreat(dog, ThreatSource.PoiseDamage, hitPoise);
            withFirstStrike.UpdateSelection(candidates, ThreatSettings.Default.ReevaluateInterval, attackLocked: false);

            Assert.AreEqual(dog.ActorId, withFirstStrike.CurrentTargetId,
                "先制した犬丸へ、1 発で狙いが移る。犬丸の脅威=" + withFirstStrike.GetThreat(dog).ToString("0.0")
                + " / 主人公=" + withFirstStrike.GetThreat(player).ToString("0.0"));

            // 対照：口火を 0 にすると、同じ 1 発では移らない（＝効いているのは口火のぶん）。
            ThreatSettings noFirstStrike = WithoutFirstStrike(ThreatSettings.Default);
            var without = new EnemyThreatTable(noFirstStrike);
            without.UpdateSelection(candidates, 0f, attackLocked: false);
            without.AddThreat(dog, ThreatSource.HpDamage, hitHp);
            without.AddThreat(dog, ThreatSource.PoiseDamage, hitPoise);
            without.UpdateSelection(candidates, noFirstStrike.ReevaluateInterval, attackLocked: false);

            Assert.AreEqual(player.ActorId, without.CurrentTargetId,
                "口火が無ければ 1 発では移らない（これが試遊で「ヘイトが向かない」と見えていた状態）。");
        }

        /// <summary>
        /// 実害の無い命中（HP も体幹も 0）で 1 回限りの権利を使い切らない。
        /// 使い切ると、その後の本当の先制が普通の一撃に落ちてしまう。
        /// </summary>
        [Test]
        public void GrazingHitWithNoDamage_DoesNotSpendTheFirstStrike()
        {
            var table = new EnemyThreatTable(ThreatSettings.Default);
            FakeThreatTarget dog = Inumaru();

            // EnemyThreatTracker は「HP も体幹も 0」の命中では TryConsumeFirstStrike を呼ばない。
            // ここではその契約（呼ばなければ消費されない）を固定する。
            Assert.IsFalse(table.FirstStrikeConsumed);

            table.AddThreat(dog, ThreatSource.HpDamage, 0f); // 0 加算は無視される。
            Assert.IsFalse(table.FirstStrikeConsumed, "実害の無い命中では口火を消費しない。");
            Assert.AreEqual(0f, table.GetAcquired(dog.ActorId), 0.001f);
        }

        // ================================================================
        // 獲得ヘイトの上限（仲間が戦闘を抱え込まない）
        // ================================================================

        /// <summary>
        /// 仲間の獲得ヘイトは天井で止まる。天井が無いと、先制した仲間が殴り続けるかぎり脅威が伸び続け、
        /// 主人公が本気で殴っても追いつけない＝主人公が殴り放題になる（試遊フィードバック 2026-09-21 その 2）。
        /// </summary>
        [Test]
        public void CompanionAcquiredThreat_StopsAtItsCap()
        {
            var table = new EnemyThreatTable(ThreatSettings.Default);
            FakeThreatTarget dog = Inumaru();

            table.AddThreat(dog, ThreatSource.FirstStrike);          // 50 × 1.5 = 75（＝天井ちょうど）
            Assert.AreEqual(75f, table.GetAcquired(dog.ActorId), 0.01f, "口火で天井に達する。");

            for (int i = 0; i < 10; i++)
            {
                table.AddThreat(dog, ThreatSource.HpDamage, 5f);     // 殴り続けても
                table.AddThreat(dog, ThreatSource.PoiseDamage, 9f);
            }

            Assert.AreEqual(75f, table.GetAcquired(dog.ActorId), 0.01f, "天井を超えない。");
            Assert.AreEqual(75f, table.GetThreat(dog), 0.01f, "基礎 0 なので脅威も天井どまり。");
        }

        /// <summary>主人公は無制限（天井 0）。仲間の天井を超えて積み上げ、狙いを取り戻せる。</summary>
        [Test]
        public void PlayerHasNoCap_AndTakesAggroBackFromTheCappedCompanion()
        {
            FakeThreatTarget player = IdlePlayer();
            FakeThreatTarget dog = Inumaru();
            var candidates = new List<IThreatTarget> { player, dog };
            var table = new EnemyThreatTable(ThreatSettings.Default);

            Assert.AreEqual(0f, player.MaxAcquiredThreat, "主人公は無制限。");

            // 犬丸が先制して天井（75）に達し、狙いを取る。
            table.UpdateSelection(candidates, 0f, attackLocked: false);
            table.AddThreat(dog, ThreatSource.FirstStrike);
            table.UpdateSelection(candidates, ThreatSettings.Default.ReevaluateInterval, attackLocked: false);
            Assert.AreEqual(dog.ActorId, table.CurrentTargetId, "前提：先制した犬丸が狙われる。");

            // 主人公が殴り返す。犬丸は天井で止まっているので、いずれ超えられる。
            float needed = 75f * ThreatSettings.Default.SwitchThresholdRatio; // 93.75
            int hits = 0;
            while (table.CurrentTargetId != player.ActorId && hits < 20)
            {
                hits++;
                table.AddThreat(player, ThreatSource.HpDamage, 9f);   // 通常攻撃 1 発ぶん
                table.AddThreat(player, ThreatSource.PoiseDamage, 12f);
                table.AddThreat(dog, ThreatSource.HpDamage, 5f);      // 犬丸も殴り続けるが天井で伸びない
                table.UpdateSelection(candidates, ThreatSettings.Default.ReevaluateInterval, attackLocked: false);
            }

            Assert.AreEqual(player.ActorId, table.CurrentTargetId,
                "主人公が殴り返せば狙いは戻る（" + hits + " 発。必要 " + needed.ToString("0.#")
                + " / 主人公 " + table.GetThreat(player).ToString("0.#") + "）。");
            Assert.LessOrEqual(hits, 5, "戻るまでが遅すぎると、短い戦闘では実質ずっと仲間が抱え込む。");
        }

        /// <summary>天井 0（未設定）は無制限。既存の主人公・敵の挙動を変えない。</summary>
        [Test]
        public void ZeroCap_MeansUnlimited()
        {
            var table = new EnemyThreatTable(ThreatSettings.Default);
            FakeThreatTarget unlimited = IdlePlayer(); // MaxAcquiredThreat = 0

            for (int i = 0; i < 10; i++)
            {
                table.AddThreat(unlimited, ThreatSource.HpDamage, 10f);
            }

            Assert.AreEqual(100f, table.GetAcquired(unlimited.ActorId), 0.01f, "0 は無制限（10 × 10）。");
        }

        // ================================================================
        // 出荷アセットの数値が、実際に狙いを動かせる関係になっているか
        // ================================================================

        /// <summary>
        /// 出荷される敵 Prefab の口火の値が、<b>主人公の基礎ヘイトを実際に上回れる</b>ことを検査する。
        ///
        /// 直列化項目を新しく足したとき、既存 Prefab の YAML には項目が無いので Unity は 0 で読む。
        /// コード側の既定値（<see cref="ThreatSettings.Default"/>）は Prefab が値を持っている限り使われないため、
        /// 「Data に足したのに実機では無効」という事故が起きる。数値そのものはオーナーの判断領域なので上限は縛らず、
        /// 「先制しても狙いが動かない値になっていないか」という関係だけを見る。
        /// </summary>
        [Test]
        public void ShippedEnemies_FirstStrikeThreat_CanActuallyPullAggroOffThePlayer()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, "主人公 Prefab がある: " + PlayerPrefabPath);
            PerceptionTargetBinder playerThreat = null;
            foreach (PerceptionTargetBinder b in player.GetComponentsInChildren<PerceptionTargetBinder>(true))
            {
                if (b.Faction == CombatFaction.Player)
                {
                    playerThreat = b;
                    break;
                }
            }

            Assert.IsNotNull(playerThreat, "主人公に脅威登録がある。");
            float playerBase = playerThreat.BaseThreat;

            var inumaru = AssetDatabase.LoadAssetAtPath<CompanionData>(InumaruDataPath);
            Assert.IsNotNull(inumaru, "犬丸 Data がある: " + InumaruDataPath);
            float dogBase = inumaru.BaseThreat;
            float dogMultiplier = inumaru.AcquiredThreatMultiplier;
            float dogCap = inumaru.MaxAcquiredThreat;
            Assert.Greater(dogCap, 0f,
                "犬丸に獲得ヘイトの上限が設定されていない（0＝無制限）。上限が無いと、先制した犬丸が戦闘を最後まで抱え込み、"
                + "主人公が殴り放題になる。Data: " + InumaruDataPath);
            Assert.Greater(dogMultiplier, 0f, "犬丸の獲得倍率が 0 だと、何を殴ってもヘイトが積まれない。");

            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { EnemyPrefabFolder });
            Assert.Greater(guids.Length, 0, "敵 Prefab が見つかる。");

            var checkedPrefabs = new List<string>();
            var offenders = new List<string>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    continue;
                }

                foreach (EnemyThreatTracker tracker in prefab.GetComponentsInChildren<EnemyThreatTracker>(true))
                {
                    var so = new SerializedObject(tracker);
                    SerializedProperty firstStrike = so.FindProperty("_settings._firstStrikeThreat");
                    SerializedProperty ratio = so.FindProperty("_settings._switchThresholdRatio");
                    Assert.IsNotNull(firstStrike, path + "：_firstStrikeThreat が直列化されている。");
                    Assert.IsNotNull(ratio, path + "：_switchThresholdRatio が直列化されている。");

                    checkedPrefabs.Add(path);

                    // 何もしていない主人公（基礎のみ）から狙いを奪うのに必要な脅威。
                    float needed = playerBase * ratio.floatValue;
                    float openingThreat = dogBase + firstStrike.floatValue * dogMultiplier;
                    if (openingThreat < needed)
                    {
                        offenders.Add(path + "：口火 " + firstStrike.floatValue.ToString("0.#")
                            + " ×犬丸倍率 " + dogMultiplier.ToString("0.##")
                            + "（＋基礎 " + dogBase.ToString("0.#") + "）= " + openingThreat.ToString("0.#")
                            + " < 必要 " + needed.ToString("0.#")
                            + "（主人公の基礎 " + playerBase.ToString("0.#") + " × 切替比率 " + ratio.floatValue.ToString("0.##") + "）");
                    }

                    // 天井が口火より低いと、先制ぶんが切り詰められて奇襲が効かなくなる。
                    if (dogCap > 0f && dogBase + dogCap < needed)
                    {
                        offenders.Add(path + "：犬丸の獲得上限 " + dogCap.ToString("0.#")
                            + "（＋基礎 " + dogBase.ToString("0.#") + "）= " + (dogBase + dogCap).ToString("0.#")
                            + " が必要 " + needed.ToString("0.#") + " を下回るため、先制しても狙いが移らない");
                    }
                }
            }

            Assert.Greater(checkedPrefabs.Count, 0, "ヘイト追跡を持つ敵 Prefab が 1 つ以上ある。");
            Assert.IsEmpty(offenders,
                "先制しても狙いが動かない敵がいる（Prefab の口火の値が小さすぎるか、未記載で 0 になっている）:\n- "
                + string.Join("\n- ", offenders));
        }

        private static ThreatSettings WithoutFirstStrike(in ThreatSettings s)
        {
            return new ThreatSettings(
                s.HpDamageWeight, s.PoiseDamageWeight, s.FlinchThreat, s.JustGuardThreat,
                s.DogTauntThreat, s.SupportSkillThreat, s.DebuffSkillThreat,
                s.ReevaluateInterval, s.SwitchThresholdRatio, s.DecayDelaySeconds, s.DecayRatePerSecond,
                firstStrikeThreat: 0f);
        }
    }
}
