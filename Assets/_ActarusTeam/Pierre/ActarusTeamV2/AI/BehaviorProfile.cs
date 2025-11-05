using UnityEngine;

namespace Teams.ActarusControllerV2.pierre
{
    public enum BehaviorProfileId
    {
        Defensive,
        Balanced,
        Aggressive
    }

    public readonly struct BehaviorProfile
    {
        public BehaviorProfile(BehaviorProfileId id, float deficitFactor, float aggressionBias, float cautionBias, float scoreSmoothing, float confidenceBias)
        {
            Id = id;
            DeficitFactor = deficitFactor;
            AggressionBias = aggressionBias;
            CautionBias = cautionBias;
            ScoreSmoothing = Mathf.Clamp01(scoreSmoothing);
            ConfidenceBias = Mathf.Max(0.1f, confidenceBias);
        }

        public BehaviorProfileId Id { get; }

        public float DeficitFactor { get; }

        public float AggressionBias { get; }

        public float CautionBias { get; }

        public float ScoreSmoothing { get; }

        public float ConfidenceBias { get; }
    }

    public static class BehaviorProfiles
    {
        private const float AggressiveSmoothing = 0.42f;
        private const float BalancedSmoothing = 0.55f;
        private const float DefensiveSmoothing = 0.68f;

        private const float AggressiveConfidence = 0.85f;
        private const float BalancedConfidence = 1.0f;
        private const float DefensiveConfidence = 1.15f;

        private const float AggressiveAggression = 1.25f;
        private const float BalancedAggression = 1.0f;
        private const float DefensiveAggression = 0.85f;

        private const float AggressiveCaution = 0.85f;
        private const float BalancedCaution = 1.0f;
        private const float DefensiveCaution = 1.25f;

        public static BehaviorProfile Select(int myScore, int bestOpponent, int waypointCount)
        {
            waypointCount = Mathf.Max(1, waypointCount);
            float diff = myScore - bestOpponent;
            float normalized = Mathf.Clamp(diff / waypointCount, -1f, 1f);
            float deficitFactor = Mathf.Clamp01(0.5f + (-normalized * 0.5f));

            BehaviorProfileId id;
            if (normalized <= -0.35f)
            {
                id = BehaviorProfileId.Aggressive;
                return new BehaviorProfile(id, deficitFactor, AggressiveAggression, AggressiveCaution, AggressiveSmoothing, AggressiveConfidence);
            }

            if (normalized >= 0.35f)
            {
                id = BehaviorProfileId.Defensive;
                return new BehaviorProfile(id, deficitFactor, DefensiveAggression, DefensiveCaution, DefensiveSmoothing, DefensiveConfidence);
            }

            id = BehaviorProfileId.Balanced;
            return new BehaviorProfile(id, deficitFactor, BalancedAggression, BalancedCaution, BalancedSmoothing, BalancedConfidence);
        }
    }
}
