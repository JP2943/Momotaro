using Momotaro.Data.Characters;
using UnityEngine;

namespace Momotaro.Gameplay.Player
{
    /// <summary>
    /// Player の Runtime Vitals を保持する薄いコンポーネント（Phase1 P1-10）。
    /// 割り当てた PlayerData の最大値から Vitals を生成する。被ダメージや HUD は Phase 1 では扱わない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVitalsHolder : MonoBehaviour
    {
        [SerializeField] private PlayerData _data;

        /// <summary>生成された Runtime Vitals。data 未設定時は null。</summary>
        public PlayerVitals Vitals { get; private set; }

        private void Awake()
        {
            if (_data != null)
            {
                Vitals = PlayerVitals.FromData(_data);
            }
        }
    }
}
