using Momotaro.Data.Combat;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// ジャスト回避・反撃強化の試遊表示（P6C 仕様 §7。仮 UI）。
    ///
    /// <list type="bullet">
    /// <item>成功したら「ジャスト回避！」を短く 1 回出す（音・点滅・小さな揺れは既存の <see cref="CombatFeedbackPresenter"/> が出す）。</item>
    /// <item>強化を持っている間は「反撃 ×倍率」と残時間のバーを出し、通常攻撃の段が始まって消費されたら消える。</item>
    /// <item>ガード不能の予告中は「通常ガード不可・ジャスガ可能」を記号つきで出す（色だけに頼らない）。</item>
    /// <item>操作説明（回避・ジャスト回避・ガード不能の扱い）を常に 1 行出す。</item>
    /// </list>
    ///
    /// <b>同じ Scene の主人公・予告にだけ結ぶ</b>（在留 Area が二つあっても他方の主人公を見ない）。描くのは自分の Scene が
    /// 活動中の Area のときだけ（非活動 Area には出さない）。値は読むだけで、報酬・強化をここで操作しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JustEvadeHudPresenter : MonoBehaviour
    {
        [Tooltip("「ジャスト回避！」を出す秒数（実時間）。")]
        [SerializeField] private float _flashSeconds = 0.8f;

        [Tooltip("同じ Scene の主人公・予告を探し直す間隔（実時間秒）。")]
        [SerializeField] private float _resolveInterval = 0.5f;

        private PlayerStateController _player;
        private EnemyUnblockableWarningPresenter _warning;
        private float _nextResolve;
        private int _seenSuccess = -1;
        private float _flashUntil;
        private GUIStyle _big;

        /// <summary>操作説明の 1 行。</summary>
        public const string ControlsHint =
            "Space／B：回避（出だしで敵の攻撃を避けるとジャスト回避 → 次の通常攻撃 1 段が強化）　赤い予告：" +
            EnemyAttackDefenseNotice.UnblockableJustGuardable;

        /// <summary>結んでいる主人公（同じ Scene のもの。テスト・診断用）。</summary>
        public PlayerStateController BoundPlayer => _player;

        /// <summary>この Scene が活動中の Area か（描画するか）。</summary>
        public bool IsDisplayActive =>
            !CurrentAreaProvider.HasScope || CurrentAreaProvider.ActiveSceneHandle == gameObject.scene.handle;

        /// <summary>成功表示を出した回数（活動中の Area で観測した成功の数。テスト用）。</summary>
        public int ShownSuccessCount { get; private set; }

        /// <summary>「ジャスト回避！」を表示中か。</summary>
        public bool IsShowingSuccess => IsDisplayActive && Time.unscaledTime < _flashUntil;

        /// <summary>保有中の表示（反撃 ×倍率・残時間）を出しているか。</summary>
        public bool IsShowingCounter => IsDisplayActive && _player != null && _player.HasJustEvadeCounter;

        /// <summary>ガード不能の注意書きを出しているか。</summary>
        public bool IsShowingUnblockableNotice => IsDisplayActive && _warning != null && _warning.ActiveWarningCount > 0;

        /// <summary>保有中の表示の文言（無ければ空）。</summary>
        public string CounterLabel => IsShowingCounter
            ? "反撃 ×" + _player.JustEvadeCounterMultiplier.ToString("0.0#") + "　残り " + _player.JustEvadeCounterRemaining.ToString("0.0") + " 秒"
            : string.Empty;

        private void Update()
        {
            Resolve();
            Observe();
        }

        /// <summary>同じ Scene の主人公と予告を探す（見つかるまで低頻度で）。テストは直接呼べる。</summary>
        public void Resolve()
        {
            if (_player != null && _warning != null)
            {
                return;
            }

            if (Time.unscaledTime < _nextResolve)
            {
                return;
            }

            _nextResolve = Time.unscaledTime + Mathf.Max(0.05f, _resolveInterval);
            UnityEngine.SceneManagement.Scene scene = gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i] == null)
                {
                    continue;
                }

                if (_player == null)
                {
                    PlayerStateController p = roots[i].GetComponentInChildren<PlayerStateController>(true);
                    if (p != null)
                    {
                        _player = p;
                        _seenSuccess = -1; // 結び直したら、その主人公の現在値から数え始める
                    }
                }

                if (_warning == null)
                {
                    _warning = roots[i].GetComponentInChildren<EnemyUnblockableWarningPresenter>(true);
                }
            }
        }

        private void Observe()
        {
            if (_player == null)
            {
                return;
            }

            int count = _player.JustEvadeSuccessCount;
            if (_seenSuccess < 0)
            {
                _seenSuccess = count;
                return;
            }

            if (count != _seenSuccess)
            {
                if (count > _seenSuccess && IsDisplayActive)
                {
                    ShownSuccessCount++;
                    _flashUntil = Time.unscaledTime + Mathf.Max(0.1f, _flashSeconds);
                }

                _seenSuccess = count;
            }
        }

        private void OnGUI()
        {
            if (!IsDisplayActive)
            {
                return;
            }

            float scale = Mathf.Max(1f, Screen.height / 720f);
            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            GUI.Label(new Rect(16f, 8f, width - 32f, 22f), ControlsHint);

            float y = 32f;
            if (IsShowingUnblockableNotice)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.45f);
                GUI.Label(new Rect(16f, y, 420f, 22f), "！" + EnemyAttackDefenseNotice.UnblockableJustGuardable);
                GUI.color = prev;
                y += 24f;
            }

            if (IsShowingCounter)
            {
                var box = new Rect(16f, y, 260f, 40f);
                GUI.Box(box, GUIContent.none);
                GUI.Label(new Rect(box.x + 8f, box.y + 2f, box.width - 16f, 20f), CounterLabel);
                float duration = Mathf.Max(0.01f, _player.JustEvadeCounterDuration);
                float t = Mathf.Clamp01(_player.JustEvadeCounterRemaining / duration);
                var bar = new Rect(box.x + 8f, box.y + 24f, box.width - 16f, 8f);
                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.8f, 0.3f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * t, bar.height), Texture2D.whiteTexture);
                GUI.color = prev;
                y += 44f;
            }

            if (IsShowingSuccess)
            {
                if (_big == null)
                {
                    _big = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
                }

                Color prev = GUI.color;
                GUI.color = new Color(1f, 0.95f, 0.4f);
                GUI.Label(new Rect(width * 0.5f - 120f, 120f, 240f, 40f), "ジャスト回避！", _big);
                GUI.color = prev;
            }

            GUI.matrix = previous;
        }
    }
}
