using System.Collections.Generic;
using Momotaro.Presentation.Transition;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// <b>Move の既存 6 コマを実 Actor のクリップから取り出す</b>（P5.5 §7.2。工程 P55-09b）。
    ///
    /// コマ送りの仕掛け（<c>SetMoveFrames</c>／<c>TickDisplayClock</c>）は工程 P55-03d-2 からあったが、
    /// <b>本番では誰も素材を渡していなかった</b>——代理は写した 1 枚を出し続け、
    /// 通路を渡る主人公が<b>滑って移動する</b>絵になっていた。
    /// ここで固めるのは「クリップからコマが出ること」と「出ないときに黙らないこと」である。
    ///
    /// <b>Animator を代理へ載せない</b>という §7.2 の縛りがあるので、
    /// 取り出しは <c>AnimationClip.SampleAnimation</c>（本番でも動く API）で行う。
    /// クリップの組み立てだけは Editor の口（<c>AnimationUtility</c>）を使う——
    /// 本番のクリップは資産として既にあるので、ここは「素材を用意する」だけである。
    /// </summary>
    public sealed class P55MoveFrameSourceTests
    {
        private const float FrameRate = 12f;
        private const int FrameCount = 6;

        private readonly List<Object> _made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            AreaTransitionMoveFrames.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _made.Count; i++)
            {
                if (_made[i] != null)
                {
                    Object.DestroyImmediate(_made[i]);
                }
            }

            _made.Clear();
            AreaTransitionMoveFrames.ClearForTests();
        }

        /// <summary>
        /// 6 コマ・12fps のクリップから<b>6 コマ</b>と<b>0.5 秒の周期</b>が出る。
        ///
        /// <b>端の 1 コマが落ちやすい。</b> クリップの長さは「最後のキーの時刻」
        /// （6 コマ・12fps なら 0.41666 秒）なので、`t <= length` だけで刻むと 5 コマになる。
        /// </summary>
        [Test]
        public void ASixFrameClip_YieldsSixFramesAndAHalfSecondCycle()
        {
            AnimationClip clip = NewMoveClip(FrameCount, out List<Sprite> sprites);
            var into = new List<Sprite>();

            Assert.IsTrue(AreaTransitionMoveFrames.TryExtract(clip, into, out float cycleSeconds),
                "取り出せる。理由=" + AreaTransitionMoveFrames.LastReason);
            Assert.AreEqual(FrameCount, into.Count, "コマ数はクリップが決める。");
            Assert.AreEqual(FrameCount / FrameRate, cycleSeconds, 0.0001f, "周期は 6/12 = 0.5 秒。");
            Assert.AreEqual(AreaTransitionDisplayProxy.MoveCycleSeconds, cycleSeconds, 0.0001f,
                "既存クリップの周期と一致する（§7.2 の「既存 6 コマ周期」）。");

            for (int i = 0; i < FrameCount; i++)
            {
                Assert.AreSame(sprites[i], into[i], i + " コマ目が順番どおり。");
            }

            Assert.AreEqual(1, AreaTransitionMoveFrames.ResolvedCount, "成功を数えている。");
            Assert.IsEmpty(AreaTransitionMoveFrames.LastReason, "理由は残らない。");
        }

        /// <summary>
        /// <b>コマ数を決め打ちにしない。</b> クリップを差し替えた日に数が変わっても、
        /// 出てくる数はクリップに従う。
        /// </summary>
        [Test]
        public void AClipWithADifferentLength_YieldsItsOwnFrameCount()
        {
            AnimationClip clip = NewMoveClip(4, out List<Sprite> sprites);
            var into = new List<Sprite>();

            Assert.IsTrue(AreaTransitionMoveFrames.TryExtract(clip, into, out float cycleSeconds));
            Assert.AreEqual(4, into.Count, "4 コマのクリップからは 4 コマ。");
            Assert.AreEqual(4f / FrameRate, cycleSeconds, 0.0001f, "周期も 4/12 秒。");
            Assert.AreSame(sprites[3], into[3], "最後のコマも落ちない。");
        }

        /// <summary>
        /// 取り出せないときは<b>渡さず、理由を残す</b>（§7.2 の「無い物は推測しない」）。
        /// 黙って 0 コマにすると、「滑って移動する」に戻ったことに誰も気付かない。
        /// </summary>
        [Test]
        public void AnEmptyClip_FailsWithAReason()
        {
            var clip = new AnimationClip { frameRate = FrameRate };
            _made.Add(clip);
            var into = new List<Sprite>();

            Assert.IsFalse(AreaTransitionMoveFrames.TryExtract(clip, into, out float cycleSeconds),
                "Sprite の曲線が無いクリップからは出ない。");
            Assert.AreEqual(0, into.Count, "渡さない。");
            Assert.AreEqual(0f, cycleSeconds, "周期も決まらない。");
            Assert.IsNotEmpty(AreaTransitionMoveFrames.LastReason, "理由を残す。");
            Assert.AreEqual(1, AreaTransitionMoveFrames.FallbackCount, "取りこぼしを数えている。");
        }

        /// <summary>クリップが無ければ失敗する（呼び出し側の配線漏れを黙って通さない）。</summary>
        [Test]
        public void NoClip_FailsWithAReason()
        {
            var into = new List<Sprite>();

            Assert.IsFalse(AreaTransitionMoveFrames.TryExtract(null, into, out _));
            Assert.IsNotEmpty(AreaTransitionMoveFrames.LastReason, "理由を残す。");
        }

        /// <summary>
        /// <b>取り出しは代理の絵を汚さない。</b> 焼き付け先は捨てる物体なので、
        /// 取り出しの前後で場面の物体は増えも減りもしない。
        /// </summary>
        [Test]
        public void Extracting_LeavesNoProbeBehind()
        {
            AnimationClip clip = NewMoveClip(FrameCount, out _);
            int before = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length;

            var into = new List<Sprite>();
            Assert.IsTrue(AreaTransitionMoveFrames.TryExtract(clip, into, out _));

            int after = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Length;
            Assert.AreEqual(before, after, "焼き付けに使った物体は残らない。");
        }

        /// <summary>
        /// 取り出したコマを代理へ渡すと、<b>表示専用の時計でコマが進む</b>（§7.2）。
        /// 取り出しと再生が繋がっていることを、ここで一度だけ通して見る。
        /// </summary>
        [Test]
        public void TheExtractedFrames_DriveTheProxyCycle()
        {
            AnimationClip clip = NewMoveClip(FrameCount, out List<Sprite> sprites);
            var into = new List<Sprite>();
            Assert.IsTrue(AreaTransitionMoveFrames.TryExtract(clip, into, out float cycleSeconds));

            var go = new GameObject("Proxy");
            _made.Add(go);
            var proxy = go.AddComponent<AreaTransitionDisplayProxy>();
            var sourceGo = new GameObject("Actor_Visual");
            _made.Add(sourceGo);
            var source = sourceGo.AddComponent<SpriteRenderer>();
            source.sprite = sprites[0];
            proxy.Capture(source);

            proxy.SetMoveFrames(into, cycleSeconds);
            Assert.AreSame(sprites[0], proxy.Renderer.sprite, "渡した直後は 1 コマ目。");

            proxy.TickDisplayClock((cycleSeconds / FrameCount) * 1.1f);
            Assert.AreEqual(1, proxy.FrameIndex, "1 コマ進む。");
            Assert.AreSame(sprites[1], proxy.Renderer.sprite, "2 コマ目の絵になる。");
        }

        // ---------------------------------------------------------------- 補助

        /// <summary>
        /// Sprite の曲線だけを持つクリップを組む（本番の <c>AN_Player_Move_*</c> と同じ形）。
        /// キーは 1/12 秒刻みで、最後のキーの時刻がクリップの長さになる。
        /// </summary>
        private AnimationClip NewMoveClip(int frames, out List<Sprite> sprites)
        {
            var clip = new AnimationClip { frameRate = FrameRate };
            _made.Add(clip);

            sprites = new List<Sprite>();
            var keys = new ObjectReferenceKeyframe[frames];
            for (int i = 0; i < frames; i++)
            {
                Sprite sprite = NewSprite("Frame" + i);
                sprites.Add(sprite);
                keys[i] = new ObjectReferenceKeyframe { time = i / FrameRate, value = sprite };
            }

            var binding = new EditorCurveBinding
            {
                path = string.Empty,
                type = typeof(SpriteRenderer),
                propertyName = "m_Sprite",
            };

            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            return clip;
        }

        private Sprite NewSprite(string name)
        {
            var texture = new Texture2D(4, 4);
            _made.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            _made.Add(sprite);
            return sprite;
        }
    }
}
