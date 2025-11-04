using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Strategic selector that evaluates capture waypoints and returns the most valuable target.
    /// Tailored for a duel situation (one player ship versus enemy ships).
    /// </summary>
    public sealed class WaypointPrioritySystem
    {
        // ────────────────────────────── Tunable constants ──────────────────────────────
        private const float EvaluationInterval = 0.25f;
        private const float ClusterDistanceThreshold = 3.5f;

        private const float DistanceNormalization = 14f;
        private const float TravelTimeNormalization = 7.5f;
        private const float FastArrivalThreshold = 3f;
        private const float SlowArrivalThreshold = 10f;
        private const float CentralityNormalization = 12f;
        private const float MemoryCooldown = 8f;

        private const float MineDangerReach = 4f;
        private const float AsteroidBuffer = 1.25f;
        private const float ProjectileAvoidanceRadius = 1.4f;
        private const float DangerPredictionHorizon = 2.75f;
        private const float EnemyPressureRadius = 8f;

        // Group weights
        private const float GroupEnemyControlWeight = 0.85f;
        private const float GroupNeutralControlWeight = 0.55f;
        private const float GroupDistanceWeight = 0.55f;
        private const float GroupDangerWeight = 0.65f;
        private const float GroupOpenAreaWeight = 0.35f;
        private const float GroupEnemyArrivalWeight = 0.6f;
        private const float GroupCentralityWeight = 0.25f;
        private const float GroupEnemyPressurePenalty = 0.4f;

        // Individual weights
        private const float IndividualControlWeight = 0.6f;
        private const float IndividualDistanceWeight = 0.32f;
        private const float IndividualSafetyWeight = 0.28f;
        private const float IndividualOpenAreaWeight = 0.18f;
        private const float IndividualOrientationWeight = 0.12f;
        private const float IndividualApproachWeight = 0.1f;
        private const float IndividualTravelWeight = 0.35f;
        private const float IndividualEnemyArrivalWeight = 0.42f;
        private const float IndividualCentralityWeight = 0.15f;

        private const float MemoryPenaltyMultiplier = 0.6f;

        // Debug display
        private const float DebugSphereSize = 0.3f;
        private const float DebugTextSize = 0.8f;
        private static readonly Color DebugLineColor = new Color(0.2f, 0.9f, 0.2f, 0.8f);
        private static readonly Color DebugSphereColor = new Color(0.4f, 1f, 0.4f, 0.8f);

        // ────────────────────────────── Runtime caches ──────────────────────────────
        private readonly Dictionary<int, float> _lastVisited = new();
        private readonly Dictionary<WayPointView, WaypointMetrics> _metricsCache = new();

        private WayPointView _cachedBestWaypoint;
        private float _cachedBestScore;
        private float _cachedBestEta;
        private float _nextEvaluationTime;

        private struct WaypointMetrics
        {
            public WayPointView Waypoint;
            public int Index;
            public Vector2 Position;
            public float DistanceFactor;
            public float Danger;
            public float OpenArea;
            public float Centrality;
            public float TravelTime;
            public float TravelFactor;
            public float EnemyEta;
            public float ArrivalAdvantage; // [-1,1] positive when we beat enemies
        }

        // ────────────────────────────── Public API ──────────────────────────────
        public WayPointView SelectBestWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data?.WayPoints == null || data.WayPoints.Count == 0)
                return null;

            // Rate-limit the heavy evaluation.
            if (Time.time < _nextEvaluationTime && _cachedBestWaypoint != null)
            {
                DrawDebug(self, _cachedBestWaypoint, _cachedBestEta, _cachedBestScore);
                return _cachedBestWaypoint;
            }

            RebuildMetrics(self, data);

            float deficitFactor = ScoreDeficitFactor(self, data); // 0 (ahead) → 1 (behind)
            float aggressionBias = Mathf.Lerp(0.8f, 1.3f, deficitFactor);

            List<List<WayPointView>> groups = ClusterWaypoints(data.WayPoints);

            _cachedBestWaypoint = null;
            _cachedBestScore = float.MinValue;
            _cachedBestEta = float.PositiveInfinity;

            foreach (List<WayPointView> group in groups)
            {
                if (group == null || group.Count == 0)
                    continue;

                float groupScore = ComputeGroupScore(self, data, group, aggressionBias, deficitFactor);
                WayPointView candidate = SelectBestInGroup(self, data, group, aggressionBias, deficitFactor, out float candidateScore, out float eta);
                if (candidate == null)
                    continue;

                float combinedScore = groupScore + candidateScore;
                if (combinedScore <= _cachedBestScore)
                    continue;

                _cachedBestScore = combinedScore;
                _cachedBestWaypoint = candidate;
                _cachedBestEta = eta;
            }

            _nextEvaluationTime = Time.time + EvaluationInterval;

            if (_cachedBestWaypoint != null)
            {
                int waypointIndex = _metricsCache[_cachedBestWaypoint].Index;
                _lastVisited[waypointIndex] = Time.time;
                Debug.Log($"Selected target: WP#{waypointIndex} ETA={_cachedBestEta:F1}s Score={_cachedBestScore:F2}");
            }

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

                float danger = DangerFactor(data, position);
                float openArea = OpenAreaFactor(self, data, position);
                float distanceFactor = DistanceFactor(self.Position, position);
                float centrality = CentralityFactor(position, mapCenter);
                float travelTime = EstimateTravelTime(self, data, waypoint, danger, openArea);
                float travelFactor = ComputeTravelFactor(travelTime);
                float enemyEta = PredictEnemyArrival(self, data, waypoint, danger, openArea);
                float arrivalAdvantage = ComputeArrivalAdvantage(travelTime, enemyEta);

                _metricsCache[waypoint] = new WaypointMetrics
                {
                    Waypoint = waypoint,
                    Index = i,
                    Position = position,
                    DistanceFactor = distanceFactor,
                    Danger = danger,
                    OpenArea = openArea,
                    Centrality = centrality,
                    TravelTime = travelTime,
                    TravelFactor = travelFactor,
                    EnemyEta = enemyEta,
                    ArrivalAdvantage = arrivalAdvantage
                };
            }
        }

        private float ComputeGroupScore(
            SpaceShipView self,
            GameData data,
            List<WayPointView> group,
            float aggressionBias,
            float deficitFactor)
        {
            int enemyOwned = 0;
            int neutral = 0;
            int selfOwned = 0;
            float distanceSum = 0f;
            float dangerSum = 0f;
            float openAreaSum = 0f;
            float centralitySum = 0f;
            float bestArrivalAdvantage = float.NegativeInfinity;
            float bestTravel = float.PositiveInfinity;
            Vector2 centroid = Vector2.zero;

            foreach (WayPointView waypoint in group)
            {
                if (waypoint == null || !_metricsCache.TryGetValue(waypoint, out WaypointMetrics metrics))
                    continue;

                if (waypoint.Owner == self.Owner) selfOwned++;
                else if (waypoint.Owner == -1) neutral++;
                else enemyOwned++;

                distanceSum += metrics.DistanceFactor;
                dangerSum += metrics.Danger;
                openAreaSum += metrics.OpenArea;
                centralitySum += metrics.Centrality;
                bestArrivalAdvantage = Mathf.Max(bestArrivalAdvantage, metrics.ArrivalAdvantage);
                bestTravel = Mathf.Min(bestTravel, metrics.TravelTime);
                centroid += metrics.Position;
            }

            int count = group.Count;
            if (count == 0)
                return float.MinValue;

            centroid /= count;

            float avgDistance = distanceSum / count;
            float avgDanger = dangerSum / count;
            float avgOpen = openAreaSum / count;
            float avgCentrality = centralitySum / count;

            float enemyRatio = count > 0 ? (float)enemyOwned / count : 0f;
            float neutralRatio = count > 0 ? (float)neutral / count : 0f;
            float selfRatio = count > 0 ? (float)selfOwned / count : 0f;

            float enemyPressure = EnemyPressure(data, self, centroid);

            float score = 0f;
            score += enemyRatio * GroupEnemyControlWeight * Mathf.Lerp(0.9f, 1.25f, deficitFactor); // more enemy focus when behind
            score += neutralRatio * GroupNeutralControlWeight * Mathf.Lerp(1.2f, 0.85f, deficitFactor); // secure neutrals when ahead
            score += avgDistance * GroupDistanceWeight * Mathf.Lerp(1.2f, 0.85f, deficitFactor);      // stick to nearby points when leading
            score += avgOpen * GroupOpenAreaWeight * Mathf.Lerp(1.15f, 0.85f, deficitFactor);         // safer lanes when calm
            score += avgCentrality * GroupCentralityWeight * Mathf.Lerp(0.9f, 1.1f, deficitFactor);    // center control in comeback
            score += Mathf.Clamp(bestArrivalAdvantage, -1f, 1f) * GroupEnemyArrivalWeight * aggressionBias;

            score -= avgDanger * GroupDangerWeight * Mathf.Lerp(1.25f, 0.7f, deficitFactor);           // very cautious when leading
            score -= enemyPressure * GroupEnemyPressurePenalty * Mathf.Lerp(1.1f, 0.65f, deficitFactor);
            score -= selfRatio * Mathf.Lerp(0.6f, 0.2f, deficitFactor);                                // abandon owned points when possible

            // Slight preference to groups we can reach quickly
            if (!float.IsInfinity(bestTravel))
            {
                float travelFactor = 1f - Mathf.Clamp01(bestTravel / TravelTimeNormalization);
                score += travelFactor * Mathf.Lerp(0.35f, 0.55f, deficitFactor);
            }

            return score;
        }

        private WayPointView SelectBestInGroup(
            SpaceShipView self,
            GameData data,
            List<WayPointView> group,
            float aggressionBias,
            float deficitFactor,
            out float bestScore,
            out float eta)
        {
            bestScore = float.MinValue;
            eta = float.PositiveInfinity;
            WayPointView bestWaypoint = null;

            foreach (WayPointView waypoint in group)
            {
                if (waypoint == null || !_metricsCache.TryGetValue(waypoint, out WaypointMetrics metrics))
                    continue;

                float control = ControlFactor(self, waypoint);
                float safety = 1f - metrics.Danger;
                float orientation = OrientationFactorRelativeToEnemy(self, data, waypoint);
                float approach = ApproachAlignmentFactor(self, waypoint);

                float score = 0f;
                score += control * IndividualControlWeight * aggressionBias;                                              // enemy > neutral > owned
                score += metrics.DistanceFactor * IndividualDistanceWeight * Mathf.Lerp(1.2f, 0.9f, deficitFactor);        // prioritize nearby when leading
                score += safety * IndividualSafetyWeight * Mathf.Lerp(1.35f, 0.8f, deficitFactor);                         // avoid risk when in front
                score += metrics.OpenArea * IndividualOpenAreaWeight * Mathf.Lerp(1.2f, 0.85f, deficitFactor);              // prefer clean lanes when calm
                score += orientation * IndividualOrientationWeight;                                                    // attack enemies from blind spots
                score += approach * IndividualApproachWeight;                                                          // less steering corrections
                score += metrics.TravelFactor * IndividualTravelWeight * Mathf.Lerp(0.9f, 1.25f, deficitFactor);          // push for fast captures when behind
                score += metrics.ArrivalAdvantage * IndividualEnemyArrivalWeight * Mathf.Lerp(0.85f, 1.2f, deficitFactor); // beat enemies to point
                score += metrics.Centrality * IndividualCentralityWeight * Mathf.Lerp(0.9f, 1.1f, deficitFactor);          // reinforce central map presence

                float memoryMultiplier = MemoryMultiplier(metrics.Index);
                score *= memoryMultiplier;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestWaypoint = waypoint;
                    eta = metrics.TravelTime;
                }
            }

            return bestWaypoint;
        }

        // ────────────────────────────── Scoring helpers ──────────────────────────────
        private float DistanceFactor(Vector2 origin, Vector2 target)
        {
            float distance = Vector2.Distance(origin, target);
            return 1f - Mathf.Clamp01(distance / DistanceNormalization);
        }

        private float DangerFactor(GameData data, Vector2 position)
        {
            if (data == null)
                return 0f;

            float accumulated = 0f;
            int samples = 0;

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

                    accumulated += mine.IsActive ? factor : factor * 0.5f;
                    samples++;
                }
            }

            if (data.Asteroids != null)
            {
                foreach (AsteroidView asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                        continue;

                    float distance = Vector2.Distance(position, asteroid.Position);
                    float safetyRadius = asteroid.Radius + AsteroidBuffer;
                    if (safetyRadius <= 0f)
                        continue;

                    float factor = 1f - Mathf.Clamp01((distance - asteroid.Radius) / safetyRadius);
                    if (factor <= 0f)
                        continue;

                    accumulated += factor * 0.8f;
                    samples++;
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
                    accumulated += Mathf.Lerp(bulletInfluence * 0.5f, bulletInfluence, forwardDanger);
                    samples++;
                }
            }

            if (samples == 0)
                return 0f;

            return Mathf.Clamp01(accumulated / samples);
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
                return 0.5f;

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
                return 0.5f;

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

        private float EstimateTravelTime(SpaceShipView ship, GameData data, WayPointView waypoint, float danger, float openArea)
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
            float orientationMultiplier = Mathf.Lerp(1.2f, 0.75f, alignment);

            float velocityProjection = Vector2.Dot(ship.Velocity, toTarget.normalized);
            float velocityBoost = Mathf.Clamp(velocityProjection, -ship.SpeedMax, ship.SpeedMax) / Mathf.Max(0.1f, ship.SpeedMax);
            float velocityMultiplier = Mathf.Lerp(0.9f, 1.1f, (velocityBoost + 1f) * 0.5f);

            float energy = Mathf.Clamp01(ship.Energy);
            float energyMultiplier = Mathf.Lerp(0.75f, 1.15f, energy);

            float hazardPenalty = 1f + danger * 0.35f + (1f - openArea) * 0.25f;
            if (ship.HitPenaltyCountdown > 0f)
                hazardPenalty += 0.3f;
            if (ship.StunPenaltyCountdown > 0f)
                hazardPenalty += 0.5f;

            float effectiveSpeed = Mathf.Max(0.15f, ship.SpeedMax * orientationMultiplier * velocityMultiplier * energyMultiplier);
            float travelTime = distance / effectiveSpeed;
            travelTime *= hazardPenalty;

            return travelTime;
        }

        private float PredictEnemyArrival(SpaceShipView self, GameData data, WayPointView waypoint, float danger, float openArea)
        {
            if (self == null || data?.SpaceShips == null || waypoint == null)
                return float.PositiveInfinity;

            float bestEta = float.PositiveInfinity;
            foreach (SpaceShipView ship in data.SpaceShips)
            {
                if (ship == null || ship.Owner == self.Owner)
                    continue;

                float eta = EstimateTravelTime(ship, data, waypoint, danger, openArea);
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

        private float MemoryMultiplier(int waypointIndex)
        {
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

        private float CentralityFactor(Vector2 position, Vector2 mapCenter)
        {
            float distance = Vector2.Distance(position, mapCenter);
            return 1f - Mathf.Clamp01(distance / CentralityNormalization);
        }

        private float EnemyPressure(GameData data, SpaceShipView self, Vector2 position)
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

                pressure += 1f - Mathf.Clamp01(distance / EnemyPressureRadius);
            }

            return Mathf.Clamp01(pressure);
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

        private List<List<WayPointView>> ClusterWaypoints(List<WayPointView> waypoints)
        {
            List<List<WayPointView>> clusters = new();
            if (waypoints == null || waypoints.Count == 0)
                return clusters;

            bool[] visited = new bool[waypoints.Count];

            for (int i = 0; i < waypoints.Count; i++)
            {
                if (visited[i])
                    continue;

                WayPointView origin = waypoints[i];
                if (origin == null)
                {
                    visited[i] = true;
                    continue;
                }

                List<WayPointView> cluster = new();
                Queue<int> frontier = new();
                frontier.Enqueue(i);
                visited[i] = true;

                while (frontier.Count > 0)
                {
                    int index = frontier.Dequeue();
                    WayPointView waypoint = waypoints[index];
                    if (waypoint == null)
                        continue;

                    cluster.Add(waypoint);

                    for (int j = 0; j < waypoints.Count; j++)
                    {
                        if (visited[j])
                            continue;

                        WayPointView candidate = waypoints[j];
                        if (candidate == null)
                        {
                            visited[j] = true;
                            continue;
                        }

                        float distance = Vector2.Distance(waypoint.Position, candidate.Position);
                        if (distance <= ClusterDistanceThreshold)
                        {
                            visited[j] = true;
                            frontier.Enqueue(j);
                        }
                    }
                }

                if (cluster.Count > 0)
                    clusters.Add(cluster);
            }

            return clusters;
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
