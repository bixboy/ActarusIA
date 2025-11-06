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
            if (!_bb || _bb.MyShip == null)
                return 0f;

            float closestThreat = GetClosestThreatDistance();
            bool targeted = _bb.IsTargetedByEnemy();

            switch (scorer.inputType)
            {
                case ScorerInputType.DistanceToTarget:
                    return targeted ? 0f : (float.IsPositiveInfinity(closestThreat) ? detectionRadius * 2f : closestThreat);

                case ScorerInputType.ShipSpeed:
                    return _bb.EnemyShip != null ? _bb.EnemyShip.Velocity.magnitude : 0f;

                case ScorerInputType.TargetWaypointOwnership:
                    return 1f;
            }

            return 0f;
        }

        public override InputData Execute()
        {
            InputData input = new InputData
            {
                targetOrientation = _bb?.MyShip != null ? _bb.MyShip.Orientation : 0f
            };

            if (!_bb || _bb.MyShip == null)
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
            _bb.HasToFireShockwave = shouldTrigger;

            return input;
        }

        // ---------------- THREAT ANALYSIS ----------------

        private float GetClosestThreatDistance()
        {
            if (_bb.MyShip == null)
                return float.PositiveInfinity;

            Vector2 myPos = _bb.MyShip.Position;
            float min = float.PositiveInfinity;

            if (_bb.EnemyShip != null)
                min = Mathf.Min(min, Vector2.Distance(myPos, _bb.EnemyShip.Position));

            if (_bb.Mines != null)
            {
                foreach (var mine in _bb.Mines)
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
            if (_bb.Bullets == null || _bb.MyShip == null)
                return false;

            Vector2 shipPos = _bb.MyShip.Position;
            Vector2 shipVel = _bb.MyShip.Velocity;

            foreach (var bullet in _bb.Bullets)
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
            if (_bb.MyShip == null)
                return false;

            Vector2 myPos = _bb.MyShip.Position;
            Vector2 forward = GetForwardDirection();
            bool found = false;

            if (_bb.EnemyShip != null)
            {
                float distance = Vector2.Distance(myPos, _bb.EnemyShip.Position);
                if (distance <= detectionRadius && IsOutsideView(forward, _bb.EnemyShip.Position - myPos))
                {
                    found = true;
                    closestDistance = Mathf.Min(closestDistance, distance);
                }
            }

            if (_bb.Mines != null)
            {
                foreach (var mine in _bb.Mines)
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
            if (_bb.EnemyShip == null || _bb.MyShip == null)
                return false;
            
            return Vector2.Distance(_bb.MyShip.Position, _bb.EnemyShip.Position) < meleeDistance;
        }
        
        private bool EnemyAboutToShoot()
        {
            if (_bb.EnemyShip == null || _bb.MyShip == null)
                return false;

            Vector2 toMe = _bb.MyShip.Position - _bb.EnemyShip.Position;
            float angle = Vector2.Angle(_bb.EnemyShip.LookAt, toMe);
    
            return angle < 15f && toMe.magnitude < 4.2f;
        }


        // ---------------- ENERGY / COOLDOWN ----------------

        private bool CanShockwaveNow() => Time.time - lastShockwaveTime >= shockwaveCooldown;

        private bool HasEnergyForShockwave(bool urgent)
        {
            float cost = _bb.MyShip.ShockwaveEnergyCost;
            if (_bb.MyEnergyLeft < cost)
                return false;
            
            if (urgent)
                return true;
            
            return (_bb.MyEnergyLeft - cost) >= postShockwaveEnergyReserve;
        }

        private bool AdaptiveEnergyApproval()
        {
            float ratio = Mathf.InverseLerp(0.15f, 0.65f, _bb.MyEnergyLeft);
            return Random.value < ratio;
        }

        // ---------------- GEOMETRY HELPERS ----------------

        private Vector2 GetForwardDirection()
        {
            if (_bb.MyShip.LookAt.sqrMagnitude > Mathf.Epsilon)
                return _bb.MyShip.LookAt.normalized;

            float rad = _bb.MyShip.Orientation * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        private bool IsOutsideView(Vector2 forward, Vector2 toTarget) =>
            Vector2.Angle(forward.normalized, toTarget.normalized) > blindSpotAngle;
    }
}
