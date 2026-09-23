using System.IO;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase5
{
    /// <summary>
    /// P5 の仮素材（単色 Material・箱・ラベル）の生成補助（P5-02。仕様書 v1.1 §2「3D プリミティブ＋単色 Material」）。
    ///
    /// <b>P5 専用フォルダへ作る。</b> 既存 Prototype を一律に移動・変更しない（§13.1）。
    /// 「エリア A／B」「門」「レバー」「調査」「戦闘区域」は<b>文字・形で識別できる</b>ようにし、
    /// Gizmos だけを表示に使わない（§3.2）。
    /// </summary>
    public static class Phase5Placeholder
    {
        /// <summary>仮 Material の置き場（§13.1）。</summary>
        public const string MaterialFolder = "Assets/_Project/Art/Environment/Placeholder/Phase5";

        /// <summary>床。</summary>
        public static readonly Color FloorColor = new Color(0.62f, 0.60f, 0.55f);

        /// <summary>壁。</summary>
        public static readonly Color WallColor = new Color(0.34f, 0.33f, 0.31f);

        /// <summary>水場（通行不可）。</summary>
        public static readonly Color WaterColor = new Color(0.25f, 0.45f, 0.70f);

        /// <summary>門（閉）。</summary>
        public static readonly Color GateColor = new Color(0.62f, 0.38f, 0.18f);

        /// <summary>レバー。</summary>
        public static readonly Color LeverColor = new Color(0.80f, 0.70f, 0.20f);

        /// <summary>調査地点。</summary>
        public static readonly Color InvestigationColor = new Color(0.30f, 0.70f, 0.45f);

        /// <summary>戦闘区域。</summary>
        public static readonly Color ArenaColor = new Color(0.72f, 0.28f, 0.28f);

        /// <summary>入口・復帰点。</summary>
        public static readonly Color EntryColor = new Color(0.55f, 0.55f, 0.85f);

        /// <summary>
        /// 単色 Material をフォルダへ作る（既にあれば色だけ合わせて再利用）。
        /// URP Lit を優先し、見つからなければ既定の Shader へ落とす（環境差で生成が止まらないように）。
        /// </summary>
        public static Material EnsureMaterial(string assetName, Color color)
        {
            EnsureFolder(MaterialFolder);
            string path = MaterialFolder + "/" + assetName + ".mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                ApplyColor(existing, color);
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Diffuse");
            var material = new Material(shader) { name = assetName };
            ApplyColor(material, color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void ApplyColor(Material material, Color color)
        {
            // URP Lit は _BaseColor、Built-in は _Color。両方試して、あるものへ入れる。
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        /// <summary>箱を 1 つ作る（正スケールのみ）。<paramref name="solid"/> が false なら Collider を外す。</summary>
        public static GameObject CreateBox(
            string name, Transform parent, Vector3 center, Vector3 size, Material material, bool solid = true)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }

            if (!solid)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }

            return go;
        }

        /// <summary>
        /// 通行を止めるだけの透明な境界を作る（水場・崖。§3.3「透明の通行境界を配置し、落下や水泳は実装しない」）。
        /// 見た目は別に置く。
        /// </summary>
        public static GameObject CreateBlocker(string name, Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.size = size;
            return go;
        }

        /// <summary>真上から読めるラベルを置く（Gizmos ではなく実体。§3.2）。</summary>
        public static TextMesh CreateLabel(string text, Transform parent, Vector3 position, Color color, float size = 0.5f)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 上から見下ろすカメラに正対させる。

            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = text;
            mesh.color = color;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.characterSize = size;
            mesh.fontSize = 48;
            return mesh;
        }

        /// <summary>フォルダを再帰的に作る。</summary>
        public static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
