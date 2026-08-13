using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵頭上の仮 HP／体幹バーの表示モデル（Phase3 P3-11。§「雑魚頭上 HP、被 Poise 時だけ体幹表示。強敵は体幹常時表示可」）。純粋計算で、
    /// HP 比率と体幹比率（0..1 に Clamp）と体幹バーの表示要否を決める。体幹は「被弾で減っている」か「常時表示（強敵）」のときだけ表示する。
    /// 描画・Camera・GC を持たず EditMode で再現でき、MonoBehaviour 側は本モデルの結果を描くだけにする（表示専用。Gameplay 値は変えない）。
    /// </summary>
    public readonly struct OverheadBarModel
    {
        /// <summary>HP バーの塗り比率（0..1）。</summary>
        public float HpFill { get; }

        /// <summary>体幹バーの塗り比率（0..1）。</summary>
        public float PoiseFill { get; }

        /// <summary>体幹バーを表示するか（被 Poise 中＝満タン未満、または常時表示指定）。</summary>
        public bool ShowPoise { get; }

        private OverheadBarModel(float hpFill, float poiseFill, bool showPoise)
        {
            HpFill = hpFill;
            PoiseFill = poiseFill;
            ShowPoise = showPoise;
        }

        /// <summary>
        /// 現在値からバー表示モデルを作る。<paramref name="alwaysShowPoise"/> が true（強敵）なら体幹を常時表示し、そうでなければ
        /// 体幹が満タン未満（＝被弾で削れている）ときだけ表示する。max が 0 以下でも 0 除算せず 0 塗りにする。
        /// </summary>
        public static OverheadBarModel Resolve(int hp, int maxHp, float poise, float maxPoise, bool alwaysShowPoise)
        {
            float hpFill = maxHp > 0 ? Mathf.Clamp01((float)hp / maxHp) : 0f;
            float poiseFill = maxPoise > 0f ? Mathf.Clamp01(poise / maxPoise) : 0f;
            bool damaged = maxPoise > 0f && poise < maxPoise - 1e-4f;
            bool showPoise = alwaysShowPoise || damaged;
            return new OverheadBarModel(hpFill, poiseFill, showPoise);
        }
    }
}
