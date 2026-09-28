using System.Collections.Generic;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Presentation.Transition
{
    /// <summary>
    /// 遷移中の表示代理を<b>まとめて</b>立て、まとめて畳む（P5.5 仕様書 §7.2）。
    ///
    /// <b>二重表示も一瞬の欠落も作らない。</b> 代理を作るのと実 Renderer を隠すのは
    /// <b>同じフレーム</b>で行い、畳むときも同じフレームで戻す（§7.2）。
    /// 別のフレームに分けると、片方だけ見えている 1 枚が必ず出る。
    ///
    /// <b>犬丸の扱いは状態で分ける</b>（§7.2）。
    /// <list type="bullet">
    /// <item><description><b>Away</b>：代理も作らない（居ないものを運ばない）。</description></item>
    /// <item><description><b>Down／Stagger</b>：姿勢を保ったまま位置だけ運ぶ（コマ送りを止める）。
    ///   回復演出を勝手に再生しない。</description></item>
    /// <item><description>健常：追従の見た目でコマ送りしながら運ぶ。</description></item>
    /// </list>
    ///
    /// <b>見えている犬丸を主人公の足元へ瞬間移動させない</b>（§7.2）。
    /// 代理の出発位置は<b>その Actor がいま居る場所</b>で、画面外なら画面外から運ぶ。
    /// </summary>
    public sealed class AreaTransitionDisplayProxySet
    {
        private readonly List<AreaTransitionDisplayProxy> _proxies = new List<AreaTransitionDisplayProxy>();
        private readonly List<SpriteRenderer> _hidden = new List<SpriteRenderer>();

        /// <summary>
        /// 到着側の犬丸から隠した Renderer（工程 P55-07b）。
        ///
        /// <b>戻すかどうかは畳むときの状態で決める。</b> 隠した時点では初期化が終わっておらず、
        /// 退場中（Away）かどうかがまだ決まっていない——そこで判断すると、
        /// 退場中の犬丸の絵を<b>畳んだ瞬間に有効化してしまう</b>。
        /// </summary>
        private readonly List<SpriteRenderer> _hiddenCompanion = new List<SpriteRenderer>();

        /// <summary>隠した相手の犬丸（畳むときに退場中かを見る）。</summary>
        private CompanionActor _hiddenCompanionActor;
        private GameObject _root;

        /// <summary>立っている代理の数（診断・テスト用）。</summary>
        public int Count => _proxies.Count;

        /// <summary>主人公の代理（無ければ null）。</summary>
        public AreaTransitionDisplayProxy Player { get; private set; }

        /// <summary>犬丸の代理（Away なら null）。</summary>
        public AreaTransitionDisplayProxy Companion { get; private set; }

        /// <summary>隠した実 Renderer の数（診断・テスト用）。</summary>
        public int HiddenRendererCount => _hidden.Count + _hiddenCompanion.Count;

        /// <summary>退場中だったので絵を戻さなかった回数（診断・テスト用）。</summary>
        public int SkippedAwayRestoreCount { get; private set; }

        /// <summary>畳んだか。</summary>
        public bool IsReleased { get; private set; }

        /// <summary>
        /// <b>退場していたので犬丸の代理を作らなかった</b>（診断・テスト用。§7.2）。
        ///
        /// 「代理が無いこと」だけでは理由が分からない——絵が見えていなくても代理は作られない。
        /// 退場の判断が効いたことを名指しで見られるようにしておく。
        /// 実際、この窓が無いせいで「Away の判定を外す」注入が検知できなかった。
        /// </summary>
        public bool CompanionSkippedBecauseAway { get; private set; }

        /// <summary>
        /// 主人公と犬丸の代理を立て、<b>同じフレームで</b>実 Renderer を隠す。
        ///
        /// 代理は <c>DontDestroyOnLoad</c> の専用の根の下に置く。出発側の Scene が
        /// 撤去されても運び続けられる必要があるため——Scene の下に置くと、
        /// 旧 Scene が消えた瞬間に代理も消えて主人公が欠ける。
        /// </summary>
        public void Build(PlayerRoot player, CompanionActor companion)
        {
            _root = new GameObject("AreaTransitionDisplayProxies");
            if (Application.isPlaying)
            {
                Object.DontDestroyOnLoad(_root);
            }

            Player = BuildFor(player != null ? player.VisualRoot : null, "Proxy_Player", frozen: false);

            // Away は代理も作らない（§7.2）。<b>絵が見えているかどうかで決めない</b>——
            // 状態で決める。見た目に頼ると、退場中でも絵が残る構成で代理が立つ。
            if (companion != null && companion.IsAway)
            {
                CompanionSkippedBecauseAway = true;
            }
            else if (companion != null)
            {
                bool keepPosture = companion.State == CompanionState.Down
                    || companion.State == CompanionState.Stagger;
                Companion = BuildFor(companion.transform, "Proxy_Companion", keepPosture);
            }
        }

        /// <summary>
        /// 到着側の実 Actor の Renderer を隠す（§6.2 手順 6「両方の実 Actor の Renderer は隠し」）。
        ///
        /// <b>代理は立てない。</b> 到着 Actor はすでに入口へ置かれているので、運ぶ絵は出発側の 1 組でよい。
        /// 隠さないと、通路を渡る代理と入口で待っている到着 Actor が<b>同じ画面に二重で映る</b>。
        /// 戻すのは <see cref="Release"/> ——出発側と同じ一覧で預かるので、
        /// 畳むときに片方だけ戻し忘れることがない。
        /// </summary>
        public void HideArrivals(PlayerRoot player, CompanionActor companion)
        {
            HideAllUnder(player != null ? player.VisualRoot : null, _hidden);

            // <b>Away でも隠す</b>（工程 P55-07b）。
            //
            // 以前はここで Away を除いていたが、<b>隠す時点ではまだ初期化が終わっていない</b>——
            // 準備中から隠し始めるようになったので、退場中かどうかがまだ決まっていない。
            // 除いてしまうと、初期化の途中で一瞬だけ犬丸が映る。
            // 「退場中の絵を戻さない」は<b>畳むとき</b>に決める（<see cref="Release"/>）。
            if (companion != null)
            {
                _hiddenCompanionActor = companion;
                HideAllUnder(companion.transform, _hiddenCompanion);
            }
        }

        /// <summary>
        /// その Actor の<b>見えている絵をすべて</b>隠し、畳むときに戻せるよう預かる。
        ///
        /// 本体 1 枚だけを隠すのでは足りない（上記）。逆に、もともと無効だった Renderer は
        /// 預からない——預かると畳むときに<b>本来出ないはずの絵を有効化する</b>。
        /// </summary>
        private static void HideAllUnder(Transform actorVisualRoot, List<SpriteRenderer> into)
        {
            if (actorVisualRoot == null)
            {
                return;
            }

            foreach (SpriteRenderer candidate in actorVisualRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (candidate == null || !candidate.enabled)
                {
                    continue;
                }

                candidate.enabled = false;
                into.Add(candidate);
            }
        }

        /// <summary>
        /// 運ぶ区間を渡す（§7.2）。
        ///
        /// <b>終点は呼び出し側が「準備済みの到着位置」を測って渡す</b>（GPT 受入④）。
        /// 以前はここで「主人公の移動差分」を犬丸へ適用していたが、
        /// 到着実体は入口から進行方向と逆へ 1.2m に置かれるので、
        /// 出発時の相対位置が偶然一致していなければ<b>畳んだ瞬間に跳ぶ</b>。
        /// 出発位置は<b>いまの居場所</b>のまま（主人公の足元へ寄せ集めない）。
        /// </summary>
        public void SetRoute(
            Vector3 playerFrom, Vector3 playerTo, Vector3 companionFrom, Vector3 companionTo)
        {
            if (Player != null)
            {
                Player.SetRoute(playerFrom, playerTo);
            }

            if (Companion != null)
            {
                Companion.SetRoute(companionFrom, companionTo);
            }
        }

        /// <summary>カメラと同じ進行度を配る（§7.2）。</summary>
        public void SetProgress(float progress)
        {
            for (int i = 0; i < _proxies.Count; i++)
            {
                _proxies[i].SetProgress(progress);
            }
        }

        /// <summary>表示専用時計を配る（unscaled）。</summary>
        public void TickDisplayClock(float unscaledDeltaTime)
        {
            for (int i = 0; i < _proxies.Count; i++)
            {
                _proxies[i].TickDisplayClock(unscaledDeltaTime);
            }
        }

        /// <summary>
        /// 代理を畳み、<b>同じフレームで</b>隠した実 Renderer を戻す（§7.2）。
        ///
        /// <b>何度呼んでも安全にする。</b> 失敗経路（Rollback）と成功経路の両方から
        /// 呼ばれるので、片方が先に畳んでいても壊れてはいけない。
        /// </summary>
        public void Release()
        {
            for (int i = 0; i < _hidden.Count; i++)
            {
                if (_hidden[i] != null)
                {
                    _hidden[i].enabled = true;
                }
            }

            _hidden.Clear();

            // <b>退場中の犬丸の絵は戻さない</b>（§6.3）。判断は<b>いまの状態</b>で行う——
            // 隠した時点ではまだ初期化が終わっておらず、退場中かどうかが決まっていない。
            bool companionAway = _hiddenCompanionActor != null && _hiddenCompanionActor.IsAway;
            for (int i = 0; i < _hiddenCompanion.Count; i++)
            {
                if (_hiddenCompanion[i] == null)
                {
                    continue;
                }

                if (companionAway)
                {
                    SkippedAwayRestoreCount++;
                    continue;
                }

                _hiddenCompanion[i].enabled = true;
            }

            _hiddenCompanion.Clear();
            _hiddenCompanionActor = null;

            for (int i = 0; i < _proxies.Count; i++)
            {
                if (_proxies[i] != null)
                {
                    _proxies[i].Release();
                }
            }

            _proxies.Clear();
            Player = null;
            Companion = null;

            if (_root != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(_root);
                }
                else
                {
                    Object.DestroyImmediate(_root);
                }

                _root = null;
            }

            IsReleased = true;
        }

        private AreaTransitionDisplayProxy BuildFor(Transform actorVisualRoot, string name, bool frozen)
        {
            if (actorVisualRoot == null)
            {
                return null;
            }

            SpriteRenderer source = FindVisibleRenderer(actorVisualRoot);
            if (source == null)
            {
                return null;
            }

            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);

            AreaTransitionDisplayProxy proxy = go.AddComponent<AreaTransitionDisplayProxy>();
            proxy.Capture(source);
            proxy.Freeze(frozen);
            _proxies.Add(proxy);

            // <b>同じフレームで</b>実 Renderer を隠す（二重表示を作らない。§7.2）。
            //
            // <b>代理が写した 1 枚だけでは足りない。</b> Actor には本体以外の飾り
            // （犬丸の向き矢印、影、下敷き）が付いていて、本体だけ隠すと<b>飾りだけが残る</b>
            // ——代理が通路を渡る間、出発地点に矢印が浮いたままになる。
            // 実際に PlayMode で踏んだ（Inumaru/DirectionArrow が見えていた）。
            HideAllUnder(actorVisualRoot, _hidden);
            return proxy;
        }

        /// <summary>その Actor のいま見えている Sprite を 1 枚選ぶ（影や装飾は選ばない）。</summary>
        private static SpriteRenderer FindVisibleRenderer(Transform actorVisualRoot)
        {
            SpriteRenderer best = null;
            foreach (SpriteRenderer candidate in actorVisualRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (candidate == null || !candidate.enabled || candidate.sprite == null)
                {
                    continue;
                }

                // 同じ Actor に複数あるときは<b>手前に描かれている方</b>を本体とみなす
                // （影・下敷きは奥に置かれている）。
                if (best == null || candidate.sortingOrder > best.sortingOrder)
                {
                    best = candidate;
                }
            }

            return best;
        }
    }
}
