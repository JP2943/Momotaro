using System.Collections.Generic;
using System.Text;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 検証用の簡易デバッグ表示（Phase2。P2-11 の完成 HUD とは別物の一時ツール）。Game ビューへ、
    /// 主人公の状態・攻撃段と、シーン内 <see cref="CombatDummy"/> の HP／被弾結果を OnGUI で表示する。
    /// 攻撃判定が出ているか（Attack 状態・段の遷移）と、ダミーへ命中して HP が減っているかを目視確認できる。
    /// 本番ビルドへは残さない前提。無効化は GameObject を非アクティブにするだけでよい。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatDebugHud : MonoBehaviour, IHitResultListener
    {
        [Tooltip("状態を表示する主人公（未指定ならシーンから自動取得）。")]
        [SerializeField] private PlayerStateController _player;

        [Tooltip("主人公の Vitals（HP/スタミナ。未指定ならシーンから自動取得）。")]
        [SerializeField] private PlayerVitalsHolder _playerVitals;

        [Tooltip("ダミー一覧を再取得する間隔（秒）。")]
        [SerializeField] private float _refreshInterval = 1f;

        [Tooltip("HUD を表示するか（Debug 切替）。ToggleVisible()/SetVisible() で切り替える（キー割当は外部の Debug 入力から接続）。")]
        [SerializeField] private bool _visible = true;

        private readonly List<CombatDummy> _dummies = new List<CombatDummy>();
        private readonly Dictionary<int, string> _lastHit = new Dictionary<int, string>();
        private readonly StringBuilder _sb = new StringBuilder();
        private GUIStyle _style;
        private float _nextRefresh;
        private string _lastPlayerResult;
        private string _lastFeedback;

        /// <summary>HUD 表示の ON/OFF を切り替える（Debug 切替。テスト・外部からも呼べる）。</summary>
        public void ToggleVisible() => _visible = !_visible;

        /// <summary>HUD 表示を設定する。</summary>
        public void SetVisible(bool visible) => _visible = visible;

        /// <summary>現在 HUD を表示中か。</summary>
        public bool IsVisible => _visible;

        private void OnEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + Mathf.Max(0.1f, _refreshInterval);
                Refresh();
            }
        }

        private void Refresh()
        {
            if (_player == null)
            {
                _player = FindFirstObjectByType<PlayerStateController>();
            }

            if (_playerVitals == null)
            {
                _playerVitals = FindFirstObjectByType<PlayerVitalsHolder>();
                _playerVitals?.Results.AddListener(this);
            }

            foreach (CombatDummy d in _dummies)
            {
                if (d != null)
                {
                    d.Results.RemoveListener(this);
                }
            }

            _dummies.Clear();
            _dummies.AddRange(FindObjectsByType<CombatDummy>(FindObjectsSortMode.None));
            foreach (CombatDummy d in _dummies)
            {
                d.Results.AddListener(this);
            }
        }

        /// <inheritdoc />
        public void OnHitResult(in HitResult result)
        {
            if (result.Target is CombatDummy dummy)
            {
                // AppliedDamage は実適用値（P2-05 で体幹・ひるみも実適用量が入る）。
                _lastHit[dummy.GetInstanceID()] =
                    $"{result.Kind} -HP{result.AppliedDamage.Hp:0} -体幹{result.AppliedDamage.Poise:0} +ひるみ{result.AppliedDamage.Flinch:0}";
            }
            else if (result.Target is PlayerVitalsHolder)
            {
                // P2-06：主人公の被弾/通常ガード結果（Guard は HP0）。
                _lastPlayerResult = $"{result.Kind} -HP{result.AppliedDamage.Hp:0}";
            }

            // 仮フィードバック（VFX/SE ID・HitStop 要求）を結果種別から解決して表示（P2-11。実演出は Dispatcher が配信）。
            CombatFeedbackCue cue = CombatFeedbackMap.Resolve(result.Kind);
            _lastFeedback = $"{result.Kind}  VFX:{(string.IsNullOrEmpty(cue.VfxId) ? "-" : cue.VfxId)}  SE:{(string.IsNullOrEmpty(cue.SeId) ? "-" : cue.SeId)}  HitStop:{cue.HitStopSeconds:0.00}s";
        }

        private void OnGUI()
        {
            if (!_visible)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = false };
                _style.normal.textColor = Color.white;
            }

            _sb.Clear();
            if (_player != null)
            {
                _sb.Append("Player: ").Append(_player.Current);
                if (_player.Current == PlayerState.Attack)
                {
                    _sb.Append("  (stage ").Append(_player.AttackStage).Append(')');
                }

                if (_player.IsGuarding)
                {
                    _sb.Append("  [GUARD]");
                }

                if (_player.IsStepping)
                {
                    _sb.Append(_player.IsInvincible ? "  [STEP:I-FRAME]" : "  [STEP]");
                }

                if (_player.JustGuardPhase != Momotaro.Gameplay.Combat.JustGuardPhase.Normal || _player.CanJustGuard)
                {
                    _sb.Append("  JG:").Append(_player.JustGuardPhase);
                    if (_player.CanJustGuard)
                    {
                        _sb.Append('*');
                    }
                }

                if (_playerVitals != null && _playerVitals.Vitals != null)
                {
                    // 表示は 0..Max へ Clamp（内部が一時的に範囲外でも UI が負値・超過を出さない）。
                    int hpMax = _playerVitals.Vitals.Health.Max;
                    int stMax = _playerVitals.Vitals.Stamina.Max;
                    _sb.Append("  HP ").Append(HudDisplay.Clamp(_playerVitals.Vitals.Health.Current, hpMax)).Append('/').Append(hpMax);
                    _sb.Append("  Stamina ").Append(HudDisplay.Clamp(_playerVitals.Vitals.Stamina.Current, stMax)).Append('/').Append(stMax);
                    if (_playerVitals.IsGuardBroken)
                    {
                        _sb.Append("  [BREAK]");
                    }
                }

                if (!string.IsNullOrEmpty(_lastPlayerResult))
                {
                    _sb.Append("  last: ").Append(_lastPlayerResult);
                }

                _sb.Append('\n');
            }

            // 直近の命中結果に対する仮フィードバック（VFX/SE ID・HitStop 要求）。
            if (!string.IsNullOrEmpty(_lastFeedback))
            {
                _sb.Append("FB: ").Append(_lastFeedback).Append('\n');
            }

            foreach (CombatDummy d in _dummies)
            {
                if (d == null)
                {
                    continue;
                }

                _sb.Append(d.name).Append(": HP ").Append(HudDisplay.Clamp(d.CurrentHp, d.MaxHp)).Append('/').Append(d.MaxHp);
                _sb.Append("  Poise ").Append(d.CurrentPoise.ToString("0")).Append('/').Append(d.MaxPoise.ToString("0"));
                _sb.Append("  Action: ").Append(d.ActionPhase);
                if (d.IsStunned)
                {
                    _sb.Append("  [STUN]");
                }

                if (d.IsFlinching)
                {
                    _sb.Append("  [FLINCH]");
                }

                if (d.IsDefeated)
                {
                    _sb.Append("  [DEFEATED]");
                }

                if (_lastHit.TryGetValue(d.GetInstanceID(), out string last))
                {
                    _sb.Append("  last: ").Append(last);
                }

                _sb.Append('\n');
            }

            GUI.Box(new Rect(8f, 8f, 460f, 24f + 22f * (_dummies.Count + 1)), GUIContent.none);
            GUI.Label(new Rect(16f, 12f, 448f, 22f * (_dummies.Count + 2)), _sb.ToString(), _style);
        }

        private void OnDisable()
        {
            foreach (CombatDummy d in _dummies)
            {
                if (d != null)
                {
                    d.Results.RemoveListener(this);
                }
            }

            _playerVitals?.Results.RemoveListener(this);
        }
    }
}
