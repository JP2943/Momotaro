using Momotaro.Data.Combat;
using Momotaro.Gameplay.Enemy;
using Momotaro.Gameplay.Enemy.Combat;
using Momotaro.Gameplay.Enemy.Locomotion;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Enemy.Threat;
using UnityEngine;

namespace Momotaro.Presentation.Diagnostics
{
    /// <summary>
    /// 敵 AI の Development 限定デバッグオーバレイ（Phase3 P3-11。§「State、Target、Threat、選択 Attack、Score、Slot、LOS、活動範囲を
    /// 切替表示」）。<see cref="_display"/> の切替でオプトイン。無効時・非 Development ビルドでは一切描かず文字列も確保しない（余計な GC なし）。
    /// 表示専用で AI の正本にしない。Actor 破棄・Camera 背面でも参照例外を出さないよう毎回 null と可視性を確認する。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyAiDebugOverlay : MonoBehaviour
    {
        [Tooltip("デバッグ表示を出すか（Development 限定・既定 無効＝オプトイン）。")]
        [SerializeField] private bool _display;

        [Tooltip("頭上ラベルの表示高さ（m）。")]
        [SerializeField] private float _height = 2.6f;

        private EnemyActor _actor;
        private EnemyThreatTracker _threat;
        private EnemyAttackController _combat;
        private EnemyPerception _perception;
        private EnemyBrain _brain;

        /// <summary>表示切替（Debug ツール・テストから）。</summary>
        public bool Display { get => _display; set => _display = value; }

        private void Awake()
        {
            _actor = GetComponentInParent<EnemyActor>();
            _threat = GetComponentInParent<EnemyThreatTracker>();
            _combat = GetComponentInParent<EnemyAttackController>();
            _perception = GetComponentInParent<EnemyPerception>();
            _brain = GetComponentInParent<EnemyBrain>();
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnGUI()
        {
            if (!_display || _actor == null)
            {
                return; // Debug OFF／Actor 破棄：何も確保・描画しない。
            }

            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(_actor.WorldPosition + Vector3.up * _height);
            if (sp.z <= 0f)
            {
                return; // カメラ背面。
            }

            string text = EnemyDebugReadout.Build(
                true,
                _actor.State,
                _threat != null ? _threat.CurrentTargetId : 0,
                _threat != null ? _threat.CurrentThreat : 0f,
                _combat != null ? _combat.CurrentAttackClass : EnemyAttackClass.Normal,
                _combat != null && _combat.IsAttacking,
                _combat != null ? _combat.DebugSelectedScore : 0f,
                _combat != null && _combat.HoldsAttackSlot,
                _perception != null ? _perception.Phase : PerceptionPhase.Unaware,
                _actor.Archetype != null ? _actor.Archetype.ActivityRadius : 0f);

            var rect = new Rect(sp.x - 150f, Screen.height - sp.y - 8f, 320f, 20f);
            GUI.Label(rect, text);
        }

        private void OnDrawGizmos()
        {
            if (!_display)
            {
                return;
            }

            float radius = _actor != null && _actor.Archetype != null ? _actor.Archetype.ActivityRadius : 0f;
            if (radius <= 0f)
            {
                return;
            }

            Vector3 center = Application.isPlaying && _brain != null ? _brain.Home : transform.position;
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
            DrawWireCircleXZ(center, radius);
        }

        private static void DrawWireCircleXZ(Vector3 center, float radius)
        {
            const int seg = 40;
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
#endif
    }
}
