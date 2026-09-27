using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// スライドの進み方だけを持つ（P5.5 仕様書 §7.1／付録 A.4。工程 P55-04a）。
    ///
    /// <b>純粋な計算に切り出してある。</b> 実カメラも Scene も知らないので、
    /// 曲線・所要時間・端の精度を Unity 無しで固定できる。
    /// 実カメラへ書くのは <see cref="AreaCameraRigHost"/> の仕事で、
    /// <b>期間中はそこだけが Rig 位置を書く</b>（付録 A.4 の「書込の一系統化」）。
    ///
    /// 曲線は §7.1 の <c>s(t)=3t²−2t³</c>。端で速度 0 になる滑り出し・滑り込みで、
    /// 始点と終点は<b>厳密に</b>一致する（数値誤差で端がずれると、翌フレームに跳ねる）。
    ///
    /// 時間は<b>unscaled</b> を渡す。ヒットストップや Pause で止まる時計を渡すと、
    /// 遷移の途中で演出が固まる（§7.1）。
    /// </summary>
    public sealed class AreaCameraSlide
    {
        /// <summary>所要秒の初期値（§7.1／§3.1）。</summary>
        public const float DefaultSeconds = 0.45f;

        private Vector3 _from;
        private Vector3 _to;
        private float _seconds;
        private float _elapsed;

        /// <summary>走っているか。</summary>
        public bool IsRunning { get; private set; }

        /// <summary>出発位置（受理時の実 Rig 位置。§7.1）。</summary>
        public Vector3 From => _from;

        /// <summary>終点（到着 Area の通常追従・clamp が算出する位置。§7.1）。</summary>
        public Vector3 To => _to;

        /// <summary>所要秒。</summary>
        public float Seconds => _seconds;

        /// <summary>経過秒（unscaled）。</summary>
        public float Elapsed => _elapsed;

        /// <summary>素の進行度（0〜1。線形）。</summary>
        public float Progress => _seconds <= 0f ? 1f : Mathf.Clamp01(_elapsed / _seconds);

        /// <summary>
        /// 曲線を通した進行度（<c>s(t)=3t²−2t³</c>。§7.1）。
        /// <b>表示代理へ配るのはこちら</b>——カメラと同じ進み方でなければ、
        /// 主人公だけ先に着いたように見える（§7.2）。
        /// </summary>
        public float Eased => Ease(Progress);

        /// <summary>いまの位置。</summary>
        public Vector3 Position => Vector3.LerpUnclamped(_from, _to, Eased);

        /// <summary>曲線（<c>s(t)=3t²−2t³</c>）。0 と 1 を厳密に返す。</summary>
        public static float Ease(float t)
        {
            float x = Mathf.Clamp01(t);
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// 始める。<b>始点は受理時の実 Rig 位置</b>で、事前に境界位置へ瞬間移動させない（§7.1）。
        /// 所要秒が 0 以下なら 1 フレームで終わる扱いにする（0 除算を作らない）。
        /// </summary>
        public void Begin(Vector3 from, Vector3 to, float seconds)
        {
            _from = from;
            _to = to;
            _seconds = seconds > 0f ? seconds : 0f;
            _elapsed = 0f;
            IsRunning = true;
        }

        /// <summary>
        /// 進める（unscaled を渡す）。<b>終わったら false を返す。</b>
        ///
        /// <b>巨大な delta で飛ばさない</b>のは呼び出し側の責任（§7.1 末尾。
        /// 非フォーカス中は表示時間を進めない）。ここは渡された分だけ進める。
        /// </summary>
        public bool Tick(float unscaledDeltaTime)
        {
            if (!IsRunning)
            {
                return false;
            }

            _elapsed += unscaledDeltaTime > 0f ? unscaledDeltaTime : 0f;
            if (_elapsed >= _seconds)
            {
                _elapsed = _seconds;
                IsRunning = false;
                return false;
            }

            return true;
        }

        /// <summary>止める（終点へは進めない。Rollback が呼ぶ）。</summary>
        public void Cancel()
        {
            IsRunning = false;
        }

        /// <summary>終点まで進めて終える。</summary>
        public void Finish()
        {
            _elapsed = _seconds;
            IsRunning = false;
        }
    }
}
