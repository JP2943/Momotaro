using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Presentation.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3.5-05B：<see cref="HitFlashPresenter"/> が被弾対象の SpriteRenderer を一瞬点滅させ、満了で元色へ戻すことを検証する。
    /// IDamageable(Component) からの解決、再点滅で元色を保持、破棄済みの無処理、ClearAll、素材未割当の無例外を確認する。
    /// </summary>
    public sealed class HitFlashPresenterTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        private sealed class FakeDamageable : MonoBehaviour, IDamageable
        {
            public int DamageableId { get; set; } = 1;
            public void ReceiveHit(in HitInfo hit) { }
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

        private HitFlashPresenter New()
        {
            var go = new GameObject("Flash");
            _spawned.Add(go);
            var p = go.AddComponent<HitFlashPresenter>();
            p.FlashSeconds = 0.1f;
            return p;
        }

        private SpriteRenderer NewRenderer(Color orig)
        {
            var go = new GameObject("R", typeof(SpriteRenderer));
            _spawned.Add(go);
            var r = go.GetComponent<SpriteRenderer>();
            r.color = orig;
            return r;
        }

        [Test]
        public void TriggerRenderer_SetsFlashColor_ThenRestores()
        {
            HitFlashPresenter p = New();
            var orig = new Color(0.2f, 0.6f, 0.3f, 1f);
            SpriteRenderer r = NewRenderer(orig);

            p.TriggerRenderer(r, Color.white);
            Assert.AreEqual(1, p.ActiveCount);
            Assert.AreEqual(Color.white, r.color, "点滅開始で点滅色。");

            p.Tick(0.1f); // 満了。
            Assert.AreEqual(0, p.ActiveCount);
            Assert.AreEqual(orig, r.color, "満了で元色へ復帰。");
        }

        [Test]
        public void Trigger_ResolvesRendererFromDamageableChild()
        {
            HitFlashPresenter p = New();
            var enemy = new GameObject("Enemy");
            _spawned.Add(enemy);
            var dmg = enemy.AddComponent<FakeDamageable>();
            var child = new GameObject("Sprite", typeof(SpriteRenderer));
            _spawned.Add(child);
            child.transform.SetParent(enemy.transform, false);
            child.GetComponent<SpriteRenderer>().color = Color.green;

            p.Trigger(dmg, Color.white);

            Assert.AreEqual(1, p.ActiveCount, "被弾対象の子 SpriteRenderer を点滅。");
            Assert.AreEqual(Color.white, child.GetComponent<SpriteRenderer>().color);
        }

        [Test]
        public void Retrigger_KeepsOriginalColor()
        {
            HitFlashPresenter p = New();
            var orig = new Color(0.2f, 0.6f, 0.3f, 1f);
            SpriteRenderer r = NewRenderer(orig);

            p.TriggerRenderer(r, Color.white);
            p.Tick(0.05f);              // 途中（点滅色寄り）。
            p.TriggerRenderer(r, Color.red); // 再点滅：元色は保持されるべき。
            Assert.AreEqual(1, p.ActiveCount, "同一対象は多重生成しない。");

            p.Tick(0.1f);
            Assert.AreEqual(orig, r.color, "再点滅後も元色へ正しく復帰（前回点滅色を元色にしない）。");
        }

        [Test]
        public void DestroyedRenderer_IsUntracked_NoException()
        {
            HitFlashPresenter p = New();
            SpriteRenderer r = NewRenderer(Color.green);
            p.TriggerRenderer(r, Color.white);
            Assert.AreEqual(1, p.ActiveCount);

            Object.DestroyImmediate(r.gameObject);
            Assert.DoesNotThrow(() => p.Tick(0.01f));
            Assert.AreEqual(0, p.ActiveCount, "破棄済みは追跡解除（残留なし）。");
        }

        [Test]
        public void NullTarget_NoException()
        {
            HitFlashPresenter p = New();
            Assert.DoesNotThrow(() => p.Trigger(null, Color.white));
            Assert.DoesNotThrow(() => p.TriggerRenderer(null, Color.white));
            Assert.AreEqual(0, p.ActiveCount);
        }

        [Test]
        public void ClearAll_RestoresOriginal()
        {
            HitFlashPresenter p = New();
            var orig = new Color(0.1f, 0.2f, 0.9f, 1f);
            SpriteRenderer r = NewRenderer(orig);
            p.TriggerRenderer(r, Color.white);

            p.ClearAll();
            Assert.AreEqual(0, p.ActiveCount);
            Assert.AreEqual(orig, r.color, "全消去で元色へ戻す。");
        }
    }
}
