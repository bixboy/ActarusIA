using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    public readonly struct WaypointEvaluationContext
    {
        public WaypointEvaluationContext(float deficitFactor, float aggressionBias, float cautionBias, float endgameUrgency)
        {
            DeficitFactor = deficitFactor;
            AggressionBias = aggressionBias;
            CautionBias = cautionBias;
            EndgameUrgency = endgameUrgency;
        }

        public float DeficitFactor { get; }
        public float AggressionBias { get; }
        public float CautionBias { get; }
        public float EndgameUrgency { get; }
    }
    
    public class WaypointEvaluator
    {
        private readonly Dictionary<WayPointView, float> _scores = new();
        
        public Dictionary<WayPointView, float> Evaluate(
            Dictionary<WayPointView, WaypointMetrics> metrics,
            WaypointEvaluationContext context)
        {
            _scores.Clear();

            if (metrics == null || metrics.Count == 0)
                return _scores;

            foreach ((WayPointView waypoint, WaypointMetrics waypointMetrics) in metrics)
            {
                float score = EvaluateWaypointScore(waypointMetrics, context);
                _scores[waypoint] = score;
            }

            return _scores;
        }

        private float EvaluateWaypointScore(WaypointMetrics metrics, WaypointEvaluationContext context)
        {
            float scoreboardBias = Mathf.Lerp(0.85f, 1.35f, context.DeficitFactor);
            float distanceBias = Mathf.Lerp(1.15f, 0.85f, context.DeficitFactor);
            float safetyBias = Mathf.Lerp(1.3f, 0.75f, context.DeficitFactor);
            float timeBias = Mathf.Lerp(1f, 1.2f, context.EndgameUrgency);
            float centralityBias = Mathf.Lerp(0.9f, 1.15f, context.EndgameUrgency);
            float contestBias = Mathf.Lerp(0.85f, 1.25f, context.DeficitFactor);

            float score = 0f;
            score += metrics.Control * AIConstants.ControlWeight * scoreboardBias;
            score += metrics.CaptureSwing * AIConstants.CaptureSwingWeight * scoreboardBias;
            score += metrics.DistanceFactor * AIConstants.DistanceWeight * distanceBias;
            score += metrics.Safety * AIConstants.SafetyWeight * safetyBias;
            score += metrics.OpenArea * AIConstants.OpenAreaWeight * context.CautionBias;
            score += metrics.Centrality * AIConstants.CentralityWeight * centralityBias;
            score += metrics.TravelFactor * AIConstants.TravelWeight * timeBias * context.AggressionBias;
            score += metrics.ArrivalAdvantage * AIConstants.EnemyArrivalWeight * context.AggressionBias;
            score += metrics.Orientation * AIConstants.OrientationWeight;
            score += metrics.Approach * AIConstants.ApproachWeight;

            score -= metrics.Danger * AIConstants.DangerPenaltyWeight * context.CautionBias;
            score -= metrics.EnemyPressure * AIConstants.EnemyPressurePenalty * context.CautionBias;
            score -= metrics.InterceptThreat * AIConstants.EnemyInterceptPenalty * context.CautionBias;
            score -= ComputeTurnPenalty(metrics);

            if (metrics.TravelTime < AIConstants.FastArrivalThreshold)
                score += AIConstants.QuickCaptureBonus * context.AggressionBias;
            else if (metrics.TravelTime > AIConstants.SlowArrivalThreshold)
                score -= AIConstants.SlowArrivalPenalty * context.CautionBias;

            if (float.IsInfinity(metrics.EnemyEta))
            {
                score += AIConstants.UncontestedBonus * scoreboardBias;
            }
            else
            {
                float contest = 1f - Mathf.Clamp01(metrics.EnemyEta / AIConstants.TravelTimeNormalization);
                score += contest * AIConstants.ContestWeight * contestBias;
            }

            score += metrics.CaptureSwing * AIConstants.EndgameSwingWeight * Mathf.Lerp(0.8f, 1.3f, context.EndgameUrgency);

            return score;
        }

        private static float ComputeTurnPenalty(WaypointMetrics metrics)
        {
            float misalignment = 1f - Mathf.Clamp01(metrics.Approach);
            if (misalignment <= 0f)
                return 0f;

            float proximityFactor = Mathf.Lerp(0.6f, 1.15f, Mathf.Clamp01(metrics.DistanceFactor));
            float orientationPenalty = Mathf.Clamp01(1f - Mathf.Clamp01(metrics.Orientation));

            float penalty = misalignment * proximityFactor;
            penalty = Mathf.Lerp(penalty, penalty * 1.35f, orientationPenalty);

            return penalty * AIConstants.TurnPenaltyWeight;
        }
    }
}
