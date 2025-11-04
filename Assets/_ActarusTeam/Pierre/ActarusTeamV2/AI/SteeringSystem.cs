using DoNotModify;
using UnityEngine;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Describes a steering system capable of computing thrust and orientation commands.
    /// </summary>
    public interface ISteeringSystem : IAvoidanceProvider
    {
        /// <summary>
        /// Updates the steering forces based on the current intentions.
        /// </summary>
        /// <param name="data">The game state.</param>
        void UpdateSteering(GameData data);

        /// <summary>
        /// Gets the thrust command generated during the last update.
        /// </summary>
        float ThrustCommand { get; }

        /// <summary>
        /// Gets the target orientation command generated during the last update.
        /// </summary>
        float OrientationCommand { get; }
    }

    /// <summary>
    /// Computes the steering force, orientation, and thrust for the spaceship.
    /// </summary>
    public sealed class SteeringSystem : ISteeringSystem
    {
        private const float ResponseTime = 0.45f;
        private const float DampingWeight = 0.8f;
        private const float AvoidWeight = 2.2f;
        private const float SafeClearance = 0.35f;
        private const float MaxSteeringClamp = 3.0f;
        private const float AlignToSteeringThrustBoost = 0.35f;
        private const float MinThrustWhenTurning = 0.15f;
        private const float LowEnergyThreshold = 0.25f;
        private const float MidEnergyThreshold = 0.55f;
        private const float BulletThreatRadius = 2.2f;
        private const float AsteroidLookAheadTime = 0.8f;
        private const float BulletLookAheadTime = 0.6f;

        private readonly Blackboard _blackboard;

        private float _thrustCommand;
        private float _orientationCommand;

        /// <summary>
        /// Initializes a new instance of the <see cref="SteeringSystem"/> class.
        /// </summary>
        /// <param name="blackboard">Shared blackboard instance.</param>
        public SteeringSystem(Blackboard blackboard)
        {
            _blackboard = blackboard;
        }

        /// <inheritdoc />
        public void UpdateSteering(GameData data)
        {
            if (_blackboard.Self == null)
            {
                _thrustCommand = 0f;
                _orientationCommand = 0f;
                return;
            }

            Vector2 targetVelocity = _blackboard.DesiredDirection * Mathf.Clamp(_blackboard.DesiredSpeed, 0f, _blackboard.Self.SpeedMax);
            Vector2 velocityError = targetVelocity - _blackboard.Self.Velocity;
            Vector2 forceTarget = velocityError / Mathf.Max(0.1f, ResponseTime);

            Vector2 forceDamping = -_blackboard.Self.Velocity * DampingWeight * Mathf.Clamp01(_blackboard.Self.Velocity.magnitude / _blackboard.Self.SpeedMax);
            Vector2 forceAvoid = ComputePredictiveAvoidance(_blackboard.Self, data) * AvoidWeight;
            Vector2 rebound = HandleReboundMomentum(_blackboard.Self);

            Vector2 unstuck = Vector2.zero;
            if (_blackboard.Self.Velocity.magnitude < _blackboard.Self.SpeedMax * 0.15f && _blackboard.DesiredSpeed > _blackboard.Self.SpeedMax * 0.4f)
            {
                Vector2 lateral = new Vector2(-_blackboard.DesiredDirection.y, _blackboard.DesiredDirection.x);
                unstuck = lateral * 0.8f;
            }

            Vector2 steering = forceTarget + forceDamping + forceAvoid + rebound + unstuck;
            if (steering.magnitude > MaxSteeringClamp)
            {
                steering = steering.normalized * MaxSteeringClamp;
            }

            float proximity = Mathf.Clamp01(_blackboard.ObstacleProximity);
            if (proximity < 1f)
            {
                float slowFactor = Mathf.Lerp(0.5f, 1f, proximity);
                steering *= slowFactor;
            }

            _blackboard.Steering = steering;
            _orientationCommand = ComputeTargetOrientation(_blackboard.Self, steering);
            _thrustCommand = ComputeThrust(_blackboard.Self, steering);
        }

        /// <inheritdoc />
        public float ThrustCommand => _thrustCommand;

        /// <inheritdoc />
        public float OrientationCommand => _orientationCommand;

        /// <inheritdoc />
        public Vector2 ComputeEmergencyEvadeDirection(GameData data)
        {
            if (_blackboard.Self == null)
            {
                return Vector2.zero;
            }

            Vector2 baseAvoid = ComputePredictiveAvoidance(_blackboard.Self, data);
            Vector2 perpendicular = new Vector2(-_blackboard.Self.Velocity.y, _blackboard.Self.Velocity.x);
            if (perpendicular.sqrMagnitude > 0.0001f)
            {
                perpendicular.Normalize();
            }

            Vector2 combined = baseAvoid + perpendicular * 0.5f;
            return combined.sqrMagnitude > 0.0001f ? combined.normalized : baseAvoid;
        }

        private Vector2 HandleReboundMomentum(SpaceShipView self)
        {
            if (Vector2.Dot(self.Velocity, _blackboard.DesiredDirection) < -0.3f)
            {
                Vector2 reflected = Vector2.Reflect(self.Velocity.normalized, _blackboard.DesiredDirection);
                Vector2 reboundForce = reflected.normalized * (self.SpeedMax * 0.7f);
                Vector2 tangent = new Vector2(-reboundForce.y, reboundForce.x) * 0.4f;
                return (reboundForce + tangent).normalized;
            }

            if (self.Velocity.magnitude < self.SpeedMax * 0.2f && self.Energy > 0.2f)
            {
                Vector2 randomNudge = Blackboard.AngleToDir(Random.Range(0f, 360f)) * 0.6f;
                return (_blackboard.DesiredDirection * 0.8f + randomNudge * 0.2f).normalized;
            }

            return Vector2.zero;
        }

        private Vector2 ComputePredictiveAvoidance(SpaceShipView self, GameData data)
        {
            Vector2 avoidance = Vector2.zero;
            int count = 0;

            Vector2 velocityDir = self.Velocity.sqrMagnitude > 0.01f
                ? self.Velocity.normalized
                : Blackboard.AngleToDir(self.Orientation);

            Vector2 futurePos = self.Position + self.Velocity * AsteroidLookAheadTime * 1.2f;

            void AddAvoidance(Vector2 toObstacle, float obstacleRadius)
            {
                float distance = toObstacle.magnitude;
                float safeDistance = obstacleRadius + self.Radius + SafeClearance;
                if (distance >= safeDistance || distance < 0.001f)
                {
                    return;
                }

                float strength = Mathf.Clamp01((safeDistance - distance) / safeDistance);
                Vector2 radial = toObstacle.normalized;
                Vector2 tangent = new Vector2(-radial.y, radial.x);
                float side = Mathf.Sign(Vector2.Dot(tangent, velocityDir));
                tangent *= side;

                Vector2 avoidanceForce = (radial * 0.6f + tangent * 1.6f) * strength;
                float frontFactor = Mathf.Clamp01(Vector2.Dot(velocityDir, -radial) + 0.4f);
                avoidance += avoidanceForce * frontFactor;
                count++;
            }

            if (data?.Asteroids != null)
            {
                foreach (var asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                    {
                        continue;
                    }

                    AddAvoidance(futurePos - asteroid.Position, asteroid.Radius);
                }
            }

            if (data?.Mines != null)
            {
                foreach (var mine in data.Mines)
                {
                    if (mine == null || !mine.IsActive)
                    {
                        continue;
                    }

                    AddAvoidance(self.Position - mine.Position, mine.ExplosionRadius);
                }
            }

            if (data?.Bullets != null)
            {
                foreach (var bullet in data.Bullets)
                {
                    if (bullet == null)
                    {
                        continue;
                    }

                    if (IsBulletThreatening(self, bullet, out Vector2 repulse))
                    {
                        Vector2 tangent = new Vector2(-repulse.y, repulse.x).normalized;
                        avoidance += (repulse * 0.8f + tangent * 0.4f);
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                avoidance /= count;
                if (avoidance.sqrMagnitude > 1e-4f)
                {
                    avoidance.Normalize();
                }
            }

            return avoidance;
        }

        private bool IsBulletThreatening(SpaceShipView self, BulletView bullet, out Vector2 repulse)
        {
            repulse = Vector2.zero;

            Vector2 relPos = self.Position - bullet.Position;
            Vector2 relVel = self.Velocity - bullet.Velocity;

            Vector2 futureRel = relPos + relVel * BulletLookAheadTime;
            float distNow = relPos.magnitude;
            float distFuture = futureRel.magnitude;

            if (distFuture + 0.2f < distNow && distFuture < BulletThreatRadius)
            {
                repulse = futureRel.sqrMagnitude > 0.0001f ? futureRel.normalized : relPos.normalized;
                float strength = Mathf.Clamp01((BulletThreatRadius - distFuture) / BulletThreatRadius);
                repulse *= strength;
                return true;
            }

            return false;
        }

        private float ComputeTargetOrientation(SpaceShipView self, Vector2 steering)
        {
            if (steering.sqrMagnitude < 0.0001f)
            {
                Vector2 forward = Blackboard.AngleToDir(self.Orientation);
                float fallbackAngle = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg;
                return Blackboard.NormalizeAngle(fallbackAngle);
            }

            float angle = Mathf.Atan2(steering.y, steering.x) * Mathf.Rad2Deg;
            return Blackboard.NormalizeAngle(angle);
        }

        private float ComputeThrust(SpaceShipView self, Vector2 steering)
        {
            Vector2 forward = Blackboard.AngleToDir(self.Orientation);
            float align = steering.sqrMagnitude > 0.0001f
                ? Mathf.Clamp01(Vector2.Dot(forward, steering.normalized))
                : 0f;

            float thrustBase;
            if (self.Energy < LowEnergyThreshold)
            {
                thrustBase = 0.25f;
            }
            else if (self.Energy < MidEnergyThreshold)
            {
                thrustBase = 0.55f;
            }
            else
            {
                thrustBase = 0.8f;
            }

            if (_blackboard.CurrentState == ShipState.Capture || _blackboard.CurrentState == ShipState.Attack)
            {
                thrustBase = Mathf.Min(1.0f, thrustBase + 0.2f);
            }

            float thrust = thrustBase + align * AlignToSteeringThrustBoost;
            if (align < 0.35f)
            {
                thrust = Mathf.Max(thrust * 0.5f, MinThrustWhenTurning);
            }

            return Mathf.Clamp01(thrust);
        }
    }
}
