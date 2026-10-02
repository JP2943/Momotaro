using UnityEngine;
using UnityEngine.InputSystem;

namespace Momotaro.Infrastructure.World
{
    /// <summary>
    /// P6A の仮メニュー（IMGUI）の共通部品（試遊のフィードバック 2026-10-02）。
    ///
    /// <list type="bullet">
    /// <item><b>大きさ</b>：<see cref="UiScale"/> 倍で描く（<see cref="BeginScaled"/>／<see cref="EndScaled"/>）。文字も枠も一緒に拡大する。</item>
    /// <item><b>パッド</b>：十字キー・左スティックの上下で選び、決定（South：A／×）で選ぶ。戻る（East：B／○）で閉じる。
    /// キーボードの ↑↓・Enter・Esc も同じ。各メニューの既存のショートカット（数字・文字キー）はそのまま残す。</item>
    /// <item><b>開いた直後のフレームは入力を見ない</b>：メニューを開いた押下（Interact など）が、そのまま 1 つ目の選択肢を決定しないため。</item>
    /// </list>
    /// 入力は Update で 1 回だけ読む（OnGUI はフレーム内に複数回呼ばれるため、そこでは読まない）。
    /// </summary>
    public sealed class PadMenuNavigator
    {
        /// <summary>仮 UI の拡大率（試遊のフィードバック：1.5 倍程度）。</summary>
        public const float UiScale = 1.5f;

        /// <summary>スティックを「押した」と見なす傾き。</summary>
        private const float StickThreshold = 0.6f;

        private int _selected;
        private int _ignoreUntilFrame = -1;
        private float _previousStickY;
        private Matrix4x4 _savedMatrix;

        /// <summary>選択中の項目。</summary>
        public int Selected => _selected;

        /// <summary>メニューを開いた（または項目が入れ替わった）。先頭を選び、このフレームの入力は見ない。</summary>
        public void Reset(int selected = 0)
        {
            _selected = selected < 0 ? 0 : selected;
            _ignoreUntilFrame = Time.frameCount;
            _previousStickY = 0f;
        }

        /// <summary>
        /// 入力を読む（Update から 1 フレーム 1 回）。決定した項目の番号を返す（無ければ -1）。
        /// <paramref name="cancelled"/> は「戻る」が押されたか。<paramref name="enabled"/> が null なら全項目を選べる。
        /// </summary>
        public int Poll(int count, bool[] enabled, out bool cancelled)
        {
            cancelled = false;
            if (count <= 0)
            {
                return -1;
            }

            if (_selected >= count)
            {
                _selected = count - 1;
            }

            Gamepad pad = Gamepad.current;
            Keyboard keyboard = Keyboard.current;
            float stickY = pad != null ? pad.leftStick.ReadValue().y : 0f;
            bool padUp = false, padDown = false, padConfirm = false, padBack = false;

            // つながっているパッドをすべて見る（複数つないでいても、どれからでも操作できる）。
            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                Gamepad g = Gamepad.all[i];
                padUp |= g.dpad.up.wasPressedThisFrame;
                padDown |= g.dpad.down.wasPressedThisFrame;
                padConfirm |= g.buttonSouth.wasPressedThisFrame;
                padBack |= g.buttonEast.wasPressedThisFrame;
            }
            bool stickUp = stickY > StickThreshold && _previousStickY <= StickThreshold;
            bool stickDown = stickY < -StickThreshold && _previousStickY >= -StickThreshold;
            _previousStickY = stickY;

            if (Time.frameCount <= _ignoreUntilFrame)
            {
                return -1;
            }

            bool up = stickUp || padUp || (keyboard != null && keyboard.upArrowKey.wasPressedThisFrame);
            bool down = stickDown || padDown || (keyboard != null && keyboard.downArrowKey.wasPressedThisFrame);
            bool confirm = padConfirm
                || (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame));
            cancelled = padBack;

            if (up)
            {
                _selected = Step(_selected, -1, count, enabled);
            }
            else if (down)
            {
                _selected = Step(_selected, +1, count, enabled);
            }

            if (confirm && IsEnabled(_selected, enabled))
            {
                return _selected;
            }

