using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    public class WaypointMemorySystem
    {
        private readonly Dictionary<int, float> _lastVisited = new();
        private readonly Dictionary<int, float> _smoothedScores = new();
        private readonly Dictionary<int, float> _rawScores = new();

        private WayPointView _currentTarget;
        private float _currentTargetScore = float.MinValue;
        private float _lastTargetUpdateTime;
        private float _targetLockUntil;

        private const int PredictionCount = 3;

        private WayPointView _cachedBestWaypoint;
        private float _cachedBestScore;
        private float _cachedBestEta;
        private readonly List<WayPointView> _cachedFutureWaypoints = new(PredictionCount);

        private float _scoreVolatility;
        private float _stability = 0.5f;
        private float _targetConfidence = 0.5f;

        public bool TryGetCachedTarget(out WayPointView waypoint, out float eta, out float score, out IReadOnlyList<WayPointView> futureWaypoints)
        {
            waypoint = _cachedBestWaypoint;
            eta = _cachedBestEta;
            score = _cachedBestScore;
            futureWaypoints = _cachedFutureWaypoints;

            return waypoint != null;
        }

        public float Stability => _stability;

        public float TargetConfidence => _targetConfidence;

        public void ProcessEvaluation(
            Dictionary<WayPointView, WaypointMetrics> metrics,
            Dictionary<WayPointView, float> rawScores,
            WaypointStrategicPlanner planner,
            out WayPointView finalTarget,
            out float finalScore,
            out float finalEta,
            out IReadOnlyList<WayPointView> futureWaypoints)
        {
            if (metrics == null || rawScores == null || rawScores.Count == 0)
            {
                _cachedBestWaypoint = null;
                _cachedBestScore = float.MinValue;
                _cachedBestEta = float.PositiveInfinity;
                _cachedFutureWaypoints.Clear();
                finalTarget = null;
                finalScore = float.MinValue;
                finalEta = float.PositiveInfinity;
                futureWaypoints = _cachedFutureWaypoints;
                UpdateStabilityMetrics(0, 0f);
                UpdateTargetConfidenceMetric();
                return;
            }

            Dictionary<WayPointView, float> smoothedScores = new();
            WayPointView evaluatedBest = null;
            float evaluatedBestScore = float.MinValue;
            float evaluatedBestEta = float.PositiveInfinity;
            float smoothingDeltaSum = 0f;
            int smoothingSamples = 0;

            foreach ((WayPointView waypoint, float rawScore) in rawScores)
            {
                if (!metrics.TryGetValue(waypoint, out WaypointMetrics waypointMetrics))
                    continue;

                float scoreWithMomentum = ApplyMomentum(waypointMetrics.Index, rawScore);
                float adjustedScore = ApplyVisitMemory(waypointMetrics.Index, waypoint, scoreWithMomentum);
                float smoothed = SmoothScore(waypointMetrics.Index, adjustedScore, out float smoothingDelta);
                smoothingDeltaSum += smoothingDelta;
                smoothingSamples++;
                smoothedScores[waypoint] = smoothed;

                if (smoothed > evaluatedBestScore)
                {
                    evaluatedBestScore = smoothed;
                    evaluatedBest = waypoint;
                    evaluatedBestEta = waypointMetrics.TravelTime;
                }
            }

            ApplyHysteresis(smoothedScores, evaluatedBest, evaluatedBestScore, evaluatedBestEta, metrics);
            UpdateCachedPredictions(smoothedScores, metrics, planner);

            finalTarget = _cachedBestWaypoint;
            finalScore = _cachedBestScore;
            finalEta = _cachedBestEta;
            futureWaypoints = _cachedFutureWaypoints;

            UpdateStabilityMetrics(smoothingSamples, smoothingDeltaSum);
            UpdateTargetConfidenceMetric();
        }

        private float ApplyMomentum(int waypointIndex, float rawScore)
        {
            float momentumBonus = 0f;
            if (_rawScores.TryGetValue(waypointIndex, out float previousRaw))
                momentumBonus = (rawScore - previousRaw) * AIConstants.ScoreMomentumWeight;

            _rawScores[waypointIndex] = rawScore;
            return rawScore + momentumBonus;
        }

        private float ApplyVisitMemory(int waypointIndex, WayPointView waypoint, float score)
        {
            float adjustedScore = score;

            if (waypoint == _currentTarget)
                adjustedScore += AIConstants.CurrentTargetBonus;
            
            else if (_lastVisited.TryGetValue(waypointIndex, out float lastTime))
            {
                float elapsed = Time.time - lastTime;
                if (elapsed < AIConstants.MemoryCooldown)
                {
                    float t = Mathf.Clamp01(elapsed / AIConstants.MemoryCooldown);
                    adjustedScore *= Mathf.Lerp(AIConstants.MemoryPenaltyMultiplier, 1f, t);
                }
            }

            return adjustedScore;
        }

        private float SmoothScore(int waypointIndex, float value, out float delta)
        {
            if (_smoothedScores.TryGetValue(waypointIndex, out float previous))
            {
                float smoothed = Mathf.Lerp(previous, value, AIConstants.ScoreSmoothing);
                _smoothedScores[waypointIndex] = smoothed;
                delta = Mathf.Abs(smoothed - previous);
                return smoothed;
            }

            _smoothedScores[waypointIndex] = value;
            delta = Mathf.Abs(value);
            return value;
        }

        private void ApplyHysteresis(Dictionary<WayPointView, float> scoredTargets, WayPointView evaluatedBest, float evaluatedScore, float evaluatedEta, Dictionary<WayPointView, WaypointMetrics> metrics)
        {
            WayPointView previousTarget = _currentTarget;
            float previousScore = _currentTargetScore;
            float now = Time.time;

            if (previousTarget != null)
            {
                if (!metrics.ContainsKey(previousTarget) || !scoredTargets.TryGetValue(previousTarget, out float storedScore))
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
                    if (metrics.TryGetValue(previousTarget, out WaypointMetrics prevMetrics))
                        finalEta = prevMetrics.TravelTime;
                }
                else
                {
                    float improvement = finalScore - previousScore;
                    float ratio = previousScore <= 0f ? improvement : improvement / Mathf.Max(Mathf.Abs(previousScore), 0.0001f);
                    
                    bool lockActive = now < _targetLockUntil;
                    float improvementThreshold = lockActive ? AIConstants.TargetSwitchBias : AIConstants.TargetSwitchBias * 0.5f;
                    float ratioThreshold = lockActive ? AIConstants.TargetSwitchRatioLocked : AIConstants.TargetSwitchRatioFree;

                    float previousEta = float.PositiveInfinity;
                    if (metrics.TryGetValue(previousTarget, out WaypointMetrics prevMetrics))
                        previousEta = prevMetrics.TravelTime;
                    
                    bool etaWin = finalEta + AIConstants.TargetEtaAdvantage < previousEta;

                    if (improvement < improvementThreshold && !etaWin)
                    {
                        keepPrevious = true;   
                    }
                    else if (ratio < ratioThreshold && !etaWin)
                    {
                        keepPrevious = true;   
                    }
                    else if (now - _lastTargetUpdateTime < AIConstants.TargetHoldMin * 0.5f && !etaWin)
                    {
                        keepPrevious = true;   
                    }
                }

                if (keepPrevious)
                {
                    finalTarget = previousTarget;
                    finalScore = previousScore;
                    if (metrics.TryGetValue(previousTarget, out WaypointMetrics prevMetrics))
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
                    MarkVisited(previousTarget, metrics);

                if (targetChanged)
                {
                    _currentTarget = finalTarget;
                    _currentTargetScore = finalScore;
                    _lastTargetUpdateTime = now;
                    _targetLockUntil = now + Mathf.Lerp(AIConstants.TargetHoldMin, AIConstants.TargetHoldMax, Mathf.Clamp01(finalScore));

                    if (metrics.TryGetValue(finalTarget, out WaypointMetrics finalMetrics))
                        Debug.Log($"Selected target: WP#{finalMetrics.Index} ETA={finalEta:F1}s Score={finalScore:F2}");
                }
                else
                {
                    _currentTargetScore = finalScore;
                }
            }
        }

        private void UpdateCachedPredictions(
            Dictionary<WayPointView, float> scoredTargets,
            Dictionary<WayPointView, WaypointMetrics> metrics,
            WaypointStrategicPlanner planner)
        {
            _cachedFutureWaypoints.Clear();

            if (scoredTargets == null || scoredTargets.Count == 0 || _cachedBestWaypoint == null)
                return;

            if (planner != null)
            {
                IReadOnlyList<WayPointView> planned = planner.PlanCaptureOrder(
                    _cachedBestWaypoint,
                    metrics,
                    scoredTargets,
                    PredictionCount);

                if (planned != null)
                {
                    foreach (WayPointView waypoint in planned)
                    {
                        if (waypoint == null || waypoint == _cachedBestWaypoint)
                            continue;

                        if (_cachedFutureWaypoints.Count >= PredictionCount)
                            break;

                        _cachedFutureWaypoints.Add(waypoint);
                    }
                }
            }

            if (_cachedFutureWaypoints.Count >= PredictionCount)
                return;

            var fallback = new List<KeyValuePair<WayPointView, float>>(scoredTargets);
            fallback.Sort((a, b) => b.Value.CompareTo(a.Value));

            foreach ((WayPointView waypoint, float _) in fallback)
            {
                if (waypoint == null || waypoint == _cachedBestWaypoint)
                    continue;

                _cachedFutureWaypoints.Add(waypoint);
                if (_cachedFutureWaypoints.Count >= PredictionCount)
                    break;
            }
        }

        private void MarkVisited(WayPointView waypoint, Dictionary<WayPointView, WaypointMetrics> metrics)
        {
            if (waypoint == null)
                return;

            if (metrics.TryGetValue(waypoint, out WaypointMetrics waypointMetrics))
                _lastVisited[waypointMetrics.Index] = Time.time;
        }

        private void UpdateStabilityMetrics(int sampleCount, float deltaSum)
        {
            float averageDelta = sampleCount > 0 ? deltaSum / sampleCount : 0f;
            _scoreVolatility = Mathf.Lerp(_scoreVolatility, averageDelta, AIConstants.VolatilitySmoothing);

            float normalization = Mathf.Max(AIConstants.VolatilityNormalization, 0.0001f);
            float normalized = Mathf.Clamp01(_scoreVolatility / normalization);
            _stability = 1f - normalized;
        }

        private void UpdateTargetConfidenceMetric()
        {
            if (_cachedBestWaypoint == null)
            {
                _targetConfidence = Mathf.Lerp(_targetConfidence, 0f, AIConstants.TargetConfidenceSmoothing);
                return;
            }

            float scoreNormalization = Mathf.Max(AIConstants.TargetConfidenceScoreNormalization, 0.0001f);
            float scoreFactor = Mathf.Clamp01(Mathf.Abs(_cachedBestScore) / scoreNormalization);

            float decayTime = Mathf.Max(AIConstants.TargetConfidenceDecayTime, 0.0001f);
            float age = Time.time - _lastTargetUpdateTime;
            float recency = Mathf.Exp(-age / decayTime);

            float confidence = Mathf.Clamp01(scoreFactor * recency);
            _targetConfidence = Mathf.Lerp(_targetConfidence, confidence, AIConstants.TargetConfidenceSmoothing);
        }
    }
}
