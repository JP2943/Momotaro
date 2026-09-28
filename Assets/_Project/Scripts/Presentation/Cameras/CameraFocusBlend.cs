using Momotaro.Core.Identification;
using UnityEngine;

namespace Momotaro.Presentation.Cameras
{
    /// <summary>
    /// カメラの基準位置を滑らかに動かす（P5-06。仕様書 v1.1 §11）。
    ///
    /// <b>補間の途中も範囲内に収める。</b> 始点と終点だけを見て線形に動かすと、
    /// 領域をまたぐ瞬間に<b>どちらの領域にも収まっていない位置</b>を通る。
    /// §11 が「途中のフレームも有効範囲内へ制限する」と書いているのはそこで、
    /// 毎フレーム収め直すのが唯一の守り方になる。
    ///
    /// <b>到着と死亡再開では補間しない。</b> 前の部屋から新しい部屋へ流れて見えるのは
    /// 「移動した」という表現で、Scene が切り替わったときにそれをやると、
    /// 居なかった場所から滑ってくる嘘になる（§11）。
    /// </summary>
    public sealed class CameraFocusBlend
    {
        /// <summary>部屋を切り替えたときの補間時間（秒。§11 の初期値）。</summary>
        public const float DefaultBlendSeconds = 0.15f;

        private StableId _regionId;
        private bool _hasRegion;
        private float _remaining;

        /// <summary>いまの基準位置。</summary>
        public Vector3 Current { get; private set; }

        /// <summary>補間中か（診断・テスト用）。</summary>
        public bool IsBlending => _remaining > 0f;

        /// <summary>部屋が切り替わった回数（診断・テスト用）。</summary>
        public int RegionChangeCount { get; private set; }

        /// <summary>
        /// 補間せずにその場へ置く（Scene 到着・死亡再開。§11）。
        ///
        /// <b>即時配置でも範囲は守る。</b> 補間を飛ばすことと、範囲の外に置いてよいことは別。
        /// ここを素通しにすると、到着の 1 フレームだけ床の外が見える。
        /// 領域も一緒に覚えるので、この直後に切替と誤判定しない。
        /// </summary>
        public void SnapTo(Vector3 focus, in CameraRegionDefinition region, Vector2 halfFootprint)
        {
            Current = CameraBoundsMath.ClampFocus(focus, region, halfFootprint);
            _regionId = region.RegionId;
            _hasRegion = true;
            _remaining = 0f;
        }

        /// <summary>初期化する（Scene 離脱・テストの後始末）。</summary>
        public void Reset()
        {
            _hasRegion = false;
            _remaining = 0f;
            RegionChangeCount = 0;
        }

        /// <summary>
        /// 1 フレーム進めて基準位置を返す。
        /// </summary>
        /// <param name="desiredFocus">追従したい床上の位置（収める前）。</param>
        /// <param name="region">いまの領域。</param>
        /// <param name="halfFootprint">床で見える範囲の半分。</param>
        /// <param name="deltaTime">経過時間（秒）。</param>
        /// <param name="blendSeconds">部屋切替の補間時間。</param>
        public Vector3 Tick(
            Vector3 desiredFocus,
            in CameraRegionDefinition region,
            Vector2 halfFootprint,
            float deltaTime,
            float blendSeconds = DefaultBlendSeconds)
        {
            Vector3 clamped = CameraBoundsMath.ClampFocus(desiredFocus, region, halfFootprint);

            if (!_hasRegion)
            {
                // 最初のフレーム。流れて見えるものが無いので、そのまま置く。
                SnapTo(clamped, region, halfFootprint);
                return Current;
            }

            if (!_regionId.Equals(region.RegionId))
            {
                _regionId = region.RegionId;
                _remaining = Mathf.Max(0f, blendSeconds);
                RegionChangeCount++;
            }

            float step = deltaTime < 0f ? 0f : deltaTime;

            if (_remaining <= 0f)
            {
                Current = clamped;
                return Current;
            }

            // 残り時間に対する割合で寄せる。残りが尽きたら終点に一致する。
            float t = _remaining <= step ? 1f : step / _remaining;
            _remaining = Mathf.Max(0f, _remaining - step);
            Vector3 moved = Vector3.Lerp(Current, clamped, t);

            // <b>途中も収め直す。</b> 補間の途中は「前の部屋の端」と「今の部屋の端」の間にあり、
            // そのままではどちらの領域からもはみ出しうる。
            Current = CameraBoundsMath.ClampFocus(moved, region, halfFootprint);
            return Current;
        }
    }
}
