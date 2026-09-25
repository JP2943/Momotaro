using Momotaro.Core.Identification;
using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Session
{
    /// <summary>
    /// 開放出入口（P5-03b。仕様書 v1.1 §6.1 の 1 行目）。
    ///
    /// <b>「範囲内で出口方向へ 0.15 秒連続入力」で遷移を要求する。</b>
    /// 接触・押し出し・Trigger に立っているだけでは遷移しない。
    /// 押し出しで一瞬めり込んだだけで別エリアへ飛ばされるのを防ぐためで、
    /// 連続入力が途切れたら溜めは 0 へ戻す。
    ///
    /// <b>到着直後の跳ね返りを止める。</b> §6.1 末尾が言うのは「別 Scene の Trigger へ押しっぱなしを
    /// 持ち込まない」ことなので、止めるのは<b>入力の持ち越し</b>であって Trigger の占有ではない。
    /// 到着後は、出口方向の入力が<b>一度切れる</b>まで溜めを開始しない。
    /// 入口と出入口が離れている配置でも、重なっている配置でも同じように効く
    /// （占有で判定すると、到着位置が Trigger の外なら素通りしてしまう）。
    /// 再開入力を到着先の操作へ流用しない P18 と同じ考え方。
    ///
    /// 判定の時間は unscaled ではなく Gameplay 時計で進める（Pause 中に溜まらない）。
    ///
    /// <b>範囲の出入りは自分の Trigger で見る。</b> 以前は <see cref="SetPlayerInside"/> を
    /// 外から呼ぶ前提だったが、呼ぶ者が実機の Scene にどこにも居らず、
    /// <b>試遊では出入口が一度も反応しなかった</b>（テストは Gate を直接叩いていたので緑のままだった）。
    /// 判定を持つ本人が Trigger を受けるようにして、配線の抜けを <see cref="IsWired"/> で検査できるようにする。
    /// 明示呼び出しも引き続き受け付ける（テスト・特殊な配置のため）。
    ///
    /// <b>主人公本人の進入だけを見る</b>のは <see cref="Momotaro.Gameplay.Encounter.AreaEncounterTrigger"/> と同じ。
    /// 仲間・敵・Projectile が踏んでも範囲内にしない。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class AreaExitGate : MonoBehaviour
    {
        /// <summary>要求までに必要な連続入力の秒数（§6.1）。</summary>
        public const float RequiredHoldSeconds = 0.15f;

        [Tooltip("行き先のエリア。")]
        [SerializeField] private StableId _destinationAreaId;

        [Tooltip("行き先の入口。")]
        [SerializeField] private StableId _destinationEntryId;

        [Tooltip("出口の向き（この向きへ入力し続けると出る）。XZ の単位ベクトル。")]
        [SerializeField] private Vector3 _exitDirection = Vector3.right;

        [Tooltip("入力が出口方向と見なされる内積の下限。1 に近いほど厳しい。")]
        [SerializeField] private float _directionThreshold = 0.5f;

        [Tooltip("主人公の根（本人かどうかの判定に使う）。")]
        [SerializeField] private PlayerRoot _player;

        private bool _playerInside;
        private bool _requiresRelease;
        private float _held;

        /// <summary>行き先のエリア。</summary>
        public StableId DestinationAreaId => _destinationAreaId;

        /// <summary>行き先の入口。</summary>
        public StableId DestinationEntryId => _destinationEntryId;

        /// <summary>主人公が範囲内に居るか（診断・テスト用）。</summary>
        public bool PlayerInside => _playerInside;

        /// <summary>要求できる状態か。到着直後は入力が一度切れるまで false（診断・テスト用）。</summary>
        public bool IsArmed => !_requiresRelease;

        /// <summary>現在の連続入力の溜め（診断・テスト用）。</summary>
        public float HeldSeconds => _held;

        /// <summary>要求を出した回数（診断・テスト用）。</summary>
        public int RequestCount { get; private set; }

        /// <summary>主人公が配線されているか（Validator・テスト用）。配線が無いと Trigger を無視する。</summary>
        public bool IsWired => _player != null;

        /// <summary>主人公を配線する（Scene 構築・テストが呼ぶ）。</summary>
        public void BindPlayer(PlayerRoot player)
        {
            if (player != null)
            {
                _player = player;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayer(other))
            {
                SetPlayerInside(true);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (IsPlayer(other))
            {
                SetPlayerInside(false);
            }
        }

        /// <summary>主人公本人か（仲間・敵・Projectile は無視する）。</summary>
        private bool IsPlayer(Collider other)
        {
            if (other == null || _player == null)
            {
                return false;
            }

            var root = other.GetComponentInParent<PlayerRoot>();
            return root != null && root == _player;
        }

        /// <summary>配線する（Builder・テストが呼ぶ）。</summary>
        public void Configure(StableId areaId, StableId entryId, Vector3 exitDirection)
        {
            _destinationAreaId = areaId;
            _destinationEntryId = entryId;
            exitDirection.y = 0f;
            if (exitDirection.sqrMagnitude > 1e-6f)
            {
                _exitDirection = exitDirection.normalized;
            }
        }

        /// <summary>主人公が範囲へ入った／出たことを伝える（Trigger でも明示呼び出しでもよい）。</summary>
        public void SetPlayerInside(bool inside)
        {
            if (_playerInside == inside)
            {
                return;
            }

            _playerInside = inside;
            _held = 0f;
        }

        /// <summary>
        /// 到着直後に呼ぶ。<b>出口方向の入力が一度切れるまで要求を止める</b>（§6.1 末尾）。
        /// 押しっぱなしの入力を新しい押下と解釈しないための対策。
        /// </summary>
        public void DisarmOnArrival()
        {
            _requiresRelease = true;
            _held = 0f;
        }

        /// <summary>
        /// 時間を進める（Gameplay 時計。Pause 中は呼ばれない）。
        /// <paramref name="moveInput"/> は XZ の移動入力。
        /// </summary>
        /// <returns>この Tick で遷移を要求すべきになったら true（1 回だけ）。</returns>
        public bool Tick(float deltaTime, Vector3 moveInput)
        {
            if (GameplayClockProvider.IsFrozen)
            {
                return false;
            }

            moveInput.y = 0f;
            bool towardExit = moveInput.sqrMagnitude > 1e-6f
                && Vector3.Dot(moveInput.normalized, _exitDirection) >= _directionThreshold;

            // 到着時に持ち込んだ入力は、出口方向から一度外れるまで無効。
            // 判定は範囲の内外に関わらず行う（Trigger の外で離しても「切れた」と数える）。
            if (_requiresRelease)
            {
                if (!towardExit)
                {
                    _requiresRelease = false;
                }

                _held = 0f;
                return false;
            }

            if (!_playerInside || !towardExit || deltaTime <= 0f)
            {
                // 範囲外、方向違い、入力なし。溜めは切れる（連続入力でなければならない）。
                _held = 0f;
                return false;
            }

            _held += deltaTime;
            if (_held < RequiredHoldSeconds)
            {
                return false;
            }

            // 要求は 1 回だけ。次は入力が一度切れるまで出さない。
            _held = 0f;
            _requiresRelease = true;
            RequestCount++;
            return true;
        }
    }
}
