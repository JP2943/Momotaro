using System;
using System.Collections.Generic;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Combat;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 敵の攻撃「振り」に同期して攻撃 SE を鳴らす Presenter（Phase3.5 P3.5-08C・敵側）。プレイヤーと異なり敵は複数体同時に
    /// 存在するため、Scene 内の <see cref="EnemyAttackController"/>（＝<see cref="IAttackSwingSource"/>）を低頻度で探索し、
    /// 各個体の判定（Active）区間の立ち上がりで一度だけ鳴らす。敵タイプ鍵（<see cref="IEnemySlashVisual.SlashVfxKey"/>）と
    /// 攻撃分類（<see cref="IAttackSwingSource.SwingStage"/>：通常/強/ガード不能/飛び道具）で SE を引き当てる。
    ///
    /// 命中の有無に依存せず「空振りでも」鳴らす（＝振りの音）。ヒット結果 SE（ダメージ・ガード・JG）とは別系統で、専用の
    /// <see cref="CombatSePlayer"/>（＝敵用スロット表・音量）を持つ。敵 SE は主人公より大幅に音量を抑える運用（Scene 構築側で設定）。
    /// Gameplay ロジックには一切干渉しない（読み取りのみ）。SE 未割当でも無音・無例外で継続する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAttackSwingSePresenter : MonoBehaviour
    {
        /// <summary>敵タイプ鍵ごとの攻撃 SE 鍵（通常/強/ガード不能）。<paramref name="key"/> は SlashVfxKey と一致させる。</summary>
        [Serializable]
        public sealed class EnemySeEntry
        {
            [Tooltip("敵タイプ鍵（例：Small=近接骸骨／Medium=侍骸骨）。EnemyAttackController.SlashVfxKey と一致させる。")]
            public string key = "Small";
            [Tooltip("通常攻撃の SE 鍵。")]
            public string normalSeId;
            [Tooltip("強攻撃の SE 鍵（侍骸骨は通常と共通で可）。")]
            public string heavySeId;
            [Tooltip("ガード不能攻撃の SE 鍵。")]
            public string unblockableSeId;
        }

        [Tooltip("敵攻撃 SE の再生器（敵用スロット・音量。主人公より大幅に抑える）。")]
        [SerializeField] private CombatSePlayer _se;

        [Tooltip("敵タイプ別の攻撃 SE 鍵（鍵は EnemyAttackController.SlashVfxKey に一致）。")]
        [SerializeField] private EnemySeEntry[] _entries;

        [Tooltip("飛び道具（弓発射）の SE 鍵。敵タイプ非依存で共通に鳴らす。")]
        [SerializeField] private string _projectileSeId = "SE_Enemy_Bow";

        [Tooltip("Scene 内の敵攻撃元を再取得する間隔（秒）。毎フレーム FindObjects しない。")]
        [SerializeField] private float _rescanInterval = 1f;

        private readonly List<IAttackSwingSource> _sources = new List<IAttackSwingSource>();
        private readonly Dictionary<IAttackSwingSource, bool> _wasActive = new Dictionary<IAttackSwingSource, bool>();
        private readonly List<IAttackSwingSource> _scratch = new List<IAttackSwingSource>();
        private float _rescanTimer;

        /// <summary>敵攻撃 SE 再生器（Scene 構築・テストが設定）。</summary>
        public CombatSePlayer Se { get => _se; set => _se = value; }

        /// <summary>敵タイプ別 SE テーブル（Scene 構築・テストが設定）。</summary>
        public EnemySeEntry[] Entries { get => _entries; set => _entries = value; }

        /// <summary>飛び道具 SE 鍵（Scene 構築・テストが設定）。</summary>
        public string ProjectileSeId { get => _projectileSeId; set => _projectileSeId = value; }

        /// <summary>直近に鳴らした敵攻撃 SE 鍵（テスト・診断用。未発火なら null）。</summary>
        public string LastSeId { get; private set; }

        /// <summary>攻撃 SE を発火した回数（Active 立ち上がり×対象 SE 有り。テスト・診断用）。</summary>
        public int PlayCount { get; private set; }

        private void OnDisable()
        {
            _wasActive.Clear(); // 再有効化・Scene 再読込後に前回状態を持ち越さない（誤発火防止）。
        }

        private void Update()
        {
            _rescanTimer += Time.unscaledDeltaTime;
            if (_rescanTimer >= _rescanInterval)
            {
                _rescanTimer = 0f;
                Rescan();
            }

            Tick();
        }

        /// <summary>観測元を明示注入する（テスト・Scene 構築。読み取りのみ）。</summary>
        public void Bind(IEnumerable<IAttackSwingSource> sources)
        {
            _sources.Clear();
            if (sources != null)
            {
                _sources.AddRange(sources);
            }
        }

        /// <summary>Scene 内の敵攻撃元（<see cref="EnemyAttackController"/>）を取得し直す（動的生成・撃破に追従）。</summary>
        public void Rescan()
        {
            _sources.Clear();
            EnemyAttackController[] found = FindObjectsByType<EnemyAttackController>(FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                _sources.Add(found[i]);
            }
        }

        /// <summary>
        /// 1 フレーム進める（Update から、またはテストが決定的に呼ぶ）。各観測元の判定区間立ち上がりを検出し、対応する攻撃 SE を
        /// 1 回鳴らす。破棄済み（撃破）観測元は追跡を解除する。時間引数は不要（真偽の縁で判定するため）。
        /// </summary>
        public void Tick()
        {
            _scratch.Clear();
            _scratch.AddRange(_sources);

            for (int i = 0; i < _scratch.Count; i++)
            {
                IAttackSwingSource src = _scratch[i];

                // 撃破・破棄された敵（Unity fake-null）は追跡を解除する。
                if (src is UnityEngine.Object o && o == null)
                {
                    _wasActive.Remove(src);
                    _sources.Remove(src);
                    continue;
                }

                bool active = src.IsSwingHitboxActive;
                _wasActive.TryGetValue(src, out bool was);

                if (active && !was)
                {
                    string seId = SeIdFor(src);
                    if (!string.IsNullOrEmpty(seId))
                    {
                        LastSeId = seId;
                        PlayCount++;
                        _se?.Play(seId); // 未割当（_se・Clip 未設定）でも無音・無例外。
                    }
                }

                _wasActive[src] = active;
            }
        }

        /// <summary>攻撃分類（<see cref="IAttackSwingSource.SwingStage"/>）と敵タイプ鍵から SE 鍵を引き当てる。対象外は null。</summary>
        private string SeIdFor(IAttackSwingSource src)
        {
            switch (src.SwingStage)
            {
                case AttackSwing.EnemyProjectile:
                    return _projectileSeId; // 弓発射は敵タイプ非依存で共通。
                case AttackSwing.EnemyMeleeNormal:
                    return EntryFor(src)?.normalSeId;
                case AttackSwing.EnemyMeleeHeavy:
                    return EntryFor(src)?.heavySeId;
                case AttackSwing.EnemyMeleeUnblockable:
                    return EntryFor(src)?.unblockableSeId;
                default:
                    return null; // 突進・非攻撃などは SE 対象外。
            }
        }

        private EnemySeEntry EntryFor(IAttackSwingSource src)
        {
            if (_entries == null)
            {
                return null;
            }

            string key = (src as IEnemySlashVisual)?.SlashVfxKey;
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i] != null && _entries[i].key == key)
                {
                    return _entries[i];
                }
            }

            return null; // 未登録の敵タイプは無処理。
        }
    }
}
