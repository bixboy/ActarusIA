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
        private const float ProjectileDangerReach = 5f;
        private const float SpreadNormalization = 6f;
        private const float TravelTimeNormalization = 6f;

        // Group score weights (tweakable)
        private const float EnemyWeight = 0.7f;
        private const float NeutralWeight = 0.45f;
        private const float DistanceWeight = 0.55f;
        private const float DangerWeight = 0.55f;
        private const float AccessibilityWeight = 0.35f;
        private const float AllyPresenceWeight = 0.25f;
        private const float EnemyPressurePenaltyWeight = 0.35f;
        private const float ProjectileDangerWeight = 0.45f;
        private const float SpreadPenaltyWeight = 0.3f;
        private const float ReadinessWeight = 0.25f;
        private const float ScoreUrgencyWeight = 0.3f;
        private const float TimeUrgencyWeight = 0.2f;

        // Individual waypoint weights (tweakable)
        private const float IndividualDistanceWeight = 0.3f;
        private const float IndividualControlWeight = 0.42f;
        private const float IndividualSafetyWeight = 0.15f;
        private const float IndividualOrientationWeight = 0.08f;
        private const float IndividualApproachWeight = 0.08f;
        private const float IndividualTravelWeight = 0.12f;
        private const float IndividualProjectilePenaltyWeight = 0.12f;

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
                Vector2 centroid = ComputeCentroid(group);
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
                return float.MinValue;

            int enemyCount = 0;
            int neutralCount = 0;
            int allyCount = 0;
            Vector2 centroid = Vector2.zero;
            float totalDistance = 0f;

            foreach (var wp in group)
            {
                if (wp == null) continue;
                centroid += wp.Position;
                totalDistance += Vector2.Distance(self.Position, wp.Position);

                if (wp.Owner == -1) neutralCount++;
                else if (wp.Owner == self.Owner) allyCount++;
                else enemyCount++;
            }

            int total = group.Count;
            centroid /= total;
            float avgDistance = total > 0 ? totalDistance / total : 0f;

            // ---- Facteurs environnementaux ----
            float danger = DangerFactor(self, data, centroid);
            float accessibility = OpenAreaFactor(self, data, centroid);
            float allyPresence = AllyProximityFactor(self, data, centroid);
            float projectileThreat = ProjectileThreatFactor(data, centroid);
            float groupDispersion = GroupDispersion(group, centroid);
            float readiness = SelfReadinessFactor(self);
            float scoreUrgency = ScoreDeficitFactor(self, data);
            float timePressure = TimePressureFactor(data);

            // ---- Facteur pression ennemie ----
            float enemyPressure = 0f;
            foreach (var ship in data.SpaceShips)
            {
                if (ship == null || ship.Owner == self.Owner) continue;
                float dist = Vector2.Distance(ship.Position, centroid);
                if (dist < 8f)
                    enemyPressure += 1f - Mathf.Clamp01(dist / 8f);
            }
            enemyPressure = Mathf.Clamp01(enemyPressure);

            // ---- Facteur de contexte dynamique ----
            float distanceFactor = 1f - Mathf.Clamp01(avgDistance / DistanceNormalization);
            float enemyRatio = total > 0 ? (float)enemyCount / total : 0f;
            float neutralRatio = total > 0 ? (float)neutralCount / total : 0f;
            float allyOvercrowding = Mathf.Clamp01(allyPresence * 1.2f);

            // ---- Score pondéré ----
            float score = 0f;
            score += (enemyRatio * EnemyWeight) + (neutralRatio * NeutralWeight); // priorité aux cibles ennemies/neutres
            score += distanceFactor * DistanceWeight;                              // plus proche = mieux
            score += accessibility * AccessibilityWeight;                          // zones ouvertes privilégiées
            score -= danger * DangerWeight;                                        // éviter les zones à risque
            score -= enemyPressure * EnemyPressurePenaltyWeight;                   // pression directe ennemie
            score -= projectileThreat * ProjectileDangerWeight;                    // menaces de projectiles
            score -= allyOvercrowding * AllyPresenceWeight;                        // éviter la redondance alliée
            score -= groupDispersion * SpreadPenaltyWeight;                        // clusters trop éparpillés

            // facteurs dynamiques
            score += ((readiness - 0.5f) * 2f) * ReadinessWeight;                  // adapter selon l'état du vaisseau
            score += scoreUrgency * ScoreUrgencyWeight;                            // rattraper le retard
            score += timePressure * TimeUrgencyWeight;                             // fin de partie = agressif

            return score;
        }

        private WayPointView SelectBestInGroup(SpaceShipView self, GameData data, List<WayPointView> group)
        {
            WayPointView best = null;
            float bestScore = float.MinValue;

            Vector2 centroid = ComputeCentroid(group);

            foreach (var wp in group)
            {
                if (wp == null) continue;

                float distanceScore = DistanceFactor(self, wp);
                float safetyScore = 1f - DangerFactor(self, data, wp.Position);
                float controlScore = ControlFactor(self, wp);
                float openArea = OpenAreaFactor(self, data, wp.Position);
                float orientationScore = OrientationFactorRelativeToEnemy(self, data, wp);
                float approachScore = ApproachAlignmentFactor(self, wp);
                float travelScore = TravelEffortFactor(self, wp);
                float projectilePenalty = ProjectileThreatFactor(data, wp.Position);

                // Bonus si le waypoint est au centre du cluster (plus facile à défendre)
                float centroidBias = 1f - Mathf.Clamp01(Vector2.Distance(centroid, wp.Position) / 5f);

                // pondération améliorée
                float score = 0f;
                score += controlScore * IndividualControlWeight;                     // priorité à capturer un ennemi ou neutre
                score += safetyScore * IndividualSafetyWeight;                       // éviter les pièges
                score += distanceScore * IndividualDistanceWeight;                   // proche = mieux
                score += openArea * 0.15f;                                         // zone dégagée
                score += centroidBias * 0.1f;                                       // central = stable
                score += orientationScore * IndividualOrientationWeight;             // exposer l'ennemi de dos
                score += approachScore * IndividualApproachWeight;                   // limiter les corrections de trajectoire
                score += travelScore * IndividualTravelWeight;                       // adapter selon vitesse/énergie
                score -= projectilePenalty * IndividualProjectilePenaltyWeight;      // éviter les tirs incoming

                if (score > bestScore)
                {
                    bestScore = score;
                    best = wp;
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

        private float ApproachAlignmentFactor(SpaceShipView self, WayPointView waypoint)
        {
            if (self == null || waypoint == null)
            {
                return 0.5f;
            }

            Vector2 desiredDirection = waypoint.Position - self.Position;
            if (desiredDirection.sqrMagnitude < Mathf.Epsilon)
            {
                return 1f;
            }
            desiredDirection.Normalize();

            Vector2 currentDirection;
            if (self.Velocity.sqrMagnitude > 0.01f)
            {
                currentDirection = self.Velocity.normalized;
            }
            else
            {
                currentDirection = self.LookAt.sqrMagnitude > Mathf.Epsilon ? self.LookAt.normalized : desiredDirection;
            }

            float alignment = Mathf.Clamp01((Vector2.Dot(currentDirection, desiredDirection) + 1f) * 0.5f);
            return alignment;
        }

        private float TravelEffortFactor(SpaceShipView self, WayPointView waypoint)
        {
            if (self == null || waypoint == null)
            {
                return 0.5f;
            }

            float distance = Vector2.Distance(self.Position, waypoint.Position);
            float speed = Mathf.Max(0.1f, self.SpeedMax);
            float travelTime = distance / speed;
            float normalizedTime = 1f - Mathf.Clamp01(travelTime / TravelTimeNormalization);

            float energy = Mathf.Clamp01(self.Energy);
            float readinessPenalty = (self.HitPenaltyCountdown > 0f ? 0.25f : 0f) + (self.StunPenaltyCountdown > 0f ? 0.5f : 0f);
            float readiness = Mathf.Clamp01(1f - readinessPenalty);

            return Mathf.Clamp01(normalizedTime * (0.6f + 0.4f * energy) * readiness);
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

        private Vector2 ComputeCentroid(List<WayPointView> group)
        {
            if (group == null || group.Count == 0)
            {
                return Vector2.zero;
            }

            Vector2 centroid = Vector2.zero;
            foreach (var wp in group)
            {
                if (wp == null) continue;
                centroid += wp.Position;
            }

            return centroid / group.Count;
        }

        private float GroupDispersion(List<WayPointView> group, Vector2 centroid)
        {
            if (group == null || group.Count == 0)
            {
                return 0f;
            }

            float accum = 0f;
            int count = 0;
            foreach (var wp in group)
            {
                if (wp == null) continue;
                accum += Vector2.Distance(wp.Position, centroid);
                count++;
            }

            if (count == 0)
            {
                return 0f;
            }

            float avg = accum / count;
            return Mathf.Clamp01(avg / SpreadNormalization);
        }

        private float ProjectileThreatFactor(GameData data, Vector2 position)
        {
            if (data?.Bullets == null || data.Bullets.Count == 0)
            {
                return 0f;
            }

            float accumulatedThreat = 0f;
            int samples = 0;

            foreach (BulletView bullet in data.Bullets)
            {
                if (bullet == null)
                {
                    continue;
                }

                Vector2 toPosition = position - bullet.Position;
                float distance = toPosition.magnitude;
                if (distance > ProjectileDangerReach)
                {
                    continue;
                }

                Vector2 velocity = bullet.Velocity;
                if (velocity.sqrMagnitude < Mathf.Epsilon)
                {
                    continue;
                }

                float alignment = Mathf.Clamp01(Vector2.Dot(velocity.normalized, toPosition.normalized));
                float factor = (1f - Mathf.Clamp01(distance / ProjectileDangerReach)) * alignment;
                if (factor <= 0f)
                {
                    continue;
                }

                accumulatedThreat += factor;
                samples++;
            }

            if (samples == 0)
            {
                return 0f;
            }

            return Mathf.Clamp01(accumulatedThreat / samples);
        }

        private float SelfReadinessFactor(SpaceShipView self)
        {
            if (self == null)
            {
                return 0.5f;
            }

            float energy = Mathf.Clamp01(self.Energy);
            float hitPenalty = self.HitPenaltyCountdown > 0f ? 0.4f : 0f;
            float stunPenalty = self.StunPenaltyCountdown > 0f ? 0.6f : 0f;
            float mobility = self.Velocity.magnitude / Mathf.Max(0.1f, self.SpeedMax);

            float readiness = 0.4f * energy + 0.35f * Mathf.Clamp01(mobility) + 0.25f * (self.Thrust > 0f ? 1f : 0.5f);
            readiness *= Mathf.Clamp01(1f - hitPenalty - stunPenalty);

            return Mathf.Clamp01(readiness);
        }

        private float ScoreDeficitFactor(SpaceShipView self, GameData data)
        {
            if (self == null || GameManager.Instance == null)
            {
                return 0f;
            }

            int myScore = GameManager.Instance.GetWayPointScoreForPlayer(self.Owner);
            int bestOpponent = myScore;

            if (data?.SpaceShips != null)
            {
                foreach (SpaceShipView ship in data.SpaceShips)
                {
                    if (ship == null || ship.Owner == self.Owner)
                    {
                        continue;
                    }

                    int enemyScore = GameManager.Instance.GetWayPointScoreForPlayer(ship.Owner);
                    if (enemyScore > bestOpponent)
                    {
                        bestOpponent = enemyScore;
                    }
                }
            }

            int totalWaypoints = data?.WayPoints?.Count ?? 0;
            if (totalWaypoints == 0)
            {
                return 0f;
            }

            int diff = myScore - bestOpponent;
            float normalized = Mathf.Clamp((float)diff / totalWaypoints, -1f, 1f);
            return -normalized; // positif quand on est en retard
        }

        private float TimePressureFactor(GameData data)
        {
            if (data == null)
            {
                return 0f;
            }

            const float endGameWindow = 20f; // secondes
            float timeLeft = Mathf.Max(0f, data.timeLeft);
            if (timeLeft >= endGameWindow)
            {
                return 0f;
            }

            return 1f - Mathf.Clamp01(timeLeft / endGameWindow);
        }


        // ──────────── DEBUG VISUALIZATION ────────────
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void DrawDebug(SpaceShipView self, List<WayPointView> bestGroup, WayPointView bestWaypoint)
        {
            if (self == null) return;

            const float lineDuration = 0.1f;
            const float textOffset = 0.8f;

            // ─────────────── CLUSTERS ───────────────
            for (int i = 0; i < _lastClusters.Count; i++)
            {
                var cluster = _lastClusters[i];
                if (cluster.Count == 0) continue;

                // Couleur douce et translucide
                Color clusterColor = new Color(0.3f, 0.6f, 1f, 0.25f);

                // Calcul du centroïde pour affichage
                Vector2 centroid = Vector2.zero;
                foreach (var wp in cluster)
                    centroid += wp.Position;
                centroid /= cluster.Count;

                // Score moyen (trouvé dans _lastGroupScores)
                float score = 0f;
                foreach (var (pos, val) in _lastGroupScores)
                {
                    if ((pos - centroid).sqrMagnitude < 0.5f)
                    {
                        score = val;
                        break;
                    }
                }

                // Sphère de cluster + texte score
                DebugExtension.DrawSphere(centroid, clusterColor, DebugSphereSize * 0.7f);
                if (score != 0)
                {
                    Color txtColor = Color.Lerp(Color.red, Color.green, Mathf.InverseLerp(-1f, 1f, score));
                    DebugExtension.DrawText(centroid + Vector2.up * textOffset, $"Cluster {i}\nS={score:F2}", txtColor,
                        0.9f);
                }

                // Si c’est le meilleur cluster, le surligner
                if (bestGroup != null && cluster == bestGroup)
                {
                    foreach (var wp in cluster)
                    {
                        Debug.DrawLine(self.Position, wp.Position, DebugBestClusterColor, lineDuration);
                        DebugExtension.DrawSphere(wp.Position, DebugBestClusterColor, DebugSphereSize);
                    }
                }
                else
                {
                    // Sinon juste des sphères discrètes
                    foreach (var wp in cluster)
                        DebugExtension.DrawSphere(wp.Position, clusterColor, DebugSphereSize * 0.5f);
                }
            }

            // ─────────────── WAYPOINT CHOISI ───────────────
            if (bestWaypoint != null)
            {
                Debug.DrawLine(self.Position, bestWaypoint.Position, DebugBestWaypointColor, lineDuration);
                DebugExtension.DrawSphere(bestWaypoint.Position, DebugBestWaypointColor, DebugSphereSize * 1.4f);
                DebugExtension.DrawText(bestWaypoint.Position + Vector2.up * textOffset, "★ TARGET",
                    DebugBestWaypointColor, 1.1f);
            }
        }
    }
}
