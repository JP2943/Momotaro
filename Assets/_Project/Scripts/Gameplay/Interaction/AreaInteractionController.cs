using System.Collections.Generic;
using Momotaro.Core.Logging;
using Momotaro.Gameplay.Companion.Investigation;
using Momotaro.Gameplay.Modes;
using Momotaro.Gameplay.Session;
using UnityEngine;

namespace Momotaro.Gameplay.Interaction
{
    /// <summary>
    /// Interact の<b>単一選択窓口</b>（P5-04。仕様書 v1.1 §7.1）。
    ///
    /// 扉・レバー・犬丸調査を同じ押下から選ぶ。対象ごとに入力を読む方式は禁止されているので、
    /// 押下の消費は Infrastructure の仲介（<c>AreaInteractInput</c>）が 1 回だけ行い、
    /// ここは<b>入力デバイスを知らない</b>まま「いま何が選ばれるか」「それを実行する」だけを担う。
    ///
    /// <b>表示と実行を同じ選択にする</b>のがこの型の役目（§7.1 の 4）。
    /// <see cref="Peek"/> が返した対象と <see cref="TryInteract"/> が実行する対象は、
    /// どちらも同じ規則・同じ瞬間の状態から選び直すので食い違わない。
    /// 押下時に有効性を再検査するため、表示から押下までの間に対象が消えても古い対象は実行しない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AreaInteractionController : MonoBehaviour
    {
        [Tooltip("いま居るエリア（AreaReady・AreaId・Floor の供給元）。")]
        [SerializeField] private AreaContext _context;

        [Tooltip("主人公の基準点。距離・遮蔽はここから測る（子ではなく根を差すこと）。")]
        [SerializeField] private Transform _playerAnchor;

        [Tooltip("受付距離の既定値（§7.1 の InteractionRadius 初期値は 1.6）。")]
        [SerializeField] private float _interactionRadius = AreaInteractionSelector.DefaultInteractionRadius;

        [Tooltip("この区画の戦闘（§8.2 の Starting〜Resolving）。未配線なら「戦闘の無い区画」として扱う。")]
        [SerializeField] private MonoBehaviour _encounterSource;

        private readonly List<IAreaInteractable> _buffer = new List<IAreaInteractable>();
        private IObstacleProbe _probe;
        private IAreaEncounterState _encounter;
        private bool _encounterResolved;

        /// <summary>直近に選ばれた対象（表示用。選べていなければ null）。</summary>
        public IAreaInteractable LastTarget { get; private set; }

        /// <summary>直近に選べなかった理由（診断・表示用）。</summary>
        public AreaInteractionRejection LastRejection { get; private set; }

        /// <summary>実行した回数（診断・テスト用。断られた分も含む＝押下を使い切った回数）。</summary>
        public int InteractCount { get; private set; }

        /// <summary>対象が断った回数（診断・テスト用）。</summary>
        public int RefusedCount { get; private set; }

        /// <summary>受付距離の既定値。</summary>
        public float InteractionRadius
        {
            get => _interactionRadius;
            set => _interactionRadius = value;
        }

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _context != null && _playerAnchor != null;

        /// <summary>Scene 構築・テストからの注入（<c>Find*</c> を使わない）。</summary>
        public void Bind(AreaContext context, Transform playerAnchor)
        {
            if (context != null)
            {
                _context = context;
            }

            if (playerAnchor != null)
            {
                _playerAnchor = playerAnchor;
            }
        }

        /// <summary>
        /// この区画の戦闘を配線する（§8.3。null は無視）。
        /// 戦闘の無い区画では未配線のままでよく、そのときは Interact を閉じない。
        /// </summary>
        public void BindEncounter(IAreaEncounterState encounter)
        {
            if (encounter == null)
            {
                return;
            }

            _encounter = encounter;
            _encounterSource = encounter as MonoBehaviour;
            _encounterResolved = true;
        }

        /// <summary>配線された戦闘（Scene 検査・診断用）。</summary>
        public IAreaEncounterState Encounter
        {
            get
            {
                ResolveEncounter();
                return _encounter;
            }
        }

        /// <summary>
        /// 遮蔽判定を差し替える（テストは Fake、実機は壁レイヤーへの物理判定）。
        /// 調査と同じ <see cref="IObstacleProbe"/> を使う：壁の越え方を 2 通り持たないため。
        /// </summary>
        public void SetObstacleProbe(IObstacleProbe probe)
        {
            _probe = probe;
        }

        /// <summary>いま使う遮蔽判定（未設定なら壁レイヤーへの物理判定を作って覚える）。</summary>
        private IObstacleProbe Probe => _probe ?? (_probe = new PhysicsObstacleProbe());

        /// <summary>
        /// 依頼を出さずに「いま押したら何が選ばれるか」を見る（候補表示のため）。<b>副作用なし。</b>
        /// </summary>
        public bool Peek(out IAreaInteractable target, out AreaInteractionRejection reason)
        {
            bool found = Select(out target, out reason);
            LastTarget = target;
            LastRejection = reason;
            return found;
        }

        /// <summary>
        /// 押下 1 回ぶんを実行する（§7.1）。
        ///
        /// 対象が無ければ false を返し、<b>呼び出し側は押下を捨てる</b>。
        /// 対象が内部条件で断った場合は true を返す：押下は使い切られており、
        /// <b>同じ押下を次点の対象へ流さない</b>。
        /// </summary>
        public bool TryInteract(out AreaInteractionOutcome outcome)
        {
            outcome = default;

            // 押下時に選び直す＝有効性の再検査（§7.1 の 4）。表示のときの対象をそのまま使わない。
            if (!Select(out IAreaInteractable target, out AreaInteractionRejection reason))
            {
                LastTarget = null;
                LastRejection = reason;
                return false;
            }

            LastTarget = target;
            LastRejection = AreaInteractionRejection.None;
            InteractCount++;

            outcome = target.Interact();
            if (!outcome.Handled)
            {
                RefusedCount++;
            }

            return true;
        }

        private bool Select(out IAreaInteractable target, out AreaInteractionRejection reason)
        {
            target = null;

            if (!IsWired)
            {
                reason = AreaInteractionRejection.NotWired;
                return false;
            }

            if (!_context.IsAreaReady)
            {
                reason = AreaInteractionRejection.AreaNotReady;
                return false;
            }

            IGameModeService modes = GameModeProvider.Current;
            if (modes == null || modes.Current != GameMode.Exploration)
            {
                reason = AreaInteractionRejection.WrongMode;
                return false;
            }

            // 戦闘の開始が確定していれば、モードがまだ Exploration でも閉じる（§8.2 手順 3、§8.3）。
            ResolveEncounter();
            if (_encounter != null && _encounter.IsEncounterActive)
            {
                reason = AreaInteractionRejection.EncounterStarting;
                return false;
            }

            AreaInteractableRegistry.CopyTo(_buffer);
            return AreaInteractionSelector.TrySelect(
                _buffer,
                _playerAnchor.position,
                _context.AreaId,
                FloorId,
                _interactionRadius,
                Probe,
                out target,
                out reason);
        }

        private void ResolveEncounter()
        {
            if (_encounterResolved)
            {
                return;
            }

            _encounterResolved = true;
            _encounter = _encounterSource as IAreaEncounterState;
        }

        /// <summary>P5 の Floor は 0 固定（§3.1）。Floor を持つ日が来たら Context から引く。</summary>
        private int FloorId => AreaDefinitionFloor;

        private const int AreaDefinitionFloor = 0;

        private void OnDisable()
        {
            // 表示の残りを次の Scene へ持ち越さない。
            LastTarget = null;
            LastRejection = AreaInteractionRejection.None;
        }
    }
}
