using System.Collections.Generic;
using Momotaro.Gameplay.Enemy.Defense;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05B：<see cref="EnemyDefeatFadePresenter"/> が撃破イベントを受けて対応する敵の SpriteRenderer をフェードアウトさせることを検証する。
    /// EnemyId→Renderer の解決、フェード進行と満了、破棄済みの無処理、素材(Renderer)未割当の無例外、ClearAll を確認する。
    /// </summary>
    public sealed class EnemyDefeatFadePresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeDefeatSource : MonoBehaviour, IEnemyDefeatSource
        {
            public EnemyDefeatChannel Defeats { get; } = new EnemyDefeatChannel();
            public int DamageableId { get; set; }
            public bool IsDefeated { get; set; }
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                if (o != null) Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        private EnemyDefeatFadePresenter New()
        {
            var go = new GameObject("Fade");
            _spawned.Add(go);
            var p = go.AddComponent<EnemyDefeatFadePresenter>();
            p.FadeSeconds = 0.2f;
            return p;
        }

        private FakeDefeatSource NewEnemy(int id, bool withRenderer, out SpriteRenderer renderer)
        {
            var go = new GameObject("Enemy" + id);
            _spawned.Add(go);
            var src = go.AddComponent<FakeDefeatSource>();
            src.DamageableId = id;
            renderer = null;
            if (withRenderer)
            {
                var child = new GameObject("Sprite", typeof(SpriteRenderer));
                _spawned.Add(child);
                child.transform.SetParent(go.transform, false);
                renderer = child.GetComponent<SpriteRenderer>();
                renderer.color = Color.white;
            }

            return src;
        }

        private static void Publish(FakeDefeatSource src)
        {
            src.IsDefeated = true;
            src.Defeats.Publish(new EnemyDefeatedEvent(src.DamageableId, default));
        }

        [Test]
        public void Defeat_StartsFade_AndCompletes()
        {
            EnemyDefeatFadePresenter p = New();
            FakeDefeatSource enemy = NewEnemy(7, true, out SpriteRenderer r);
            p.Bind(new IEnemyDefeatSource[] { enemy });

            Publish(enemy);
            Assert.AreEqual(1, p.ActiveFadeCount, "撃破でフェード開始。");

            p.Tick(0.1f); // t=0.5
            Assert.AreEqual(0.5f, r.color.a, 0.03f, "フェード中はアルファが減衰。");

            p.Tick(0.2f); // 満了
            Assert.AreEqual(0, p.ActiveFadeCount, "満了でフェード終了。");
            Assert.AreEqual(0f, r.color.a, 0.001f, "透明へ到達。");
        }

        [Test]
        public void BeginFade_Directly_FadesRenderer()
        {
            EnemyDefeatFadePresenter p = New();
            NewEnemy(1, true, out SpriteRenderer r);

            p.BeginFade(new[] { r });
            Assert.AreEqual(1, p.ActiveFadeCount);
            p.Tick(0.2f);
            Assert.AreEqual(0f, r.color.a, 0.001f);
        }

        [Test]
        public void DestroyedRenderer_NoException()
        {
            EnemyDefeatFadePresenter p = New();
            FakeDefeatSource enemy = NewEnemy(3, true, out SpriteRenderer r);
            p.Bind(new IEnemyDefeatSource[] { enemy });
            Publish(enemy);
            Assert.AreEqual(1, p.ActiveFadeCount);

            Object.DestroyImmediate(r.gameObject);
            Assert.DoesNotThrow(() => p.Tick(0.05f));
            Assert.AreEqual(0, p.ActiveFadeCount, "破棄済みはフェードを終える（残留なし）。");
        }

        [Test]
        public void NoRenderer_Defeat_NoFade_NoException()
        {
            EnemyDefeatFadePresenter p = New();
            FakeDefeatSource enemy = NewEnemy(9, false, out _);
            p.Bind(new IEnemyDefeatSource[] { enemy });

            Assert.DoesNotThrow(() => Publish(enemy));
            Assert.AreEqual(0, p.ActiveFadeCount, "表示体が無い敵は無処理。");
        }

        [Test]
        public void ClearAll_StopsFades()
        {
            EnemyDefeatFadePresenter p = New();
            FakeDefeatSource enemy = NewEnemy(2, true, out _);
            p.Bind(new IEnemyDefeatSource[] { enemy });
            Publish(enemy);
            Assert.AreEqual(1, p.ActiveFadeCount);

            p.ClearAll();
            Assert.AreEqual(0, p.ActiveFadeCount);
        }
    }
}
