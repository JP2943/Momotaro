using System;
using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// 敵の直線 Projectile（Phase3 P3-08。§9.2）。<see cref="EnemyProjectileState"/> で直進し、前回位置→今回位置の区間を
    /// SphereCast で連続判定して、薄い壁・高速移動の対象をすり抜けない。最も手前の有効衝突を優先し、発射者・敵 Faction は通過、
    /// 壁で消滅、敵対（主人公・仲間）へは 1 発 1Hit で命中する。命中は Phase 2 と同じ <see cref="IDamageable.ReceiveHit"/> 経路
    /// （<see cref="EnemyHitFactory"/> 生成の <see cref="HitInfo"/>）で解決し、無敵＞JG＞Guard＞Damage は被弾側が担保する。JG 成立時の発射者
    /// 体幹返却も HitInfo の JG 反射値で成立する。発射者が Down／消失していても攻撃者を null に落として例外を出さない。Pause 中は進まない。
    /// Gameplay Root は回転させず（表示は VisualRoot の Billboard／4 方向 Sprite が担う）、追尾・放物線・範囲弾は対象外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyProjectile : MonoBehaviour
    {
        [Tooltip("命中判定の半径（m）。SphereCast／終端 Overlap 共通。")]
        [SerializeField] private float _radius = 0.25f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly Collider[] _overlap = new Collider[8];
        private EnemyProjectileState _state;
        private EnemyAttackSnapshot _snapshot;
        private ICombatActor _owner;
        private UnityEngine.Object _ownerObject; // 破棄検出用（Unity のダングリング null を判定）。
        private CombatFaction _ownerFaction = CombatFaction.Enemy;
        private float _attackPower;
        private HitId _hitId;
        private Vector3 _prevPos;
        private bool _live;

        /// <summary>飛翔中か（テスト／Debug）。</summary>
        public bool IsLive => _live;

        /// <summary>これまでの飛距離（テスト／Debug）。</summary>
        public float Traveled => _state != null ? _state.Traveled : 0f;

        /// <summary>進行方向（XZ 正規化。表示の 4 方向決定に用いる）。</summary>
        public Vector3 Direction => _state != null ? _state.Direction : Vector3.forward;

        /// <summary>
        /// 発射初期化（Launcher から）。原本 Data ではなく不変 <see cref="EnemyAttackSnapshot"/> と発射時の攻撃力を写し取る。
        /// </summary>
        public void Initialize(in EnemyAttackSnapshot snapshot, Vector3 origin, Vector3 direction,
            ICombatActor owner, float attackPower, HitId hitId)
        {
            _snapshot = snapshot;
            _owner = owner;
            _ownerObject = owner as UnityEngine.Object;
            _ownerFaction = owner?.Faction ?? CombatFaction.Enemy;
            _attackPower = attackPower;
            _hitId = hitId;
            _state = new EnemyProjectileState(origin, direction, snapshot.ProjectileSpeed,
                snapshot.ProjectileMaxDistance, snapshot.ProjectileLifetimeSeconds);
            _prevPos = origin;
            transform.position = origin;
            _live = true;

            // 発射方向を表示側へ即時通知（生成フレームの初期化順・既定値に依存せず 4 方向表示を確定させる）。
            var visual = GetComponentInChildren<IProjectileVisual>(true);
            visual?.OnProjectileLaunched(_state.Direction);
        }

        private void FixedUpdate()
        {
            if (!_live || _state == null || !IsGameplayActive())
            {
                return; // Pause／会話中は停止（既に飛翔中でも進めない）。
            }

            Step(Time.fixedDeltaTime);
        }

        /// <summary>1 物理ステップ進める（FixedUpdate から、またはテストが決定的に呼ぶ）。生存なら true。</summary>
        public bool Step(float deltaTime)
        {
            if (!_live || _state == null)
            {
                return false;
            }

            Vector3 from = _prevPos;
            Vector3 to = _state.Advance(deltaTime);
            Vector3 seg = to - from;
            float dist = seg.magnitude;

            // 前回位置→今回位置を連続判定（薄い壁・高速対象をすり抜けない。§9.2）。最も手前の有効衝突を優先する。
            Physics.SyncTransforms();
            if (dist > 1e-5f)
            {
                Vector3 dir = seg / dist;
                int count = Physics.SphereCastNonAlloc(from, _radius, dir, _hits, dist, ~0, QueryTriggerInteraction.Collide);
                SortByDistance(count);
                for (int i = 0; i < count; i++)
                {
                    Collider col = _hits[i].collider;
                    if (col == null)
                    {
                        continue;
                    }

                    // SphereCast の start 重なりは distance=0・point 不定になるため、命中点は現在位置寄りで代用する。
                    Vector3 point = _hits[i].distance > 1e-4f ? _hits[i].point : col.ClosestPoint(from);
                    if (Resolve(col, point, out bool destroyed))
                    {
                        if (destroyed)
                        {
                            return false; // 手前の有効衝突で消滅（貫通しない）。
                        }
                        // Pass（発射者・味方陣営）：奥の衝突を続けて確認する。
                    }
                }
            }
            else
            {
                // ほぼ静止ステップ：終端 Overlap で取りこぼしを補う。
                int c = Physics.OverlapSphereNonAlloc(to, _radius, _overlap, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < c; i++)
                {
                    if (_overlap[i] != null && Resolve(_overlap[i], to, out bool destroyed) && destroyed)
                    {
                        return false;
                    }
                }
            }

            transform.position = to;
            _prevPos = to;

            if (_state.ShouldExpire)
            {
                Expire();
                return false; // 寿命／最大飛距離で消滅。
            }

            return true;
        }

        /// <summary>1 Collider を判定する。何らかの当たり（命中/壁/通過）なら true、そのうち消滅したら <paramref name="destroyed"/>=true。</summary>
        private bool Resolve(Collider col, Vector3 hitPoint, out bool destroyed)
        {
            destroyed = false;
            Transform root = col.transform.root;
            bool selfOrOwner = root == transform.root
                || (_ownerObject != null && _owner is Component oc && oc.transform.root == root);

            var damageable = col.GetComponentInParent<IDamageable>();
            bool hasDamageable = damageable != null;
            var actor = col.GetComponentInParent<ICombatActor>();
            bool hostile = actor == null || PerceptionTargetRegistry.IsHostile(_ownerFaction, actor.Faction);
            bool isWall = col.gameObject.layer == CombatLayers.WallLayer;

            ProjectileImpact impact = ProjectileHitDecision.Decide(selfOrOwner, hasDamageable, hostile, isWall);
            switch (impact)
            {
                case ProjectileImpact.HitTarget:
                    ApplyHit(damageable, hitPoint);
                    DestroySelf();
                    destroyed = true;
                    return true;
                case ProjectileImpact.DestroyOnWall:
                    DestroySelf();
                    destroyed = true;
                    return true;
                default:
                    return true; // 通過（自分・発射者・味方陣営）だが「当たりは処理した」＝奥へ続行。
            }
        }

        private void SortByDistance(int count)
        {
            // 手前優先（挿入ソート。count は小さい）。
            for (int i = 1; i < count; i++)
            {
                RaycastHit h = _hits[i];
                int j = i - 1;
                while (j >= 0 && _hits[j].distance > h.distance)
                {
                    _hits[j + 1] = _hits[j];
                    j--;
                }

                _hits[j + 1] = h;
            }
        }

        private void ApplyHit(IDamageable target, Vector3 hitPoint)
        {
            // 発射者が消失していれば攻撃者を null に落として例外を出さない（JG 反射先が無くても安全）。§9.2
            ICombatActor attacker = _ownerObject != null ? _owner : null;
            HitInfo hit = EnemyHitFactory.Build(_snapshot, _attackPower, attacker, target, _state.Direction, hitPoint, _hitId);
            target.ReceiveHit(hit);
        }

        private void Expire() => DestroySelf();

        private void DestroySelf()
        {
            _live = false;
            Destroy(gameObject);
        }

        private static bool IsGameplayActive()
        {
            IGameModeService modes = GameModeProvider.Current;
            if (modes == null)
            {
                return true;
            }

            GameMode m = modes.Current;
            return m == GameMode.Exploration || m == GameMode.Combat;
        }
    }
}
