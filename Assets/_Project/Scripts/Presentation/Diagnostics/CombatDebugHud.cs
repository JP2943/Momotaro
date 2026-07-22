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

        private readonly List<CombatDummy> _dummies = new List<CombatDummy>();
        private readonly Dictionary<int, string> _lastHit = new Dictionary<int, string>();
        private readonly StringBuilder _sb = new StringBuilder();
        private GUIStyle _style;
        private float _nextRefresh;
        private string _lastPlayerResult;

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
        }

        private void OnGUI()
        {
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
                    _sb.Append("  HP ").Append(_playerVitals.Vitals.Health.Current).Append('/').Append(_playerVitals.Vitals.Health.Max);
                    _sb.Append("  Stamina ").Append(_playerVitals.Vitals.Stamina.Current).Append('/').Append(_playerVitals.Vitals.Stamina.Max);
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

            foreach (CombatDummy d in _dummies)
            {
                if (d == null)
                {
                    continue;
                }

                _sb.Append(d.name).Append(": HP ").Append(d.CurrentHp).Append('/').Append(d.MaxHp);
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
