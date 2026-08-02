using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Momotaro.Tests.EditMode
{
    /// <summary>
    /// P3-05 受入修正：戦闘シーン SCN_VS_Field の床上面がゲームプレイ接地面 Y=0 に一致することを検証する。接地敵は root Y=0／
    /// Collider 0..1／Y 位置固定で組むため、床上面が Y=0 でないと敵 Collider が床へめり込み、押し戻せず水平移動も阻害されて
    /// 継続的な path blocked 警告が出る（本テストはその不整合を静的に防ぐ）。
    /// </summary>
    public sealed class SceneGroundPlaneTests
    {
        private const string ScenePath = "Assets/_Project/Scenes/SCN_VS_Field.unity";

        [Test]
        public void VsField_FloorTopSurface_IsAtGroundPlaneZero()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                BoxCollider floor = FindFloorCollider(scene);
                Assert.IsNotNull(floor, "Floor の BoxCollider が見つからない（SCN_VS_Field）。");

                // 回転なしの床。ワールド上面 = bounds.max.y。
                float topY = floor.bounds.max.y;
                Assert.AreEqual(0f, topY, 1e-3f,
                    "床上面は接地面 Y=0 に一致すること（敵 Collider 0..1 が床にめり込まない）。実測 topY=" + topY);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static BoxCollider FindFloorCollider(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "Floor")
                    {
                        return t.GetComponent<BoxCollider>();
                    }
                }
            }

            return null;
        }
    }
}
