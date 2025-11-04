using DoNotModify;
using UnityEngine;

namespace Teams.ActarusControllerV1.pierre
{
    /// <summary>
    /// Describes a perception module capable of populating the blackboard with world information.
    /// </summary>
    public interface IPerceptionSystem
    {
        /// <summary>
        /// Updates the perception data stored in the blackboard.
        /// </summary>
        /// <param name="self">The controlled ship view.</param>
        /// <param name="data">The full game snapshot.</param>
        void UpdatePerception(SpaceShipView self, GameData data);
    }

    /// <summary>
    /// Handles detection of relevant entities and threats in the environment.
    /// </summary>
    public sealed class PerceptionSystem : IPerceptionSystem
    {
        private const float EnemyDetectionRange = 7.0f;
        private const float BulletThreatRadius = 2.2f;
        private const float MineThreatRadiusMultiplier = 1.05f;
        private const float AsteroidLookAheadTime = 0.8f;
        private const float BulletLookAheadTime = 0.6f;
        private const float SafeClearance = 0.35f;

        private readonly Blackboard _blackboard;

        /// <summary>
        /// Initializes a new instance of the <see cref="PerceptionSystem"/> class.
        /// </summary>
        /// <param name="blackboard">Shared AI blackboard.</param>
        public PerceptionSystem(Blackboard blackboard)
        {
            _blackboard = blackboard;
        }

        /// <inheritdoc />
        public void UpdatePerception(SpaceShipView self, GameData data)
        {
            _blackboard.Self = self;
            _blackboard.Enemy = FindEnemy(self, data);
            _blackboard.TargetWaypoint = FindPriorityWaypoint(self, data);
            _blackboard.EnemyVisible = IsEnemyVisible(self, _blackboard.Enemy, data);
            _blackboard.HasImminentThreat = HasImminentThreat(self, data);
            _blackboard.ObstacleProximity = EstimateObstacleProximity(self, data);
            _blackboard.Steering = Vector2.zero;
        }

        /// <summary>
        /// Locates the first enemy ship within the provided game data.
        /// </summary>
        private SpaceShipView FindEnemy(SpaceShipView self, GameData data)
        {
            if (self == null || data?.SpaceShips == null)
            {
                return null;
            }

            foreach (var ship in data.SpaceShips)
            {
                if (ship != null && ship.Owner != self.Owner)
                {
                    return ship;
                }
            }

            return null;
        }

        /// <summary>
        /// Selects the highest priority waypoint to capture or orbit.
        /// </summary>
        private WayPointView FindPriorityWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data?.WayPoints == null || data.WayPoints.Count == 0)
            {
                return null;
            }

            WayPointView best = null;
            float bestDist = float.MaxValue;

            foreach (var waypoint in data.WayPoints)
            {
                if (waypoint == null)
                {
                    continue;
                }

                if (waypoint.Owner != self.Owner)
                {
                    float distance = Vector2.Distance(self.Position, waypoint.Position);
                    if (distance < bestDist)
                    {
                        bestDist = distance;
                        best = waypoint;
                    }
                }
            }

            if (best != null)
            {
                return best;
            }

            foreach (var waypoint in data.WayPoints)
            {
                if (waypoint == null)
                {
                    continue;
                }

                float distance = Vector2.Distance(self.Position, waypoint.Position);
                if (distance < bestDist)
                {
                    bestDist = distance;
                    best = waypoint;
                }
            }

            return best;
        }

        /// <summary>
        /// Checks whether the enemy is visible within range and not occluded by obstacles.
        /// </summary>
        private bool IsEnemyVisible(SpaceShipView self, SpaceShipView enemy, GameData data)
        {
            if (self == null || enemy == null)
            {
                return false;
            }

            Vector2 toEnemy = enemy.Position - self.Position;
            float distance = toEnemy.magnitude;
            if (distance > EnemyDetectionRange)
            {
                return false;
            }

            if (distance < 0.001f)
            {
                return true;
            }

            Vector2 direction = toEnemy / distance;
            if (data?.Asteroids != null)
            {
                foreach (var asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                    {
                        continue;
                    }

                    Vector2 toAsteroid = asteroid.Position - self.Position;
                    float projection = Vector2.Dot(toAsteroid, direction);
                    if (projection <= 0f || projection >= distance)
                    {
                        continue;
                    }

                    Vector2 closest = self.Position + direction * projection;
                    float separation = (asteroid.Position - closest).magnitude;
                    float blockingRadius = asteroid.Radius + self.Radius * 0.5f;
                    if (separation < blockingRadius)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Determines if any bullet, mine, or asteroid poses an immediate threat to the ship.
        /// </summary>
        private bool HasImminentThreat(SpaceShipView self, GameData data)
        {
            if (self == null)
            {
                return false;
            }

            if (data?.Bullets != null)
            {
                foreach (var bullet in data.Bullets)
                {
                    if (bullet == null)
                    {
                        continue;
                    }

                    if (IsBulletThreatening(self, bullet))
                    {
                        return true;
                    }
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

                    float distance = Vector2.Distance(self.Position, mine.Position);
                    if (distance < mine.ExplosionRadius * MineThreatRadiusMultiplier)
                    {
                        return true;
                    }
                }
            }

            if (data?.Asteroids != null)
            {
                Vector2 futurePos = self.Position + self.Velocity * AsteroidLookAheadTime;
                foreach (var asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                    {
                        continue;
                    }

                    float safeDistance = asteroid.Radius + self.Radius + SafeClearance;
                    if (Vector2.Distance(futurePos, asteroid.Position) < safeDistance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Estimates a normalized proximity to the closest obstacle around the ship.
        /// </summary>
        private float EstimateObstacleProximity(SpaceShipView self, GameData data)
        {
            if (self == null)
            {
                return 1f;
            }

            float nearest = float.MaxValue;

            if (data?.Asteroids != null)
            {
                foreach (var asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                    {
                        continue;
                    }

                    float distance = Vector2.Distance(self.Position, asteroid.Position) - (asteroid.Radius + self.Radius);
                    if (distance < nearest)
                    {
                        nearest = distance;
                    }
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

                    float distance = Vector2.Distance(self.Position, mine.Position) - (mine.ExplosionRadius + self.Radius);
                    if (distance < nearest)
                    {
                        nearest = distance;
                    }
                }
            }

            if (nearest == float.MaxValue)
            {
                return 1f;
            }

            return Mathf.Clamp01(nearest / 2.5f);
        }

        /// <summary>
        /// Tests whether a bullet is on a collision course with the ship.
        /// </summary>
        private bool IsBulletThreatening(SpaceShipView self, BulletView bullet)
        {
            Vector2 relativePosition = self.Position - bullet.Position;
            Vector2 relativeVelocity = self.Velocity - bullet.Velocity;

            Vector2 futureRelative = relativePosition + relativeVelocity * BulletLookAheadTime;
            float currentDistance = relativePosition.magnitude;
            float futureDistance = futureRelative.magnitude;

            if (futureDistance + 0.2f < currentDistance && futureDistance < BulletThreatRadius)
            {
                return true;
            }

            return false;
        }
    }
}
