using System.Collections.Generic;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Defense;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 敵撃破時にスプライトをフェードアウトさせる Presentation 効果（Phase3.5 P3.5-05B）。Scene 内の <see cref="EnemyActor"/>
    /// （＝<see cref="IEnemyDefeatSource"/>）を低頻度で探索して各撃破チャネル（<see cref="EnemyDefeatChannel"/>）を購読し、撃破イベントの
    /// <c>EnemyId</c> に対応する敵の SpriteRenderer 群を透明へ向けて減衰させる。撃破後も敵は Down 状態で表示体を保持するため生存中に適用できる。
    ///
    /// 時間は <see cref="Tick"/> で外部から与える。破棄済み Renderer は無処理でフェードを終える（残留なし）。Disable・Scene 離脱では購読解除し、
    /// 進行中フェードは元色へ復元して半透明残留を残さない（他 Presenter の後始末方針に整合。撃破済みの最終見た目は Scene 再構築側の責務）。
    /// Gameplay ロジックには一切干渉しない（読み取りのみ）。素材未割当（Renderer 無し）でも例外なく継続する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyDefeatFadePresenter : MonoBehaviour, IEnemyDefeatListener
    {
        [Tooltip("撃破後、フェード開始までダウン体を表示し続ける保持時間（秒。Phase3.5 調整）。この間は敵が Down 姿勢のまま見える。")]
        [SerializeField] private float _downHoldSeconds = 1f;

        [Tooltip("撃破フェードの長さ（秒）。")]
        [SerializeField] private float _fadeSeconds = 0.6f;

        [Tooltip("Scene 内の敵を再取得する間隔（秒）。毎フレーム FindObjects しない。")]
        [SerializeField] private float _rescanInterval = 1f;

        private sealed class FadeState
        {
            public SpriteRenderer[] Renderers;
            public Color[] Orig;
            public float Elapsed;
        }

        private sealed class PendingFade
        {
            public SpriteRenderer[] Renderers;
            public float DelayRemaining;
        }

        private readonly List<IEnemyDefeatSource> _sources = new List<IEnemyDefeatSource>();
        private readonly Dictionary<int, SpriteRenderer[]> _renderersById = new Dictionary<int, SpriteRenderer[]>();
        private readonly List<FadeState> _fades = new List<FadeState>();
        private readonly List<PendingFade> _pending = new List<PendingFade>();
        private float _rescanTimer;

        /// <summary>進行中のフェード数（テスト・検証用）。</summary>
        public int ActiveFadeCount => _fades.Count;

        /// <summary>これまでに開始したフェード総数（テスト・検証用）。</summary>
        public int TotalFaded { get; private set; }

        /// <summary>フェード長（秒。Scene 構築 P3.5-06・テストが設定）。</summary>
        public float FadeSeconds { get => _fadeSeconds; set => _fadeSeconds = value; }

        /// <summary>撃破後フェード開始までのダウン保持時間（秒。Scene 構築・試遊調整・テストが設定）。</summary>
        public float DownHoldSeconds { get => _downHoldSeconds; set => _downHoldSeconds = value; }

        /// <summary>フェード開始待ちのダウン体数（テスト・検証用）。</summary>
        public int PendingCount => _pending.Count;

        /// <summary>観測元を明示注入する（テスト・Scene 構築。読み取りのみ）。</summary>
        public void Bind(IEnumerable<IEnemyDefeatSource> sources)
        {
            Unsubscribe();
            _sources.Clear();
            _renderersById.Clear();
            if (sources != null)
            {
                foreach (IEnemyDefeatSource s in sources)
                {
                    Register(s);
                }
            }
        }

        /// <summary>Scene 内の敵（<see cref="EnemyActor"/>）を取得し直し購読し直す（動的生成・撃破に追従）。</summary>
        public void Rescan()
        {
            Unsubscribe();
            _sources.Clear();
            _renderersById.Clear();
            EnemyActor[] found = FindObjectsByType<EnemyActor>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                Register(found[i]);
            }
        }

        private void Register(IEnemyDefeatSource s)
        {
            if (s == null)
            {
                return;
            }

            _sources.Add(s);
            s.Defeats.AddListener(this);

            if (s is Component comp && comp != null)
            {
                SpriteRenderer[] rs = comp.GetComponentsInChildren<SpriteRenderer>(true);
                if (rs != null && rs.Length > 0)
                {
                    _renderersById[s.DamageableId] = rs;
                }
            }
        }

        private void Unsubscribe()
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                if (_sources[i] != null)
                {
                    _sources[i].Defeats.RemoveListener(this);
                }
            }
        }

        /// <inheritdoc />
        public void OnEnemyDefeated(in EnemyDefeatedEvent defeated)
        {
            if (!_renderersById.TryGetValue(defeated.EnemyId, out SpriteRenderer[] rs))
            {
                return;
            }

            // ダウン保持（Phase3.5 調整）：撃破直後は Down 姿勢のまま一定時間表示し、その後フェードを開始する。
            // 保持 0 以下なら従来どおり即フェード。
            if (_downHoldSeconds > 0f)
            {
                _pending.Add(new PendingFade { Renderers = rs, DelayRemaining = _downHoldSeconds });
            }
            else
            {
                BeginFade(rs);
            }
        }

        /// <summary>指定 Renderer 群のフェードを開始する（テスト・直接呼び出し可）。空・null は無処理。</summary>
        public void BeginFade(SpriteRenderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            var orig = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                orig[i] = renderers[i] != null ? renderers[i].color : Color.white;
            }

            _fades.Add(new FadeState { Renderers = renderers, Orig = orig, Elapsed = 0f });
            TotalFaded++;
        }

        /// <summary>時間を進めてフェードを更新する。満了・全破棄で終える（残留なし）。</summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                deltaTime = 0f;
            }

            float dur = _fadeSeconds <= 0f ? 0.0001f : _fadeSeconds;

            for (int i = _fades.Count - 1; i >= 0; i--)
            {
                FadeState f = _fades[i];
                f.Elapsed += deltaTime;
                float t = f.Elapsed / dur;
                float alphaScale = t >= 1f ? 0f : 1f - t;

                bool anyAlive = false;
                for (int j = 0; j < f.Renderers.Length; j++)
                {
                    SpriteRenderer r = f.Renderers[j];
                    if (r == null)
                    {
                        continue; // 破棄済みは飛ばす。
                    }

                    anyAlive = true;
                    Color c = f.Orig[j];
                    c.a = f.Orig[j].a * alphaScale;
                    r.color = c;
                }

                if (t >= 1f || !anyAlive)
                {
                    _fades.RemoveAt(i);
                }
            }

            // ダウン保持の満了判定はフェード更新の後に行う（P3.5-10 修正）：保持満了で開始したフェードを同一 Tick で進めず、
            // 「保持満了 → 次 Tick からフェード開始」の順序を保証する（満了を跨ぐ Tick の余剰時間でフェードが即完了するのを防ぐ）。
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                PendingFade p = _pending[i];
                p.DelayRemaining -= deltaTime;
                if (p.DelayRemaining <= 0f)
                {
                    _pending.RemoveAt(i);
                    BeginFade(p.Renderers);
                }
            }
        }

        /// <summary>
        /// 全フェードを打ち切る（Disable・Scene 離脱・Retry）。途中まで減衰させた Renderer は元色へ復元し、半透明残留を残さない
        /// （他 Presenter の後始末方針に整合）。撃破済みの最終的な見た目（非表示化）は Scene 再構築側の責務とする。
        /// </summary>
        public void ClearAll()
        {
            for (int i = 0; i < _fades.Count; i++)
            {
                FadeState f = _fades[i];
                for (int j = 0; j < f.Renderers.Length; j++)
                {
                    if (f.Renderers[j] != null)
                    {
                        f.Renderers[j].color = f.Orig[j];
                    }
                }
            }

            _fades.Clear();
            _pending.Clear(); // フェード開始待ちも破棄（Disable・Scene 離脱・Retry。残留なし）。
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearAll();
        }

        private void Update()
        {
            _rescanTimer += Time.unscaledDeltaTime;
            if (_rescanTimer >= _rescanInterval)
            {
                _rescanTimer = 0f;
                Rescan();
            }

            Tick(Time.deltaTime);
        }
    }
}
