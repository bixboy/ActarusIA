using System;
using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Builds a waypoint neighbour graph and evaluates multi-step capture plans to support
    /// hierarchical waypoint planning.
    /// The planner is intentionally lightweight – waypoint counts are modest, so we can
    /// recompute graph metrics every evaluation without a large performance penalty.
    /// </summary>
    public class WaypointStrategicPlanner
    {
        private class NodeProfile
        {
            public WayPointView Waypoint;
            public Vector2 Position;
            public readonly List<int> Neighbours = new();
            public float Closeness;
            public float Domination;
            public float NormalizedCloseness;
            public float NormalizedDomination;
        }

        private readonly List<NodeProfile> _nodes = new();
        private readonly Dictionary<WayPointView, int> _indices = new();
        private readonly List<WayPointView> _scratchPlan = new();
        private readonly List<WayPointView> _bestPlan = new();
        private readonly List<int> _searchStack = new();
        private readonly HashSet<int> _searchVisited = new();

        private float[,] _adjacencyMatrix;
        private float _bestScore;

        public void UpdateGraph(GameData data)
        {
            _nodes.Clear();
            _indices.Clear();
            _adjacencyMatrix = null;

            if (data?.WayPoints == null)
                return;

            for (int i = 0; i < data.WayPoints.Count; i++)
            {
                WayPointView waypoint = data.WayPoints[i];
                if (waypoint == null)
                    continue;

                var node = new NodeProfile
                {
                    Waypoint = waypoint,
                    Position = waypoint.Position
                };

                _indices[waypoint] = _nodes.Count;
                _nodes.Add(node);
            }

            int count = _nodes.Count;
            if (count == 0)
                return;

            _adjacencyMatrix = new float[count, count];

            BuildNeighbourhoods();
            ComputeCloseness();
            ComputeDominationCycles();
            NormalizeStrategicValues();
        }

        public IReadOnlyList<WayPointView> PlanCaptureOrder(
            WayPointView current,
            Dictionary<WayPointView, WaypointMetrics> metrics,
            Dictionary<WayPointView, float> scores,
            int desiredCount)
        {
            _scratchPlan.Clear();
            _bestPlan.Clear();
            _searchStack.Clear();
            _searchVisited.Clear();
            _bestScore = float.NegativeInfinity;

            if (current == null || desiredCount <= 0)
                return _bestPlan;

            if (!_indices.TryGetValue(current, out int startIndex))
                return _bestPlan;

            _searchVisited.Add(startIndex);
            SearchRecursive(startIndex, metrics, scores, 0, 0f, desiredCount);
            _searchVisited.Clear();

            return _bestPlan;
        }

        private void SearchRecursive(
            int nodeIndex,
            Dictionary<WayPointView, WaypointMetrics> metrics,
            Dictionary<WayPointView, float> scores,
            int depth,
            float accumulated,
            int desiredCount)
        {
            if (depth >= AIConstants.StrategicPredictionDepth || _scratchPlan.Count >= desiredCount)
            {
                if (_scratchPlan.Count > _bestPlan.Count ||
                    (_scratchPlan.Count == _bestPlan.Count && accumulated > _bestScore))
                {
                    _bestPlan.Clear();
                    _bestPlan.AddRange(_scratchPlan);
                    _bestScore = accumulated;
                }

                return;
            }

            List<int> candidates = RankCandidates(nodeIndex, metrics, scores);
            int limit = Mathf.Min(candidates.Count, AIConstants.StrategicBranchingFactor);

            for (int i = 0; i < limit; i++)
            {
                int candidate = candidates[i];
                if (_searchVisited.Contains(candidate))
                    continue;

                float stepScore = EvaluateCandidate(nodeIndex, candidate, metrics, scores, depth);
                float newScore = accumulated + stepScore;

                if (_bestPlan.Count >= desiredCount && newScore <= accumulated)
                    continue;

                _searchVisited.Add(candidate);
                _searchStack.Add(candidate);
                _scratchPlan.Add(_nodes[candidate].Waypoint);

                SearchRecursive(candidate, metrics, scores, depth + 1, newScore, desiredCount);

                _scratchPlan.RemoveAt(_scratchPlan.Count - 1);
                _searchStack.RemoveAt(_searchStack.Count - 1);
                _searchVisited.Remove(candidate);
            }

            if (candidates.Count == 0 || limit == 0)
            {
                if (_scratchPlan.Count > _bestPlan.Count ||
                    (_scratchPlan.Count == _bestPlan.Count && accumulated > _bestScore))
                {
                    _bestPlan.Clear();
                    _bestPlan.AddRange(_scratchPlan);
                    _bestScore = accumulated;
                }
            }
        }

        private List<int> RankCandidates(
            int nodeIndex,
            Dictionary<WayPointView, WaypointMetrics> metrics,
            Dictionary<WayPointView, float> scores)
        {
            var result = new List<int>();

            if (nodeIndex < 0 || nodeIndex >= _nodes.Count)
                return result;

            NodeProfile origin = _nodes[nodeIndex];
            foreach (int neighbour in origin.Neighbours)
            {
                if (neighbour == nodeIndex)
                    continue;

                result.Add(neighbour);
            }

            if (result.Count == 0)
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (i == nodeIndex)
                        continue;

                    result.Add(i);
                }
            }

            result.Sort((a, b) => EvaluateHeuristic(b, metrics, scores).CompareTo(EvaluateHeuristic(a, metrics, scores)));
            return result;
        }

        private float EvaluateCandidate(
            int fromIndex,
            int toIndex,
            Dictionary<WayPointView, WaypointMetrics> metrics,
            Dictionary<WayPointView, float> scores,
            int depth)
        {
            float heuristic = EvaluateHeuristic(toIndex, metrics, scores);

            float travelPenalty = 0f;
            WayPointView waypoint = _nodes[toIndex].Waypoint;
            if (metrics != null && metrics.TryGetValue(waypoint, out WaypointMetrics waypointMetrics))
            {
                float normalized = 1f - Mathf.Clamp01(waypointMetrics.TravelTime / AIConstants.StrategicTravelNormalization);
                travelPenalty = 1f - normalized;
            }

            float distance = Vector2.Distance(_nodes[fromIndex].Position, _nodes[toIndex].Position);
            float controlBias = Mathf.Clamp01(1f - distance / AIConstants.StrategicEdgeNormalization);

            float depthDiscount = Mathf.Pow(AIConstants.StrategicFutureDiscount, depth);
            float score = heuristic * depthDiscount;
            score += controlBias * AIConstants.StrategicAdjacencyWeight * depthDiscount;
            score -= travelPenalty * AIConstants.StrategicTravelPenalty;

            return score;
        }

        private float EvaluateHeuristic(
            int index,
            Dictionary<WayPointView, WaypointMetrics> metrics,
            Dictionary<WayPointView, float> scores)
        {
            if (index < 0 || index >= _nodes.Count)
                return 0f;

            NodeProfile profile = _nodes[index];
            WayPointView waypoint = profile.Waypoint;

            float rawScore = 0f;
            if (scores != null && waypoint != null && scores.TryGetValue(waypoint, out float stored))
                rawScore = stored;

            float captureSwing = 0f;
            if (metrics != null && metrics.TryGetValue(waypoint, out WaypointMetrics waypointMetrics))
                captureSwing = waypointMetrics.CaptureSwing + waypointMetrics.Control;

            float value = rawScore * AIConstants.StrategicScoreWeight;
            value += profile.NormalizedCloseness * AIConstants.StrategicCentralityWeight;
            value += profile.NormalizedDomination * AIConstants.StrategicDominationWeight;
            value += captureSwing * AIConstants.StrategicSwingWeight;

            return value;
        }

        private void BuildNeighbourhoods()
        {
            int count = _nodes.Count;
            if (count == 0)
                return;

            for (int i = 0; i < count; i++)
                _nodes[i].Neighbours.Clear();

            for (int i = 0; i < count; i++)
            {
                NodeProfile node = _nodes[i];
                var distances = new List<(float distance, int index)>();

                for (int j = 0; j < count; j++)
                {
                    if (i == j)
                        continue;

                    float distance = Vector2.Distance(node.Position, _nodes[j].Position);
                    distances.Add((distance, j));
                }

                distances.Sort((a, b) => a.distance.CompareTo(b.distance));
                int limit = Mathf.Min(AIConstants.StrategicNeighbourCount, distances.Count);

                for (int k = 0; k < limit; k++)
                {
                    (float distance, int neighbourIndex) = distances[k];
                    if (!node.Neighbours.Contains(neighbourIndex))
                        node.Neighbours.Add(neighbourIndex);
                    if (!_nodes[neighbourIndex].Neighbours.Contains(i))
                        _nodes[neighbourIndex].Neighbours.Add(i);
                    _adjacencyMatrix[i, neighbourIndex] = distance;
                    _adjacencyMatrix[neighbourIndex, i] = distance;
                }
            }

            for (int i = 0; i < count; i++)
            {
                NodeProfile node = _nodes[i];
                node.Neighbours.Sort((a, b) => a.CompareTo(b));
            }
        }

        private void ComputeCloseness()
        {
            int count = _nodes.Count;
            if (count == 0)
                return;

            float[] distances = new float[count];
            bool[] visited = new bool[count];

            for (int origin = 0; origin < count; origin++)
            {
                for (int i = 0; i < count; i++)
                {
                    distances[i] = float.PositiveInfinity;
                    visited[i] = false;
                }
                distances[origin] = 0f;

                for (int step = 0; step < count; step++)
                {
                    int current = -1;
                    float bestDistance = float.PositiveInfinity;

                    for (int i = 0; i < count; i++)
                    {
                        if (!visited[i] && distances[i] < bestDistance)
                        {
                            bestDistance = distances[i];
                            current = i;
                        }
                    }

                    if (current == -1)
                        break;

                    visited[current] = true;

                    for (int neighbour = 0; neighbour < count; neighbour++)
                    {
                        float weight = _adjacencyMatrix[current, neighbour];
                        if (weight <= 0f || visited[neighbour])
                            continue;

                        float candidate = bestDistance + weight;
                        if (candidate < distances[neighbour])
                            distances[neighbour] = candidate;
                    }
                }

                float sum = 0f;
                int reachable = 0;

                for (int i = 0; i < count; i++)
                {
                    if (i == origin)
                        continue;

                    float distance = distances[i];
                    if (float.IsPositiveInfinity(distance))
                        continue;

                    sum += distance;
                    reachable++;
                }

                float closeness = 0f;
                if (reachable > 0)
                    closeness = 1f / (AIConstants.StrategicClosenessEpsilon + (sum / reachable));

                _nodes[origin].Closeness = closeness;
            }
        }

        private void ComputeDominationCycles()
        {
            int count = _nodes.Count;
            if (count == 0)
                return;

            var triangleSet = new HashSet<int>();
            var squareSet = new HashSet<int>();

            for (int i = 0; i < count; i++)
            {
                NodeProfile node = _nodes[i];

                foreach (int j in node.Neighbours)
                {
                    if (j == i)
                        continue;

                    foreach (int k in _nodes[j].Neighbours)
                    {
                        if (k == i || k == j)
                            continue;

                        if (IsAdjacent(k, i))
                        {
                            int key = HashCycle(i, j, k);
                            if (triangleSet.Add(key))
                            {
                                _nodes[i].Domination += AIConstants.StrategicTriangleCycleWeight;
                                _nodes[j].Domination += AIConstants.StrategicTriangleCycleWeight;
                                _nodes[k].Domination += AIConstants.StrategicTriangleCycleWeight;
                            }
                        }

                        foreach (int l in _nodes[k].Neighbours)
                        {
                            if (l == i || l == j || l == k)
                                continue;

                            if (IsAdjacent(l, i) && (IsAdjacent(l, j) || IsAdjacent(l, k)))
                            {
                                int key = HashCycle(i, j, k, l);
                                if (squareSet.Add(key))
                                {
                                    _nodes[i].Domination += AIConstants.StrategicSquareCycleWeight;
                                    _nodes[j].Domination += AIConstants.StrategicSquareCycleWeight;
                                    _nodes[k].Domination += AIConstants.StrategicSquareCycleWeight;
                                    _nodes[l].Domination += AIConstants.StrategicSquareCycleWeight;
                                }
                            }
                        }
                    }
                }

                float degreeInfluence = Mathf.Clamp01(node.Neighbours.Count / (float)Mathf.Max(1, AIConstants.StrategicNeighbourCount));
                node.Domination += degreeInfluence * AIConstants.StrategicDegreeWeight;
            }
        }

        private void NormalizeStrategicValues()
        {
            float maxCloseness = 0f;
            float maxDomination = 0f;

            foreach (NodeProfile node in _nodes)
            {
                if (node.Closeness > maxCloseness)
                    maxCloseness = node.Closeness;

                if (node.Domination > maxDomination)
                    maxDomination = node.Domination;
            }

            float closenessNormalization = Mathf.Max(maxCloseness, 0.0001f);
            float dominationNormalization = Mathf.Max(maxDomination, 0.0001f);

            foreach (NodeProfile node in _nodes)
            {
                node.NormalizedCloseness = Mathf.Clamp01(node.Closeness / closenessNormalization);
                node.NormalizedDomination = Mathf.Clamp01(node.Domination / dominationNormalization);
            }
        }

        private bool IsAdjacent(int a, int b)
        {
            if (_adjacencyMatrix == null)
                return false;

            return _adjacencyMatrix[a, b] > 0f;
        }

        private static int HashCycle(int a, int b, int c)
        {
            int min = Mathf.Min(a, Mathf.Min(b, c));
            int max = Mathf.Max(a, Mathf.Max(b, c));
            int mid = a + b + c - min - max;
            return (min << 20) ^ (mid << 10) ^ max;
        }

        private static int HashCycle(int a, int b, int c, int d)
        {
            int[] values = { a, b, c, d };
            Array.Sort(values);
            int hash = 17;
            for (int i = 0; i < values.Length; i++)
                hash = hash * 31 + values[i];
            return hash;
        }
    }
}
