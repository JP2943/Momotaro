using System.Collections.Generic;
using Momotaro.Presentation.Transition;
using NUnit.Framework;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// 遷移中の表示代理の契約（P5.5 仕様書 §7.2。工程 P55-03d-2）。
    ///
    /// ここで固めるのは 4 つ。
    /// <list type="number">
    /// <item><description><b>描画部品しか持たない</b>——Rigidbody・Collider・Animator などを
    ///   1 つも持たない（§11 P06「表示代理から命中も登録も発生しない」）。</description></item>
    /// <item><description><b>見た目を写す</b>——Sprite・足元位置・縮尺・色・Sorting。</description></item>
    /// <item><description><b>位置はカメラと同じ進行度</b>で運ぶ（自前の時計で進めない）。</description></item>
    /// <item><description><b>コマ送りは表示専用の unscaled 時計</b>で、Down／Stagger は止める。</description></item>
    /// </list>
    ///
    /// 実 Actor からの写し取りと実 Renderer の隠し方は実 Scene が要るので PlayMode で見る。
    /// </summary>
    public sealed class P55DisplayProxyTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _spawned.Count; i++)
            {
                if (_spawned[i] != null)
                {
                    Object.DestroyImmediate(_spawned[i]);
                }
            }

            _spawned.Clear();
        }

        // ---------------------------------------------------------------- 持ち物

        /// <summary>
        /// <b>描画部品しか持たない</b>（§7.2／§11 P06）。
        ///
        /// Prefab を丸ごと複製してあとからスクリプトを外す方式だと、外し忘れが 1 つあるだけで
        /// 代理から命中や登録が発生する。だから「何を外したか」ではなく
        /// <b>「何も付いていないこと」</b>を見る——将来 Actor 側に部品が増えても、
        /// この検査は勝手に強くなる。
        /// </summary>
        [Test]
        public void TheProxy_CarriesNothingButItsRenderer()
        {
            AreaTransitionDisplayProxy proxy = NewProxy(out _);

            Component[] all = proxy.gameObject.GetComponents<Component>();
            var unexpected = new List<string>();
            for (int i = 0; i < all.Length; i++)
            {
                Component c = all[i];
                if (c is Transform || c is SpriteRenderer || c is AreaTransitionDisplayProxy)
                {
                    continue;
                }

                unexpected.Add(c.GetType().Name);
            }

            Assert.IsEmpty(unexpected,
                "代理は描画部品だけを持つ（余計な部品: " + string.Join(", ", unexpected) + "）。");

            // 子にも何も足さない（影・当たり・演出を連れてこない）。
            Assert.AreEqual(0, proxy.transform.childCount, "代理は子を持たない。");
            Assert.IsNull(proxy.GetComponent<Rigidbody>(), "物理を持たない。");
            Assert.IsNull(proxy.GetComponent<Collider>(), "当たりを持たない。");
            Assert.IsNull(proxy.GetComponent<Animator>(),
                "Animator を持たない（AnimationEvent が発火する経路を作らない。§7.2）。");
        }

        // ---------------------------------------------------------------- 写し

        /// <summary>見た目を写す（§7.2「同じ Sprite・足元位置・縮尺・色・Sorting」）。</summary>
        [Test]
        public void TheProxy_CopiesTheLookIncludingFootprintAndSorting()
        {
            AreaTransitionDisplayProxy proxy = NewProxy(out SpriteRenderer source);

            Assert.AreSame(source.sprite, proxy.Renderer.sprite, "同じ Sprite。");
            Assert.AreEqual(source.color, proxy.Renderer.color, "同じ色。");
            Assert.AreEqual(source.flipX, proxy.Renderer.flipX, "同じ向き。");
            Assert.AreEqual(source.sortingOrder, proxy.Renderer.sortingOrder, "同じ Sorting 順。");
            Assert.AreEqual(source.sortingLayerID, proxy.Renderer.sortingLayerID, "同じ Sorting レイヤ。");

            // <b>足元で合わせる。</b> 根ではなく描画ノードの位置を使う——
            // 根で合わせると代理だけ沈む／浮く。
            Assert.AreEqual(source.transform.position, proxy.transform.position, "同じ足元位置。");
            Assert.AreEqual(source.transform.lossyScale, proxy.transform.localScale, "同じ縮尺。");
        }

        // ---------------------------------------------------------------- 進行度

        /// <summary>
        /// 位置は<b>与えられた進行度</b>で決まる（§7.2「カメラと同じ補間進行度で移動する」）。
        ///
        /// 自前の時計で位置を進めると、同じ 0.45 秒でも端でずれて
        /// 「主人公だけ先に着く」ように見える。だから時計から位置を作らせない。
        /// </summary>
        [Test]
        public void TheProxy_MovesOnlyByTheProgressItIsGiven()
        {
            AreaTransitionDisplayProxy proxy = NewProxy(out _);
            var from = new Vector3(1f, 0f, 2f);
            var to = new Vector3(11f, 0f, 2f);
            proxy.SetRoute(from, to);

            Assert.AreEqual(from, proxy.transform.position, "進行度 0 では出発位置。");

            proxy.SetProgress(0.5f);
            Assert.AreEqual(6f, proxy.transform.position.x, 0.001f, "半分で中間。");

            proxy.SetProgress(1f);
            Assert.AreEqual(to, proxy.transform.position, "1 で到着位置。");

            // 時計を回しても位置は動かない（位置と絵を別の入口にしてある）。
            Vector3 atEnd = proxy.transform.position;
            proxy.TickDisplayClock(1f);
            Assert.AreEqual(atEnd, proxy.transform.position, "表示時計は位置を動かさない。");

            proxy.SetProgress(2f);
            Assert.AreEqual(to, proxy.transform.position, "1 を超える指定でも行き過ぎない。");
            proxy.SetProgress(-1f);
            Assert.AreEqual(from, proxy.transform.position, "負の指定でも戻り過ぎない。");
        }

        // ---------------------------------------------------------------- コマ送り

        /// <summary>
        /// Move の 6 コマ周期を<b>表示専用の時計</b>で回す（§7.2）。
        ///
        /// コマが渡されていなければ<b>写した 1 枚を出し続ける</b>——手元に無いコマを
        /// 推測で作るより、止まった絵の方が嘘が小さい。
        /// </summary>
        [Test]
        public void TheProxy_PlaysTheSixFrameCycleOnItsOwnUnscaledClock()
        {
            AreaTransitionDisplayProxy proxy = NewProxy(out SpriteRenderer source);

            // コマが無いあいだは写した 1 枚のまま。
            proxy.TickDisplayClock(10f);
            Assert.AreSame(source.sprite, proxy.Renderer.sprite, "コマが無ければ絵は変わらない。");
            Assert.AreEqual(0, proxy.FrameIndex);

            List<Sprite> frames = NewFrames(AreaTransitionDisplayProxy.MoveFrameCount);
            proxy.SetMoveFrames(frames);
            Assert.AreSame(frames[0], proxy.Renderer.sprite, "渡した直後は 1 コマ目。");

            float perFrame = AreaTransitionDisplayProxy.MoveCycleSeconds / frames.Count;
            proxy.TickDisplayClock(perFrame * 1.1f);
            Assert.AreEqual(1, proxy.FrameIndex, "1 コマ進む。");
            Assert.AreSame(frames[1], proxy.Renderer.sprite);

            // 1 周して戻る（周期である）。
            proxy.TickDisplayClock(AreaTransitionDisplayProxy.MoveCycleSeconds);
            Assert.AreEqual(1, proxy.FrameIndex, "1 周まわって同じコマへ戻る。");

            Assert.Greater(proxy.DisplaySeconds, 0f, "表示専用の時計が進んでいる。");
        }

        /// <summary>
        /// Down／Stagger は<b>姿勢を保って位置だけ運ぶ</b>（§7.2）。
        /// 回復演出を勝手に再生しないので、コマ送りを止める。
        /// </summary>
        [Test]
        public void AFrozenProxy_KeepsItsPostureButStillTravels()
        {
            AreaTransitionDisplayProxy proxy = NewProxy(out _);
            List<Sprite> frames = NewFrames(AreaTransitionDisplayProxy.MoveFrameCount);
            proxy.SetMoveFrames(frames);
            proxy.Freeze(true);

            proxy.SetRoute(Vector3.zero, new Vector3(10f, 0f, 0f));
            proxy.TickDisplayClock(AreaTransitionDisplayProxy.MoveCycleSeconds * 3f);

            Assert.IsTrue(proxy.IsFrozen);
            Assert.AreEqual(0, proxy.FrameIndex, "コマは進まない（姿勢を保つ）。");
            Assert.AreSame(frames[0], proxy.Renderer.sprite);

            proxy.SetProgress(0.5f);
            Assert.AreEqual(5f, proxy.transform.position.x, 0.001f, "位置だけは運ばれる。");
        }

        // ---------------------------------------------------------------- 補助

        private AreaTransitionDisplayProxy NewProxy(out SpriteRenderer source)
        {
            var sourceGo = new GameObject("Actor_Visual");
            _spawned.Add(sourceGo);
            sourceGo.transform.position = new Vector3(3f, 0.9f, -2f);
            sourceGo.transform.localScale = new Vector3(1.3f, 1.3f, 1f);

            source = sourceGo.AddComponent<SpriteRenderer>();
            source.sprite = NewSprite();
            source.color = new Color(0.8f, 0.9f, 1f, 1f);
            source.flipX = true;
            source.sortingOrder = 7;

            var go = new GameObject("Proxy");
            _spawned.Add(go);
            AreaTransitionDisplayProxy proxy = go.AddComponent<AreaTransitionDisplayProxy>();
            proxy.Capture(source);
            return proxy;
        }

        private List<Sprite> NewFrames(int count)
        {
            var frames = new List<Sprite>();
            for (int i = 0; i < count; i++)
            {
                frames.Add(NewSprite());
            }

            return frames;
        }

        private Sprite NewSprite()
        {
            var texture = new Texture2D(4, 4);
            _spawned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0f));
            _spawned.Add(sprite);
            return sprite;
        }
    }
}
