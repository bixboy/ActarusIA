using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class ShootEnemy : UtilityAction
    {
        [SerializeField, Range(0f, 1f)] private float minimumThrustWhenAligned = 0.2f;
        [SerializeField, Range(0f, 45f)] private float alignmentTolerance = 8f;
        [SerializeField, Range(0f, 1f)] private float predictionFallbackTime = 0.25f;

        public ShootEnemy(Blackboard bb) : base(bb)
        {
        }

        protected override float GetInputValue(Scorer scorer)
        {
            if (!_bb || _bb.myShip == null || _bb.enemyShip == null)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.Distance:
                    return Vector2.Distance(_bb.myShip.Position, _bb.enemyShip.Position);
                
                case ScorerInputType.Speed:
                    return _bb.enemyShip.Velocity.magnitude;
                
                case ScorerInputType.Ownership:
                    return 1f;
                
                default:
                    return 0f;
            }
        }

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (!_bb || _bb.myShip == null || _bb.enemyShip == null)
                return input;

            Vector2 shooterPos = _bb.myShip.Position;
            Vector2 targetPos = _bb.enemyShip.Position;
            Vector2 targetVelocity = _bb.enemyShip.Velocity;

            Vector2 aimPoint = ComputeAimPoint(shooterPos, targetPos, targetVelocity);
            Vector2 shootDirection = (aimPoint - shooterPos).normalized;

            if (shootDirection.sqrMagnitude < Mathf.Epsilon)
            {
                shootDirection = _bb.enemyShip.Position - shooterPos;
                if (shootDirection.sqrMagnitude > Mathf.Epsilon)
                    shootDirection.Normalize();
            }

            float desiredOrientation = Mathf.Atan2(shootDirection.y, shootDirection.x) * Mathf.Rad2Deg;
            input.targetOrientation = desiredOrientation;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.myShip.Orientation, desiredOrientation));

            // Smooth thrust to keep some momentum when we are almost aligned.
            float thrustWhenAligned = Mathf.Lerp(minimumThrustWhenAligned, 1f, 1f - Mathf.Clamp01(angleDiff / 90f));
            input.thrust = angleDiff <= _bb.angleTolerance ? thrustWhenAligned : 0f;

            bool enoughEnergy = _bb.energy >= _bb.myShip.ShootEnergyCost;
            bool aligned = angleDiff <= Mathf.Min(_bb.angleTolerance, alignmentTolerance);
            bool clearShot = HasLineOfFire(shooterPos, aimPoint);

            input.shoot = enoughEnergy && aligned && clearShot;

            return input;
        }

        private Vector2 ComputeAimPoint(Vector2 shooterPos, Vector2 targetPos, Vector2 targetVelocity)
        {
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
                float discriminant = b * b - 4f * a * c;
                if (discriminant < 0f)
                {
                    t = predictionFallbackTime;
                }
                else
                {
                    float sqrt = Mathf.Sqrt(discriminant);
                    float t1 = (-b + sqrt) / (2f * a);
                    float t2 = (-b - sqrt) / (2f * a);

                    t = SelectBestTime(t1, t2);
                    if (t < 0f)
                        t = predictionFallbackTime;
                }
            }

            return targetPos + targetVelocity * t;
        }

        private static float SelectBestTime(float t1, float t2)
        {
            bool t1Valid = t1 > 0f;
            bool t2Valid = t2 > 0f;

            if (t1Valid && t2Valid)
                return Mathf.Min(t1, t2);

            if (t1Valid)
                return t1;

            if (t2Valid)
                return t2;

            return -1f;
        }

        private bool HasLineOfFire(Vector2 from, Vector2 to)
        {
            if (!_bb)
                return false;

            Vector2 direction = to - from;
            if (direction.sqrMagnitude < Mathf.Epsilon)
                return true;

            if (_bb.asteroids != null)
            {
                foreach (AsteroidView asteroid in _bb.asteroids)
                {
                    if (IsObstacleBlocking(from, to, asteroid.Position, asteroid.Radius))
                        return false;
                }
            }

            if (_bb.mines != null)
            {
                foreach (MineView mine in _bb.mines)
                {
                    if (IsObstacleBlocking(from, to, mine.Position, mine.BulletHitRadius))
                        return false;
                }
            }

            return true;
        }

        private static bool IsObstacleBlocking(Vector2 from, Vector2 to, Vector2 obstaclePos, float obstacleRadius)
        {
            float distance = DistancePointToSegment(obstaclePos, from, to);
            return distance < obstacleRadius;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float abSqrMag = ab.sqrMagnitude;
            
            if (abSqrMag < Mathf.Epsilon)
                return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / abSqrMag);
            Vector2 projection = a + ab * t;
            return Vector2.Distance(point, projection);
        }
    }
}