using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class ShootEnemy : UtilityAction
    {
        [Header("Aiming")]
        [SerializeField, Range(0f, 1f)] private float minimumThrustWhenAligned = 0.2f;
        [SerializeField, Range(0f, 45f)] private float alignmentTolerance = 8f;
        [SerializeField, Range(0f, 1f)] private float predictionFallbackTime = 0.25f;
        [SerializeField, Range(0f, 1f)] private float aimAssistStrength = 0.15f;

        [Header("Mine Targeting")]
        [SerializeField] private float mineShootRange = 4.0f;
        [SerializeField] private float mineAlignmentTolerance = 12f;

        public ShootEnemy(Blackboard bb) : base(bb) {}

        protected override float GetInputValue(Scorer scorer)
        {
            if (!_bb || _bb.MyShip == null)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.DistanceToTarget:
                    return _bb.EnemyShip != null ? Vector2.Distance(_bb.MyShip.Position, _bb.EnemyShip.Position) : 0f;

                case ScorerInputType.ShipSpeed:
                    return _bb.EnemyShip != null ? _bb.EnemyShip.Velocity.magnitude : 0f;

                case ScorerInputType.TargetWaypointOwnership:
                    return 1f;

                default:
                    return 0f;
            }
        }

        public override InputData Execute()
        {
            InputData input = new InputData();
            if (!_bb || _bb.MyShip == null)
                return input;

            bool targetIsMine;
            Vector2 targetPos = SelectBestTarget(out targetIsMine);

            Vector2 shooterPos = _bb.MyShip.Position;
            Vector2 targetVel = targetIsMine ? Vector2.zero : _bb.EnemyShip.Velocity;
            Vector2 aimPoint = ComputeAimPoint(shooterPos, targetPos, targetVel);

            aimPoint = Vector2.Lerp(aimPoint, targetPos, aimAssistStrength);

            Vector2 shootDirection = aimPoint - shooterPos;
            if (shootDirection.sqrMagnitude > 0.0001f)
            {
                shootDirection.Normalize();
            }
            else
            {
                shootDirection = (_bb.EnemyShip != null) ? (_bb.EnemyShip.Position - shooterPos).normalized : shootDirection;
            }

            float desiredOrientation = Mathf.Atan2(shootDirection.y, shootDirection.x) * Mathf.Rad2Deg;
            input.targetOrientation = desiredOrientation;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.MyShip.Orientation, desiredOrientation));
            input.thrust = angleDiff <= _bb.AngleTolerance ? Mathf.Lerp(minimumThrustWhenAligned, 1f, 1f - Mathf.Clamp01(angleDiff / 90f)) : 0f;

            bool enoughEnergy = _bb.MyEnergyLeft >= _bb.MyShip.ShootEnergyCost;
            bool aligned = angleDiff <= Mathf.Min(_bb.AngleTolerance, alignmentTolerance);
            bool clearShot = HasLineOfFire(shooterPos, aimPoint);
            bool closing = targetIsMine || IsClosingTarget(shootDirection, targetVel);

            input.shoot = enoughEnergy && aligned && clearShot && closing;

            return input;
        }

        // ---------------- TARGET SELECTION ----------------

        private Vector2 SelectBestTarget(out bool isMine)
        {
            isMine = false;

            if (_bb.EnemyShip != null)
            {
                MineView blockingMine = GetMineBlockingFireLine();
                if (blockingMine != null)
                {
                    isMine = true;
                    return blockingMine.Position;
                }

                return _bb.EnemyShip.Position;
            }

            MineView nearest = GetClosestMine();
            if (nearest != null)
            {
                isMine = true;
                return nearest.Position;
            }

            return _bb.MyShip.Position;
        }

        private MineView GetClosestMine()
        {
            if (_bb.Mines == null) 
                return null;

            MineView best = null;
            float minDist = float.PositiveInfinity;
            Vector2 pos = _bb.MyShip.Position;

            foreach (var mine in _bb.Mines)
            {
                if (mine == null) continue;
                float d = Vector2.Distance(pos, mine.Position);
                if (d < minDist && d <= mineShootRange)
                {
                    minDist = d;
                    best = mine;
                }
            }

            return best;
        }

        private MineView GetMineBlockingFireLine()
        {
            if (_bb.Mines == null || _bb.EnemyShip == null) 
                return null;

            Vector2 from = _bb.MyShip.Position;
            Vector2 to = _bb.EnemyShip.Position;

            foreach (var mine in _bb.Mines)
            {
                if (mine == null) 
                    continue;
                
                float dist = DistancePointToSegment(mine.Position, from, to);
                if (dist <= mine.BulletHitRadius && Vector2.Distance(from, mine.Position) <= mineShootRange)
                    return mine;
            }

            return null;
        }

        // ---------------- AIM PREDICTION ----------------

        private Vector2 ComputeAimPoint(Vector2 shooterPos, Vector2 targetPos, Vector2 targetVelocity)
        {
            if (_bb.EnemyShip != null && targetVelocity.sqrMagnitude > 0.001f)
            {
                targetVelocity += _bb.EnemyShip.LookAt.normalized * 0.35f;
            }

            Vector2 toTarget = targetPos - shooterPos;
            float projectileSpeed = BulletView.Speed;

            float a = targetVelocity.sqrMagnitude - projectileSpeed * projectileSpeed;
            float b = 2f * Vector2.Dot(toTarget, targetVelocity);
            float c = toTarget.sqrMagnitude;

            float t;
            if (Mathf.Abs(a) < 0.0001f)
            {
                t = Mathf.Approximately(b, 0f) ? 0f : Mathf.Max(-c / b, 0f);
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc < 0f)
                {
                    t = predictionFallbackTime;
                }
                else
                {
                    float sqrt = Mathf.Sqrt(disc);
                    float t1 = (-b + sqrt) / (2f * a);
                    float t2 = (-b - sqrt) / (2f * a);
                    t = SelectBestTime(t1, t2);
                    if (t < 0f) t = predictionFallbackTime;
                }
            }

            return targetPos + targetVelocity * t;
        }

        private static float SelectBestTime(float t1, float t2)
        {
            bool v1 = t1 > 0f;
            bool v2 = t2 > 0f;
            
            if (v1 && v2) 
                return Mathf.Min(t1, t2);
            
            if (v1) 
                return t1;
            
            if (v2) 
                return t2;
            
            return -1f;
        }

        // ---------------- SAFETY & LINE OF FIRE ----------------

        private bool IsClosingTarget(Vector2 shootDir, Vector2 targetVel)
        {
            float closingSpeed = Vector2.Dot(targetVel - _bb.MyShip.Velocity, shootDir);
            return closingSpeed > 0f;
        }

        private bool HasLineOfFire(Vector2 from, Vector2 to)
        {
            if (_bb.Asteroids != null)
            {
                foreach (AsteroidView a in _bb.Asteroids)
                {
                    if (IsObstacleBlocking(from, to, a.Position, a.Radius)) 
                        return false;   
                }
            }

            if (_bb.Mines != null)
            {
                foreach (MineView m in _bb.Mines)
                {
                    if (IsObstacleBlocking(from, to, m.Position, m.BulletHitRadius)) 
                        return false;   
                }
            }

            return true;
        }

        private static bool IsObstacleBlocking(Vector2 from, Vector2 to, Vector2 o, float r)
        {
            float d = DistancePointToSegment(o, from, to);
            return d < r;
        }

        private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 0.0001f));
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
