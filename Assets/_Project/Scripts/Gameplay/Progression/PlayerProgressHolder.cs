using System;
using UnityEngine;

namespace Momotaro.Gameplay.Progression
{
    /// <summary>
    /// 進行データ（<see cref="PlayerProgressState"/>）を Scene 上で保持する窓口（P4-00）。付与のルールは純粋 State 側に置き、
    /// 本コンポーネントは保持と通知（<see cref="VirtueChanged"/>）だけを担う。表示（HUD・Debug）は Presentation 層が
    /// このイベントを購読する（Gameplay から Presentation を参照しない）。
    ///
    /// <b>意図的に <c>DontDestroyOnLoad</c> を使わない</b>：試遊 Scene の Retry は Scene 再読込であり、本コンポーネントが
    /// 破棄されることで徳・付与済み記録がともにリセットされる（P4-00 仕様）。完成版の永続化・チェックポイント復元は対象外で、
    /// 将来 Infrastructure（Save）が本 Holder を読み書きする形で拡張する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerProgressHolder : MonoBehaviour
    {
        private readonly PlayerProgressState _state = new PlayerProgressState();

        /// <summary>進行データ本体（読み取り・テスト用）。</summary>
        public PlayerProgressState State => _state;

        /// <summary>徳の累計。</summary>
        public int Virtue => _state.Virtue;

        /// <summary>付与済みとして記録された GrantOnce 報酬の数（診断・テスト用）。</summary>
        public int GrantedRewardCount => _state.GrantedRewardCount;

        /// <summary>徳が実際に変化した瞬間のみ発火する（引数は変化後の累計）。HUD・Debug 表示が購読する。</summary>
        public event Action<int> VirtueChanged;

        /// <summary>報酬の付与を試みる（ルールは <see cref="PlayerProgressState.TryGrant"/>）。徳が変化したときだけ通知する。</summary>
        /// <param name="reward">付与要求。</param>
        /// <param name="grantedVirtue">実際に加算された徳量。</param>
        /// <returns>処理結果。</returns>
        public RewardGrantResult Grant(in RewardSnapshot reward, out int grantedVirtue)
        {
            RewardGrantResult result = _state.TryGrant(reward, out grantedVirtue);
            if (grantedVirtue > 0)
            {
                VirtueChanged?.Invoke(_state.Virtue);
            }

            return result;
        }

        /// <summary>徳と付与済み記録を初期化する（新規セッション・検証の再試行用）。変化があれば通知する。</summary>
        public void ResetProgress()
        {
            bool changed = _state.Virtue != 0;
            _state.Reset();
            if (changed)
            {
                VirtueChanged?.Invoke(_state.Virtue);
            }
        }
    }
}
