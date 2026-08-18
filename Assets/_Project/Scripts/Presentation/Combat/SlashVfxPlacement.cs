using Momotaro.Presentation.Characters;
using UnityEngine;

namespace Momotaro.Presentation.Combat
{
    /// <summary>
    /// 剣閃 VFX の「表示位置・回転」を求める純関数（Phase3.5 P3.5-06 修正／案A）。判定中心（<c>SwingCenter</c>）は戦闘の当たり判定であり、
    /// 見た目の刀身高さやカメラ正対とは無関係なため、Presentation 側で次を行う：(1) 高さオフセットで刀身高さへ持ち上げる、(2) キャラと同じく
    /// カメラへ正対（billboard）させて俯瞰カメラでの縦圧縮・沈み込みを防ぐ、(3) <see cref="CameraFacingBillboard.ComputeDisplayPosition"/> と同じく
    /// カメラ側（−forward）へ DepthOffset だけ逃がして、不透明ジオメトリ（床・壁）との描画深度交差による欠けを防ぐ。
    ///
    /// カメラが無ければオフセット（高さ）のみを適用し、回転は identity（billboard／DepthOffset なし）とする。純関数なのでテストが決定的。
    /// </summary>
    public static class SlashVfxPlacement
    {
        /// <summary>
        /// 表示位置・回転を求める。<paramref name="camera"/> が null なら <c>swingCenter + up*heightOffset</c>・identity を返す（billboard なし）。
        /// カメラありなら、その持ち上げ点をカメラ側へ <paramref name="depthOffset"/> 逃がした点と、カメラ回転を返す。
        /// </summary>
        public static void Compute(Vector3 swingCenter, Camera camera, float heightOffset, float depthOffset,
            out Vector3 position, out Quaternion rotation)
        {
            Vector3 anchor = swingCenter + Vector3.up * heightOffset;
            if (camera == null)
            {
                position = anchor;
                rotation = Quaternion.identity;
                return;
            }

            Vector3 cameraForward = camera.transform.forward;
            position = CameraFacingBillboard.ComputeDisplayPosition(anchor, cameraForward, depthOffset);
            rotation = camera.transform.rotation;
        }
    }
}
