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

        [Tooltip("この出入口の安定 ID。接続レコードを引くための鍵（P5.5 §3.1）。")]
        [SerializeField] private StableId _exitId;

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

        /// <summary>
        /// この出入口の安定 ID（P5.5 §3.1 の <c>ExitId</c>）。
        ///
        /// <b>接続レコードを引くための鍵</b>で、行き先そのものではない。行き先は接続レコード側に
        /// 正本があり（<c>ToAreaId</c>／<c>EntryId</c>）、ここの行き先指定は
        /// P5 の直接遷移が使う従来の経路である。
        /// <b>空でもよい</b>——P5 の Area は接続レコードを持たないので、
        /// 必須にすると従来の Scene が一斉に不合格になる。P5.5 の Scene 検査だけが必須にする。
        /// </summary>
        public StableId ExitId => _exitId;

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

        /// <summary>主人公の使用中で要求を控えたフレーム数（診断・テスト用。P6B 03）。</summary>
        public int SuppressedWhileBusyCount { get; private set; }

        /// <summary>範囲内を測り直した回数（診断・テスト用。工程 P55-15a）。</summary>
        public int ResyncCount { get; private set; }

        /// <summary>
        /// 測り直したときに<b>固まっていた true を落とした</b>回数（診断・テスト用）。
        /// 0 でなければ、その配置では残留が起きていた（試遊報告①）。
        /// </summary>
        public int StaleOccupancyClearedCount { get; private set; }

        /// <summary>この Tick で有効でなかったため何もしなかった回数（診断・テスト用）。</summary>
        public int SkippedWhileDisabledCount { get; private set; }

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

        /// <summary>この出入口の ID を割り当てる（Builder・テストが呼ぶ。P5.5 §3.1）。</summary>
        public void ConfigureExitId(StableId exitId)
        {
            _exitId = exitId;
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
        /// <b>「範囲内」を、いまの重なりから測り直す</b>（工程 P55-15a。試遊報告①）。
        ///
        /// <b>入場のたびに呼ぶ。</b> <c>OnTriggerEnter</c>／<c>OnTriggerExit</c> は
        /// この MonoBehaviour が有効なあいだしか届かないので、
        /// <see cref="AreaActivityGate"/> が非活動 Area を止めている間に主人公が範囲から出ても
        /// <b>退出が届かない</b>。しかも到着入口は出入口の Trigger の外にあるので
        /// （東西配置：Trigger −14.2〜−12.6／到着 −11.5）、
        /// 再入場でも入退出が一度も起きず、<b>去ったときの true が固まったまま残る</b>。
        ///
        /// 残ると、出口方向へ 0.15 秒入力するだけで<b>主人公がどこに居ても遷移が要求される</b>
        /// ——「戦闘区域を歩いていたら隣の Area へ飛ばされる」という形で出る。
        ///
        /// <b>記憶を消すだけにはしない。</b> 一律 false にすると、
        /// 到着位置が本当に Trigger の中にある配置で「出られない」が起きる。
        /// <see cref="AreaTriggerOccupancy"/> で<b>実際の重なりを測る</b>。
        /// </summary>
        public void ResyncOccupancy()
        {
            ResyncCount++;

            bool inside = AreaTriggerOccupancy.IsOverlappingPlayer(
                GetComponent<Collider>(), _player);

            if (_playerInside && !inside)
            {
                StaleOccupancyClearedCount++;
            }

            _playerInside = inside;
            _held = 0f;
        }

        /// <summary>
        /// 時間を進める（Gameplay 時計。Pause 中は呼ばれない）。
        /// <paramref name="moveInput"/> は XZ の移動入力。
        /// </summary>
        /// <returns>この Tick で遷移を要求すべきになったら true（1 回だけ）。</returns>
        public bool Tick(float deltaTime, Vector3 moveInput) => Tick(deltaTime, moveInput, requestAllowed: true);

        /// <summary>
        /// <paramref name="requestAllowed"/> が false の間は<b>溜めも要求もしない</b>（P6B 03。きびだんご使用中）。
        /// 要求して受付に断られると「一度離れるまで無効」になってしまうので、使用中はそもそも要求を出さない。
        /// 使用が終われば、外向きの入力を続けたまま通常どおり溜まって遷移できる（離れ直し不要）。
        /// </summary>
        public bool Tick(float deltaTime, Vector3 moveInput, bool requestAllowed)
        {
            if (GameplayClockProvider.IsFrozen)
            {
                return false;
            }

            // <b>止められている出入口は入力を数えない</b>（工程 P55-15a。試遊報告①）。
            //
            // <see cref="AreaActivityGate"/> は非活動 Area の出入口を <c>enabled = false</c> にする。
            // <c>Tick</c> は Unity の呼び出しではなく素のメソッドなので、
            // <b>止まっていても呼べてしまう</b>——先読みで載っているだけの Area の出入口が、
            // 共有の移動入力で遷移を要求できる状態だった。
            if (!enabled)
            {
                SkippedWhileDisabledCount++;
                _held = 0f;
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

            if (!requestAllowed)
            {
                SuppressedWhileBusyCount++;
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
