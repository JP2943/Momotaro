using System.Collections.Generic;
using Momotaro.Gameplay.Player;
using Momotaro.Presentation.Player;
using UnityEngine;

namespace Momotaro.Presentation.Transition
{
    /// <summary>
    /// 表示代理へ渡す <b>Move の既存 6 コマ</b>を実 Actor から取り出す（P5.5 §7.2。工程 P55-09b）。
    ///
    /// §7.2 は「Move の既存 6 コマ周期を表示専用の unscaled 時計で再生する」と定める。
    /// コマ送りの仕掛け（<see cref="AreaTransitionDisplayProxy.SetMoveFrames"/>）は前からあったが、
    /// <b>本番では誰も素材を渡していなかった</b>——代理は写した 1 枚を出し続け、
    /// 通路を渡る主人公が<b>滑って移動する</b>絵になっていた。
    ///
    /// <b>Animator を代理へ載せない。</b> §7.2 は「描画部品だけを持つ」「AnimationEvent や
    /// 攻撃／足音等のゲーム通知を発火しない」と定める。Animator を複製すると、
    /// 止めたはずの通知が代理から出る道ができてしまう。
    ///
    /// <b>コマは「引き出して」渡す。</b> 実 Actor の Animator が持っているクリップを
    /// <see cref="AnimationClip.SampleAnimation"/> で<b>捨てる物体へ</b>焼き付け、
    /// その <c>SpriteRenderer.sprite</c> を読み取る。
    /// <list type="bullet">
    /// <item><description>Editor 専用 API（<c>AnimationUtility</c>）を使わない——本番で動く必要がある。</description></item>
    /// <item><description>サンプリングは<b>代理を作るときの 6 回だけ</b>。毎フレーム焼くと、
    /// 通知の出ない保証も速さも危うくなる。</description></item>
    /// <item><description>焼き付け先は<b>捨てる物体</b>。代理そのものへ焼くと、
    /// 取り出しの途中の絵が 1 フレーム見えうる。</description></item>
    /// </list>
    ///
    /// <b>取り出せなければ渡さない。</b> 止まった絵のほうが、推測で作ったコマより嘘が小さい
    /// （<see cref="AreaTransitionDisplayProxy.SetMoveFrames"/> の既定の考え方と同じ）。
    /// 理由は <see cref="LastReason"/> に残す——「動かない」を黙って通さないため。
    /// </summary>
    public static class AreaTransitionMoveFrames
    {
        /// <summary>サンプリングの上限（暴走止め）。6 コマ想定に対して十分な余裕。</summary>
        public const int MaxFrames = 64;

        /// <summary>直近の取り出しがうまくいかなかった理由（診断・テスト用。成功なら空）。</summary>
        public static string LastReason { get; private set; } = string.Empty;

        /// <summary>取り出しに成功した回数（診断・テスト用）。</summary>
        public static int ResolvedCount { get; private set; }

        /// <summary>取り出せなかった回数（診断・テスト用）。</summary>
        public static int FallbackCount { get; private set; }

        /// <summary>テスト間で数を持ち越さないための掃除（テスト専用）。</summary>
        public static void ClearForTests()
        {
            LastReason = string.Empty;
            ResolvedCount = 0;
            FallbackCount = 0;
        }

        /// <summary>
        /// その Actor の<b>いまの向きの Move クリップ</b>からコマを取り出す。
        ///
        /// 向きとクリップ名の対応は <see cref="PlayerVisualNames"/> が正本である——
        /// ここで名前を組み立て直すと、命名規則が 2 か所に散る。
        /// </summary>
        /// <param name="actorRoot">Actor の根（Animator と <see cref="PlayerFacing"/> を下に持つ）。</param>
        /// <param name="into">取り出したコマ（呼び出し側が用意する。中身は上書きされる）。</param>
        /// <param name="cycleSeconds">1 周期の秒数（クリップの <c>frameRate</c> から決める）。</param>
        /// <returns>1 コマ以上取り出せたら true。</returns>
        public static bool TryResolvePlayerMoveFrames(
            Transform actorRoot, List<Sprite> into, out float cycleSeconds)
        {
            cycleSeconds = 0f;
            into?.Clear();

            if (actorRoot == null || into == null)
            {
                return Fail("Actor の根が無い。");
            }

            var animator = actorRoot.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                return Fail("Animator が無い（仮表示の Actor はコマを持たない）。");
            }

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (controller == null)
            {
                return Fail("Animator に Controller が差さっていない。");
            }

