using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Momotaro.Data.Characters;
using Momotaro.Gameplay.Enemy;
using Momotaro.Presentation.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Momotaro.Tests.PlayMode
{
    /// <summary>
    /// P3-11：仮 UI／デバッグオーバレイが Actor 破棄・Pool 返却・Camera 外でも参照例外を出さないことを検証する（§テスト）。実 Camera・実
    /// フレーム進行で頭上バーとデバッグ表示を動かし、Actor を破棄しても、Camera 背面へ移しても例外・エラーログが出ないことを確認する。
    /// </summary>
    public sealed class EnemyDiagnosticsPlayTests
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

        private Camera MakeCamera()
        {
            var go = new GameObject("MainCamera");
            _spawned.Add(go);
            go.transform.position = new Vector3(0, 5, -8);
            go.transform.LookAt(Vector3.zero);
            var cam = go.AddComponent<Camera>();
            go.tag = "MainCamera";
            return cam;
        }

        private GameObject MakeEnemyWithDiagnostics()
        {
            var arch = ScriptableObject.CreateInstance<EnemyArchetypeData>();
            _spawned.Add(arch);
            SetField(arch, "_maxHp", 40);
            SetField(arch, "_poiseMax", 40f);

            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var actor = go.AddComponent<EnemyActor>();
            SetField(actor, "_archetype", arch);
            go.AddComponent<EnemyOverheadBars>();
            var overlay = go.AddComponent<EnemyAiDebugOverlay>();
            overlay.Display = true; // デバッグ表示を有効化して描画経路を通す。
            return go;
        }

        [UnityTest]
        public IEnumerator Diagnostics_NoException_OnActorDestroy_AndOffCamera()
        {
            MakeCamera();
            GameObject enemy = MakeEnemyWithDiagnostics();
            var actor = enemy.GetComponent<EnemyActor>();

            for (int i = 0; i < 3; i++) yield return null; // 通常表示のフレーム。

            // Camera 背面へ移動（画面外）でも例外を出さない。
            enemy.transform.position = new Vector3(0, 0, -50f);
            for (int i = 0; i < 2; i++) yield return null;

            // Actor（表示元）を破棄しても、ビューは参照例外を出さずに描画をスキップする。
            Object.Destroy(actor);
            for (int i = 0; i < 3; i++) yield return null;

            LogAssert.NoUnexpectedReceived(); // ここまでで Error/Exception ログが無いこと。
            Assert.Pass("Actor 破棄・画面外でも参照例外なし。");
        }
    }
}
