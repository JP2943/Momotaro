using UnityEngine;

namespace Momotaro.Gameplay.Combat
{
    /// <summary>
    /// 命中方向・背後判定のための純粋計算（P2-01・依頼 §8）。World XZ 平面を前提とし、
    /// 入力の Y 成分は無視する。0 ベクトルなど不正入力でも NaN を出さず安全な既定へ落とす。
    /// UnityEngine の数学型のみを用い、Input System / Animator / Scene には一切依存しない。
    /// </summary>
    public static class CombatGeometry
    {
        /// <summary>方向とみなす最小の長さ（これ未満は「向き無し」として扱う）。</summary>
        public const float MinDirectionMagnitude = 1e-4f;

        /// <summary>背後判定の既定しきい値（度）。仕様書 3.1：正面から 135 度以上を背後とする。</summary>
        public const float DefaultBackMinDegrees = 135f;

        /// <summary>正面とみなす既定の最大角（度）。確定仕様ではない試作値（判定関数で上書き可能）。</summary>
        public const float DefaultFrontMaxDegrees = 45f;

        /// <summary>ベクトルを XZ 平面へ射影する（Y=0）。</summary>
        public static Vector3 FlattenXZ(Vector3 v)
        {
            return new Vector3(v.x, 0f, v.z);
        }

        /// <summary>
        /// XZ 平面へ射影し正規化した方向を返す。長さが <see cref="MinDirectionMagnitude"/> 未満なら
        /// false を返し <paramref name="direction"/> は <see cref="Vector3.zero"/>。
        /// </summary>
        public static bool TryGetDirectionXZ(Vector3 v, out Vector3 direction)
        {
            Vector3 flat = FlattenXZ(v);
            float magnitude = flat.magnitude;
            if (magnitude < MinDirectionMagnitude)
            {
                direction = Vector3.zero;
                return false;
            }

            direction = flat / magnitude;
            return true;
        }

        /// <summary>
        /// 被弾側の前方向と「被弾側→攻撃者」方向の間の角度（0〜180 度）を返す。
        /// いずれかが向き無しのときは 0 度（正面扱い）を返す。
        /// </summary>
        public static float BearingDegrees(Vector3 defenderForward, Vector3 defenderToAttacker)
        {
            if (!TryGetDirectionXZ(defenderForward, out Vector3 forward) ||
                !TryGetDirectionXZ(defenderToAttacker, out Vector3 toAttacker))
            {
                return 0f;
            }

            float dot = Mathf.Clamp(Vector3.Dot(forward, toAttacker), -1f, 1f);
            return Mathf.Acos(dot) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// 命中方向を前方／側面／背後へ分類する。<paramref name="frontMaxDegrees"/> 以下を前方、
        /// <paramref name="backMinDegrees"/> 以上を背後、その間を側面とする。
        /// 向き無し（0 ベクトル等）の場合は安全側の <see cref="HitBearing.Front"/> を返す。
        /// </summary>
        public static HitBearing ClassifyBearing(
            Vector3 defenderForward,
            Vector3 defenderToAttacker,
            float frontMaxDegrees = DefaultFrontMaxDegrees,
            float backMinDegrees = DefaultBackMinDegrees)
        {
            if (!TryGetDirectionXZ(defenderForward, out _) ||
                !TryGetDirectionXZ(defenderToAttacker, out _))
            {
                return HitBearing.Front;
            }

            float angle = BearingDegrees(defenderForward, defenderToAttacker);
            if (angle >= backMinDegrees)
            {
                return HitBearing.Back;
            }

            if (angle <= frontMaxDegrees)
            {
                return HitBearing.Front;
            }

            return HitBearing.Side;
        }

        /// <summary>
        /// 背後攻撃か（仕様書 3.1：正面から <paramref name="backMinDegrees"/> 度以上）。
        /// 向き無しのときは false。
        /// </summary>
        public static bool IsBackHit(
            Vector3 defenderForward,
            Vector3 defenderToAttacker,
            float backMinDegrees = DefaultBackMinDegrees)
        {
            if (!TryGetDirectionXZ(defenderForward, out _) ||
                !TryGetDirectionXZ(defenderToAttacker, out _))
            {
                return false;
            }

            return BearingDegrees(defenderForward, defenderToAttacker) >= backMinDegrees;
        }

        /// <summary>
        /// 攻撃の進行方向（攻撃者→対象）から「被弾側→攻撃者」方向を求める補助。
        /// XZ 平面へ射影して符号反転する。向き無しなら <see cref="Vector3.zero"/>。
        /// </summary>
        public static Vector3 DefenderToAttackerFromAttackDirection(Vector3 attackDirection)
        {
            if (!TryGetDirectionXZ(attackDirection, out Vector3 dir))
            {
                return Vector3.zero;
            }

            return -dir;
        }
    }
}
