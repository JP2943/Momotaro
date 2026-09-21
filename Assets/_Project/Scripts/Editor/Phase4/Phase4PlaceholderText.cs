using Momotaro.Presentation.Characters;
using UnityEditor;
using UnityEngine;

namespace Momotaro.Editor.Phase4
{
    /// <summary>
    /// 仮表示の文字と形（P4-07B。v1.0 §11「状態は色だけに依存させず、文字または小さな形状マーカーも併用する」）を
    /// Prefab・Scene へ組む共通処理。新しい素材は作らず、組み込みフォントと組み込み Sprite だけを使う。
    /// Prefab Builder（表示代理のラベル）と探索の層 Builder（地点マーカー）の両方が同じ形で作る（2 か所に持たない）。
    /// </summary>
    public static class Phase4PlaceholderText
    {
        /// <summary>地点マーカーの輪に使う組み込み Sprite（円。地面へ寝かせて使う）。</summary>
        public const string RingSpritePath = "UI/Skin/Knob.psd";

        /// <summary>組み込みフォント（uGUI の HUD と同じもの。無ければ Arial）。</summary>
        public static Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            return font;
        }

        /// <summary>地点マーカーの輪の Sprite（組み込み。無ければ null＝Validator が拾う）。</summary>
        public static Sprite ResolveRingSprite()
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>(RingSpritePath);
        }

        /// <summary>
        /// カメラへ正対する頭上ラベルを作る。<paramref name="parent"/> の下に置き場（Anchor）を作り、その子にラベル本体（Billboard＋TextMesh）を置く。
        /// 表示側は置き場の位置だけを動かす（Billboard は親からの相対位置で描く）。
        /// </summary>
        public static TextMesh CreateLabel(string name, Transform parent, Vector3 localPosition, Color color, float characterSize = 0.08f)
        {
            var anchor = new GameObject(name + "Anchor");
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = localPosition;

            var go = new GameObject(name);
            go.transform.SetParent(anchor.transform, false);
            go.AddComponent<CameraFacingBillboard>().SetDepthOffset(0.3f);

            var text = go.AddComponent<TextMesh>();
            Font font = ResolveFont();
            text.font = font;
            text.fontSize = 48;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.richText = false;
            text.text = string.Empty;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && font != null)
            {
                renderer.sharedMaterial = font.material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return text;
        }
    }
}
