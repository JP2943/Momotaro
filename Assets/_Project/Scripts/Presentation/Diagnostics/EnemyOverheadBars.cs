using Momotaro.Gameplay.Enemy;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵頭上の仮 HP／体幹バー（Phase3 P3-11。§「雑魚頭上 HP、被 Poise 時だけ体幹表示。強敵は体幹常時表示可」）。<see cref="OverheadBarModel"/>
    /// の結果を、キャッシュした 1px テクスチャで描くだけの表示専用ビュー（完成 HUD デザインは対象外）。Camera 背面・Actor 破棄・Pool 化・
    /// 画面外でも参照例外を出さないよう、毎フレーム null と可視性を確認してから描く。文字列を確保しない（バーはテクスチャのみ）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyOverheadBars : MonoBehaviour
    {
        [Tooltip("表示元の Actor（未指定なら親から取得）。")]
        [SerializeField] private EnemyActor _actor;

        [Tooltip("頭上の表示高さ（m）。")]
        [SerializeField] private float _height = 2.0f;

        [Tooltip("バーの画面上の幅・高さ（px）。")]
        [SerializeField] private float _barWidth = 60f;
        [SerializeField] private float _barHeight = 6f;

        [Tooltip("表示するか（仮 UI の一括切替。既定 有効）。")]
        [SerializeField] private bool _display = true;

        private static Texture2D _white;

        private void Awake()
        {
            if (_actor == null)
            {
                _actor = GetComponentInParent<EnemyActor>();
            }
        }

        private static Texture2D White()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _white.hideFlags = HideFlags.HideAndDontSave;
            }

            return _white;
        }

        private void OnGUI()
        {
            if (!_display || _actor == null)
            {
                return; // Actor 破棄・Pool 返却・無効時は何も描かない（参照例外を出さない）。
            }

            if (_actor.IsDown || _actor.MaxHp <= 0)
            {
                return; // 撃破後は非表示。
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(_actor.WorldPosition + Vector3.up * _height);
            if (sp.z <= 0f)
            {
                return; // カメラ背面は描かない。
            }

            float x = sp.x - _barWidth * 0.5f;
            float y = Screen.height - sp.y;
            if (x + _barWidth < 0f || x > Screen.width || y + _barHeight * 2f < 0f || y > Screen.height)
            {
                return; // 画面外は描かない（安全側）。
            }

            OverheadBarModel model = OverheadBarModel.Resolve(
                _actor.CurrentHp, _actor.MaxHp, _actor.CurrentPoise, _actor.MaxPoise,
                _actor.Archetype != null && _actor.Archetype.AlwaysShowPoise);

            DrawBar(x, y, model.HpFill, new Color(0.15f, 0.15f, 0.15f, 0.85f), new Color(0.85f, 0.2f, 0.2f, 0.95f));

            if (model.ShowPoise)
            {
                DrawBar(x, y + _barHeight + 1f, model.PoiseFill,
                    new Color(0.15f, 0.15f, 0.15f, 0.85f), new Color(0.95f, 0.8f, 0.2f, 0.95f));
            }
        }

        private void DrawBar(float x, float y, float fill, Color back, Color fore)
        {
            Texture2D tex = White();
            Color prev = GUI.color;

            GUI.color = back;
            GUI.DrawTexture(new Rect(x, y, _barWidth, _barHeight), tex);

            GUI.color = fore;
            GUI.DrawTexture(new Rect(x, y, _barWidth * Mathf.Clamp01(fill), _barHeight), tex);

            GUI.color = prev;
        }
    }
}
