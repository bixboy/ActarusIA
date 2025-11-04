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

        private WayPointView _cachedBestWaypoint;
        private float _cachedBestScore;
        private float _cachedBestEta;
        
        public bool TryGetCachedTarget(out WayPointView waypoint, out float eta, out float score)
        {
            waypoint = _cachedBestWaypoint;
            eta = _cachedBestEta;
            score = _cachedBestScore;
            
            return waypoint != null;
        }
        
        public void ProcessEvaluation(Dictionary<WayPointView, WaypointMetrics> metrics, Dictionary<WayPointView, float> rawScores, out WayPointView finalTarget, out float finalScore, out float finalEta)
        {
            if (metrics == null || rawScores == null || rawScores.Count == 0)
            {
                _cachedBestWaypoint = null;
                _cachedBestScore = float.MinValue;
                _cachedBestEta = float.PositiveInfinity;
                finalTarget = null;
                finalScore = float.MinValue;
                finalEta = float.PositiveInfinity;
                return;
            }

            Dictionary<WayPointView, float> smoothedScores = new();
            WayPointView evaluatedBest = null;
            float evaluatedBestScore = float.MinValue;
            float evaluatedBestEta = float.PositiveInfinity;

            foreach ((WayPointView waypoint, float rawScore) in rawScores)
            {
                if (!metrics.TryGetValue(waypoint, out WaypointMetrics waypointMetrics))
                    continue;

                float scoreWithMomentum = ApplyMomentum(waypointMetrics.Index, rawScore);
                float adjustedScore = ApplyVisitMemory(waypointMetrics.Index, waypoint, scoreWithMomentum);
                float smoothed = SmoothScore(waypointMetrics.Index, adjustedScore);
                smoothedScores[waypoint] = smoothed;

                if (smoothed > evaluatedBestScore)
                {
                    evaluatedBestScore = smoothed;
                    evaluatedBest = waypoint;
                    evaluatedBestEta = waypointMetrics.TravelTime;
                }
            }

            ApplyHysteresis(smoothedScores, evaluatedBest, evaluatedBestScore, evaluatedBestEta, metrics);

            finalTarget = _cachedBestWaypoint;
            finalScore = _cachedBestScore;
            finalEta = _cachedBestEta;
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

        private float SmoothScore(int waypointIndex, float value)
        {
            if (_smoothedScores.TryGetValue(waypointIndex, out float previous))
            {
                float smoothed = Mathf.Lerp(previous, value, AIConstants.ScoreSmoothing);
                _smoothedScores[waypointIndex] = smoothed;
                return smoothed;
            }

            _smoothedScores[waypointIndex] = value;
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

        private void MarkVisited(WayPointView waypoint, Dictionary<WayPointView, WaypointMetrics> metrics)
        {
            if (waypoint == null)
                return;

            if (metrics.TryGetValue(waypoint, out WaypointMetrics waypointMetrics))
                _lastVisited[waypointMetrics.Index] = Time.time;
        }
    }
}
