using System;
using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Defense;
using UnityEngine;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 戦闘試遊セッションの統括（Phase3.5 P3.5-03。仕様書 §5 / §2.2）。勝敗・Wave・Retry が依存する型付き状態
    /// （<see cref="CombatSessionMachine"/>）と、Player／Enemy 死亡の型付き購読、敵登録・生存数管理、Scene 再読込 Adapter を提供する。
    ///
    /// 本 Task の範囲は「基盤」：状態遷移・購読・生存数・再読込契約まで。Wave の内容・回復・残留 Cleanup（P3.5-07）、HUD 表示（P3.5-04）、
    /// 入力ロック・結果パネル（P3.5-08）は先回りしない。実際の Wave 進行は外部（P3.5-07）が本 Session の遷移 API と生存数を用いて駆動する。
    ///
    /// 購読は OnEnable／OnDisable で対称に管理し、Scene 再読込後に重複しない。敵登録の撃破購読も同様に対称管理する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatSessionController : MonoBehaviour, IPlayerDefeatListener, IEnemyDefeatListener
    {
        private readonly CombatSessionMachine _machine = new CombatSessionMachine();
        private readonly List<IEnemyDefeatSource> _enemies = new List<IEnemyDefeatSource>();
        private readonly HashSet<int> _registeredIds = new HashSet<int>();
        private readonly HashSet<int> _deadIds = new HashSet<int>();
        private int _alive;

        private PlayerDefeatChannel _playerDefeats;
        private ICombatSceneReloader _reloader;

        /// <summary>現在の Session 状態。</summary>
        public CombatSessionState State => _machine.Current;

        /// <summary>Session 状態が変化した瞬間のみ発火（HUD 等が購読。P3.5-04）。</summary>
        public event Action<CombatSessionState> StateChanged;

        /// <summary>登録済み敵の生存数が &gt;0 から 0 へ落ちた瞬間に一度だけ発火（Wave 進行判断の入力。P3.5-07 が購読）。</summary>
        public event Action AllEnemiesDefeated;

        /// <summary>現在の生存敵数。</summary>
        public int AliveEnemyCount => _alive;

        /// <summary>現在の登録敵数（死亡含む）。</summary>
        public int RegisteredEnemyCount => _registeredIds.Count;

        // ---- 配線（Scene 構築・テストが注入。P3.5-06 で Scene Builder が接続する） ----

        /// <summary>Player 死亡通知チャネルを購読対象として設定する（対称管理・重複購読なし）。</summary>
        public void BindPlayerDefeat(PlayerDefeatChannel channel)
        {
            if (_playerDefeats == channel)
            {
                return;
            }

            _playerDefeats?.RemoveListener(this);
            _playerDefeats = channel;
            if (isActiveAndEnabled)
            {
                _playerDefeats?.AddListener(this);
            }
        }

        /// <summary>Scene 再読込 Adapter を設定する（未設定なら再読込要求は状態のみ遷移し何もしない）。</summary>
        public void SetReloader(ICombatSceneReloader reloader) => _reloader = reloader;

        // ---- 状態遷移 API（外部の Wave/Retry 制御が呼ぶ。適用時のみ StateChanged 発火） ----

        /// <summary>Wave を開始する（Preparing/Intermission → Playing）。</summary>
        public bool StartWave() => Apply(_machine.StartWave());

        /// <summary>Wave 間休止へ入る（Playing → Intermission）。</summary>
        public bool ToIntermission() => Apply(_machine.ToIntermission());

        /// <summary>勝利へ遷移する（Playing → Victory）。重複は拒否。</summary>
        public bool ToVictory() => Apply(_machine.ToVictory());

        /// <summary>敗北へ遷移する（Playing/Intermission → Defeat）。重複は拒否。</summary>
        public bool ToDefeat() => Apply(_machine.ToDefeat());

        /// <summary>
        /// 再読込を要求する（Victory/Defeat → Reloading）。二重要求は状態機が拒否するため、再読込 Adapter は一度だけ呼ばれる。
        /// 開始できたら true。
        /// </summary>
        public bool RequestReload()
        {
            if (!Apply(_machine.ToReloading()))
            {
                return false; // Victory/Defeat 以外、または既に Reloading（二重要求）。
            }

            _reloader?.ReloadCurrent(); // 一回だけ。以後は Reloading のため ToReloading が false になり再呼び出しされない。
            return true;
        }

        private bool Apply(bool changed)
        {
            if (changed)
            {
                StateChanged?.Invoke(_machine.Current);
            }

            return changed;
        }

        // ---- 敵登録・生存数 ----

        /// <summary>敵を登録し撃破を購読する（重複登録は無視）。登録時点で撃破済みなら生存数へ数えない。</summary>
        public void RegisterEnemy(IEnemyDefeatSource enemy)
        {
            if (enemy == null || !_registeredIds.Add(enemy.DamageableId))
            {
                return;
            }

            _enemies.Add(enemy);
            enemy.Defeats?.AddListener(this);

            if (enemy.IsDefeated)
            {
                _deadIds.Add(enemy.DamageableId);
            }
            else
            {
                _alive++;
            }
        }

        /// <summary>敵の登録を解除し購読を外す（Wave 終了・退場）。生存中だった場合のみ生存数を減らす。</summary>
        public void UnregisterEnemy(IEnemyDefeatSource enemy)
        {
            if (enemy == null || !_registeredIds.Remove(enemy.DamageableId))
            {
                return;
            }

            _enemies.Remove(enemy);
            enemy.Defeats?.RemoveListener(this);

            if (!_deadIds.Remove(enemy.DamageableId) && _alive > 0)
            {
                _alive--;
            }
        }

        /// <summary>全敵の登録・購読・生存数を初期化する（Wave 遷移・Cleanup・Disable。二重呼び出し安全）。</summary>
        public void ClearEnemies()
        {
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i]?.Defeats?.RemoveListener(this);
            }

            _enemies.Clear();
            _registeredIds.Clear();
            _deadIds.Clear();
            _alive = 0;
        }

        // ---- 型付き購読の実装 ----

        /// <inheritdoc />
        public void OnPlayerDefeated(in PlayerDefeatedEvent defeated)
        {
            // Player 死亡で Defeat へ一度だけ遷移（状態機が重複を拒否）。
            ToDefeat();
        }

        /// <inheritdoc />
        public void OnEnemyDefeated(in EnemyDefeatedEvent defeated)
        {
            int id = defeated.EnemyId;
            if (!_registeredIds.Contains(id) || _deadIds.Contains(id))
            {
                return; // 未登録・重複通知は拒否。
            }

            _deadIds.Add(id);
            if (_alive > 0)
            {
                _alive--;
                if (_alive == 0)
                {
                    // 生存 >0 → 0 の瞬間に一度だけ通知（0 体の一時状態では発火しない＝誤 Victory を作らない）。
                    AllEnemiesDefeated?.Invoke();
                }
            }
        }

        // ---- ライフサイクル（対称購読） ----

        private void OnEnable()
        {
            _playerDefeats?.AddListener(this);
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i]?.Defeats?.AddListener(this);
            }
        }

        private void OnDisable()
        {
            _playerDefeats?.RemoveListener(this);
            for (int i = 0; i < _enemies.Count; i++)
            {
                _enemies[i]?.Defeats?.RemoveListener(this);
            }
        }
    }
}
