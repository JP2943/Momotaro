using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05：<see cref="EnemyUnblockableWarningPresenter"/> がガード不能攻撃の予兆（Prepare）中に敵頭上へ予告を継続表示することを検証する。
    /// 予兆中の表示（頭上オフセット・ループ）、予兆終了・撃破での消灯、複数体、素材未割当の無処理、再利用、HideAll を確認する。
    /// </summary>
    public sealed class EnemyUnblockableWarningPresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeWarn : IEnemyUnblockableWarningSource
        {
            public bool IsUnblockableTelegraphing { get; set; }
            public Vector3 WarningPosition { get; set; }
        }

        private sealed class FakeWarnBehaviour : MonoBehaviour, IEnemyUnblockableWarningSource
        {
            public bool IsUnblockableTelegraphing { get; set; }
            public Vector3 WarningPosition { get; set; }
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

        private Sprite MakeSprite(string name)
        {
            var tex = new Texture2D(4, 4);
            _spawned.Add(tex);
            var s = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100f);
            s.name = name;
            _spawned.Add(s);
            return s;
        }

        private Sprite[] MakeWarningFrames()
        {
            return new[] { MakeSprite("w0"), MakeSprite("w1"), MakeSprite("w2"), MakeSprite("w3") };
        }

        private EnemyUnblockableWarningPresenter NewPresenter(bool assign = true)
        {
            var go = new GameObject("WarnPresenter");
            _spawned.Add(go);
            var p = go.AddComponent<EnemyUnblockableWarningPresenter>();
            if (assign)
            {
                p.WarningFrames = MakeWarningFrames();
            }

            return p;
        }

        private static WarningVfxInstance FirstShown(EnemyUnblockableWarningPresenter p)
        {
            var found = p.GetComponentsInChildren<WarningVfxInstance>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].IsShown) return found[i];
            }

            return null;
        }

        [Test]
        public void Telegraphing_ShowsWarning_AboveEnemy()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var src = new FakeWarn { IsUnblockableTelegraphing = true, WarningPosition = new Vector3(5f, 0f, 3f) };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });

            p.Tick(0.01f);

            Assert.AreEqual(1, p.ActiveCount, "予兆中は予告を表示する。");
            WarningVfxInstance w = FirstShown(p);
            Assert.IsNotNull(w);
            Assert.AreEqual(new Vector3(5f, 2f, 3f), w.transform.position, "既定の頭上オフセット(2m)を加えて表示。");
        }

        [Test]
        public void StopsTelegraphing_HidesWarning()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var src = new FakeWarn { IsUnblockableTelegraphing = true };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });
            p.Tick(0.01f);
            Assert.AreEqual(1, p.ActiveCount);

            src.IsUnblockableTelegraphing = false; // 予兆終了（Active へ）。
            p.Tick(0.01f);
            Assert.AreEqual(0, p.ActiveCount, "予兆終了で予告を消す。");
        }

        [Test]
        public void LoopsFrames_WhileTelegraphing()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var src = new FakeWarn { IsUnblockableTelegraphing = true };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });

            p.Tick(0.2f); // 既定ループ0.4秒・4コマ → idx=floor(0.2/0.4*4)=2。
            Assert.AreEqual("w2", FirstShown(p).CurrentSprite.name, "予兆中はコマをループ再生する。");
        }

        [Test]
        public void TwoEnemies_TwoWarnings()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var a = new FakeWarn { IsUnblockableTelegraphing = true, WarningPosition = new Vector3(1f, 0f, 0f) };
            var b = new FakeWarn { IsUnblockableTelegraphing = true, WarningPosition = new Vector3(-1f, 0f, 0f) };
            p.Bind(new IEnemyUnblockableWarningSource[] { a, b });

            p.Tick(0.01f);
            Assert.AreEqual(2, p.ActiveCount, "複数体それぞれに予告を出す。");
        }

        [Test]
        public void NoFrames_NoWarning_NoException()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter(assign: false);
            var src = new FakeWarn { IsUnblockableTelegraphing = true };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.ActiveCount, "素材未割当でも例外なく継続。");
        }

        [Test]
        public void DestroyedSource_HidesWarning()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var go = new GameObject("Enemy");
            _spawned.Add(go);
            var b = go.AddComponent<FakeWarnBehaviour>();
            b.IsUnblockableTelegraphing = true;
            p.Bind(new IEnemyUnblockableWarningSource[] { b });
            p.Tick(0.01f);
            Assert.AreEqual(1, p.ActiveCount);

            Object.DestroyImmediate(go);

            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.ActiveCount, "破棄された敵の予告は消灯（残留なし）。");
        }

        [Test]
        public void Reuses_HiddenInstances()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var src = new FakeWarn { IsUnblockableTelegraphing = true };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });

            p.Tick(0.01f);
            Assert.AreEqual(1, p.TotalCount);
            src.IsUnblockableTelegraphing = false;
            p.Tick(0.01f);
            Assert.AreEqual(0, p.ActiveCount);

            src.IsUnblockableTelegraphing = true;
            p.Tick(0.01f);
            Assert.AreEqual(1, p.ActiveCount);
            Assert.AreEqual(1, p.TotalCount, "隠したインスタンスを再利用し新規生成しない。");
        }

        [Test]
        public void HideAll_ClearsWarnings()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var src = new FakeWarn { IsUnblockableTelegraphing = true };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });
            p.Tick(0.01f);
            Assert.AreEqual(1, p.ActiveCount);

            p.HideAll();
            Assert.AreEqual(0, p.ActiveCount);
        }

        [Test]
        public void Warning_AppliesWarningColor_AsTint()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var color = new Color(1f, 0.1f, 0.1f, 1f);
            p.WarningColor = color;
            var src = new FakeWarn { IsUnblockableTelegraphing = true };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });

            p.Tick(0.01f);

            WarningVfxInstance w = FirstShown(p);
            Assert.IsNotNull(w);
            Assert.AreEqual(color, w.CurrentColor, "予告色（赤系 Tint）を適用する。");
        }

        [Test]
        public void ReusedWarning_ResetsColor_NoResidualTint()
        {
            EnemyUnblockableWarningPresenter p = NewPresenter();
            var src = new FakeWarn { IsUnblockableTelegraphing = true };
            p.Bind(new IEnemyUnblockableWarningSource[] { src });

            p.WarningColor = new Color(1f, 0f, 0f, 1f);
            p.Tick(0.01f);
            src.IsUnblockableTelegraphing = false;
            p.Tick(0.01f); // 隠してプールへ。
            Assert.AreEqual(0, p.ActiveCount);

            var second = new Color(1f, 0.5f, 0f, 1f);
            p.WarningColor = second;
            src.IsUnblockableTelegraphing = true;
            p.Tick(0.01f);

            Assert.AreEqual(1, p.TotalCount, "同じインスタンスを再利用。");
            Assert.AreEqual(second, FirstShown(p).CurrentColor, "再利用時に前回色が残らない。");
        }
    }
}
