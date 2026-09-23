using System;
using Momotaro.Core.Identification;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// 門を 1 つ開けるレバー（P5-04。仕様書 v1.1 §7.3）。
    ///
    /// <b>1 レバー＝1 FlagId を false → true にするだけ。</b> 反転も時間制限も複数条件もない。
    ///
    /// 順序は §7.3 のとおり <b>内部 Flag を先に確定 → 表示・Collider・経路更新 → 通知</b>。
    /// 通知を先に出すと、購読者が「開いたはず」の世界を見たときにまだ門が閉じている。
    /// また通知の中から同じレバーを引き直されても、Flag の確定は
    /// <see cref="AreaRuntimeState.TryOpen"/> が 1 回しか通さないので変更は 1 回で済む。
    ///
    /// 開通済みなら<b>状態変更も通知も再発火しない</b>。「開通済み」と表示するだけ（§7.3）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaFlagLever : MonoBehaviour, IAreaInteractable
    {
        [Tooltip("このレバーが開ける FlagId（門と同じ値を差すこと）。")]
        [SerializeField] private string _flagId = string.Empty;

        [Tooltip("このエリアの AreaId（AreaRoot の定義と揃える）。")]
        [SerializeField] private string _areaId = string.Empty;

        [Tooltip("開ける門。")]
        [SerializeField] private AreaFlagDoor _door;

        [Tooltip("距離・遮蔽の基準点（未配線なら自分の位置）。")]
        [SerializeField] private Transform _anchor;

        [Tooltip("固有の受付距離。0 以下なら窓口の既定値を使う。")]
        [SerializeField] private float _interactionRadius;

        private bool _committing;

        /// <summary>開通させた回数（診断・テスト用）。<b>1 を超えてはいけない</b>。</summary>
        public int OpenedCount { get; private set; }

        /// <summary>開通済みで断った回数（診断・テスト用）。</summary>
        public int AlreadyOpenCount { get; private set; }

        /// <summary>開通したときに 1 度だけ発火する（FlagId を渡す）。</summary>
        public event Action<StableId> Opened;

        /// <summary>この門の FlagId。</summary>
        public StableId FlagId => string.IsNullOrEmpty(_flagId) ? default : new StableId(_flagId);

        /// <summary>配線された門（Validator・テスト用）。</summary>
        public AreaFlagDoor Door => _door;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(StableId flagId, StableId areaId, AreaFlagDoor door, Transform anchor)
        {
            _flagId = flagId.Value ?? string.Empty;
            _areaId = areaId.Value ?? string.Empty;
            _door = door;
            _anchor = anchor;
        }

        /// <inheritdoc />
        public StableId InteractableId =>
            string.IsNullOrEmpty(_flagId) ? default : new StableId("lever_" + _flagId);

        /// <inheritdoc />
        public StableId AreaId => string.IsNullOrEmpty(_areaId) ? default : new StableId(_areaId);

        /// <inheritdoc />
        public int FloorId => 0; // P5 は Floor 0 のみ（§3.1）。

        /// <inheritdoc />
        public Vector3 InteractionAnchor => _anchor != null ? _anchor.position : transform.position;

        /// <inheritdoc />
        public float InteractionRadius => _interactionRadius;

        /// <inheritdoc />
        public bool IsAvailable => isActiveAndEnabled && _door != null && !FlagId.IsEmpty;

        /// <inheritdoc />
        public string Prompt => "レバーを引く";

        /// <inheritdoc />
        public AreaInteractionOutcome Interact()
        {
            if (!IsAvailable)
            {
                return AreaInteractionOutcome.Refused("レバーの配線がありません。");
            }

            // 通知の中から引き直されても、ここで弾く（Flag の確定も TryOpen が 1 回しか通さない）。
            if (_committing)
            {
                return AreaInteractionOutcome.Refused("処理中です。");
            }

            GameSessionState session = GameSessionProvider.Current;
            if (session == null || !session.TryGetArea(AreaId, out AreaRuntimeState area))
            {
                return AreaInteractionOutcome.Refused("エリアの記録がありません。");
            }

            if (area.IsOpen(FlagId))
            {
                // 開通済み。状態変更も通知も再発火しない（§7.3）。
                AlreadyOpenCount++;
                return AreaInteractionOutcome.Refused("開通済み");
            }

            _committing = true;
            try
            {
                // 1. 内部 Flag を先に確定する（§7.3）。ここが正本。
                if (!area.TryOpen(FlagId))
                {
                    AlreadyOpenCount++;
                    return AreaInteractionOutcome.Refused("開通済み");
                }

                // 2. 表示・Collider・経路更新。失敗はエラーとして表面化する（§7.3）。
                if (!_door.TryApplyOpened(out string error))
                {
                    GameLog.Error(LogCategory.Scene, "Lever committed the flag but the door did not open: " + error);
                    return AreaInteractionOutcome.Refused("門を開けられませんでした：" + error);
                }

                OpenedCount++;
            }
            finally
            {
                _committing = false;
            }

            // 3. 通知は最後（購読者が見る世界は、もう開いている）。
            Opened?.Invoke(FlagId);
            return AreaInteractionOutcome.Accepted("門が開いた");
        }

        private void OnEnable() => AreaInteractableRegistry.Register(this);

        private void OnDisable() => AreaInteractableRegistry.Unregister(this);
    }
}
