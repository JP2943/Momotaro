using System.Collections;
using Momotaro.Gameplay.Enemy.Combat.Projectile;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3.5-08：試遊 Scene の実再読込による再初期化と、3 回以上の連続 Retry 回帰を検証する（仕様書 §9.2 / §11）。
    /// 実 Scene を Single で 3 回読み込み直し、毎回「HP 満タン・Wave 先頭・残留 Projectile なし・Session が Preparing/Playing・
    /// 主要要素が 1 つずつ（重複生成／購読の累積なし）」を確認する。対象 Scene が Build Settings 未登録で読み込めない環境では
    /// <see cref="Assert.Ignore(string)"/> で明示スキップする（実行条件を隠さない）。
    /// </summary>
    public sealed class CombatTrialReloadPlayTests
    {
        private const string SceneName = "SCN_Phase35_CombatTrial";

        /// <summary>
        /// 読み込んだ試遊 Scene を後続テストへ持ち越さない（P4-00 追加）。本テストは Scene を Single で読み込んだまま終了するため、
        /// 残留した Scene 常駐物が後続の PlayMode テストを壊す：Wave1 の敵が「Scene 全体の敵数」を数えるテストに混入し、
        /// Floor/Wall の Collider が物理テストの移動体を拘束し、<c>GameplaySceneMode</c> が（GameModeProvider が null の間 Update で
        /// 適用を再試行し続けるため）後続テストの差し込んだ GameMode サービスへ Exploration を上書きして Pause 判定を崩す。
        /// ここで Scene のルートを破棄し、GameMode の提供点も初期化して、テスト間の独立性を回復する。
        /// </summary>
        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && active.name == SceneName)
            {
                foreach (GameObject root in active.GetRootGameObjects())
                {
                    if (root != null)
                    {
                        Object.DestroyImmediate(root); // 遅延破棄だと次テストの 1 フレーム目まで残るため即時。
                    }
                }
            }

            GameModeProvider.Current = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reload_Reinitializes_And_SurvivesRepeatedRetries()
        {
            if (!Application.CanStreamedLevelBeLoaded(SceneName))
            {
                Assert.Ignore("試遊 Scene が Build Settings に未登録のため実再読込テストをスキップします（" + SceneName
                    + "）。File > Build Profiles で Scene List に追加すると本テストが有効になります。");
            }

            for (int i = 0; i < 3; i++)
            {
                int pass = i + 1;
                yield return SceneManager.LoadSceneAsync(SceneName, LoadSceneMode.Single);
                // Awake/OnEnable/Start（Wave1 自動開始）まで数フレーム進める。敵はまだ接近前で被弾しない。
                yield return null;
                yield return null;

                Assert.AreEqual(1, Object.FindObjectsByType<CombatSessionController>(FindObjectsSortMode.None).Length,
                    "再読込 " + pass + " 回目：Session は 1 つ（重複生成・購読累積なし）。");
                Assert.AreEqual(1, Object.FindObjectsByType<WaveRunner>(FindObjectsSortMode.None).Length,
                    "再読込 " + pass + " 回目：WaveRunner は 1 つ。");
                Assert.AreEqual(1, Object.FindObjectsByType<CombatPlayHud>(FindObjectsSortMode.None).Length,
                    "再読込 " + pass + " 回目：試遊 HUD は 1 つ。");

                var session = Object.FindFirstObjectByType<CombatSessionController>();
                Assert.IsNotNull(session, "再読込 " + pass + " 回目：Session が存在。");
                Assert.IsTrue(session.State == CombatSessionState.Preparing || session.State == CombatSessionState.Playing,
                    "再読込直後は Preparing/Playing（Victory/Defeat/Reloading を持ち越さない）。実際: " + session.State);

                var waves = Object.FindFirstObjectByType<WaveRunner>();
                Assert.IsNotNull(waves);
                Assert.LessOrEqual(waves.CurrentWave, 1, "Wave は先頭（1 以下）から再開。");

                var vitals = Object.FindFirstObjectByType<PlayerVitalsHolder>();
                Assert.IsNotNull(vitals, "Player Vitals が存在。");
                Assert.IsNotNull(vitals.Vitals, "Vitals が生成済み。");
                Assert.AreEqual(vitals.Vitals.Health.Max, vitals.Vitals.Health.Current,
                    "再読込直後は HP 満タン（新規 Session 初期化）。");

                Assert.AreEqual(0, EnemyProjectileRegistry.LiveCount,
                    "再読込直後に残留 Projectile なし（静的レジストリのリークなし）。");
            }
        }
    }
}
