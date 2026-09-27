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
        private GameObject _root;

        /// <summary>立っている代理の数（診断・テスト用）。</summary>
        public int Count => _proxies.Count;

        /// <summary>主人公の代理（無ければ null）。</summary>
        public AreaTransitionDisplayProxy Player { get; private set; }

        /// <summary>犬丸の代理（Away なら null）。</summary>
        public AreaTransitionDisplayProxy Companion { get; private set; }

        /// <summary>隠した実 Renderer の数（診断・テスト用）。</summary>
        public int HiddenRendererCount => _hidden.Count;

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
        /// 運ぶ区間を渡す（§7.2）。犬丸は<b>自分の居場所から</b>運ぶので、
        /// 主人公と同じ差分だけ動かす——主人公の足元へ寄せ集めない。
        /// </summary>
        public void SetRoute(Vector3 playerFrom, Vector3 playerTo)
        {
            Vector3 delta = playerTo - playerFrom;

            if (Player != null)
            {
                Player.SetRoute(playerFrom, playerTo);
            }

            if (Companion != null)
            {
                Vector3 companionFrom = Companion.transform.position;
                Companion.SetRoute(companionFrom, companionFrom + delta);
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
            source.enabled = false;
            _hidden.Add(source);
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
