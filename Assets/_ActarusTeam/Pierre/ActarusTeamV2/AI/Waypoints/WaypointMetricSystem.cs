using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    public struct WaypointMetrics
    {
        public WayPointView Waypoint;
        public int Index;
        public Vector2 Position;
        public float DistanceFactor;
        public float Danger;
        public float Safety;
        public float OpenArea;
        public float Centrality;
        public float TravelTime;
        public float TravelFactor;
        public float EnemyEta;
        public float ArrivalAdvantage;
        public float EnemyPressure;
        public float InterceptThreat;
        public float Control;
        public float CaptureSwing;
        public float Orientation;
        public float Approach;
    }
    
    public class WaypointMetricSystem
    {
        private readonly Dictionary<WayPointView, WaypointMetrics> _metrics = new();
        
        public Dictionary<WayPointView, WaypointMetrics> ComputeMetrics(SpaceShipView self, GameData data)
        {
            _metrics.Clear();

            if (self == null || data?.WayPoints == null)
                return _metrics;

            Vector2 mapCenter = AIUtility.ComputeMapCenter(data.WayPoints);

            for (int i = 0; i < data.WayPoints.Count; i++)
            {
                WayPointView waypoint = data.WayPoints[i];
                if (waypoint == null)
                    continue;

                Vector2 position = waypoint.Position;

                float danger = DangerFactor(self, data, position);
                float openArea = OpenAreaFactor(self, data, position);
                float distanceFactor = DistanceFactor(self.Position, position);
                float centrality = CentralityFactor(position, mapCenter);
                float enemyPressure = EnemyPressureField(data, self, position);
                float interceptThreat = EnemyInterceptFactor(self, data, waypoint);
                float control = ControlFactor(self, waypoint);
                float captureSwing = CaptureSwingFactor(self, waypoint);
                float orientation = OrientationFactorRelativeToEnemy(self, data, waypoint);
                float approach = ApproachAlignmentFactor(self, waypoint);

                float travelTime = EstimateTravelTime(self, data, waypoint, danger, openArea, enemyPressure);
                float travelFactor = ComputeTravelFactor(travelTime);

                float enemyEta = PredictEnemyArrival(self, data, waypoint);
                float arrivalAdvantage = ComputeArrivalAdvantage(travelTime, enemyEta);

                WaypointMetrics metrics = new WaypointMetrics
                {
                    Waypoint = waypoint,
                    Index = i,
                    Position = position,
                    DistanceFactor = distanceFactor,
                    Danger = danger,
                    Safety = 1f - danger,
                    OpenArea = openArea,
                    Centrality = centrality,
                    TravelTime = travelTime,
                    TravelFactor = travelFactor,
                    EnemyEta = enemyEta,
                    ArrivalAdvantage = arrivalAdvantage,
                    EnemyPressure = enemyPressure,
                    InterceptThreat = interceptThreat,
                    Control = control,
                    CaptureSwing = captureSwing,
                    Orientation = orientation,
                    Approach = approach
                };

                _metrics[waypoint] = metrics;
            }

            return _metrics;
        }

        private static float DistanceFactor(Vector2 origin, Vector2 target)
        {
            float distance = Vector2.Distance(origin, target);
            return 1f - Mathf.Clamp01(distance / AIConstants.DistanceNormalization);
        }

        private float DangerFactor(SpaceShipView self, GameData data, Vector2 position)
        {
            if (data == null)
                return 0f;

            float weightedDanger = 0f;
            float totalWeight = 0f;
            Vector2 origin = self != null ? self.Position : position;

            if (data.Mines != null)
            {
                foreach (MineView mine in data.Mines)
                {
                    if (mine == null)
                        continue;

                    float distance = Vector2.Distance(position, mine.Position);
                    float radius = mine.ExplosionRadius + AIConstants.MineDangerReach;
                    if (radius <= 0f)
                        continue;

                    float factor = 1f - Mathf.Clamp01(distance / radius);
                    if (factor <= 0f)
                        continue;

                    float weight = AIConstants.MineDangerWeight * (mine.IsActive ? 1f : 0.6f);
                    weightedDanger += factor * weight;
                    totalWeight += weight;
                }
            }

            if (data.Asteroids != null)
            {
                foreach (AsteroidView asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                        continue;

                    float safeRadius = asteroid.Radius + AIConstants.AsteroidBuffer;
                    if (safeRadius <= 0f)
                        continue;

                    float distanceToPath = AIUtility.DistancePointToSegment(asteroid.Position, origin, position);
                    float factor = 1f - Mathf.Clamp01((distanceToPath - asteroid.Radius) / Mathf.Max(safeRadius, 0.001f));
                    if (factor <= 0f)
                        continue;

                    float weight = AIConstants.AsteroidDangerWeight;
                    weightedDanger += factor * weight;
                    totalWeight += weight;
                }
            }

            if (data.Bullets != null)
            {
                foreach (BulletView bullet in data.Bullets)
                {
                    if (bullet == null)
                        continue;

                    Vector2 start = bullet.Position;
                    Vector2 velocity = bullet.Velocity;
                    float speed = velocity.magnitude;
                    if (speed <= 0.01f)
                    {
                        velocity = velocity == Vector2.zero ? new Vector2(1f, 0f) : velocity;
                        speed = BulletView.Speed;
                    }

                    Vector2 direction = velocity.normalized;
                    Vector2 end = start + direction * speed * AIConstants.DangerPredictionHorizon;
                    float distanceToPath = AIUtility.DistancePointToSegment(position, start, end);
                    float bulletInfluence = 1f - Mathf.Clamp01(distanceToPath / AIConstants.ProjectileAvoidanceRadius);
                    if (bulletInfluence <= 0f)
                        continue;

                    float forwardDanger = Mathf.Clamp01(Vector2.Dot(direction, (position - start).normalized));
                    float weight = AIConstants.ProjectileDangerWeight;
                    weightedDanger += Mathf.Lerp(bulletInfluence * 0.5f, bulletInfluence, forwardDanger) * weight;
                    totalWeight += weight;
                }
            }

            if (data.SpaceShips != null)
            {
                foreach (SpaceShipView ship in data.SpaceShips)
                {
                    if (ship == null || ship.Owner == self.Owner)
                        continue;

                    float lane = EnemyFireLaneContribution(ship, position);
                    if (lane <= 0f)
                        continue;

                    float weight = AIConstants.EnemyLaneDangerWeight * Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(ship.Energy));
                    weightedDanger += lane * weight;
                    totalWeight += weight;
                }
            }

            if (totalWeight <= Mathf.Epsilon)
                return 0f;

            return Mathf.Clamp01(weightedDanger / totalWeight);
        }

        private float EnemyFireLaneContribution(SpaceShipView enemy, Vector2 position)
        {
            Vector2 toTarget = position - enemy.Position;
            float distance = toTarget.magnitude;
            if (distance <= Mathf.Epsilon || distance > AIConstants.EnemyFireLaneReach)
                return 0f;

            Vector2 direction = toTarget / distance;
            Vector2 forward = AIUtility.GetForwardVector(enemy);

            float alignment = Mathf.Clamp01(Vector2.Dot(forward, direction));
            if (alignment <= 0f)
                return 0f;

            float distanceFactor = 1f - Mathf.Clamp01(distance / AIConstants.EnemyFireLaneReach);
            float velocityFactor = enemy.Velocity.sqrMagnitude > 0.001f
                ? Mathf.Clamp01((Vector2.Dot(enemy.Velocity.normalized, direction) + 1f) * 0.5f)
                : 0.5f;

            float lane = alignment * alignment * Mathf.Lerp(0.7f, 1.1f, velocityFactor) * distanceFactor;
            if (enemy.HasShot)
                lane += 0.1f * distanceFactor;

            return Mathf.Clamp01(lane);
        }

        private float OpenAreaFactor(SpaceShipView self, GameData data, Vector2 targetPosition)
        {
            if (self == null)
                return 0f;

            if (data?.Asteroids == null || data.Asteroids.Count == 0)
                return 1f;

            Vector2 origin = self.Position;
            float maxObstruction = 0f;

            foreach (AsteroidView asteroid in data.Asteroids)
            {
                if (asteroid == null)
                    continue;

                float distanceToPath = AIUtility.DistancePointToSegment(asteroid.Position, origin, targetPosition);
                float safeRadius = asteroid.Radius + AIConstants.AsteroidBuffer;
                if (safeRadius <= 0f)
                    continue;

                float obstruction = 1f - Mathf.Clamp01((distanceToPath - asteroid.Radius) / safeRadius);
                if (obstruction > maxObstruction)
                    maxObstruction = obstruction;
            }

            return 1f - maxObstruction;
        }

        private float ControlFactor(SpaceShipView self, WayPointView waypoint)
        {
            if (self == null || waypoint == null)
                return 0f;

            if (waypoint.Owner == self.Owner)
                return 0f;

            if (waypoint.Owner == -1)
                return 0.55f;

            return 1f;
        }

        private float CaptureSwingFactor(SpaceShipView self, WayPointView waypoint)
        {
            if (self == null || waypoint == null)
                return 0f;

            if (waypoint.Owner == self.Owner)
                return -0.35f;

            if (waypoint.Owner == -1)
                return 0.6f;

            return 1f;
        }

        private float OrientationFactorRelativeToEnemy(SpaceShipView self, GameData data, WayPointView waypoint)
        {
            if (self == null || data?.SpaceShips == null || waypoint == null)
                return 0.5f;

            SpaceShipView closestEnemy = null;
            float closestDistance = float.MaxValue;
            foreach (SpaceShipView ship in data.SpaceShips)
            {
                if (ship == null || ship.Owner == self.Owner)
                    continue;

                float distance = Vector2.Distance(waypoint.Position, ship.Position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestEnemy = ship;
                }
            }

            if (closestEnemy == null || closestDistance <= 0.1f)
                return 0.6f;

            Vector2 toWaypoint = (waypoint.Position - closestEnemy.Position).normalized;
            Vector2 enemyForward = AIUtility.GetForwardVector(closestEnemy);
            float dot = Vector2.Dot(enemyForward, toWaypoint);
            return Mathf.Clamp01(0.5f * (1f - dot));
        }

        private float ApproachAlignmentFactor(SpaceShipView self, WayPointView waypoint)
        {
            if (self == null || waypoint == null)
                return 0.5f;

            Vector2 desiredDirection = waypoint.Position - self.Position;
            if (desiredDirection.sqrMagnitude < Mathf.Epsilon)
                return 1f;

            desiredDirection.Normalize();
            Vector2 currentDirection = self.Velocity.sqrMagnitude > 0.01f
                ? self.Velocity.normalized
                : (self.LookAt.sqrMagnitude > Mathf.Epsilon ? self.LookAt.normalized : desiredDirection);

            float alignment = Mathf.Clamp01((Vector2.Dot(currentDirection, desiredDirection) + 1f) * 0.5f);
            return alignment;
        }

        private float EnemyPressureField(GameData data, SpaceShipView self, Vector2 position)
        {
            if (data?.SpaceShips == null)
                return 0f;

            float pressure = 0f;
            foreach (SpaceShipView ship in data.SpaceShips)
            {
                if (ship == null || ship.Owner == self.Owner)
                    continue;

                float distance = Vector2.Distance(position, ship.Position);
                if (distance > AIConstants.EnemyPressureRadius)
                    continue;

                Vector2 toPosition = (position - ship.Position).normalized;
                Vector2 forward = AIUtility.GetForwardVector(ship);
                float facing = Mathf.Clamp01((Vector2.Dot(forward, toPosition) + 1f) * 0.5f);
                float distFactor = 1f - Mathf.Clamp01(distance / AIConstants.EnemyPressureRadius);
                pressure += Mathf.Lerp(distFactor * 0.7f, distFactor, facing);
            }

            return Mathf.Clamp01(pressure);
        }

        private float EnemyInterceptFactor(SpaceShipView self, GameData data, WayPointView waypoint)
        {
            if (self == null || data?.SpaceShips == null || waypoint == null)
                return 0f;

            float highestThreat = 0f;
            Vector2 position = waypoint.Position;

            foreach (SpaceShipView ship in data.SpaceShips)
            {
                if (ship == null || ship.Owner == self.Owner)
                    continue;

                Vector2 toWaypoint = position - ship.Position;
                float distance = toWaypoint.magnitude;
                if (distance <= Mathf.Epsilon)
                    return 1f;

                float distanceFactor = 1f - Mathf.Clamp01(distance / AIConstants.EnemyInterceptRadius);
                if (distanceFactor <= 0f)
                    continue;

                Vector2 direction = toWaypoint / distance;
                Vector2 forward = AIUtility.GetForwardVector(ship);
                
                float facing = Mathf.Clamp01((Vector2.Dot(forward, direction) + 1f) * 0.5f);
                float velocityAlignment = ship.Velocity.sqrMagnitude > 0.001f ? Mathf.Clamp01((Vector2.Dot(ship.Velocity.normalized, direction) + 1f) * 0.5f) : 0.5f;
                float speedRatio = Mathf.Clamp01(ship.Velocity.magnitude / Mathf.Max(0.1f, ship.SpeedMax));

                float threat = distanceFactor * Mathf.Lerp(facing, facing * 1.2f, speedRatio) * Mathf.Lerp(0.75f, 1.1f, velocityAlignment);
                if (ship.HasShot)
                    threat += 0.08f;
                if (ship.HasDroppedMine)
                    threat += 0.05f;

                highestThreat = Mathf.Max(highestThreat, Mathf.Clamp01(threat));
            }

            return Mathf.Clamp01(highestThreat);
        }

        private float EstimateTravelTime(SpaceShipView ship, GameData data, WayPointView waypoint, float danger, float openArea, float enemyPressure)
        {
            if (ship == null || waypoint == null)
                return float.PositiveInfinity;

            _ = data;

            Vector2 toTarget = waypoint.Position - ship.Position;
            float distance = toTarget.magnitude;
            if (distance <= 0.05f)
                return 0f;

            Vector2 forward = AIUtility.GetForwardVector(ship);
            float alignment = Mathf.Clamp01((Vector2.Dot(forward, toTarget.normalized) + 1f) * 0.5f);
            float orientationMultiplier = Mathf.Lerp(1.15f, 0.75f, alignment);

            float velocityProjection = Vector2.Dot(ship.Velocity, toTarget.normalized);
            float velocityBoost = Mathf.Clamp(velocityProjection, -ship.SpeedMax, ship.SpeedMax) / Mathf.Max(0.1f, ship.SpeedMax);
            float velocityMultiplier = Mathf.Lerp(0.9f, 1.1f, (velocityBoost + 1f) * 0.5f);

            float energy = Mathf.Clamp01(ship.Energy);
            float energyMultiplier = Mathf.Lerp(0.75f, 1.1f, energy);

            float hazardPenalty = 1f + danger * 0.4f + (1f - openArea) * 0.25f + enemyPressure * 0.3f;
            if (ship.HitPenaltyCountdown > 0f)
                hazardPenalty += 0.3f;
            if (ship.StunPenaltyCountdown > 0f)
                hazardPenalty += 0.55f;

            float effectiveSpeed = Mathf.Max(0.2f, ship.SpeedMax * orientationMultiplier * velocityMultiplier * energyMultiplier);
            float travelTime = distance / effectiveSpeed;
            travelTime *= hazardPenalty;

            return travelTime;
        }

        private float PredictEnemyArrival(SpaceShipView self, GameData data, WayPointView waypoint)
        {
            if (self == null || data?.SpaceShips == null || waypoint == null)
                return float.PositiveInfinity;

            float bestEta = float.PositiveInfinity;
            foreach (SpaceShipView ship in data.SpaceShips)
            {
                if (ship == null || ship.Owner == self.Owner)
                    continue;

                float danger = DangerFactor(ship, data, waypoint.Position);
                float openArea = OpenAreaFactor(ship, data, waypoint.Position);
                float enemyPressure = EnemyPressureField(data, ship, waypoint.Position);
                float eta = EstimateTravelTime(ship, data, waypoint, danger, openArea, enemyPressure);
                if (eta < bestEta)
                    bestEta = eta;
            }

            return bestEta;
        }

        private float ComputeTravelFactor(float travelTime)
        {
            if (float.IsInfinity(travelTime))
                return -1f;

            float travelFactor = 1f - Mathf.Clamp01(travelTime / AIConstants.TravelTimeNormalization);
            if (travelTime < AIConstants.FastArrivalThreshold)
                travelFactor += 0.15f;
            else if (travelTime > AIConstants.SlowArrivalThreshold)
                travelFactor -= 0.2f;

            return Mathf.Clamp(travelFactor, -1f, 1f);
        }

        private float ComputeArrivalAdvantage(float selfTime, float enemyEta)
        {
            if (float.IsInfinity(selfTime))
                return -1f;

            if (float.IsInfinity(enemyEta))
                return Mathf.Clamp01((AIConstants.SlowArrivalThreshold - selfTime) / AIConstants.SlowArrivalThreshold);

            float delta = enemyEta - selfTime;
            if (delta >= 0f)
                return Mathf.Clamp01(delta / AIConstants.FastArrivalThreshold);

            return -Mathf.Clamp01((-delta) / AIConstants.SlowArrivalThreshold);
        }

        private float CentralityFactor(Vector2 position, Vector2 mapCenter)
        {
            float distance = Vector2.Distance(position, mapCenter);
            return 1f - Mathf.Clamp01(distance / AIConstants.CentralityNormalization);
        }
    }
}
