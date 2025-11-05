using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class ShockwaveDefense : UtilityAction
    {
        [Header("Detection Settings")]
        [SerializeField, Min(0f)] private float detectionRadius = 2.5f;
        [SerializeField, Range(0f, 180f)] private float blindSpotAngle = 60f;

        [Header("Energy Strategy")]
        [SerializeField, Range(0f, 1f)] private float postShockwaveEnergyReserve = 0.1f;
        [SerializeField] private float meleeDistance = 1.1f;

        [Header("Cooldown")]
        [SerializeField, Min(0f)] private float shockwaveCooldown = 0.8f;
        private float lastShockwaveTime;

        public ShockwaveDefense(Blackboard bb) : base(bb) { }

        protected override float GetInputValue(Scorer scorer)
        {
            if (!_bb || _bb.myShip == null)
                return 0f;

            float closestThreat = GetClosestThreatDistance();
            bool targeted = _bb.IsTargetedByEnemy();

            switch (scorer.inputType)
            {
                case ScorerInputType.Distance:
                    return targeted ? 0f : (float.IsPositiveInfinity(closestThreat) ? detectionRadius * 2f : closestThreat);

                case ScorerInputType.Speed:
                    return _bb.enemyShip != null ? _bb.enemyShip.Velocity.magnitude : 0f;

                case ScorerInputType.Ownership:
                    return 1f;
            }

            return 0f;
        }

        public override InputData Execute()
        {
            InputData input = new InputData
            {
                targetOrientation = _bb?.myShip != null ? _bb.myShip.Orientation : 0f
            };

            if (!_bb || _bb.myShip == null)
                return input;

            bool targeted = _bb.IsTargetedByEnemy();
            bool blindspotThreat = TryGetThreatOutsideView(out _);
            bool projectileThreat = IsBulletCollisionLikely(out float eta) && eta < 0.35f;
            bool meleeClash = IsMeleeClash();
            bool preFireThreat = EnemyAboutToShoot();

            bool shouldTrigger = (targeted || blindspotThreat || projectileThreat || meleeClash || preFireThreat);

            shouldTrigger &= HasEnergyForShockwave(targeted);
            shouldTrigger &= CanShockwaveNow();
            shouldTrigger &= AdaptiveEnergyApproval();

            if (shouldTrigger)
                lastShockwaveTime = Time.time;

            input.fireShockwave = shouldTrigger;
            _bb.hasToFireShockwave = shouldTrigger;

            return input;
        }

        // ---------------- THREAT ANALYSIS ----------------

        private float GetClosestThreatDistance()
        {
            if (_bb.myShip == null)
                return float.PositiveInfinity;

            Vector2 myPos = _bb.myShip.Position;
            float min = float.PositiveInfinity;

            if (_bb.enemyShip != null)
                min = Mathf.Min(min, Vector2.Distance(myPos, _bb.enemyShip.Position));

            if (_bb.mines != null)
            {
                foreach (var mine in _bb.mines)
                {
                    if (mine != null)
                        min = Mathf.Min(min, Vector2.Distance(myPos, mine.Position));   
                }
            }

            return min;
        }

        private bool IsBulletCollisionLikely(out float eta)
        {
            eta = float.PositiveInfinity;
            if (_bb.bullets == null || _bb.myShip == null)
                return false;

            Vector2 shipPos = _bb.myShip.Position;
            Vector2 shipVel = _bb.myShip.Velocity;

            foreach (var bullet in _bb.bullets)
            {
                if (bullet == null)
                    continue;

                Vector2 relPos = bullet.Position - shipPos;
                Vector2 relVel = bullet.Velocity - shipVel;
                float speed2 = relVel.sqrMagnitude;

                if (speed2 < 0.01f)
                    continue;

                float t = -Vector2.Dot(relPos, relVel) / speed2;
                if (t <= 0f)
                    continue;

                Vector2 futureDelta = relPos + relVel * t;
                if (futureDelta.sqrMagnitude <= detectionRadius * detectionRadius)
                {
                    eta = t;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetThreatOutsideView(out float closestDistance)
        {
            closestDistance = float.PositiveInfinity;
            if (_bb.myShip == null)
                return false;

            Vector2 myPos = _bb.myShip.Position;
            Vector2 forward = GetForwardDirection();
            bool found = false;

            if (_bb.enemyShip != null)
            {
                float distance = Vector2.Distance(myPos, _bb.enemyShip.Position);
                if (distance <= detectionRadius && IsOutsideView(forward, _bb.enemyShip.Position - myPos))
                {
                    found = true;
                    closestDistance = Mathf.Min(closestDistance, distance);
                }
            }

            if (_bb.mines != null)
            {
                foreach (var mine in _bb.mines)
                {
                    if (mine == null)
                        continue;

                    float distance = Vector2.Distance(myPos, mine.Position);
                    if (distance <= detectionRadius && IsOutsideView(forward, mine.Position - myPos))
                    {
                        found = true;
                        closestDistance = Mathf.Min(closestDistance, distance);
                    }
                }
            }

            return found;
        }

        private bool IsMeleeClash()
        {
            if (_bb.enemyShip == null || _bb.myShip == null)
                return false;
            
            return Vector2.Distance(_bb.myShip.Position, _bb.enemyShip.Position) < meleeDistance;
        }
        
        private bool EnemyAboutToShoot()
        {
            if (_bb.enemyShip == null || _bb.myShip == null)
                return false;

            Vector2 toMe = _bb.myShip.Position - _bb.enemyShip.Position;
            float angle = Vector2.Angle(_bb.enemyShip.LookAt, toMe);
    
            return angle < 15f && toMe.magnitude < 4.2f;
        }


        // ---------------- ENERGY / COOLDOWN ----------------

        private bool CanShockwaveNow() => Time.time - lastShockwaveTime >= shockwaveCooldown;

        private bool HasEnergyForShockwave(bool urgent)
        {
            float cost = _bb.myShip.ShockwaveEnergyCost;
            if (_bb.energy < cost)
                return false;
            
            if (urgent)
                return true;
            
            return (_bb.energy - cost) >= postShockwaveEnergyReserve;
        }

        private bool AdaptiveEnergyApproval()
        {
            float ratio = Mathf.InverseLerp(0.15f, 0.65f, _bb.energy);
            return Random.value < ratio;
        }

        // ---------------- GEOMETRY HELPERS ----------------

        private Vector2 GetForwardDirection()
        {
            if (_bb.myShip.LookAt.sqrMagnitude > Mathf.Epsilon)
                return _bb.myShip.LookAt.normalized;

            float rad = _bb.myShip.Orientation * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        private bool IsOutsideView(Vector2 forward, Vector2 toTarget) =>
            Vector2.Angle(forward.normalized, toTarget.normalized) > blindSpotAngle;
    }
}
