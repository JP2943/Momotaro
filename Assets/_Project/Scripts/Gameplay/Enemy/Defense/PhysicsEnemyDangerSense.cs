using Momotaro.Gameplay.Combat;
using UnityEngine;

namespace Momotaro.Gameplay.Enemy.Defense
{
    /// <summary>
    /// 物理による既定の危険観測（Phase3 P3-10。§9「観測可能な危険に反応」「入力を直接読まない」）。半径内の敵対アクター（Player）で、
    /// 攻撃の予備動作／判定中（<see cref="ICombatActivityState.IsPoiseVulnerableAction"/>）のものを危険とみなす。これは入力そのもの
    /// ではなく「攻撃という観測可能な事象」を読む（＝入力瞬間ではなく予兆に反応する）。命中前でも観測でき、回避・ガードの予備動作に間に合う。
    /// </summary>
    public sealed class PhysicsEnemyDangerSense : IEnemyDangerSense
    {
        private readonly float _radius;
        private readonly LayerMask _mask;
        private readonly Collider[] _buffer;

        /// <param name="radius">危険を観測する半径（m）。</param>
        /// <param name="mask">観測対象レイヤー（既定は全レイヤー。Faction で Player に絞る）。</param>
        /// <param name="bufferSize">OverlapSphere の非確保バッファ数。</param>
        public PhysicsEnemyDangerSense(float radius = 2.5f, int mask = ~0, int bufferSize = 16)
        {
            _radius = Mathf.Max(0.1f, radius);
            _mask = mask;
            _buffer = new Collider[Mathf.Max(1, bufferSize)];
        }

        /// <inheritdoc />
        public EnemyDangerStimulus Sense(Vector3 selfPosition, Vector3 selfForward, int selfDamageableId)
        {
            Physics.SyncTransforms();
            int count = Physics.OverlapSphereNonAlloc(selfPosition, _radius, _buffer, _mask, QueryTriggerInteraction.Collide);

            float nearestSqr = float.MaxValue;
            bool found = false;
            Vector3 sourcePos = selfPosition;
            bool nearestUnblockable = false;

            for (int i = 0; i < count; i++)
            {
                Collider col = _buffer[i];
                if (col == null)
                {
                    continue;
                }

                var actor = col.GetComponentInParent<ICombatActor>();
                if (actor == null || actor.Faction != CombatFaction.Player)
                {
                    continue; // 敵対（Player）のみ危険源とみなす。
                }

                // 観測可能な危険：まず攻撃の質を晒す契約（IAttackThreatSource）を読む。無ければ体幹補正状態で代替（Unblockable は不明→false）。
                bool attacking;
                bool unblockable;
                var threat = col.GetComponentInParent<IAttackThreatSource>();
                if (threat != null)
                {
                    attacking = threat.IsThreateningAttack;
                    unblockable = threat.IsUnblockableThreat;
                }
                else
                {
                    var activity = col.GetComponentInParent<ICombatActivityState>();
                    attacking = activity != null && activity.IsPoiseVulnerableAction;
                    unblockable = false;
                }

                if (!attacking)
                {
                    continue; // 攻撃の予備動作／判定中でなければ無視。入力そのものは読まない。
                }

                Vector3 p = actor.WorldPosition;
                float sqr = (p - selfPosition).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    sourcePos = p;
                    nearestUnblockable = unblockable;
                    found = true;
                }
            }

            if (!found)
            {
                return EnemyDangerStimulus.None;
            }

            // 進行方向＝危険源→自分（命中の AttackDirection と同じ向き）。Unblockable は攻撃側の契約から観測して伝える。
            return new EnemyDangerStimulus(sourcePos, selfPosition - sourcePos, nearestUnblockable);
        }
    }
}
