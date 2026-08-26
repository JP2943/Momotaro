using System;
using UnityEngine;

namespace Momotaro.Gameplay.Scenes
{
    /// <summary>
    /// 1 つの Wave の敵構成（Phase3.5 P3.5-07。仕様書 §8.2 / Table7）。近接（骸骨剣士）・遠距離（骸骨弓兵）・強敵（侍骸骨）の
    /// 体数のみを保持する純粋データ。Prefab・座標・進行時間は <see cref="WaveRunner"/> 側の Serialized 設定が持ち、ここには
    /// 敵名・座標を直書きしない（§8.3）。Inspector で編集でき、Editor Builder が Table7 の既定構成を決定的に流し込む。
    /// </summary>
    [Serializable]
    public struct WaveDefinition
    {
        [Tooltip("近接（骸骨剣士）体数。")]
        [Min(0)] public int melee;

        [Tooltip("遠距離（骸骨弓兵）体数。")]
        [Min(0)] public int ranged;

        [Tooltip("強敵（侍骸骨）体数。")]
        [Min(0)] public int elite;

        public WaveDefinition(int melee, int ranged, int elite)
        {
            this.melee = melee < 0 ? 0 : melee;
            this.ranged = ranged < 0 ? 0 : ranged;
            this.elite = elite < 0 ? 0 : elite;
        }

        /// <summary>総体数（負値は 0 に丸めて合算）。</summary>
        public int Total => Clamp(melee) + Clamp(ranged) + Clamp(elite);

        private static int Clamp(int v) => v < 0 ? 0 : v;
    }
}
