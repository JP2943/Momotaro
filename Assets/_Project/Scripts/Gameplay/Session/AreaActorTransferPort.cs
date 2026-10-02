using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
using Momotaro.Gameplay.Save;
using Momotaro.Gameplay.Transfer;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// Actor の値を採取・復元する窓口（P5-03b。仕様書 v1.1 §4.4〜§4.6）。
    /// エリアごとに 1 つ置き、その Scene の主人公・仲間を<b>明示参照</b>で持つ（Find* を使わない）。
    ///
    /// <b>採取の前に行動を終わらせるのがこの型の主な仕事。</b> §4.4 が「行動の中断により CD が開始される場合は
    /// 中断完了後の値を採取する」と定めており、順序を間違えると中断で生じた CD が丸ごと落ちる
    /// （`CompanionCombatController.CancelAttack` がその実例。§4.5 の台帳に明記してある）。
    /// だから <see cref="Capture"/> は「止める → 採る」を 1 か所にまとめ、呼び出し側が順序を間違えられないようにする。
    ///
    /// <b>復元は値と状態の 2 段。</b> 値の Import だけでは復元完了にしない（§4.6）。
    /// 生存値を入れたあと、配置状態を <see cref="CompanionStateArbiter.TryRestoreState"/> で反映する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaActorTransferPort : MonoBehaviour, ISaveActorSource, IRestTarget
    {
        [Header("配置（到着時に入口へ置換する対象の根）")]
        [Tooltip("主人公の Prefab 根。Rigidbody を持つので transform だけ動かしても戻される。")]
        [SerializeField] private Transform _playerRoot;

        [Tooltip("仲間の Prefab 根。")]
        [SerializeField] private Transform _companionRoot;

        [Header("主人公")]
        [SerializeField] private PlayerVitalsHolder _playerVitals;
        [SerializeField] private PlayerHitReaction _playerHitReaction;

        [Tooltip("主人公の行動状態。死亡再開の中立化に使う（未配線なら主人公の根から解決する）。")]
        [SerializeField] private PlayerStateController _playerState;

        [Header("仲間")]
        [SerializeField] private CompanionActor _companionActor;
        [SerializeField] private CompanionHitReceiver _companionVitals;
        [SerializeField] private CompanionCombatController _companionCombat;
        [SerializeField] private CompanionDefenseController _companionDefense;
        [SerializeField] private CompanionGuardianController _companionGuardian;
        [SerializeField] private CompanionStateArbiter _companionStates;

        /// <summary>直近の適用が成功したか（診断・テスト用）。</summary>
        public bool LastApplySucceeded { get; private set; }

        /// <summary>配置対象と Actor がそろっているか（Scene 検査・診断用）。</summary>
        public bool IsWired => _playerRoot != null && _companionRoot != null && _playerVitals != null;

        /// <summary>直近の適用の失敗理由（診断・テスト用。成功なら空）。</summary>
        public string LastApplyFailure { get; private set; } = string.Empty;

        /// <summary>配置対象の根を配線する（Builder が呼ぶ）。</summary>
        public void BindRoots(Transform playerRoot, Transform companionRoot)
        {
            _playerRoot = playerRoot;
            _companionRoot = companionRoot;
        }

        /// <summary>配線する（Builder・Area 初期化担当・テストが呼ぶ）。</summary>
        public void Bind(
            PlayerVitalsHolder playerVitals, PlayerHitReaction playerHitReaction,
            CompanionActor companionActor, CompanionHitReceiver companionVitals,
            CompanionCombatController companionCombat, CompanionDefenseController companionDefense,
            CompanionGuardianController companionGuardian, CompanionStateArbiter companionStates,
            PlayerStateController playerState = null)
        {
            if (playerState != null)
            {
                _playerState = playerState;
            }

            _playerVitals = playerVitals;
            _playerHitReaction = playerHitReaction;
            _companionActor = companionActor;
            _companionVitals = companionVitals;
            _companionCombat = companionCombat;
            _companionDefense = companionDefense;
            _companionGuardian = companionGuardian;
            _companionStates = companionStates;
        }

        /// <summary>
        /// 進行中の行動を<b>同期的に止めてから</b>値を採取する（§6.2 手順 4 → 5）。
        /// 止める順は「行動 → 所有権」。止め終わってから採るので、中断で生じた CD が Snapshot に乗る。
        /// </summary>
        public AreaTransferSnapshot Capture(StableId companionId, StableId originAreaId, StableId originEntryId)
        {
            // --- 4. 進行中の行動を止める ---
            if (_companionCombat != null)
            {
                _companionCombat.CancelAttack();
            }

            if (_companionDefense != null)
            {
                // 構えは Release 後、回避は中断後の CD を採る（§4.5）。
                _companionDefense.Guard?.Release();
                _companionDefense.Evade?.Interrupt();
            }

            // 所有権を解放する。<b>これ自体も攻撃を止める</b>：進行中の券が無効になると
            // ICompanionActionParticipant 経由で同じ呼び出しの中で打ち切られるため、上の CancelAttack は
            // 実質二重になっている（欠陥注入で確かめた：CancelAttack だけ外しても CD は正しく乗る）。
            // 残しているのは、Arbiter が未配線の構成でも採取順が守られるようにするため。
            // どちらか片方を消すと、その構成で中断由来の CD が落ちる。
            if (_companionStates != null)
            {
                _companionStates.ResetArbitration();
            }

            // --- 5. 中断完了後の値を採取する ---
            bool hasPlayer = _playerVitals != null;
            PlayerVitalsTransferSnapshot playerVitals = hasPlayer
                ? _playerVitals.ExportTransferSnapshot()
                : default;
            HitReactionTransferSnapshot playerHit = _playerHitReaction != null
                ? _playerHitReaction.ExportTransferSnapshot()
                : default;

            bool hasCompanion = _companionActor != null;
            CompanionVitalsTransferSnapshot companionVitals = _companionVitals != null
                ? _companionVitals.Vitals.ExportTransferSnapshot()
                : default;
            CompanionCombatTransferSnapshot companionCombat = _companionCombat != null
                ? _companionCombat.ExportTransferSnapshot()
                : default;
            CompanionDefenseTransferSnapshot companionDefense = _companionDefense != null
                ? _companionDefense.ExportTransferSnapshot()
                : default;
            CompanionGuardianTransferSnapshot companionGuardian = _companionGuardian != null
                ? _companionGuardian.ExportTransferSnapshot()
                : default;
            // 行動状態（Attack／Guard／Chase／Investigate 等）はそのまま持ち越せない。
            // 止めても状態名は変わらないので、生の State を運ぶと到着側の復元が拒否する
            // （GPT レビュー R1 で指摘された）。§4.6 の復元表に従って、生存値から配置状態を決める。
            CompanionState companionState = ResolveRestorableState(hasCompanion, companionVitals);

            return new AreaTransferSnapshot(
                hasPlayer, playerVitals, playerHit,
                hasCompanion, companionId, companionVitals, companionCombat, companionDefense,
                companionGuardian, companionState,
                originAreaId, originEntryId);
        }

        /// <summary>
        /// 到着側で値と状態を復元する（§4.5／§4.6）。<b>AreaReady より前</b>に呼ぶ。
        ///
        /// 1 つでも Import に失敗したら false を返す。部分適用したまま活動させないため、
        /// 呼び出し側は遷移の失敗として §6.3 へ戻す。
        /// </summary>
        public bool TryApply(in AreaTransferSnapshot snapshot)
        {
            LastApplyFailure = string.Empty;

            if (snapshot.IsEmpty)
            {
                LastApplySucceeded = true;
                return true; // 初回入場・直開き。運ぶ値が無いのは正常。
            }

            // 先に配線を確かめる。<b>参照が欠けていたら成功にしない。</b>
            // 以前は null を飛ばして成功扱いだったため、復元先を配線し忘れた Scene でも
            // 遷移が通り、値だけ静かに消えていた（GPT レビュー R1）。
            if (!TryValidateReferences(snapshot, out string missing))
            {
                return Fail("復元先が未配線です: " + missing);
            }

            if (snapshot.HasPlayer)
            {
                if (!_playerVitals.TryImportTransferSnapshot(snapshot.PlayerVitals))
                {
                    return Fail("主人公の生存値を復元できませんでした（値域か Break 中）。");
                }

                if (!_playerHitReaction.TryImportTransferSnapshot(snapshot.PlayerHitReaction))
                {
                    return Fail("主人公の被弾後無敵を復元できませんでした。");
                }
            }

            if (snapshot.HasCompanion)
            {
                if (!_companionVitals.Vitals.TryImportTransferSnapshot(snapshot.CompanionVitals))
                {
                    return Fail("仲間の生存値を復元できませんでした（HP と Down の矛盾か値域）。");
                }

                if (!_companionCombat.TryImportTransferSnapshot(snapshot.CompanionCombat))
                {
                    return Fail("仲間の攻撃 CD を復元できませんでした。");
                }

                if (!_companionDefense.TryImportTransferSnapshot(snapshot.CompanionDefense))
                {
                    return Fail("仲間の防御 CD を復元できませんでした。");
                }

                if (!_companionGuardian.TryImportTransferSnapshot(snapshot.CompanionGuardian))
                {
                    return Fail("仲間の守護 CD を復元できませんでした。");
                }

                // 値のあとに配置状態（§4.6）。Arbiter を唯一の窓口にする。
                if (!_companionStates.TryRestoreState(snapshot.CompanionState))
                {
                    return Fail("仲間の配置状態 " + snapshot.CompanionState + " を復元できませんでした。");
                }
            }

            LastApplySucceeded = true;
            return true;
        }

        /// <summary>Snapshot が運んでいる分の復元先がすべて配線されているか。</summary>
        private bool TryValidateReferences(in AreaTransferSnapshot snapshot, out string missing)
        {
            if (snapshot.HasPlayer)
            {
                if (_playerVitals == null)
                {
                    missing = "PlayerVitalsHolder";
                    return false;
                }

                if (_playerHitReaction == null)
                {
                    missing = "PlayerHitReaction";
                    return false;
                }
            }

            if (snapshot.HasCompanion)
            {
                if (_companionVitals == null)
                {
                    missing = "CompanionHitReceiver";
                    return false;
                }

                if (_companionCombat == null)
                {
                    missing = "CompanionCombatController";
                    return false;
                }

                if (_companionDefense == null)
                {
                    missing = "CompanionDefenseController";
                    return false;
                }

                if (_companionGuardian == null)
                {
                    missing = "CompanionGuardianController";
                    return false;
                }

                if (_companionStates == null)
                {
                    missing = "CompanionStateArbiter";
                    return false;
                }
            }

            missing = string.Empty;
            return true;
        }

        /// <summary>直近の入場ごとの中立化を行った回数（診断・テスト用）。</summary>
        public int AreaEntryResetCount { get; private set; }

        /// <summary>
        /// <b>エリアへ入場するたび</b>の中立化（P5.5 §6.2 手順 5。工程 P55-10b）。
        ///
        /// 進行中の行動と先行入力を捨てるだけで、<b>値には触らない</b>——
        /// HP・スタミナ・CD は運ばれてきた Snapshot が正本である。
        /// だから呼ぶ順は <b>中立化 → 配置 → <see cref="TryApply"/></b> で固定する。
        /// 逆にすると、ここで構えを解いたときに生じた CD が復元値を上書きする。
        ///
        /// <b>旧実装では要らなかった。</b> 入場ごとに Scene を作り直していたので Actor が新品になり、
        /// 持ち越しようがなかった。旧 Area を<b>保持して再利用する</b>と（§6.2 手順 11）
        /// 同じ Actor へ戻ってくるので、出て行ったときの攻撃モーション・構え・先行入力が残る。
        /// <see cref="RestoreForCampaignRespawn"/> の「Scene が作り直されることを当てにしない」と
        /// <b>同じ理由</b>で、ここにも 1 か所だけ置く。
        /// </summary>
        public void ResetForAreaEntry()
        {
            AreaEntryResetCount++;

            // 犬丸：進行中の行動 → 所有権（採取と同じ順）。
            if (_companionCombat != null)
            {
                _companionCombat.CancelAttack();
            }

            if (_companionDefense != null)
            {
                _companionDefense.Guard?.Release();
                _companionDefense.Evade?.Interrupt();
            }

            if (_companionStates != null)
            {
                _companionStates.ResetArbitration();
            }

            // 主人公：状態機械・攻撃・先行入力・向きのロック・移動抑制。値は触らない。
            ResolvePlayerState()?.ResetForAreaEntry();
        }

        /// <summary>
        /// 本編型死亡再開の全回復（P5-08。仕様書 v1.1 §9.1 手順 6）。
        /// 主人公と加入済み犬丸を全回復・CD 解除・短時間状態解除して、<b>出撃できる状態</b>へ戻す。
        ///
        /// <b>Scene が作り直されることを当てにしない。</b> 再開は A のロードを伴うので実際には
        /// Actor が新品になることが多いが、それに頼ると「同じ Area を作り直さない再開」を
        /// 足した瞬間に、死んだままの主人公が探索へ戻る。復帰の中身はここに 1 か所で持つ。
        ///
        /// 進行 State（徳・GrantOnce・調査済み・門・訪問・加入）には<b>触れない</b>（§4.1 の表）。
        /// </summary>
        public void RestoreForCampaignRespawn()
        {
            // --- 行動を止める（採取と同じ順：行動 → 所有権） ---
            if (_companionCombat != null)
            {
                _companionCombat.CancelAttack();
            }

            // 構え・回避は<b>中断だけでは足りない</b>（GPT レビュー R6 の指摘 2）。
            // Release／Interrupt は動作を止めるがクールダウンを残す。この API の契約は
            // 「Scene 再生成に依存せず全 CD を解除する」なので、初期化まで行う。
            // 通常の P5 では再開で Scene を作り直すため新品になり表面化しないが、
            // それは偶然であって、契約を満たしているからではない。
            if (_companionDefense != null)
            {
                _companionDefense.ResetDefense();
            }

            if (_companionStates != null)
            {
                _companionStates.ResetArbitration();
            }

            // 守護のクールダウンも解除する（同上）。庇い待ちのまま再開すると、
            // 再開直後の一戦だけ庇えない犬丸になる。
            if (_companionGuardian != null)
            {
                _companionGuardian.ResetGuardian();
            }

            // --- 主人公：全回復・死亡確定の解除・短時間状態の解除 ---
            if (_playerVitals != null)
            {
                _playerVitals.RestoreForCampaignRespawn();
            }

            if (_playerHitReaction != null)
            {
                _playerHitReaction.ResetHurt();
            }

            ResolvePlayerState()?.ResetForCampaignRespawn();

            // --- 犬丸：全回復（Down 解除・復帰待ち解除・ひるみ解除）と CD 解除 ---
            if (_companionVitals != null && _companionVitals.Vitals != null)
            {
                _companionVitals.Vitals.Reset();
            }

            _companionCombat?.TryImportTransferSnapshot(new CompanionCombatTransferSnapshot(0f));

            // 状態は Follow へ。Down のまま再開すると「加入済み犬丸が復帰する」に反する（§15 の E20）。
            if (_companionStates != null)
            {
                _companionStates.TryRestoreState(CompanionState.Follow);
            }
            else
            {
                _companionActor?.ResetState(CompanionState.Follow);
            }
        }

        // ---- 保存（P6A-02。仕様 §8）----

        /// <inheritdoc />
        public bool CanExportForSave => _playerVitals != null && !_playerVitals.IsDefeated;

        /// <summary>保存のための採取を行った回数（診断・テスト用）。</summary>
        public int SaveExportCount { get; private set; }

        /// <summary>
        /// 保存値を<b>非破壊で</b>採る（仕様 §8／受入 P6A 14）。<see cref="Capture"/> と違って行動を止めない——
        /// 攻撃中断・回復・付与・入力消費を起こさない。値は<b>中断用に投影</b>する（<c>P6_SaveInventory.md</c> §2）：
        /// Break・Hurt 硬直・ひるみは一時動作として 0、HP・スタミナ・各 CD・Down と復帰待ちはそのまま。
        /// </summary>
        public PartySaveValues ExportForSave()
        {
            SaveExportCount++;

            PlayerVitalsTransferSnapshot vitals = _playerVitals != null
                ? _playerVitals.ExportTransferSnapshot()
                : default;
            HitReactionTransferSnapshot hit = _playerHitReaction != null
                ? _playerHitReaction.ExportTransferSnapshot()
                : default;
            var player = new PlayerSaveValues(
                vitals.Health.Current,
                vitals.Stamina.Current,
                vitals.Stamina.RegenDelayRemaining,
                hit.InvincibleRemaining);

            bool hasCompanion = _companionActor != null && _companionVitals != null && _companionVitals.Vitals != null;
            if (!hasCompanion)
            {
                return new PartySaveValues(player, false, default);
            }

            CompanionVitalsTransferSnapshot cv = _companionVitals.Vitals.ExportTransferSnapshot();
            float attackCd = _companionCombat != null ? _companionCombat.ExportTransferSnapshot().CooldownRemaining : 0f;
            CompanionDefenseTransferSnapshot defense = _companionDefense != null
                ? _companionDefense.ExportTransferSnapshot()
                : default;
            float guardianCd = _companionGuardian != null
                ? _companionGuardian.ExportTransferSnapshot().CooldownRemaining
                : 0f;

            var companion = new CompanionSaveValues(
                CompanionIds.Inumaru, cv.Hp, cv.IsDown, cv.RecoveryRemaining, cv.PostHitInvincibleRemaining,
                attackCd, defense.Guard.CooldownRemaining, defense.Evade.CooldownRemaining, guardianCd);
            return new PartySaveValues(player, true, companion);
        }

        /// <summary>
        /// 保存値を適用する（Continue。仕様 §10 の手順 6）。<b>遷移の復元と同じ検証付きの窓口</b>（<see cref="TryApply"/>）を通す——
        /// 値域が不正なら部分適用せずに false。生存なら Follow、Down なら Down で置く（安全な追従姿勢。仕様 §8）。
        /// 回復・通知・報酬は起こさない。
        /// </summary>
        public bool TryApplySaveValues(in PartySaveValues party)
        {
            PlayerSaveValues p = party.Player;
            var playerVitals = new PlayerVitalsTransferSnapshot(
                new VitalTransferSnapshot(p.Hp),
                new StaminaTransferSnapshot(p.Stamina, p.StaminaRegenDelay, 0f));
            var playerHit = new HitReactionTransferSnapshot(0f, p.InvincibleRemaining);

            CompanionSaveValues c = party.Companion;
            var companionVitals = new CompanionVitalsTransferSnapshot(
                c.Hp, c.IsDown, c.RecoveryRemaining, c.InvincibleRemaining, new FlinchTransferSnapshot(0f, 0f, 0f, 0f));
            var snapshot = new AreaTransferSnapshot(
                true, playerVitals, playerHit,
                party.HasCompanion, c.CompanionId, companionVitals,
                new CompanionCombatTransferSnapshot(c.AttackCooldown),
                new CompanionDefenseTransferSnapshot(
                    new GuardAbilityTransferSnapshot(c.GuardCooldown), new EvadeAbilityTransferSnapshot(c.EvadeCooldown)),
                new CompanionGuardianTransferSnapshot(c.GuardianCooldown),
                party.HasCompanion && c.IsDown ? CompanionState.Down : CompanionState.Follow,
                default, default);
            return TryApply(snapshot);
        }

        /// <summary>
        /// 休息の全回復（P6A-03。仕様 §5）。<b>死亡再開と同じ中身</b>を使う——主人公の HP・スタミナ最大、
        /// 犬丸の HP 回復と Down からの復帰、ひるみ等の解消、攻撃・防御・守護の CD 解除。
        /// 場所は変えない。
        /// </summary>
        public void RestoreForRest() => RestoreForCampaignRespawn();

        /// <summary>成長による最大 HP の加算を主人公へ置き直す（P6A。何度呼んでも同じ結果）。</summary>
        public void ApplyMaxHpBonus(int bonus)
        {
            _playerVitals?.ApplyMaxHpBonus(bonus);
        }

        /// <summary>主人公の基礎最大 HP の倍率（campaign のテスト専用の調整。P6A）。加算より前に掛ける。</summary>
        public void SetPlayerMaxHpScale(float scale)
        {
            _playerVitals?.SetMaxHpScale(scale);
        }

        private PlayerFacing _playerFacing;

        private PlayerStateController ResolvePlayerState()
        {
            if (_playerState == null && _playerRoot != null)
            {
                _playerState = _playerRoot.GetComponentInChildren<PlayerStateController>(true);
            }

            return _playerState;
        }

        private bool Fail(string reason)
        {
            LastApplyFailure = reason;
            LastApplySucceeded = false;
            return false;
        }

        /// <summary>
        /// 到着位置へ置く（§4.4「位置は目的地入口と安全な隊列位置へ置換」）。
        ///
        /// <b>通常の Follow Warp とは別の配置処理</b>（§10.2 末尾）。遷移中は Gameplay 時計を止めているので
        /// <c>CompanionMotor.WarpTo</c> は拒否される。そこを通さず直接置くのが正しい。
        ///
        /// <b>Rigidbody を一緒に動かす。</b> transform だけ動かしても物理側の位置が古いままで、
        /// 次の FixedUpdate で引き戻される（実際に踏んだ）。速度も消して、到着直後に滑り出さないようにする。
        /// 高さは現在値を保つ（接地を崩さない。<c>WarpTo</c> と同じ考え方）。
        /// </summary>
        public void PlaceAt(Vector3 playerPosition, Vector3 companionPosition, Vector3 facing)
        {
            PlaceRoot(_playerRoot, playerPosition);
            PlaceRoot(_companionRoot, companionPosition);

            if (_companionActor != null)
            {
                _companionActor.SetFacing(facing);
            }

            // 主人公にも入口定義の向きを適用する（§4.4「向きは Data の入口定義が正本」）。
            // 以前は犬丸だけに適用しており、到着直後の主人公だけ前の Area の向きのままだった。
            // ConfirmFromInput はロック状態に関わらず明示的に確定する（到着の配置は権威なので、
            // 死亡時に固定した向きも上書きしてよい）。次のフレームからは通常どおり入力が勝つ。
            PlayerFacing playerFacing = ResolvePlayerFacing();
            if (playerFacing != null)
            {
                playerFacing.ConfirmFromInput(new Vector2(facing.x, facing.z));
            }
        }

        private PlayerFacing ResolvePlayerFacing()
        {
            if (_playerFacing == null && _playerRoot != null)
            {
                _playerFacing = _playerRoot.GetComponentInChildren<PlayerFacing>(true);
            }

            return _playerFacing;
        }

        private static void PlaceRoot(Transform root, Vector3 position)
        {
            if (root == null)
            {
                return;
            }

            Vector3 destination = new Vector3(position.x, root.position.y, position.z);
            root.position = destination;

            var body = root.GetComponentInChildren<Rigidbody>();
            if (body != null)
            {
                body.position = destination;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// 持ち越す配置状態を決める（§4.6 の復元表）。
        ///
        /// <list type="bullet">
        /// <item><description>Away → Away を維持（通常表示・戦闘参加を有効化しない）。</description></item>
        /// <item><description>非 Away で IsDown → Down。</description></item>
        /// <item><description>非 Away・非 Down でひるみ残りあり → Stagger。</description></item>
        /// <item><description>上記以外 → Follow（旧攻撃・旧防御・旧探索を再開しない）。</description></item>
        /// </list>
        ///
        /// 行動状態は<b>ここで落とす</b>のが正しい。到着時に行動の途中へ復元しないという §4.6 の規則そのもので、
        /// 生の State を運んで到着側で弾くと、遷移そのものが失敗してしまう。
        /// </summary>
        private CompanionState ResolveRestorableState(bool hasCompanion, in CompanionVitalsTransferSnapshot vitals)
        {
            if (!hasCompanion)
            {
                return CompanionState.Follow;
            }

            if (_companionActor != null && _companionActor.State == CompanionState.Away)
            {
                return CompanionState.Away;
            }

            if (vitals.IsDown)
            {
                return CompanionState.Down;
            }

            if (vitals.Flinch.FlinchRemaining > 0f)
            {
                return CompanionState.Stagger;
            }

            return CompanionState.Follow;
        }
    }
}
