using Momotaro.Core.Identification;
using Momotaro.Gameplay.Companion;
using Momotaro.Gameplay.Player;
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
    public sealed class AreaActorTransferPort : MonoBehaviour
    {
        [Header("配置（到着時に入口へ置換する対象の根）")]
        [Tooltip("主人公の Prefab 根。Rigidbody を持つので transform だけ動かしても戻される。")]
        [SerializeField] private Transform _playerRoot;

        [Tooltip("仲間の Prefab 根。")]
        [SerializeField] private Transform _companionRoot;

        [Header("主人公")]
        [SerializeField] private PlayerVitalsHolder _playerVitals;
        [SerializeField] private PlayerHitReaction _playerHitReaction;

        [Header("仲間")]
        [SerializeField] private CompanionActor _companionActor;
        [SerializeField] private CompanionHitReceiver _companionVitals;
        [SerializeField] private CompanionCombatController _companionCombat;
        [SerializeField] private CompanionDefenseController _companionDefense;
        [SerializeField] private CompanionGuardianController _companionGuardian;
        [SerializeField] private CompanionStateArbiter _companionStates;

        /// <summary>直近の適用が成功したか（診断・テスト用）。</summary>
        public bool LastApplySucceeded { get; private set; }

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
            CompanionGuardianController companionGuardian, CompanionStateArbiter companionStates)
        {
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
            CompanionState companionState = hasCompanion ? _companionActor.State : CompanionState.Follow;

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

            if (snapshot.HasPlayer && _playerVitals != null
                && !_playerVitals.TryImportTransferSnapshot(snapshot.PlayerVitals))
            {
                return Fail("主人公の生存値を復元できませんでした（値域か Break 中）。");
            }

            if (snapshot.HasPlayer && _playerHitReaction != null
                && !_playerHitReaction.TryImportTransferSnapshot(snapshot.PlayerHitReaction))
            {
                return Fail("主人公の被弾後無敵を復元できませんでした。");
            }

            if (snapshot.HasCompanion)
            {
                if (_companionVitals != null
                    && !_companionVitals.Vitals.TryImportTransferSnapshot(snapshot.CompanionVitals))
                {
                    return Fail("仲間の生存値を復元できませんでした（HP と Down の矛盾か値域）。");
                }

                if (_companionCombat != null
                    && !_companionCombat.TryImportTransferSnapshot(snapshot.CompanionCombat))
                {
                    return Fail("仲間の攻撃 CD を復元できませんでした。");
                }

                if (_companionDefense != null
                    && !_companionDefense.TryImportTransferSnapshot(snapshot.CompanionDefense))
                {
                    return Fail("仲間の防御 CD を復元できませんでした。");
                }

                if (_companionGuardian != null
                    && !_companionGuardian.TryImportTransferSnapshot(snapshot.CompanionGuardian))
                {
                    return Fail("仲間の守護 CD を復元できませんでした。");
                }

                // 値のあとに配置状態（§4.6）。Arbiter を唯一の窓口にする。
                if (_companionStates != null && !_companionStates.TryRestoreState(snapshot.CompanionState))
                {
                    return Fail("仲間の配置状態 " + snapshot.CompanionState + " を復元できませんでした。");
                }
            }

            LastApplySucceeded = true;
            return true;
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
    }
}
