using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.Actarus
{
    public sealed class WaypointStrategicPlanner
    {
        public readonly struct StrategicPlanResult
        {
            private readonly WaypointStrategicPlanner _planner;
            private readonly Dictionary<WayPointView, WaypointMetrics> _metrics;
            private readonly Dictionary<WayPointView, float> _scores;

            internal StrategicPlanResult(WaypointStrategicPlanner planner,
                Dictionary<WayPointView, WaypointMetrics> metrics,
                Dictionary<WayPointView, float> scores)
            {
                _planner = planner;
                _metrics = metrics;
                _scores = scores;
            }

            public bool IsValid => _planner != null && _metrics != null && _metrics.Count > 0;

            public void FillFuturePath(WayPointView start, int desiredCount, List<WayPointView> buffer)
            {
                if (!IsValid || start == null || desiredCount <= 0 || buffer == null)
                    return;

                _planner.FillFuturePath(start, desiredCount, _metrics, _scores, buffer);
            }
        }

        private sealed class Node
        {
            public WayPointView Waypoint;
            public Vector2 Position;
            public readonly List<Neighbour> Neighbours = new(6);
            public float Closeness;
            public float Domination;
            public float NormalizedCloseness;
            public float NormalizedDomination;
        }

        private struct Neighbour
        {
            public int Index;
            public float Distance;
        }

        private readonly List<Node> _nodes = new();
        private readonly Dictionary<WayPointView, int> _indices = new();
        private readonly List<(float distance, int index)> _distanceBuffer = new();
        private readonly List<int> _candidateBuffer = new();
        private readonly List<int> _searchPath = new();
        private readonly List<int> _bestPath = new();

        private float[] _distanceScratch;
        private bool[] _visitedScratch;
        private bool[] _searchVisited;
        private float _bestScore;

        public StrategicPlanResult Plan(Dictionary<WayPointView, WaypointMetrics> metrics, Dictionary<WayPointView, float> scores)
        {
            BuildGraph(metrics);

            if (_nodes.Count == 0)
                return default;

            ComputeCloseness();
            ComputeDomination();
            NormalizeStrategicValues();

            return new StrategicPlanResult(this, metrics, scores);
        }

        private void BuildGraph(Dictionary<WayPointView, WaypointMetrics> metrics)
        {
            _indices.Clear();

            if (metrics == null || metrics.Count == 0)
            {
                _nodes.Clear();
                return;
            }

            int index = 0;
            foreach (KeyValuePair<WayPointView, WaypointMetrics> pair in metrics)
            {
                Node node;
                if (index < _nodes.Count)
                {
                    node = _nodes[index];
                    node.Neighbours.Clear();
                }
                else
                {
                    node = new Node();
                    _nodes.Add(node);
                }

                node.Waypoint = pair.Key;
                node.Position = pair.Value.Position;
                node.Closeness = 0f;
                node.Domination = 0f;
                node.NormalizedCloseness = 0f;
                node.NormalizedDomination = 0f;

                _indices[node.Waypoint] = index;
                index++;
            }

            if (index < _nodes.Count)
                _nodes.RemoveRange(index, _nodes.Count - index);

            BuildNeighbourhoods();
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
                Node node = _nodes[i];
                _distanceBuffer.Clear();

                for (int j = 0; j < count; j++)
                {
                    if (i == j)
                        continue;

                    float distance = Vector2.Distance(node.Position, _nodes[j].Position);
                    _distanceBuffer.Add((distance, j));
                }

                _distanceBuffer.Sort((a, b) => a.distance.CompareTo(b.distance));
                int limit = Mathf.Min(AIConstants.StrategicNeighbourCount, _distanceBuffer.Count);

                for (int k = 0; k < limit; k++)
                {
                    (float distance, int neighbourIndex) = _distanceBuffer[k];
                    AddNeighbour(i, neighbourIndex, distance);
                    AddNeighbour(neighbourIndex, i, distance);
                }
            }
        }

        private void AddNeighbour(int from, int to, float distance)
        {
            if (from < 0 || from >= _nodes.Count || to < 0 || to >= _nodes.Count)
                return;

            List<Neighbour> neighbours = _nodes[from].Neighbours;
            for (int i = 0; i < neighbours.Count; i++)
            {
                if (neighbours[i].Index == to)
                    return;
            }

            neighbours.Add(new Neighbour { Index = to, Distance = distance });
        }

        private void ComputeCloseness()
        {
            int count = _nodes.Count;
            EnsureScratchCapacity(count);

            for (int origin = 0; origin < count; origin++)
            {
                for (int i = 0; i < count; i++)
                {
                    _distanceScratch[i] = float.PositiveInfinity;
                    _visitedScratch[i] = false;
                }

                _distanceScratch[origin] = 0f;

                for (int step = 0; step < count; step++)
                {
                    int current = -1;
                    float best = float.PositiveInfinity;

                    for (int i = 0; i < count; i++)
                    {
                        if (!_visitedScratch[i] && _distanceScratch[i] < best)
                        {
                            best = _distanceScratch[i];
                            current = i;
                        }
                    }

                    if (current == -1)
                        break;

                    _visitedScratch[current] = true;

                    List<Neighbour> neighbours = _nodes[current].Neighbours;
                    for (int i = 0; i < neighbours.Count; i++)
                    {
                        int index = neighbours[i].Index;
                        if (_visitedScratch[index])
                            continue;

                        float candidate = best + neighbours[i].Distance;
                        if (candidate < _distanceScratch[index])
                            _distanceScratch[index] = candidate;
                    }
                }

                float sum = 0f;
                int reachable = 0;
                for (int i = 0; i < count; i++)
                {
                    if (i == origin)
                        continue;

                    float distance = _distanceScratch[i];
                    if (float.IsPositiveInfinity(distance))
                        continue;

                    sum += distance;
                    reachable++;
                }

                _nodes[origin].Closeness = reachable > 0 ? 1f / (AIConstants.StrategicClosenessEpsilon + (sum / reachable)) : 0f;
            }
        }

        private void ComputeDomination()
        {
            int count = _nodes.Count;
            if (count == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                List<Neighbour> neighbours = _nodes[i].Neighbours;

                for (int aIdx = 0; aIdx < neighbours.Count; aIdx++)
                {
                    int a = neighbours[aIdx].Index;
                    
                    if (a <= i)
                        continue;

                    List<Neighbour> aNeighbours = _nodes[a].Neighbours;
                    for (int bIdx = 0; bIdx < aNeighbours.Count; bIdx++)
                    {
                        int b = aNeighbours[bIdx].Index;
                        if (b <= a || b == i)
                            continue;

                        if (AreAdjacent(b, i))
                        {
                            float weight = AIConstants.StrategicTriangleCycleWeight;
                            _nodes[i].Domination += weight;
                            _nodes[a].Domination += weight;
                            _nodes[b].Domination += weight;
                        }

                        List<Neighbour> bNeighbours = _nodes[b].Neighbours;
                        for (int cIdx = 0; cIdx < bNeighbours.Count; cIdx++)
                        {
                            int c = bNeighbours[cIdx].Index;
                            if (c == i || c == a || c == b || c <= b || c <= i)
                                continue;

                            if (AreAdjacent(c, i) && (AreAdjacent(c, a) || AreAdjacent(c, b)))
                            {
                                float weight = AIConstants.StrategicSquareCycleWeight;
                                _nodes[i].Domination += weight;
                                _nodes[a].Domination += weight;
                                _nodes[b].Domination += weight;
                                _nodes[c].Domination += weight;
                            }
                        }
                    }
                }

                float degreeInfluence = Mathf.Clamp01(neighbours.Count / (float)Mathf.Max(1, AIConstants.StrategicNeighbourCount));
                _nodes[i].Domination += degreeInfluence * AIConstants.StrategicDegreeWeight;
            }
        }

        private void NormalizeStrategicValues()
        {
            float maxCloseness = 0f;
            float maxDomination = 0f;

            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Closeness > maxCloseness)
                    maxCloseness = _nodes[i].Closeness;
                
                if (_nodes[i].Domination > maxDomination)
                    maxDomination = _nodes[i].Domination;
            }

            float closenessNorm = Mathf.Max(maxCloseness, 0.0001f);
            float dominationNorm = Mathf.Max(maxDomination, 0.0001f);

            for (int i = 0; i < _nodes.Count; i++)
            {
                _nodes[i].NormalizedCloseness = Mathf.Clamp01(_nodes[i].Closeness / closenessNorm);
                _nodes[i].NormalizedDomination = Mathf.Clamp01(_nodes[i].Domination / dominationNorm);
            }
        }

        private void FillFuturePath(
            WayPointView start,
            int desiredCount,
            Dictionary<WayPointView, WaypointMetrics> metrics,
            Dictionary<WayPointView, float> scores,
            List<WayPointView> output)
        {
            output.Clear();

            if (!_indices.TryGetValue(start, out int startIndex))
                return;

            int count = _nodes.Count;
            EnsureScratchCapacity(count);
            
            for (int i = 0; i < count; i++)
                _searchVisited[i] = false;

            _searchPath.Clear();
            _bestPath.Clear();
            _bestScore = float.NegativeInfinity;

            _searchVisited[startIndex] = true;
            Search(startIndex, 0, 0f, Mathf.Min(desiredCount, AIConstants.StrategicPredictionDepth), metrics, scores);
            _searchVisited[startIndex] = false;

            if (_bestPath.Count == 0)
            {
                _candidateBuffer.Clear();
                for (int i = 0; i < count; i++)
                {
                    if (i == startIndex)
                        continue;
                    
                    _candidateBuffer.Add(i);
                }

                if (_candidateBuffer.Count > 1)
                    SortCandidates(_candidateBuffer, metrics, scores);

                int limit = Mathf.Min(desiredCount, _candidateBuffer.Count);
                for (int i = 0; i < limit; i++)
                {
                    output.Add(_nodes[_candidateBuffer[i]].Waypoint);   
                }

                return;
            }

            int length = Mathf.Min(desiredCount, _bestPath.Count);
            for (int i = 0; i < length; i++)
                output.Add(_nodes[_bestPath[i]].Waypoint);
        }

        private void Search(int current, int depth, float accumulated, int desiredDepth, Dictionary<WayPointView, WaypointMetrics> metrics, Dictionary<WayPointView, float> scores)
        {
            if (depth >= desiredDepth)
            {
                CommitPath(depth, accumulated);
                return;
            }

            BuildCandidates(current, metrics, scores);
            int limit = Mathf.Min(_candidateBuffer.Count, AIConstants.StrategicBranchingFactor);

            if (limit == 0)
            {
                CommitPath(depth, accumulated);
                return;
            }

            for (int i = 0; i < limit; i++)
            {
                int next = _candidateBuffer[i];
                if (_searchVisited[next])
                    continue;

                float step = EvaluateStep(current, next, depth, metrics, scores);
                if (_searchPath.Count > depth)
                {
                    _searchPath[depth] = next;    
                }

                else
                {
                    _searchPath.Add(next);   
                }

                _searchVisited[next] = true;
                Search(next, depth + 1, accumulated + step, desiredDepth, metrics, scores);
                _searchVisited[next] = false;
            }

            CommitPath(depth, accumulated);
        }

        private void CommitPath(int depth, float score)
        {
            if (depth == 0)
                return;

            if (depth > _bestPath.Count || (depth == _bestPath.Count && score > _bestScore))
            {
                _bestPath.Clear();
                for (int i = 0; i < depth; i++)
                {
                    _bestPath.Add(_searchPath[i]);   
                }
                
                _bestScore = score;
            }
        }

        private void BuildCandidates(int index, Dictionary<WayPointView, WaypointMetrics> metrics, Dictionary<WayPointView, float> scores)
        {
            _candidateBuffer.Clear();
            List<Neighbour> neighbours = _nodes[index].Neighbours;

            for (int i = 0; i < neighbours.Count; i++)
            {
                _candidateBuffer.Add(neighbours[i].Index);   
            }

            if (_candidateBuffer.Count == 0)
            {
                for (int i = 0; i < _nodes.Count; i++)
                {
                    if (i == index)
                        continue;
                    
                    _candidateBuffer.Add(i);
                }
            }

            if (_candidateBuffer.Count > 1)
                SortCandidates(_candidateBuffer, metrics, scores);
        }

        private void SortCandidates(List<int> candidates, Dictionary<WayPointView, WaypointMetrics> metrics, Dictionary<WayPointView, float> scores)
        {
            for (int i = 0; i < candidates.Count - 1; i++)
            {
                int bestIndex = i;
                float bestScore = EvaluateHeuristic(candidates[i], metrics, scores);

                for (int j = i + 1; j < candidates.Count; j++)
                {
                    float candidateScore = EvaluateHeuristic(candidates[j], metrics, scores);
                    if (candidateScore > bestScore)
                    {
                        bestScore = candidateScore;
                        bestIndex = j;
                    }
                }

                if (bestIndex != i)
                {
                    (candidates[i], candidates[bestIndex]) = (candidates[bestIndex], candidates[i]);
                }
            }
        }

        private float EvaluateHeuristic(int index, Dictionary<WayPointView, WaypointMetrics> metrics, Dictionary<WayPointView, float> scores)
        {
            Node node = _nodes[index];

            float rawScore = 0f;
            if (scores != null && scores.TryGetValue(node.Waypoint, out float stored))
                rawScore = stored;

            float swing = 0f;
            if (metrics != null && metrics.TryGetValue(node.Waypoint, out WaypointMetrics waypointMetrics))
                swing = waypointMetrics.CaptureSwing + waypointMetrics.Control;

            float value = rawScore * AIConstants.StrategicScoreWeight;
            value += node.NormalizedCloseness * AIConstants.StrategicCentralityWeight;
            value += node.NormalizedDomination * AIConstants.StrategicDominationWeight;
            value += swing * AIConstants.StrategicSwingWeight;

            return value;
        }

        private float EvaluateStep(int from, int to, int depth, Dictionary<WayPointView, WaypointMetrics> metrics, Dictionary<WayPointView, float> scores)
        {
            float heuristic = EvaluateHeuristic(to, metrics, scores);
            float distance = Vector2.Distance(_nodes[from].Position, _nodes[to].Position);
            float controlBias = Mathf.Clamp01(1f - distance / AIConstants.StrategicEdgeNormalization);

            float travelPenalty = 0f;
            if (metrics != null && metrics.TryGetValue(_nodes[to].Waypoint, out WaypointMetrics waypointMetrics))
            {
                float normalized = Mathf.Clamp01(waypointMetrics.TravelTime / AIConstants.StrategicTravelNormalization);
                travelPenalty = normalized;
            }

            float discount = Mathf.Pow(AIConstants.StrategicFutureDiscount, depth);
            float score = heuristic * discount;
            
            score += controlBias * AIConstants.StrategicAdjacencyWeight * discount;
            score -= travelPenalty * AIConstants.StrategicTravelPenalty;
            
            return score;
        }

        private bool AreAdjacent(int a, int b)
        {
            if (a < 0 || a >= _nodes.Count || b < 0 || b >= _nodes.Count)
                return false;

            List<Neighbour> neighbours = _nodes[a].Neighbours;
            for (int i = 0; i < neighbours.Count; i++)
            {
                if (neighbours[i].Index == b)
                    return true;
            }

            return false;
        }

        private void EnsureScratchCapacity(int count)
        {
            if (_distanceScratch == null || _distanceScratch.Length < count)
                _distanceScratch = new float[count];
            
            if (_visitedScratch == null || _visitedScratch.Length < count)
                _visitedScratch = new bool[count];
            
            if (_searchVisited == null || _searchVisited.Length < count)
                _searchVisited = new bool[count];
        }
    }
}
