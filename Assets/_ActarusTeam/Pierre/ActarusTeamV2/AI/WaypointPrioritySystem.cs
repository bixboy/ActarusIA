using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Strategic selector that evaluates waypoint clusters and picks the best capture target.
    /// </summary>
    public sealed class WaypointPrioritySystem
    {
        private const float ClusterDistanceThreshold = 3.5f;
        private const float DistanceNormalization = 12f;
        private const float HazardReach = 6f;
        private const float AsteroidBuffer = 0.75f;

        // Group score weights (tweakable)
        private const float EnemyWeight = 0.7f;
        private const float NeutralWeight = 0.4f;
        private const float DistanceWeight = 0.6f;
        private const float DangerWeight = 0.5f;
        private const float AccessibilityWeight = 0.3f;
        private const float AllyPresenceWeight = 0.3f;

        // Individual waypoint weights (tweakable)
        private const float IndividualDistanceWeight = 0.35f;
        private const float IndividualControlWeight = 0.4f;
        private const float IndividualSafetyWeight = 0.15f;
        private const float IndividualOrientationWeight = 0.1f;

        // Debug
        private const float DebugTextSize = 0.8f;
        private const float DebugSphereSize = 0.25f;
        private static readonly Color DebugBestClusterColor = Color.yellow;
        private static readonly Color DebugBestWaypointColor = Color.green;
        private static readonly Color DebugClusterLinkColor = new Color(1f, 1f, 1f, 0.25f);

        private List<List<WayPointView>> _lastClusters = new();
        private List<(Vector2 pos, float score)> _lastGroupScores = new();
        private WayPointView _lastBestWaypoint;

        // ──────────── Public entry ────────────
        public WayPointView SelectBestWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data == null)
                return null;

            _lastClusters.Clear();
            _lastGroupScores.Clear();
            _lastBestWaypoint = null;

            List<List<WayPointView>> groups = ClusterWaypoints(data);
            _lastClusters = groups;

            List<WayPointView> bestGroup = null;
            float bestGroupScore = float.MinValue;

            foreach (List<WayPointView> group in groups)
            {
                float score = ComputeGroupScore(self, data, group);
                if (score > bestGroupScore)
                {
                    bestGroupScore = score;
                    bestGroup = group;
                }

                // Debug store centroid & score
                Vector2 centroid = Vector2.zero;
                foreach (var wp in group) centroid += wp.Position;
                centroid /= group.Count;
                _lastGroupScores.Add((centroid, score));
            }

            if (bestGroup == null || bestGroup.Count == 0)
                return null;

            WayPointView best = SelectBestInGroup(self, data, bestGroup);
            _lastBestWaypoint = best;

            // Debug draw call
            DrawDebug(self, bestGroup, best);

            return best;
        }


        // ──────────── Grouping ────────────
        private List<List<WayPointView>> ClusterWaypoints(GameData data)
        {
            List<List<WayPointView>> clusters = new();
            if (data?.WayPoints == null || data.WayPoints.Count == 0)
                return clusters;

            List<WayPointView> waypoints = data.WayPoints;
            bool[] visited = new bool[waypoints.Count];

            for (int i = 0; i < waypoints.Count; i++)
            {
                WayPointView origin = waypoints[i];
                if (origin == null || visited[i])
                    continue;

                List<WayPointView> cluster = new();
                Queue<int> frontier = new();
                frontier.Enqueue(i);
                visited[i] = true;

                while (frontier.Count > 0)
                {
                    int currentIndex = frontier.Dequeue();
                    WayPointView current = waypoints[currentIndex];
                    if (current == null)
                        continue;

                    cluster.Add(current);

                    for (int j = 0; j < waypoints.Count; j++)
                    {
                        if (visited[j]) continue;
                        WayPointView candidate = waypoints[j];
                        if (candidate == null)
                        {
                            visited[j] = true;
                            continue;
                        }

                        float distance = Vector2.Distance(current.Position, candidate.Position);
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

        // ──────────── Scoring ────────────
        private float ComputeGroupScore(SpaceShipView self, GameData data, List<WayPointView> group)
        {
            if (group == null || group.Count == 0)
            {
                return float.MinValue;
            }

            int enemyCount = 0;
            int neutralCount = 0;
            Vector2 centroid = Vector2.zero;
            float distanceAccumulator = 0f;

            foreach (WayPointView waypoint in group)
            {
                if (waypoint == null)
                {
                    continue;
                }

                centroid += waypoint.Position;
                distanceAccumulator += DistanceFactor(self, waypoint);

                if (waypoint.Owner == -1)
                {
                    neutralCount++;
                }
                else if (waypoint.Owner != self.Owner)
                {
                    enemyCount++;
                }
            }

            int effectiveCount = group.Count;
            if (effectiveCount == 0)
            {
                return float.MinValue;
            }

            centroid /= effectiveCount;
            float averageDistanceScore = distanceAccumulator / effectiveCount;

            float enemyRatio = effectiveCount > 0 ? (float)enemyCount / effectiveCount : 0f;
            float neutralRatio = effectiveCount > 0 ? (float)neutralCount / effectiveCount : 0f;

            float danger = DangerFactor(self, data, centroid);
            float allyPresence = AllyProximityFactor(self, data, centroid);
            float accessibility = OpenAreaFactor(self, data, centroid);

            float score = 0f;
            score += enemyRatio * EnemyWeight;
            score += neutralRatio * NeutralWeight;
            score += averageDistanceScore * DistanceWeight;
            score -= danger * DangerWeight;
            score += accessibility * AccessibilityWeight;
            score -= allyPresence * AllyPresenceWeight;

            return score;
        }

        private WayPointView SelectBestInGroup(SpaceShipView self, GameData data, List<WayPointView> group)
        {
            WayPointView best = null;
            float bestScore = float.MinValue;

            foreach (WayPointView waypoint in group)
            {
                float distanceScore = DistanceFactor(self, waypoint);
                float controlScore = ControlFactor(self, waypoint);
                float safetyScore = 1f - DangerFactor(self, data, waypoint.Position);
                float orientationScore = OrientationFactorRelativeToEnemy(self, data, waypoint);

                float score = distanceScore * IndividualDistanceWeight
                              + controlScore * IndividualControlWeight
                              + Mathf.Clamp01(safetyScore) * IndividualSafetyWeight
                              + orientationScore * IndividualOrientationWeight;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = waypoint;
                }
            }

            return best;
        }

        // ──────────── Utils ────────────
        private float DistanceFactor(SpaceShipView self, WayPointView waypoint)
        {
            if (self == null || waypoint == null)
            {
                return 0f;
            }

            float distance = Vector2.Distance(self.Position, waypoint.Position);
            float normalized = 1f - Mathf.Clamp01(distance / DistanceNormalization);
            return normalized;
        }

        private float DangerFactor(SpaceShipView self, GameData data, Vector2 position)
        {
            _ = self; // kept for extensibility (e.g. team-specific hazards)

            if (data == null)
            {
                return 0f;
            }

            float accumulatedDanger = 0f;
            int samples = 0;

            if (data.Mines != null)
            {
                foreach (MineView mine in data.Mines)
                {
                    if (mine == null)
                    {
                        continue;
                    }

                    float distance = Vector2.Distance(position, mine.Position);
                    float influenceRadius = mine.ExplosionRadius + HazardReach;
                    if (influenceRadius <= 0f)
                    {
                        continue;
                    }

                    float factor = 1f - Mathf.Clamp01(distance / influenceRadius);
                    if (factor <= 0f)
                    {
                        continue;
                    }

                    accumulatedDanger += mine.IsActive ? factor : factor * 0.5f;
                    samples++;
                }
            }

            if (data.Asteroids != null)
            {
                foreach (AsteroidView asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                    {
                        continue;
                    }

                    float distance = Vector2.Distance(position, asteroid.Position);
                    float influenceRadius = asteroid.Radius + HazardReach;
                    if (influenceRadius <= 0f)
                    {
                        continue;
                    }

                    float factor = 1f - Mathf.Clamp01((distance - asteroid.Radius) / influenceRadius);
                    if (factor <= 0f)
                    {
                        continue;
                    }

                    accumulatedDanger += Mathf.Clamp01(factor);
                    samples++;
                }
            }

            if (samples == 0)
            {
                return 0f;
            }

            return Mathf.Clamp01(accumulatedDanger / samples);
        }

        private float AllyProximityFactor(SpaceShipView self, GameData data, Vector2 position)
        {
            if (self == null || data?.SpaceShips == null)
            {
                return 0f;
            }

            float closestDistance = float.MaxValue;
            foreach (SpaceShipView ship in data.SpaceShips)
            {
                if (ship == null || ship.Owner != self.Owner || ship == self)
                {
                    continue;
                }

                float distance = Vector2.Distance(position, ship.Position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                }
            }

            if (closestDistance == float.MaxValue)
            {
                return 0f;
            }

            float normalized = 1f - Mathf.Clamp01(closestDistance / DistanceNormalization);
            return normalized;
        }

        private float ControlFactor(SpaceShipView self, WayPointView waypoint)
        {
            if (self == null || waypoint == null)
            {
                return 0f;
            }

            if (waypoint.Owner == self.Owner)
            {
                return 0f;
            }

            if (waypoint.Owner == -1)
            {
                return 0.5f;
            }

            return 1f;
        }

        private float OrientationFactorRelativeToEnemy(SpaceShipView self, GameData data, WayPointView waypoint)
        {
            if (data == null || waypoint == null)
            {
                return 0.5f;
            }

            int selfOwner = self != null ? self.Owner : -1;
            SpaceShipView enemy = FindEnemyShip(data, selfOwner, waypoint.Position);

            if (enemy == null)
            {
                return 0.5f;
            }

            Vector2 toWaypoint = waypoint.Position - enemy.Position;
            if (toWaypoint.sqrMagnitude < Mathf.Epsilon)
            {
                return 0.5f;
            }

            toWaypoint.Normalize();
            Vector2 enemyForward = OrientationToVector(enemy.Orientation);
            float dot = Vector2.Dot(enemyForward, toWaypoint);
            float orientationScore = 0.5f * (1f - dot);
            return Mathf.Clamp01(orientationScore);
        }

        private float OpenAreaFactor(SpaceShipView self, GameData data, Vector2 targetPosition)
        {
            if (self == null)
            {
                return 0f;
            }

            if (data?.Asteroids == null || data.Asteroids.Count == 0)
            {
                return 1f;
            }

            Vector2 origin = self.Position;
            float maxBlocking = 0f;

            foreach (AsteroidView asteroid in data.Asteroids)
            {
                if (asteroid == null)
                {
                    continue;
                }

                float distanceToPath = DistancePointToSegment(asteroid.Position, origin, targetPosition);
                float safeRadius = asteroid.Radius + AsteroidBuffer;
                if (safeRadius <= 0f)
                {
                    continue;
                }

                float obstruction = 1f - Mathf.Clamp01((distanceToPath - asteroid.Radius) / safeRadius);
                if (obstruction > maxBlocking)
                {
                    maxBlocking = obstruction;
                }
            }

            float openArea = 1f - Mathf.Clamp01(maxBlocking);
            return openArea;
        }

        private float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            if (ab.sqrMagnitude < Mathf.Epsilon)
            {
                return Vector2.Distance(point, a);
            }

            float t = Vector2.Dot(point - a, ab) / ab.sqrMagnitude;
            t = Mathf.Clamp01(t);
            Vector2 closest = a + ab * t;
            return Vector2.Distance(point, closest);
        }

        private SpaceShipView FindEnemyShip(GameData data, int selfOwner, Vector2 targetPosition)
        {
            if (data?.SpaceShips == null)
            {
                return null;
            }

            SpaceShipView best = null;
            float bestDistance = float.MaxValue;

            foreach (SpaceShipView ship in data.SpaceShips)
            {
                if (ship == null)
                {
                    continue;
                }

                if (selfOwner != -1 && ship.Owner == selfOwner)
                {
                    continue;
                }

                float sqrDistance = (ship.Position - targetPosition).sqrMagnitude;
                if (sqrDistance < bestDistance)
                {
                    bestDistance = sqrDistance;
                    best = ship;
                }
            }

            return best;
        }

        private Vector2 OrientationToVector(float orientationDegrees)
        {
            float rad = orientationDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
        
        
        // ──────────── DEBUG VISUALIZATION ────────────
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void DrawDebug(SpaceShipView self, List<WayPointView> bestGroup, WayPointView bestWaypoint)
        {
            // Draw clusters
            foreach (var cluster in _lastClusters)
            {
                Color clusterColor = new(Random.value, Random.value, Random.value, 0.5f);
                foreach (var wp in cluster)
                {
                    Debug.DrawLine(self.Position, wp.Position, clusterColor, 0.1f);
                    foreach (var other in cluster)
                    {
                        if (wp == other) continue;
                        Debug.DrawLine(wp.Position, other.Position, DebugClusterLinkColor, 0.1f);
                    }
                    DebugExtension.DrawSphere(wp.Position, clusterColor, DebugSphereSize);
                }
            }

            // Draw centroids with scores
            foreach (var (pos, score) in _lastGroupScores)
            {
                Color c = Color.Lerp(Color.red, Color.green, Mathf.Clamp01((score + 1f) * 0.5f));
                DebugExtension.DrawSphere(pos, c, DebugSphereSize * 0.8f);
                DebugExtension.DrawText(pos + Vector2.up * 0.5f, $"S={score:F2}", c, DebugTextSize);
            }

            // Highlight best group
            if (bestGroup != null)
            {
                foreach (var wp in bestGroup)
                    Debug.DrawLine(self.Position, wp.Position, DebugBestClusterColor, 0.1f);
            }

            // Highlight chosen waypoint
            if (bestWaypoint != null)
            {
                Debug.DrawLine(self.Position, bestWaypoint.Position, DebugBestWaypointColor, 0.1f);
                DebugExtension.DrawSphere(bestWaypoint.Position, DebugBestWaypointColor, DebugSphereSize * 1.5f);
                DebugExtension.DrawText(bestWaypoint.Position + Vector2.up * 0.7f, "★ TARGET", DebugBestWaypointColor, 1.1f);
            }
        }

        // ──────────── Rest of your Utils (unchanged) ────────────
        // ... (DistanceFactor, DangerFactor, etc. — tu gardes tout pareil)
        // ⚠️ n'oublie pas d'ajouter le script DebugExtension ci-dessous dans ton projet
    }
}