            var facing = actorRoot.GetComponentInChildren<PlayerFacing>(true);
            FacingDirection direction = facing != null ? facing.Current : FacingDirection.Down;
            string clipName = PlayerVisualNames.ClipName(PlayerState.Move, direction);

            AnimationClip clip = FindClip(controller, clipName);
            if (clip == null)
            {
                return Fail("Move のクリップが見つからない（" + clipName + "）。");
            }

            return TryExtract(clip, into, out cycleSeconds);
        }

        /// <summary>
        /// クリップからコマを取り出す（向きの解決を挟まない口。テスト・再利用用）。
        ///
        /// <b>コマ数はクリップに数えさせる。</b> 「6 コマ」は既存クリップの性質であって、
        /// こちらが決める数ではない——決め打ちにすると、クリップを差し替えた日に
        /// <b>最後のコマだけ落ちる</b>（長さと刻みから出る数は 6 だが、端の扱いで 5 になる）。
        /// </summary>
        public static bool TryExtract(AnimationClip clip, List<Sprite> into, out float cycleSeconds)
        {
            cycleSeconds = 0f;
            into?.Clear();

            if (clip == null || into == null)
            {
                return Fail("クリップが無い。");
            }

            float frameRate = clip.frameRate > 0f ? clip.frameRate : 12f;
            float step = 1f / frameRate;

            GameObject probe = null;
            try
            {
                probe = new GameObject("AreaTransitionMoveFrameProbe");
                probe.hideFlags = HideFlags.HideAndDontSave;
                var renderer = probe.AddComponent<SpriteRenderer>();

                // <b>焼き付け先には Animator が要る。</b> Mecanim のクリップを
                // <see cref="AnimationClip.SampleAnimation"/> で焼くには、
                // 相手が Animator を持っている必要がある（無いと何も書き込まれない）。
                //
                // <b>Controller は差さない。</b> 差さなければ Animator は自分では何も再生しないので、
                // AnimationEvent も通知も出ない（§7.2）。ここは<b>捨てる物体</b>なので、
                // 代理そのものは描画部品だけのまま保たれる。
                probe.AddComponent<Animator>();

                // <b>コマ数は長さと刻みから出す。</b>
                //
                // Unity の <c>AnimationClip.length</c> は<b>コマ境界まで含めた長さ</b>で、
                // 6 コマ・12fps なら 0.5 秒（最後のキーの時刻 0.41666 秒ではない）。
                // だから長さ × 刻み数がそのままコマ数になる。
                //
                // <b>「0 から長さまで刻む」ではいけない。</b> 端に 1 つ余計に当たり、
                // 最後のコマが<b>二重に</b>入る（実測：6 コマのクリップから 7 コマ出た）。
                int frames = Mathf.Clamp(Mathf.RoundToInt(clip.length * frameRate), 1, MaxFrames);
                for (int i = 0; i < frames; i++)
                {
                    renderer.sprite = null;
                    clip.SampleAnimation(probe, i * step);
                    if (renderer.sprite != null)
                    {
                        into.Add(renderer.sprite);
                    }
                }
            }
            finally
            {
                if (probe != null)
                {
                    Object.DestroyImmediate(probe);
                }
            }

            if (into.Count == 0)
            {
                return Fail("クリップから Sprite を取り出せなかった（" + clip.name + "）。");
            }

            cycleSeconds = into.Count / frameRate;
            LastReason = string.Empty;
            ResolvedCount++;
            return true;
        }

        private static AnimationClip FindClip(RuntimeAnimatorController controller, string clipName)
        {
            AnimationClip[] clips = controller.animationClips;
            if (clips == null)
            {
                return null;
            }

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].name == clipName)
                {
                    return clips[i];
                }
            }

            return null;
        }

        private static bool Fail(string reason)
        {
            LastReason = reason;
            FallbackCount++;
            return false;
        }
    }
}