            return -1;
        }

        /// <summary>マウスで押されたときに選択も合わせる。</summary>
        public void Select(int index) => _selected = index < 0 ? 0 : index;

        private static int Step(int from, int delta, int count, bool[] enabled)
        {
            int i = from;
            for (int n = 0; n < count; n++)
            {
                i = (i + delta + count) % count;
                if (IsEnabled(i, enabled))
                {
                    return i;
                }
            }

            return from;
        }

        private static bool IsEnabled(int index, bool[] enabled) =>
            enabled == null || (index >= 0 && index < enabled.Length && enabled[index]);

        // ---------------------------------------------------------------- 描画

        /// <summary>拡大した座標系で描き始める。以後の Rect は <see cref="VirtualWidth"/>×<see cref="VirtualHeight"/> の画面として扱う。</summary>
        public void BeginScaled()
        {
            _savedMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale, UiScale, 1f));
        }

        /// <summary>拡大を戻す。</summary>
        public void EndScaled()
        {
            GUI.matrix = _savedMatrix;
        }

        /// <summary>拡大後の画面の幅。</summary>
        public static float VirtualWidth => Screen.width / UiScale;

        /// <summary>拡大後の画面の高さ。</summary>
        public static float VirtualHeight => Screen.height / UiScale;

        /// <summary>
        /// 選択肢を 1 つ描く（選択中は印を付けて色を変える）。マウスで押されたら true。
        /// </summary>
        public bool DrawItem(int index, string label, bool enabled = true, float height = 30f)
        {
            bool selected = index == _selected;
            Color previous = GUI.backgroundColor;
            if (selected)
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0.35f, 1f);
            }

            bool wasEnabled = GUI.enabled;
            GUI.enabled = wasEnabled && enabled;
            bool clicked = GUILayout.Button((selected ? "▶ " : "　") + label, GUILayout.Height(height));
            GUI.enabled = wasEnabled;
            GUI.backgroundColor = previous;
            if (clicked)
            {
                Select(index);
            }

            return clicked;
        }

        /// <summary>
        /// 仮 UI に出る文字（各メニュー・保存表示・案内の文字と、ひらがな・カタカナ・英数字）。<b>初回描画の停止を先に済ませる</b>ために使う。
        /// </summary>
        /// <remarks>
        /// 実ビルドの計測で、起動して最初にお地蔵様のメニューを開いたフレームが 300ms ほど止まった。保存・GC・入力の切り替えを外しても止まり、
        /// 同じ文字を IMGUI で先に描くとそちらへ停止が移った（<c>P6A_性能測定記録.md</c> §6）。日本語の文字を初めて描くときの準備が原因なので、
        /// 操作の無いタイトルで先に描いておく（<see cref="DrawPrewarm"/>）。
        /// </remarks>
        public const string PrewarmCharacters = "×…↑↓▶○　、。ー・（）／：？一世中主了人代休位作保倒側備元充先全公内再冒初別動十取合回在地場壊太失始字存定居帰常度後得復徳息態成戦戻所手操敗敵断方旅時最期桃構様残決混済準点片状理用由着移立築終置蔵行補解試読起退進遊違選避郎長閉開間闘険食駐ぁあぃいぅうぇえぉおかがきぎくぐけげこごさざしじすずせぜそぞただちぢっつづてでとどなにぬねのはばぱひびぴふぶぷへべぺほぼぽまみむめもゃやゅゆょよらりるれろゎわゐゑをんゔゕゖァアィイゥウェエォオカガキギクグケゲコゴサザシジスズセゼソゾタダチヂッツヅテデトドナニヌネノハバパヒビピフブプヘベペホボポマミムメモャヤュユョヨラリルレロヮワヰヱヲンヴヵヶヷヸヹヺ!\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

        /// <summary>
        /// <see cref="PrewarmCharacters"/> を<b>見えない色で</b>描く（タイトルの OnGUI から。拡大した座標系の中で呼ぶ）。
        /// ラベル・ボタン・枠の 3 つの様式で描く（様式ごとに文字の大きさが違うことがある）。
        /// </summary>
        public static void DrawPrewarm()
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0f);
            var rect = new Rect(0f, 0f, 4000f, 40f);
            GUI.Label(rect, PrewarmCharacters);
            GUI.Button(rect, PrewarmCharacters);
            GUI.Box(rect, PrewarmCharacters);
            GUI.color = previous;
        }

        /// <summary>操作の案内（パッドとキーボード）。</summary>
        public const string Hint = "↑↓／十字キー：選ぶ　決定：A（×）／Enter　戻る：B（○）／Esc";
    }
}
