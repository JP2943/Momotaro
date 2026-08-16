using System;
using Momotaro.Gameplay.Scenes;
using Momotaro.Gameplay.Vitals;

namespace Momotaro.Presentation.Hud
{
    /// <summary>
    /// 共同開発者向け試遊 HUD の表示値を束ねる ViewModel（Phase3.5 P3.5-04。仕様書 §6）。
    ///
    /// Player の HP／Stamina（<see cref="Vital"/> の型付き購読）、GuardBreak／Special（イベントの無い状態はポーリング）、
    /// 戦闘 Session の状態（<see cref="CombatSessionController.StateChanged"/> の型付き購読）、Wave を集約し、
    /// いずれかの表示値が実際に変化した時だけ <see cref="Changed"/> を発火する。View（Canvas）とは分離し、
    /// UnityEngine.UI 非依存の純粋クラスとして EditMode で決定的にテストできる。
    ///
    /// Player／Session は遅延生成され得るため <see cref="BindPlayer"/>／<see cref="BindSession"/> で後から注入し、
    /// 破棄時は Unbind で購読を外す。同一参照の再バインドや Scene 再読込後の再バインドで購読を重複させない
    /// （対称管理）。実際の勝敗・入力ロック・Retry 遷移は先回りせず、ここは表示値の集約のみを担う。
    /// </summary>
    public sealed class CombatHudViewModel : IDisposable
    {
        // ---- Player 供給元（HP/Stamina は Vital、Guard/Special はイベントが無いためポーリング用デリゲート） ----
        private Vital _health;
        private Vital _stamina;
        private Func<bool> _guardBroken;
        private Func<bool> _specialReady;
        private Func<bool> _specialCharging;
        private bool _hasPlayer;

        // ---- Session 供給元 ----
        private CombatSessionController _session;
        private bool _hasSession;

        private int _wave = 1;

        /// <summary>現在 HP。未 Bind 時は 0。</summary>
        public int HpCurrent { get; private set; }

        /// <summary>最大 HP。未 Bind 時は 0。</summary>
        public int HpMax { get; private set; }

        /// <summary>HP 割合（0〜1）。</summary>
        public float HpRatio { get; private set; }

        /// <summary>現在スタミナ。未 Bind 時は 0。</summary>
        public int StaminaCurrent { get; private set; }

        /// <summary>最大スタミナ。未 Bind 時は 0。</summary>
        public int StaminaMax { get; private set; }

        /// <summary>スタミナ割合（0〜1）。</summary>
        public float StaminaRatio { get; private set; }

        /// <summary>ガードブレイク（行動不能）中か。</summary>
        public bool GuardBroken { get; private set; }

        /// <summary>必殺技がフル充填（発動可能）か。</summary>
        public bool SpecialReady { get; private set; }

        /// <summary>必殺技チャージ中（未充填）か。</summary>
        public bool SpecialCharging { get; private set; }

        /// <summary>戦闘 Session の現在状態。未 Bind 時は <see cref="CombatSessionState.Preparing"/>。</summary>
        public CombatSessionState Phase { get; private set; } = CombatSessionState.Preparing;

        /// <summary>現在 Wave（1 始まり）。連続 Wave 進行（P3.5-07）が <see cref="SetWave"/> で更新する。</summary>
        public int Wave { get; private set; } = 1;

        /// <summary>Player が Bind 済みか。</summary>
        public bool HasPlayer => _hasPlayer;

        /// <summary>Session が Bind 済みか。</summary>
        public bool HasSession => _hasSession;

        /// <summary>いずれかの表示値が変化した瞬間のみ発火（View が購読して再描画する）。</summary>
        public event Action Changed;

        /// <summary>
        /// Player の供給元を注入する（遅延生成対応）。HP／Stamina は <see cref="Vital"/> を型付き購読し、
        /// GuardBreak／Special はイベントが無いためポーリング用デリゲートで受け取る。異なる参照での再 Bind は
        /// 旧購読を外してから張り替え、購読を重複させない。同一 Vital 参照ならデリゲートのみ更新する。
        /// </summary>
        public void BindPlayer(Vital health, Vital stamina, Func<bool> guardBroken,
            Func<bool> specialReady, Func<bool> specialCharging)
        {
            if (_hasPlayer && ReferenceEquals(_health, health) && ReferenceEquals(_stamina, stamina))
            {
                // 同一供給元：購読は張り替えず、ポーリング用デリゲートのみ差し替える（重複購読防止）。
                _guardBroken = guardBroken;
                _specialReady = specialReady;
                _specialCharging = specialCharging;
                return;
            }

            UnbindPlayer();

            _health = health;
            _stamina = stamina;
            _guardBroken = guardBroken;
            _specialReady = specialReady;
            _specialCharging = specialCharging;
            _hasPlayer = health != null || stamina != null;

            if (_health != null)
            {
                _health.Changed += OnVitalChanged;
            }

            if (_stamina != null)
            {
                _stamina.Changed += OnVitalChanged;
            }

            Recompute();
        }

