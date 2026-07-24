using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P2-11：フィードバック配信（<see cref="CombatFeedbackDispatcher"/>）が結果チャネルを購読して仮 Cue を配信し、無効化で確実に
    /// 購読解除、シーン再取得（<see cref="CombatFeedbackDispatcher.Rescan"/>）で購読し直し、Gameplay へ干渉しない（Logic 非依存）
    /// ことを検証する。
    /// </summary>
    public sealed class CombatFeedbackDispatcherTests
    {
        private static readonly MethodInfo OnDisableMethod =
            typeof(CombatFeedbackDispatcher).GetMethod("OnDisable", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            _spawned.Clear();
        }

        private static void SetField(object t, string n, object v)
        {
            System.Type ty = t.GetType();
            FieldInfo f = null;
            while (ty != null && f == null)
            {
                f = ty.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);
                ty = ty.BaseType;
            }

            f.SetValue(t, v);
        }

        private sealed class FakeFeedback : ICombatFeedbackListener
        {
            public bool Got;
            public int Count;
            public CombatFeedbackEvent Last;
            public void OnCombatFeedback(in CombatFeedbackEvent feedback) { Got = true; Count++; Last = feedback; }
        }

        private CombatDummy MakeDummy(int hp = 100)
        {
            var e = ScriptableObject.CreateInstance<EnemyData>();
            _spawned.Add(e);
            SetField(e, "_maxHp", hp);
            SetField(e, "_defense", 0f);
            SetField(e, "_poiseMax", 100f);
            SetField(e, "_flinchResistance", 60f);

            var go = new GameObject("Dummy");
            _spawned.Add(go);
            go.SetActive(false);
            var d = go.AddComponent<CombatDummy>();
            SetField(d, "_data", e);
            go.SetActive(true);
            return d;
        }

        private CombatFeedbackDispatcher MakeDispatcher()
        {
            var go = new GameObject("Dispatcher");
            _spawned.Add(go);
            return go.AddComponent<CombatFeedbackDispatcher>();
        }

        private PlayerVitalsHolder MakePlayer(bool active = true)
        {
            // 結果チャネル（Results）のみ使用するため PlayerData は不要（Vitals 未使用）。
            var go = new GameObject("Player");
            _spawned.Add(go);
            go.SetActive(false);
            var p = go.AddComponent<PlayerVitalsHolder>();
            go.SetActive(active);
            return p;
        }

        private static HitResult DamageOn(IDamageable target, int id)
        {
            return HitResult.Damage(HitId.Single(id), null, target, new HitDamage(6f, 0f, 0f));
        }

        [Test]
        public void Rescan_SubscribesToDummyResults()
        {
            var dummy = MakeDummy();
            var disp = MakeDispatcher();
            disp.Rescan();
            Assert.AreEqual(1, dummy.Results.ListenerCount, "ダミー結果を購読する。");
        }

        [Test]
        public void HitResult_PublishesFeedbackCue()
        {
            var dummy = MakeDummy();
            var disp = MakeDispatcher();
            disp.Rescan();
            var fake = new FakeFeedback();
            disp.Feedback.AddListener(fake);

            // ダミーの結果チャネルへ結果を流す（購読経由で Dispatcher が受ける）。
            dummy.Results.Publish(HitResult.Damage(HitId.Single(1), null, dummy, new HitDamage(8f, 0f, 0f)));

            Assert.IsTrue(fake.Got, "フィードバックが配信される。");
            Assert.AreEqual(HitResultKind.Damage, fake.Last.Result.Kind);
            Assert.AreEqual("SE_Hit_Normal", fake.Last.Cue.SeId, "種別に応じた仮 Cue。");
            Assert.Greater(fake.Last.Cue.HitStopSeconds, 0f);
        }

        [Test]
        public void OnDisable_UnsubscribesAll()
        {
            var dummy = MakeDummy();
            var disp = MakeDispatcher();
            disp.Rescan();
            Assert.AreEqual(1, dummy.Results.ListenerCount);

            OnDisableMethod.Invoke(disp, null);
            Assert.AreEqual(0, dummy.Results.ListenerCount, "無効化で購読解除（通知購読解除）。");
        }

        [Test]
        public void SceneReload_Rescan_ResubscribesNewDummies()
        {
            var first = MakeDummy();
            var disp = MakeDispatcher();
            disp.Rescan();
            Assert.AreEqual(1, first.Results.ListenerCount);

            // シーン再読込相当：古いダミーを破棄し、新しいダミーを生成 → 再取得で購読し直す。
            Object.DestroyImmediate(first.gameObject);
            var second = MakeDummy();
            disp.Rescan();
            Assert.AreEqual(1, second.Results.ListenerCount, "再取得で新しいダミーを購読。");
        }

        [Test]
        public void Hud_DebugToggle_TogglesVisibility()
        {
            var go = new GameObject("HUD");
            _spawned.Add(go);
            go.SetActive(false);
            var hud = go.AddComponent<CombatDebugHud>();
            go.SetActive(true);

            hud.SetVisible(true);
            Assert.IsTrue(hud.IsVisible);
            hud.ToggleVisible();
            Assert.IsFalse(hud.IsVisible, "Debug 切替で非表示へ。");
            hud.ToggleVisible();
            Assert.IsTrue(hud.IsVisible, "再切替で表示へ。");
        }

        [Test]
        public void Dispatch_DoesNotMutateGameplay_LogicIndependent()
        {
            var dummy = MakeDummy(100);
            var disp = MakeDispatcher();
            int hpBefore = dummy.CurrentHp;

            // Dispatcher の結果処理は読み取りのみ（HP・体幹などを変えない）。
            disp.OnHitResult(HitResult.Damage(HitId.Single(2), null, dummy, new HitDamage(8f, 0f, 0f)));

            Assert.AreEqual(hpBefore, dummy.CurrentHp, "フィードバック配信は Gameplay を変更しない。");
        }

        [Test]
        public void PlayerReplaced_Rescan_ResubscribesToNewPlayerOnce_UnsubscribesOld_NoDuplicate()
        {
            // 旧 Player（active）と新 Player（inactive）を用意。FindFirstObjectByType は active のみ検出する。
            var oldPlayer = MakePlayer(active: true);
            var newPlayer = MakePlayer(active: false);
            var disp = MakeDispatcher();

            disp.Rescan();
            Assert.AreEqual(1, oldPlayer.Results.ListenerCount, "初回は旧 Player を購読する。");

            // Player の入れ替え（再生成相当）：旧を非アクティブ、新をアクティブにして再取得。
            oldPlayer.gameObject.SetActive(false);
            newPlayer.gameObject.SetActive(true);
            disp.Rescan();

            Assert.AreEqual(0, oldPlayer.Results.ListenerCount, "旧 Player は購読解除される。");
            Assert.AreEqual(1, newPlayer.Results.ListenerCount, "新 Player を購読する。");

            var fake = new FakeFeedback();
            disp.Feedback.AddListener(fake);

            // 旧 Player の結果は受信しない。
            oldPlayer.Results.Publish(DamageOn(oldPlayer, 1));
            Assert.IsFalse(fake.Got, "旧 Player の結果は受信しない。");

            // 新 Player の結果は 1 回だけ受信する。
            newPlayer.Results.Publish(DamageOn(newPlayer, 2));
            Assert.AreEqual(1, fake.Count, "新 Player の結果を 1 回だけ受信する。");

            // 複数回 Rescan しても重複購読しない（同一対象は再購読しない）。
            disp.Rescan();
            disp.Rescan();
            Assert.AreEqual(1, newPlayer.Results.ListenerCount, "複数回 Rescan でも重複購読しない。");

            fake.Count = 0;
            newPlayer.Results.Publish(DamageOn(newPlayer, 3));
            Assert.AreEqual(1, fake.Count, "重複通知されない（1 回のみ）。");
        }

        [Test]
        public void PlayerRemoved_Rescan_ClearsSubscriptionState()
        {
            var player = MakePlayer(active: true);
            var disp = MakeDispatcher();
            disp.Rescan();
            Assert.AreEqual(1, player.Results.ListenerCount);

            // Player 破棄後の再取得で購読状態がクリアされ、以後の Rescan でも例外なく安定する。
            Object.DestroyImmediate(player.gameObject);
            Assert.DoesNotThrow(() => { disp.Rescan(); disp.Rescan(); }, "Player 不在でも Rescan は安定する。");
        }

        [Test]
        public void VsFieldScene_ContainsFeedbackDispatcher()
        {
            // 検証 Scene に Dispatcher が実配置され、実行時に購読・配信が働くことを担保する（スクリプト追加のみでないこと）。
            const string path = "Assets/_Project/Scenes/SCN_VS_Field.unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var found = Object.FindObjectsByType<CombatFeedbackDispatcher>(FindObjectsSortMode.None);
                Assert.GreaterOrEqual(found.Length, 1, "検証 Scene に CombatFeedbackDispatcher が配置されている。");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
