using System;
using Momotaro.Core.Logging;
using UnityEngine;

namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 進行データ（<see cref="PlayerProgressState"/>）を Scene 上で保持する窓口（P4-00、P5-01 で外部注入に対応）。
    /// 付与のルールは純粋 State 側に置き、本コンポーネントは保持と通知（<see cref="VirtueChanged"/>）だけを担う。
    /// 表示（HUD・Debug）は Presentation 層がこのイベントを購読する（Gameplay から Presentation を参照しない）。
    ///
    /// <b>意図的に <c>DontDestroyOnLoad</c> を使わない。</b> Scene をまたいで運ぶのは Holder ではなく
    /// <see cref="PlayerProgressState"/> の参照であり、Session がそれを 1 個だけ所有する（仕様書 v1.1 §4.2）。
    /// 未注入なら従来どおり自前のローカル State を使うので、P3.5／P4 の試遊 Scene は挙動が変わらない。
    ///
    /// <b>共有 State の保護。</b> 外部 State を Bind したあとは、この Holder から
    /// <see cref="ResetProgress"/> できない。本編型の死亡・Scene 変更が共有された徳を消さないためで、
    /// 新規 Session の破棄・再作成は Session の所有者だけが行う（§4.2）。
    ///
    /// <b>読み取りは「使用」にしない。</b> 初期化前に HUD が <see cref="Virtue"/> を読んだだけで
    /// Bind 不能にならない（§4.2）。HUD は Bind 後の表示開始時に現在値を読み直す。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProgressHolder : MonoBehaviour
    {
        private readonly PlayerProgressState _localState = new PlayerProgressState();

        private PlayerProgressState _boundState;
        private bool _used;

        /// <summary>進行データ本体（読み取り・テスト用）。未注入ならローカル State。</summary>
        public PlayerProgressState State => _boundState ?? _localState;

        /// <summary>外部 State が注入済みか（配線確認・Validator・テスト用）。</summary>
        public bool IsBound => _boundState != null;

        /// <summary>付与・初期化に使われたか（Bind 保護の判定。読み取りでは立たない）。</summary>
        public bool IsUsed => _used;

        /// <summary>ローカル State の実体（診断・テスト用。注入中でも取り違えを検出できる）。</summary>
        public PlayerProgressState LocalState => _localState;

        /// <summary>徳の累計。</summary>
        public int Virtue => State.Virtue;

        /// <summary>付与済みとして記録された GrantOnce 報酬の数（診断・テスト用）。</summary>
        public int GrantedRewardCount => State.GrantedRewardCount;

        /// <summary>徳が実際に変化した瞬間のみ発火する（引数は変化後の累計）。HUD・Debug 表示が購読する。</summary>
        public event Action<int> VirtueChanged;

        /// <summary>
        /// Session が所有する外部 State を注入する（§4.2）。報酬購読・Actor の活動開始より<b>前</b>に呼ぶ
        /// （§5.1 手順 4 が手順 6 より前にあるのはこのため）。
        ///
        /// 規則：null は無視。<b>同一参照の再 Bind は冪等</b>（Scene 再読込後の再配線で State を捨てない）。
        /// 使用後（<see cref="Grant"/>／<see cref="ResetProgress"/> のあと）の<b>別参照</b>への差し替えは拒否し、
        /// 既存 State を保持する。読み取りだけでは拒否条件にならない。
        /// </summary>
        /// <returns>注入が成立したか。</returns>
        public bool Bind(PlayerProgressState state)
        {
            if (state == null)
            {
                GameLog.WarningOnce(LogCategory.Boot, "progress_bind_null",
                    "進行データの注入に null が渡されたため無視しました（既存の State を保持します）。");
                return false;
            }

            if (ReferenceEquals(_boundState, state))
            {
                return true;
            }

            if (_used)
            {
                GameLog.WarningOnce(LogCategory.Boot, "progress_bind_after_use",
                    "この Holder は既に付与・初期化に使われているため、別の進行 State へ差し替えませんでした。"
                    + "注入は報酬購読と Actor の活動開始より前に行ってください（仕様書 §5.1）。");
                return false;
            }

            _boundState = state;
            return true;
        }

        /// <summary>報酬の付与を試みる（ルールは <see cref="PlayerProgressState.TryGrant"/>）。徳が変化したときだけ通知する。</summary>
        /// <param name="reward">付与要求。</param>
        /// <param name="grantedVirtue">実際に加算された徳量。</param>
        /// <returns>処理結果。</returns>
        public RewardGrantResult Grant(in RewardSnapshot reward, out int grantedVirtue)
        {
            _used = true;

            PlayerProgressState state = State;
            RewardGrantResult result = state.TryGrant(reward, out grantedVirtue);
            if (grantedVirtue > 0)
            {
                VirtueChanged?.Invoke(state.Virtue);
            }

            return result;
        }

        /// <summary>
        /// 徳と付与済み記録を初期化する（試遊 Scene の新規セッション・検証の再試行用）。変化があれば通知する。
        ///
        /// <b>外部 State を Bind 済みなら拒否する</b>。本編型の死亡再開・Scene 変更から共有された進行を
        /// 消させないための保護で、Session の破棄・再作成は所有者だけが行う（§4.2）。
        /// </summary>
        /// <returns>初期化したか。Bind 済みで拒否した場合は false。</returns>
        public bool ResetProgress()
        {
            if (_boundState != null)
            {
                GameLog.WarningOnce(LogCategory.Boot, "progress_reset_shared",
                    "共有された進行 State は Scene 側から初期化できません（ResetProgress を無視しました）。"
                    + "新規 Session の作成は Session の所有者が行います（仕様書 §4.2）。");
                return false;
            }

            _used = true;
            // 発火条件は P4-00 のまま「徳が実際に変化したときだけ」。GrantOnce 記録だけの消去では通知しない。
            bool changed = _localState.Virtue != 0;
            _localState.Reset();
            if (changed)
            {
                VirtueChanged?.Invoke(_localState.Virtue);
            }

            return true;
        }
    }
}
