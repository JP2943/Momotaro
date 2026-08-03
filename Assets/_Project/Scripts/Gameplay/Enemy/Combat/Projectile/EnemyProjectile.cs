using Momotaro.Gameplay.Combat;
using Momotaro.Gameplay.Enemy.Perception;
using Momotaro.Gameplay.Modes;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Combat.Projectile
{
    /// <summary>
    /// 敵の直線 Projectile（Phase3 P3-08。§9.2）。<see cref="EnemyProjectileState"/> で直進し、毎物理ステップの Overlap で
    /// 壁消滅・Faction フィルタ（敵には当てず主人公へ命中）・1 発 1Hit・寿命／最大飛距離での破棄を行う。命中は Phase 2 と同じ
    /// <see cref="IDamageable.ReceiveHit"/> 経路（<see cref="EnemyHitFactory"/> 生成の <see cref="HitInfo"/>）で解決し、無敵＞JG＞Guard＞Damage は
    /// 被弾側が担保する。JG 成立時の発射者体幹返却も HitInfo の JG 反射値で成立する。発射者が Down／消失していても攻撃者を null に落として
    /// 例外を出さない。Pause／会話中は進まない。追尾・放物線・範囲弾は対象外。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EnemyProjectile : MonoBehaviour
    {
        [Tooltip("命中判定 Overlap の半径（m）。")]
        [SerializeField] private float _radius = 0.25f;

        private readonly Collider[] _buffer = new Collider[16];
        private EnemyProjectileState _state;
        private EnemyAttackSnapshot _snapshot;
        private ICombatActor _owner;
        private Object _ownerObject; // 破棄検出用（Unity のダングリング null を判定）。
        private CombatFaction _ownerFaction = CombatFaction.Enemy;
        private float _attackPower;
        private HitId _hitId;
        private bool _live;

        /// <summary>飛翔中か（テスト／Debug）。</summary>
        public bool IsLive => _live;

        /// <summary>これまでの飛距離（テスト／Debug）。</summary>
        public float Traveled => _state != null ? _state.Traveled : 0f;

        /// <summary>
        /// 発射初期化（Launcher から）。原本 Data ではなく不変 <see cref="EnemyAttackSnapshot"/> と発射時の攻撃力を写し取る。
        /// </summary>
        public void Initialize(in EnemyAttackSnapshot snapshot, Vector3 origin, Vector3 direction,
            ICombatActor owner, float attackPower, HitId hitId)
        {
            _snapshot = snapshot;
            _owner = owner;
            _ownerObject = owner as Object;
            _ownerFaction = owner?.Faction ?? CombatFaction.Enemy;
            _attackPower = attackPower;
            _hitId = hitId;
            _state = new EnemyProjectileState(origin, direction, snapshot.ProjectileSpeed,
                snapshot.ProjectileMaxDistance, snapshot.ProjectileLifetimeSeconds);
            transform.position = origin;
            _live = true;
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

            Vector3 pos = _state.Advance(deltaTime);
            transform.position = pos;

            // 移動中の対象を取りこぼさないよう問い合わせ前に同期する（autoSync=0 対策。melee 経路と同様）。
            Physics.SyncTransforms();
            int count = Physics.OverlapSphereNonAlloc(pos, _radius, _buffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider col = _buffer[i];
                if (col == null)
                {
                    continue;
                }

                if (Resolve(col, pos))
                {
                    return false; // 命中または壁で消滅。
                }
            }

            if (_state.ShouldExpire)
            {
                Expire();
                return false; // 寿命／最大飛距離で消滅。
            }

            return true;
        }

        /// <summary>1 Collider を解決する。命中／壁で消滅したら true（破棄する）。</summary>
        private bool Resolve(Collider col, Vector3 hitPoint)
        {
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
                    Destroy(gameObject);
                    _live = false;
                    return true;
                case ProjectileImpact.DestroyOnWall:
                    Destroy(gameObject);
                    _live = false;
                    return true;
                default:
                    return false;
            }
        }

        private void ApplyHit(IDamageable target, Vector3 hitPoint)
        {
            // 発射者が消失していれば攻撃者を null に落として例外を出さない（JG 反射先が無くても安全）。§9.2
            ICombatActor attacker = _ownerObject != null ? _owner : null;
            HitInfo hit = EnemyHitFactory.Build(_snapshot, _attackPower, attacker, target, _state.Direction, hitPoint, _hitId);
            target.ReceiveHit(hit);
        }

        private void Expire()
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
