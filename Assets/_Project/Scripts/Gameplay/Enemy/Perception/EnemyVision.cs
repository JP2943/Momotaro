using Momotaro.Data.Characters;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Perception
{
    /// <summary>認識段階（Phase3 §4）。非戦闘の短い視認は Suspicious、完全認識で Alert。</summary>
    public enum PerceptionPhase
    {
        /// <summary>未認識。</summary>
        Unaware = 0,

        /// <summary>不審（短い視認・共有・音）。最後に見た/聞いた地点を調査する。</summary>
        Suspicious = 1,

        /// <summary>警戒（戦闘開始）。</summary>
        Alert = 2,
    }

    /// <summary>
    /// 認識の設定値（Phase3 §4.1 / Table 7）。<see cref="EnemyArchetypeData"/> から写して使う不変値。
    /// </summary>
    public readonly struct PerceptionSettings
    {
        /// <summary>正面視野角（度、全角）。</summary>
        public float ViewAngleDegrees { get; }
        /// <summary>通常視認距離（m）。</summary>
        public float ViewDistance { get; }
        /// <summary>警戒中視認距離（m）。</summary>
        public float AlertViewDistance { get; }
        /// <summary>背後の近接認識半径（m）。</summary>
        public float BackAwarenessRadius { get; }
        /// <summary>完全認識までの蓄積秒。</summary>
        public float FullRecognitionSeconds { get; }
        /// <summary>視認喪失後の追跡継続秒。</summary>
        public float LoseSightSeconds { get; }

        public PerceptionSettings(float viewAngle, float viewDistance, float alertViewDistance,
            float backAwareness, float fullRecognition, float loseSight)
        {
            ViewAngleDegrees = viewAngle;
            ViewDistance = viewDistance;
            AlertViewDistance = alertViewDistance;
            BackAwarenessRadius = backAwareness;
            FullRecognitionSeconds = fullRecognition;
            LoseSightSeconds = loseSight;
        }

        /// <summary>アーキタイプから設定を写す。</summary>
        public static PerceptionSettings From(EnemyArchetypeData data)
        {
            return new PerceptionSettings(
                data.ViewAngleDegrees, data.ViewDistance, data.AlertViewDistance,
                data.BackAwarenessRadius, data.FullRecognitionSeconds, data.LoseSightSeconds);
        }
    }

    /// <summary>
    /// 視線遮蔽の問い合わせ契約（Phase3 §4.1）。壁・遮蔽物で視線が通るかを返す。物理実装（<see cref="PhysicsLineOfSightProbe"/>）
    /// を注入し、テストでは Fake を差し替える（yield/物理に依存しない再現テスト）。
    /// </summary>
    public interface ILineOfSightProbe
    {
        /// <summary><paramref name="from"/> から <paramref name="to"/> まで壁で遮られず視線が通るなら true。</summary>
        bool HasLineOfSight(Vector3 from, Vector3 to);
    }

    /// <summary>
    /// 視覚判定の純粋関数（Phase3 §4.1）。XZ 平面で距離・Facing 角を先に判定し、視線（LOS）は外部から与える
    /// （負荷平準化のため通過候補だけ LOS 問い合わせする設計に合わせ、hasLineOfSight を引数化）。
    /// </summary>
    public static class VisionCheck
    {
        /// <summary>XZ 平面での距離。</summary>
        public static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>対象が視野コーン内（角度・距離）か。LOS は含めない純粋幾何。</summary>
        public static bool IsWithinViewCone(Vector3 observerPos, Vector3 observerForward, Vector3 targetPos,
            float viewAngleDegrees, float maxDistance)
        {
            Vector3 to = targetPos - observerPos;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > maxDistance || dist < 1e-5f)
            {
                return dist < 1e-5f; // 同一地点は視野内扱い
            }

            Vector3 fwd = observerForward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                return false;
            }

            float angle = Vector3.Angle(fwd, to);
            return angle <= viewAngleDegrees * 0.5f;
        }

        /// <summary>背後を含む近接認識（半径以内なら角度に関係なく感知）か。</summary>
        public static bool IsWithinBackAwareness(Vector3 observerPos, Vector3 targetPos, float backAwarenessRadius)
        {
            return PlanarDistance(observerPos, targetPos) <= backAwarenessRadius;
        }

        /// <summary>
        /// この観測フレームで対象を感知できるか（幾何 AND 視線）。<paramref name="isAlert"/> 時は警戒中視認距離を使う。
        /// 視野コーン内、または背後近接圏内で、かつ視線が通っている（<paramref name="hasLineOfSight"/>）ときに true。
        /// </summary>
        public static bool CanSense(Vector3 observerPos, Vector3 observerForward, Vector3 targetPos,
            in PerceptionSettings settings, bool isAlert, bool hasLineOfSight)
        {
            if (!hasLineOfSight)
            {
                return false; // 壁・遮蔽で視線が通らなければ感知しない（背後近接も同様）。
            }

            float maxDist = isAlert ? settings.AlertViewDistance : settings.ViewDistance;
            bool inCone = IsWithinViewCone(observerPos, observerForward, targetPos, settings.ViewAngleDegrees, maxDist);
            bool backNear = IsWithinBackAwareness(observerPos, targetPos, settings.BackAwarenessRadius);
            return inCone || backNear;
        }
    }
}