        /// <summary>Player 供給元の購読を外す（破棄・Scene 離脱）。二重呼び出し安全。</summary>
        public void UnbindPlayer()
        {
            if (_health != null)
            {
                _health.Changed -= OnVitalChanged;
            }

            if (_stamina != null)
            {
                _stamina.Changed -= OnVitalChanged;
            }

            _health = null;
            _stamina = null;
            _guardBroken = null;
            _specialReady = null;
            _specialCharging = null;
            _hasPlayer = false;
            Recompute();
        }

        /// <summary>Session の供給元を注入し状態を型付き購読する（遅延生成対応・重複購読なし）。</summary>
        public void BindSession(CombatSessionController session)
        {
            if (ReferenceEquals(_session, session))
            {
                return;
            }

            UnbindSession();

            _session = session;
            _hasSession = session != null;
            if (_session != null)
            {
                _session.StateChanged += OnStateChanged;
            }

            Recompute();
        }

        /// <summary>Session の購読を外す（破棄・Scene 離脱）。二重呼び出し安全。</summary>
        public void UnbindSession()
        {
            if (_session != null)
            {
                _session.StateChanged -= OnStateChanged;
            }

            _session = null;
            _hasSession = false;
            Recompute();
        }

        /// <summary>Wave 番号を設定する（連続 Wave 進行 P3.5-07 が駆動。1 未満は 1 に丸め）。</summary>
        public void SetWave(int wave)
        {
            _wave = wave < 1 ? 1 : wave;
            Recompute();
        }

        /// <summary>
        /// イベントの無い値（GuardBreak／Special）を反映するためのポーリング更新。View が毎フレーム呼ぶ。
        /// 変化が無ければ <see cref="Changed"/> は発火しない。
        /// </summary>
        public void Tick()
        {
            Recompute();
        }

        private void OnVitalChanged(VitalChanged _)
        {
            Recompute();
        }

        private void OnStateChanged(CombatSessionState _)
        {
            Recompute();
        }

        private void Recompute()
        {
            int hpC = _health != null ? _health.Current : 0;
            int hpM = _health != null ? _health.Max : 0;
            float hpR = _health != null ? _health.Ratio : 0f;
            int stC = _stamina != null ? _stamina.Current : 0;
            int stM = _stamina != null ? _stamina.Max : 0;
            float stR = _stamina != null ? _stamina.Ratio : 0f;
            bool gb = _hasPlayer && _guardBroken != null && _guardBroken();
            bool sr = _hasPlayer && _specialReady != null && _specialReady();
            bool sc = _hasPlayer && _specialCharging != null && _specialCharging();
            CombatSessionState ph = _session != null ? _session.State : CombatSessionState.Preparing;
            int wv = _wave < 1 ? 1 : _wave;

            bool changed =
                hpC != HpCurrent || hpM != HpMax || !Approximately(hpR, HpRatio) ||
                stC != StaminaCurrent || stM != StaminaMax || !Approximately(stR, StaminaRatio) ||
                gb != GuardBroken || sr != SpecialReady || sc != SpecialCharging ||
                ph != Phase || wv != Wave;

            if (!changed)
            {
                return;
            }

            HpCurrent = hpC;
            HpMax = hpM;
            HpRatio = hpR;
            StaminaCurrent = stC;
            StaminaMax = stM;
            StaminaRatio = stR;
            GuardBroken = gb;
            SpecialReady = sr;
            SpecialCharging = sc;
            Phase = ph;
            Wave = wv;

            Changed?.Invoke();
        }

        private static bool Approximately(float a, float b)
        {
            float d = a - b;
            return (d < 0f ? -d : d) < 1e-4f;
        }

        /// <summary>全購読を外す（破棄）。</summary>
        public void Dispose()
        {
            UnbindPlayer();
            UnbindSession();
            Changed = null;
        }
    }
}
