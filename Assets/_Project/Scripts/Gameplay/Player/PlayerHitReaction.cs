using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の被弾リアクション（Hurt 硬直・被弾後無敵）を保持する Gameplay コンポーネント（Phase3.5 P3.5-01。仕様書 §2.2）。
    /// タイマの正本は純粋クラス <see cref="HitReactionState"/> が持ち、本コンポーネントは Serialized な時間設定と Update 駆動、
    /// そして被弾側契約 <see cref="IPlayerHurtReaction"/> の公開のみを担う（既存 Controller へ過剰集約しない）。
    ///
    /// 接続：Player Prefab（PF_Player_Momotaro）のルートへ 1 つ付ける。<see cref="PlayerVitalsHolder"/> が実 HP ダメージ時に
    /// <see cref="BeginHurt"/> を呼び、被弾後無敵中の命中を無効化する。<see cref="PlayerStateController"/> が <see cref="IsHurt"/> を
    /// 読み、Hurt を最優先状態として全行動を中立化する。いずれも <c>GetComponentInParent</c> で解決するためルート配置で足りる。
    ///
    /// 時間は Game Time（<see cref="Time.deltaTime"/>）で進めるため、Pause（timeScale 0）で自然に停止する（仕様書 §2.3）。
    /// P3.5-09 で調整する場合は本コンポーネントの Serialized 値、または Data 化した設定を差し替える。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHitReaction : MonoBehaviour, IPlayerHurtReaction
    {
        [Tooltip("被弾硬直（強制行動不能）の秒数。仕様書 Table3 の初期値 0.30。")]
        [SerializeField] private float _hurtSeconds = 0.30f;

        [Tooltip("被弾後無敵の秒数（Hurt 開始から）。仕様書 Table3 の初期値 0.50。混成戦の永久拘束を防ぐ。")]
        [SerializeField] private float _postHitInvincibleSeconds = 0.50f;

        private HitReactionState _state;

        private HitReactionState State
        {
            get
            {
                if (_state == null)
                {
                    _state = new HitReactionState(_hurtSeconds, _postHitInvincibleSeconds);
                }

                return _state;
            }
        }

        /// <inheritdoc />
        public bool IsHurt => State.IsHurt;

        /// <inheritdoc />
        public bool IsPostHitInvincible => State.IsInvincible;

        /// <summary>Hurt 硬直の残り秒（検証・HUD 用）。</summary>
        public float HurtRemaining => State.HurtRemaining;

        /// <summary>硬直の設定秒（Runtime 確認・デバッグ・Prefab 接続テスト用）。</summary>
        public float HurtSeconds => State.HurtSeconds;

        /// <summary>被弾後無敵の設定秒（Runtime 確認・デバッグ・Prefab 接続テスト用）。</summary>
        public float PostHitInvincibleSeconds => State.InvincibleSeconds;

        /// <inheritdoc />
        public void BeginHurt() => State.Begin();

        /// <summary>時間を進める（テストから直接駆動できるよう分離）。</summary>
        public void Tick(float deltaTime) => State.Tick(deltaTime);

        /// <summary>
        /// Hurt 硬直・被弾後無敵を即時解除する（Phase3.5 P3.5-07。Wave 間の Player 中立化 §8.3）。OnDisable と同じ
        /// 純粋タイマ Reset を明示入口として公開し、WaveRunner が Intermission 入りで呼ぶ。二重呼び出し安全。
        /// </summary>
        public void ResetHurt() => State.Reset();

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void OnDisable()
        {
            // Disable / Scene 離脱 / Retry で硬直・無敵を残さない（仕様書 §2.3）。
            State.Reset();
        }
    }
}
