using Momotaro.Gameplay.Player;
using UnityEngine;

namespace Momotaro.Gameplay.Encounter
{
    /// <summary>
    /// 戦闘開始の Trigger（P5-07。仕様書 v1.1 §8.2 手順 1）。
    ///
    /// <b>主人公本人の進入だけを見る。</b> 仲間・敵・Projectile の進入は無視する（手順 1）。
    /// 判定を「Trigger に何か入った」にすると、先に走った犬丸が戦闘を始めてしまう。
    ///
    /// <b>条件はここに書かない。</b> 受付条件の正本は <see cref="AreaEncounterRunner"/> で、
    /// ここは「主人公が入った」を伝えるだけ。両方に条件を書くと必ず食い違う。
    ///
    /// 失敗したら<b>退出まで再要求しない</b>（§8.2「失敗後は Trigger 退出→再進入で再試行できる」）。
    /// 居座ったまま毎フレーム再試行すると、失敗のログと生成・破棄が延々と続く。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class AreaEncounterTrigger : MonoBehaviour
    {
        [Tooltip("開始を要求する先。")]
        [SerializeField] private AreaEncounterRunner _runner;

        [Tooltip("主人公の根（本人かどうかの判定に使う）。")]
        [SerializeField] private PlayerRoot _player;

        private bool _playerInside;

        /// <summary>要求した回数（診断・テスト用）。</summary>
        public int RequestCount { get; private set; }

        /// <summary>主人公が中に居るか（診断・テスト用）。</summary>
        public bool PlayerInside => _playerInside;

        /// <summary>配線されているか（Validator・テスト用）。</summary>
        public bool IsWired => _runner != null && _player != null;

        /// <summary>Scene 構築・テストからの注入。</summary>
        public void Bind(AreaEncounterRunner runner, PlayerRoot player)
        {
            if (runner != null)
            {
                _runner = runner;
            }

            if (player != null)
            {
                _player = player;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsWired || _playerInside || !IsPlayer(other))
            {
                return;
            }

            _playerInside = true;
            RequestCount++;
            _runner.TryStart();
        }

        private void OnTriggerExit(Collider other)
        {
            if (!IsWired || !IsPlayer(other))
            {
                return;
            }

            _playerInside = false;
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
    }
}
