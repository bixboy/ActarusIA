using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Strategic selector that evaluates every waypoint and returns the highest value capture target.
    /// Tailored for a duel environment: one player controlled ship versus hostile ships only.
    /// </summary>
    public sealed class WaypointPrioritySystem
    {
        // ────────────────────────────── Tunable constants ──────────────────────────────
        private const float EvaluationInterval = 0.25f;
        private const float DistanceNormalization = 14f;
        private const float TravelTimeNormalization = 7.5f;
        private const float FastArrivalThreshold = 3f;
        private const float SlowArrivalThreshold = 10f;
        private const float CentralityNormalization = 12f;
        private const float MemoryCooldown = 8f;
        private const float EndgameTimeHorizon = 25f;

        private const float MineDangerReach = 4f;
        private const float AsteroidBuffer = 1.25f;
        private const float ProjectileAvoidanceRadius = 1.4f;
        private const float DangerPredictionHorizon = 3f;
        private const float EnemyPressureRadius = 8.5f;
        private const float EnemyInterceptRadius = 9.5f;
        private const float EnemyFireLaneReach = 11f;

        private const float MineDangerWeight = 1.1f;
        private const float AsteroidDangerWeight = 0.7f;
        private const float ProjectileDangerWeight = 1.25f;
        private const float EnemyLaneDangerWeight = 1.35f;

        private const float ControlWeight = 1.15f;
        private const float CaptureSwingWeight = 0.9f;
        private const float DistanceWeight = 0.45f;
        private const float SafetyWeight = 0.55f;
        private const float DangerPenaltyWeight = 0.35f;
        private const float OpenAreaWeight = 0.3f;
        private const float CentralityWeight = 0.25f;
        private const float TravelWeight = 0.65f;
        private const float EnemyArrivalWeight = 0.7f;
        private const float ContestWeight = 0.3f;
        private const float OrientationWeight = 0.18f;
        private const float ApproachWeight = 0.12f;
        private const float EnemyPressurePenalty = 0.55f;
        private const float EnemyInterceptPenalty = 0.45f;
        private const float EndgameSwingWeight = 0.4f;
        private const float UncontestedBonus = 0.18f;
        private const float QuickCaptureBonus = 0.25f;
        private const float SlowArrivalPenalty = 0.25f;

        private const float ScoreSmoothing = 0.55f;
        private const float ScoreMomentumWeight = 0.18f;
        private const float MemoryPenaltyMultiplier = 0.5f;
        private const float CurrentTargetBonus = 0.2f;

        private const float TargetSwitchBias = 0.22f;
        private const float TargetSwitchRatioLocked = 0.18f;
        private const float TargetSwitchRatioFree = 0.08f;
        private const float TargetEtaAdvantage = 0.7f;
        private const float TargetHoldMin = 1.2f;
        private const float TargetHoldMax = 2.6f;

        // Debug display
        private const float DebugSphereSize = 0.3f;
        private const float DebugTextSize = 0.8f;
        private static readonly Color DebugLineColor = new Color(0.2f, 0.9f, 0.2f, 0.8f);
        private static readonly Color DebugSphereColor = new Color(0.4f, 1f, 0.4f, 0.8f);

        // ────────────────────────────── Runtime caches ──────────────────────────────
        private readonly Dictionary<int, float> _lastVisited = new();
        private readonly Dictionary<int, float> _smoothedScores = new();
        private readonly Dictionary<int, float> _rawScores = new();
        private readonly Dictionary<WayPointView, WaypointMetrics> _metricsCache = new();

        private WayPointView _cachedBestWaypoint;
        private float _cachedBestScore;
        private float _cachedBestEta;

        private WayPointView _currentTarget;
        private float _currentTargetScore = float.MinValue;
        private float _lastTargetUpdateTime;
        private float _targetLockUntil;
        private float _nextEvaluationTime;

        private struct WaypointMetrics
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

        // ────────────────────────────── Public API ──────────────────────────────
        public WayPointView SelectBestWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data?.WayPoints == null || data.WayPoints.Count == 0)
                return null;

            if (Time.time < _nextEvaluationTime && _cachedBestWaypoint != null)
            {
                DrawDebug(self, _cachedBestWaypoint, _cachedBestEta, _cachedBestScore);
                return _cachedBestWaypoint;
            }

            RebuildMetrics(self, data);

            float deficitFactor = ScoreDeficitFactor(self, data); // 0 (ahead) → 1 (behind)
            float aggressionBias = Mathf.Lerp(0.75f, 1.4f, deficitFactor);
            float cautionBias = Mathf.Lerp(1.35f, 0.8f, deficitFactor);
            float endgameUrgency = EndgameUrgency(data);

            Dictionary<WayPointView, float> scoredTargets = new();
            WayPointView evaluatedBest = EvaluateWaypoints(
                self,
                data,
                deficitFactor,
                aggressionBias,
                cautionBias,
                endgameUrgency,
                scoredTargets,
                out float bestScore,
                out float bestEta);

            ApplyHysteresis(scoredTargets, evaluatedBest, bestScore, bestEta);

            _nextEvaluationTime = Time.time + EvaluationInterval;

            DrawDebug(self, _cachedBestWaypoint, _cachedBestEta, _cachedBestScore);
            return _cachedBestWaypoint;
        }

        // ────────────────────────────── Core evaluation ──────────────────────────────
        private void RebuildMetrics(SpaceShipView self, GameData data)
        {
            _metricsCache.Clear();

            if (self == null || data?.WayPoints == null)
                return;

            Vector2 mapCenter = ComputeMapCenter(data.WayPoints);

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

                _metricsCache[waypoint] = metrics;
            }
        }

        private WayPointView EvaluateWaypoints(
            SpaceShipView self,
            GameData data,
            float deficitFactor,
            float aggressionBias,
            float cautionBias,
            float endgameUrgency,
            Dictionary<WayPointView, float> scoredTargets,
            out float bestScore,
            out float bestEta)
        {
            _ = self;
            _ = data;

            bestScore = float.MinValue;
            bestEta = float.PositiveInfinity;
            WayPointView bestWaypoint = null;

            foreach (WaypointMetrics metrics in _metricsCache.Values)
            {
                float rawScore = EvaluateWaypointScore(metrics, deficitFactor, aggressionBias, cautionBias, endgameUrgency);

                float momentumBonus = 0f;
                if (_rawScores.TryGetValue(metrics.Index, out float previousRaw))
                    momentumBonus = (rawScore - previousRaw) * ScoreMomentumWeight;
                _rawScores[metrics.Index] = rawScore;

                float adjustedScore = rawScore + momentumBonus;
                adjustedScore *= MemoryMultiplier(metrics.Index, metrics.Waypoint);
                if (metrics.Waypoint == _currentTarget)
                    adjustedScore += CurrentTargetBonus;

                float smoothedScore = SmoothScore(metrics.Index, adjustedScore);
                scoredTargets[metrics.Waypoint] = smoothedScore;

                if (smoothedScore > bestScore)
                {
                    bestScore = smoothedScore;
                    bestWaypoint = metrics.Waypoint;
                    bestEta = metrics.TravelTime;
                }
            }

            return bestWaypoint;
        }

        private float EvaluateWaypointScore(
            WaypointMetrics metrics,
            float deficitFactor,
            float aggressionBias,
            float cautionBias,
            float endgameUrgency)
        {
            float scoreboardBias = Mathf.Lerp(0.85f, 1.35f, deficitFactor);
            float distanceBias = Mathf.Lerp(1.15f, 0.85f, deficitFactor);
            float safetyBias = Mathf.Lerp(1.3f, 0.75f, deficitFactor);
            float timeBias = Mathf.Lerp(1f, 1.2f, endgameUrgency);
            float centralityBias = Mathf.Lerp(0.9f, 1.15f, endgameUrgency);
            float contestBias = Mathf.Lerp(0.85f, 1.25f, deficitFactor);

            float score = 0f;
            score += metrics.Control * ControlWeight * scoreboardBias;                                           // prefer hostile points
            score += metrics.CaptureSwing * CaptureSwingWeight * scoreboardBias;                                // capture swing towards victory
            score += metrics.DistanceFactor * DistanceWeight * distanceBias;                                    // shorter travel preferred when ahead
            score += metrics.Safety * SafetyWeight * safetyBias;                                                // avoid lethal zones when leading
            score += metrics.OpenArea * OpenAreaWeight * cautionBias;                                           // easier approach corridors
            score += metrics.Centrality * CentralityWeight * centralityBias;                                    // central map leverage
            score += metrics.TravelFactor * TravelWeight * timeBias * aggressionBias;                           // fast captures prioritized when behind
            score += metrics.ArrivalAdvantage * EnemyArrivalWeight * aggressionBias;                            // beat enemies to the point
            score += metrics.Orientation * OrientationWeight;                                                   // attack angles on enemies guarding the point
            score += metrics.Approach * ApproachWeight;                                                         // require less turning/boosting effort

            score -= metrics.Danger * DangerPenaltyWeight * cautionBias;                                        // punish risky pockets
            score -= metrics.EnemyPressure * EnemyPressurePenalty * cautionBias;                                // avoid enemy swarms unless desperate
            score -= metrics.InterceptThreat * EnemyInterceptPenalty * cautionBias;                             // respect interception lines

            if (metrics.TravelTime < FastArrivalThreshold)
                score += QuickCaptureBonus * aggressionBias;
            else if (metrics.TravelTime > SlowArrivalThreshold)
                score -= SlowArrivalPenalty * safetyBias;

            if (float.IsInfinity(metrics.EnemyEta))
            {
                score += UncontestedBonus * scoreboardBias;                                                     // free capture opportunity
            }
            else
            {
                float contest = 1f - Mathf.Clamp01(metrics.EnemyEta / TravelTimeNormalization);
                score += contest * ContestWeight * contestBias;                                                 // race enemy arrivals when close
            }

            score += metrics.CaptureSwing * EndgameSwingWeight * Mathf.Lerp(0.8f, 1.3f, endgameUrgency);        // late game: value decisive swing

            return score;
        }

        // ────────────────────────────── Factor calculators ──────────────────────────────
        private float DistanceFactor(Vector2 origin, Vector2 target)
        {
            float distance = Vector2.Distance(origin, target);
            return 1f - Mathf.Clamp01(distance / DistanceNormalization);
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
                    float radius = mine.ExplosionRadius + MineDangerReach;
                    if (radius <= 0f)
                        continue;

                    float factor = 1f - Mathf.Clamp01(distance / radius);
                    if (factor <= 0f)
                        continue;

                    float weight = MineDangerWeight * (mine.IsActive ? 1f : 0.6f);
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

                    float safeRadius = asteroid.Radius + AsteroidBuffer;
                    if (safeRadius <= 0f)
                        continue;

                    float distanceToPath = DistancePointToSegment(asteroid.Position, origin, position);
                    float factor = 1f - Mathf.Clamp01((distanceToPath - asteroid.Radius) / Mathf.Max(safeRadius, 0.001f));
                    if (factor <= 0f)
                        continue;

                    float weight = AsteroidDangerWeight;
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
                    Vector2 end = start + direction * speed * DangerPredictionHorizon;
                    float distanceToPath = DistancePointToSegment(position, start, end);
                    float bulletInfluence = 1f - Mathf.Clamp01(distanceToPath / ProjectileAvoidanceRadius);
                    if (bulletInfluence <= 0f)
                        continue;

                    float forwardDanger = Mathf.Clamp01(Vector2.Dot(direction, (position - start).normalized));
                    float weight = ProjectileDangerWeight;
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

                    float weight = EnemyLaneDangerWeight * Mathf.Lerp(0.85f, 1.15f, Mathf.Clamp01(ship.Energy));
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
            if (distance <= Mathf.Epsilon || distance > EnemyFireLaneReach)
                return 0f;

            Vector2 direction = toTarget / distance;
            Vector2 forward = enemy.LookAt.sqrMagnitude > Mathf.Epsilon ? enemy.LookAt.normalized : OrientationToVector(enemy.Orientation);

            float alignment = Mathf.Clamp01(Vector2.Dot(forward, direction));
            if (alignment <= 0f)
                return 0f;

            float distanceFactor = 1f - Mathf.Clamp01(distance / EnemyFireLaneReach);
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

                float distanceToPath = DistancePointToSegment(asteroid.Position, origin, targetPosition);
                float safeRadius = asteroid.Radius + AsteroidBuffer;
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
                return -0.35f;   // staying on owned points gives little value

            if (waypoint.Owner == -1)
                return 0.6f;     // neutral points still meaningful swing

            return 1f;           // enemy owned points provide full swing
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
            Vector2 enemyForward = OrientationToVector(closestEnemy.Orientation);
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
                if (distance > EnemyPressureRadius)
                    continue;

                Vector2 toPosition = (position - ship.Position).normalized;
                Vector2 forward = ship.LookAt.sqrMagnitude > Mathf.Epsilon ? ship.LookAt.normalized : OrientationToVector(ship.Orientation);
                float facing = Mathf.Clamp01((Vector2.Dot(forward, toPosition) + 1f) * 0.5f);
                float distFactor = 1f - Mathf.Clamp01(distance / EnemyPressureRadius);
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

                float distanceFactor = 1f - Mathf.Clamp01(distance / EnemyInterceptRadius);
                if (distanceFactor <= 0f)
                    continue;

                Vector2 direction = toWaypoint / distance;
                Vector2 forward = ship.LookAt.sqrMagnitude > Mathf.Epsilon ? ship.LookAt.normalized : OrientationToVector(ship.Orientation);
                float facing = Mathf.Clamp01((Vector2.Dot(forward, direction) + 1f) * 0.5f);
                float velocityAlignment = ship.Velocity.sqrMagnitude > 0.001f
                    ? Mathf.Clamp01((Vector2.Dot(ship.Velocity.normalized, direction) + 1f) * 0.5f)
                    : 0.5f;
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

        private float EstimateTravelTime(
            SpaceShipView ship,
            GameData data,
            WayPointView waypoint,
            float danger,
            float openArea,
            float enemyPressure)
        {
            if (ship == null || waypoint == null)
                return float.PositiveInfinity;

            _ = data; // reserved for future path sampling adjustments

            Vector2 toTarget = waypoint.Position - ship.Position;
            float distance = toTarget.magnitude;
            if (distance <= 0.05f)
                return 0f;

            Vector2 forward = ship.LookAt.sqrMagnitude > Mathf.Epsilon ? ship.LookAt.normalized : OrientationToVector(ship.Orientation);
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

            float travelFactor = 1f - Mathf.Clamp01(travelTime / TravelTimeNormalization);
            if (travelTime < FastArrivalThreshold)
                travelFactor += 0.15f;
            else if (travelTime > SlowArrivalThreshold)
                travelFactor -= 0.2f;

            return Mathf.Clamp(travelFactor, -1f, 1f);
        }

        private float ComputeArrivalAdvantage(float selfTime, float enemyEta)
        {
            if (float.IsInfinity(selfTime))
                return -1f;

            if (float.IsInfinity(enemyEta))
                return Mathf.Clamp01((SlowArrivalThreshold - selfTime) / SlowArrivalThreshold);

            float delta = enemyEta - selfTime;
            if (delta >= 0f)
                return Mathf.Clamp01(delta / FastArrivalThreshold);

            return -Mathf.Clamp01((-delta) / SlowArrivalThreshold);
        }

        private float MemoryMultiplier(int waypointIndex, WayPointView waypoint)
        {
            if (waypoint != null && waypoint == _currentTarget)
                return 1f;

            if (_lastVisited.TryGetValue(waypointIndex, out float lastTime))
            {
                float elapsed = Time.time - lastTime;
                if (elapsed < MemoryCooldown)
                {
                    float t = Mathf.Clamp01(elapsed / MemoryCooldown);
                    return Mathf.Lerp(MemoryPenaltyMultiplier, 1f, t);
                }
            }

            return 1f;
        }

        private float SmoothScore(int waypointIndex, float value)
        {
            if (_smoothedScores.TryGetValue(waypointIndex, out float previous))
            {
                float smoothed = Mathf.Lerp(previous, value, ScoreSmoothing);
                _smoothedScores[waypointIndex] = smoothed;
                return smoothed;
            }

            _smoothedScores[waypointIndex] = value;
            return value;
        }

        private float CentralityFactor(Vector2 position, Vector2 mapCenter)
        {
            float distance = Vector2.Distance(position, mapCenter);
            return 1f - Mathf.Clamp01(distance / CentralityNormalization);
        }

        private float ScoreDeficitFactor(SpaceShipView self, GameData data)
        {
            if (self == null || GameManager.Instance == null)
                return 0.5f;

            int myScore = GameManager.Instance.GetScoreForPlayer(self.Owner);
            int bestOpponentScore = myScore;

            if (data?.SpaceShips != null)
            {
                foreach (SpaceShipView ship in data.SpaceShips)
                {
                    if (ship == null || ship.Owner == self.Owner)
                        continue;

                    int score = GameManager.Instance.GetScoreForPlayer(ship.Owner);
                    if (score > bestOpponentScore)
                        bestOpponentScore = score;
                }
            }

            int totalWaypoints = data?.WayPoints?.Count ?? 1;
            int scoreDiff = bestOpponentScore - myScore;
            float normalized = Mathf.Clamp01((scoreDiff / Mathf.Max(1f, totalWaypoints)) * 0.5f + 0.5f);
            return normalized;
        }

        private float EndgameUrgency(GameData data)
        {
            if (data == null)
                return 0f;

            float timeLeft = Mathf.Max(0f, data.timeLeft);
            if (EndgameTimeHorizon <= Mathf.Epsilon)
                return 0f;

            return Mathf.Clamp01(1f - timeLeft / EndgameTimeHorizon);
        }

        private Vector2 ComputeMapCenter(List<WayPointView> waypoints)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;
            foreach (WayPointView waypoint in waypoints)
            {
                if (waypoint == null)
                    continue;
                sum += waypoint.Position;
                count++;
            }

            return count > 0 ? sum / count : Vector2.zero;
        }

        private void ApplyHysteresis(
            Dictionary<WayPointView, float> scoredTargets,
            WayPointView evaluatedBest,
            float evaluatedScore,
            float evaluatedEta)
        {
            WayPointView previousTarget = _currentTarget;
            float previousScore = _currentTargetScore;
            float now = Time.time;

            if (previousTarget != null)
            {
                if (!_metricsCache.ContainsKey(previousTarget) || !scoredTargets.TryGetValue(previousTarget, out float storedScore))
                {
                    previousTarget = null;
                    previousScore = float.MinValue;
                }
                else
                {
                    previousScore = storedScore;
                }
            }

            WayPointView finalTarget = evaluatedBest;
            float finalScore = evaluatedScore;
            float finalEta = evaluatedEta;

            if (previousTarget != null)
            {
                bool keepPrevious = false;
                if (finalTarget == null)
                {
                    keepPrevious = true;
                }
                else if (finalTarget == previousTarget)
                {
                    keepPrevious = true;
                    finalScore = previousScore;
                    if (_metricsCache.TryGetValue(previousTarget, out WaypointMetrics prevMetrics))
                        finalEta = prevMetrics.TravelTime;
                }
                else
                {
                    float improvement = finalScore - previousScore;
                    float ratio = previousScore <= 0f ? improvement : improvement / Mathf.Max(Mathf.Abs(previousScore), 0.0001f);
                    bool lockActive = now < _targetLockUntil;
                    float improvementThreshold = lockActive ? TargetSwitchBias : TargetSwitchBias * 0.5f;
                    float ratioThreshold = lockActive ? TargetSwitchRatioLocked : TargetSwitchRatioFree;

                    float previousEta = float.PositiveInfinity;
                    if (_metricsCache.TryGetValue(previousTarget, out WaypointMetrics prevMetrics))
                        previousEta = prevMetrics.TravelTime;
                    bool etaWin = finalEta + TargetEtaAdvantage < previousEta;

                    if (improvement < improvementThreshold && !etaWin)
                        keepPrevious = true;
                    else if (ratio < ratioThreshold && !etaWin)
                        keepPrevious = true;
                    else if (now - _lastTargetUpdateTime < TargetHoldMin * 0.5f && !etaWin)
                        keepPrevious = true;
                }

                if (keepPrevious)
                {
                    finalTarget = previousTarget;
                    finalScore = previousScore;
                    if (_metricsCache.TryGetValue(previousTarget, out WaypointMetrics prevMetrics))
                        finalEta = prevMetrics.TravelTime;
                }
            }

            _cachedBestWaypoint = finalTarget;
            _cachedBestScore = finalScore;
            _cachedBestEta = finalEta;

            if (finalTarget != null)
            {
                bool targetChanged = finalTarget != previousTarget;
                if (targetChanged && previousTarget != null)
                    MarkVisited(previousTarget);

                if (targetChanged)
                {
                    _currentTarget = finalTarget;
                    _currentTargetScore = finalScore;
                    _lastTargetUpdateTime = now;
                    _targetLockUntil = now + Mathf.Lerp(TargetHoldMin, TargetHoldMax, Mathf.Clamp01(finalScore));
                    if (_metricsCache.TryGetValue(finalTarget, out WaypointMetrics metrics))
                        Debug.Log($"Selected target: WP#{metrics.Index} ETA={finalEta:F1}s Score={finalScore:F2}");
                }
                else
                {
                    _currentTargetScore = finalScore;
                }
            }
            else if (previousTarget != null)
            {
                MarkVisited(previousTarget);
                _currentTarget = null;
                _currentTargetScore = float.MinValue;
                _targetLockUntil = 0f;
            }
        }

        private void MarkVisited(WayPointView waypoint)
        {
            if (waypoint == null)
                return;

            if (_metricsCache.TryGetValue(waypoint, out WaypointMetrics metrics))
                _lastVisited[metrics.Index] = Time.time;
        }

        // ────────────────────────────── Math helpers ──────────────────────────────
        private static Vector2 OrientationToVector(float orientationDegrees)
        {
            float radians = orientationDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float segmentLengthSq = segment.sqrMagnitude;
            if (segmentLengthSq <= Mathf.Epsilon)
                return Vector2.Distance(point, segmentStart);

            float projection = Vector2.Dot(point - segmentStart, segment) / segmentLengthSq;
            projection = Mathf.Clamp01(projection);
            Vector2 closest = segmentStart + projection * segment;
            return Vector2.Distance(point, closest);
        }

        // ────────────────────────────── Debug rendering ──────────────────────────────
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void DrawDebug(SpaceShipView self, WayPointView waypoint, float eta, float score)
        {
            if (self == null || waypoint == null)
                return;

            const float textOffset = 0.75f;
            const float lineDuration = 0.25f;

            Debug.DrawLine(self.Position, waypoint.Position, DebugLineColor, lineDuration);
            DebugExtension.DrawSphere(waypoint.Position, DebugSphereColor, DebugSphereSize);
            DebugExtension.DrawText(
                waypoint.Position + Vector2.up * textOffset,
                $"ETA={eta:F1}s | SCORE={score:F2}",
                Color.white,
                DebugTextSize,
                lineDuration);
        }
    }
}
