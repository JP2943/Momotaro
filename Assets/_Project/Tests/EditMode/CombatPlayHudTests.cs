using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Scenes;
using Momotaro.Presentation.Hud;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-04：<see cref="CombatPlayHud"/> の Canvas 構築（Screen Space・16:9 基準）、二重構築で UI を重複させないこと、
    /// Player／Session の遅延 Bind を検証する（仕様書 §6）。描画の見た目（Anchor 位置・フリンジ等）は手動受入。
    /// </summary>
    public sealed class CombatPlayHudTests
    {
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

        private static void SetField(object target, string name, object value)
        {
            System.Type t = target.GetType();
            FieldInfo f = null;
            while (t != null && f == null)
            {
                f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            Assert.IsNotNull(f, "field not found: " + name);
            f.SetValue(target, value);
        }

        private CombatPlayHud NewHud()
        {
            var go = new GameObject("CombatPlayHud");
            _spawned.Add(go);
            return go.AddComponent<CombatPlayHud>();
        }

        [Test]
        public void PlainGameObject_EnsureBuilt_DoesNotThrow()
        {
            // 通常 Transform の GameObject に載っていても、transform を RectTransform へキャストせず
            // 子 Canvas を生成するため InvalidCastException を出さない（回帰防止）。
            CombatPlayHud hud = NewHud();
            Assert.IsNull(hud.transform as RectTransform, "前提：ルートは通常 Transform。");
            Assert.DoesNotThrow(() => hud.EnsureBuilt());
        }

        [Test]
        public void EnsureBuilt_CreatesScreenSpaceCanvas_With16By9Reference()
        {
            CombatPlayHud hud = NewHud();
            hud.EnsureBuilt();

            var canvas = hud.GetComponentInChildren<Canvas>();
            Assert.IsNotNull(canvas, "子に Canvas を構築する。");
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.IsNotNull(canvas.transform as RectTransform, "Canvas は RectTransform 上に載る。");

            var scaler = hud.GetComponentInChildren<CanvasScaler>();
            Assert.IsNotNull(scaler, "CanvasScaler を構築する。");
            Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(1920f, scaler.referenceResolution.x, 0.01f, "16:9 基準 1920 幅。");
            Assert.AreEqual(1080f, scaler.referenceResolution.y, 0.01f);

            Assert.Greater(hud.transform.childCount, 0, "表示要素（子 Canvas）を生成する。");
        }

        [Test]
        public void EnsureBuilt_IsIdempotent_NoDuplicateUi()
        {
            CombatPlayHud hud = NewHud();
            hud.EnsureBuilt();
            int childCount = hud.transform.childCount;

            hud.EnsureBuilt(); // Scene 再読込等での再構築要求相当。

            Assert.AreEqual(childCount, hud.transform.childCount, "二重構築で UI を重複生成しない。");
        }

        [Test]
        public void Bind_LateConnectsPlayerAndSession()
        {
            var playerGo = new GameObject("Player");
            _spawned.Add(playerGo);
            var holder = playerGo.AddComponent<PlayerVitalsHolder>();
            var data = ScriptableObject.CreateInstance<PlayerData>();
            _spawned.Add(data);
            SetField(data, "_maxHp", 100);
            SetField(data, "_maxStamina", 100);
            SetField(holder, "_data", data);

            var sessionGo = new GameObject("Session");
            _spawned.Add(sessionGo);
            var session = sessionGo.AddComponent<CombatSessionController>();

            CombatPlayHud hud = NewHud();
            hud.EnsureBuilt();
            hud.Bind(holder, null, session); // playerState は任意（null 可）。

            Assert.IsTrue(hud.ViewModel.HasPlayer, "遅延 Bind で Player を接続。");
            Assert.IsTrue(hud.ViewModel.HasSession, "遅延 Bind で Session を接続。");
            Assert.AreEqual(100, hud.ViewModel.HpMax);

            // 状態変化が VM を通じて反映される（例外なく再描画される）。
            Assert.DoesNotThrow(() => session.StartWave());
            Assert.AreEqual(CombatSessionState.Playing, hud.ViewModel.Phase);
        }
    }
}
