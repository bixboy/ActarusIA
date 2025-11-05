using UnityEngine;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Central repository for AI tuning constants shared across the waypoint evaluation systems.
    /// Keeping the parameters grouped in a single place simplifies balancing and testing.
    /// </summary>
    public static class AIConstants
    {
        // Evaluation cadence
        public const float EvaluationInterval = 0.25f;
        public const float EvaluationIntervalMin = 0.18f;
        public const float EvaluationIntervalMax = 0.5f;
        public const float EvaluationConfidenceBias = 0.3f;
        public const float EnvironmentChangeIntervalMultiplier = 0.6f;
        public const float EnvironmentSignatureTimeFactor = 0.25f;

        // Spatial normalisation factors
        public const float DistanceNormalization = 14f;
        public const float TravelTimeNormalization = 7.5f;
        public const float FastArrivalThreshold = 3f;
        public const float SlowArrivalThreshold = 10f;
        public const float CentralityNormalization = 12f;

        // Memory tuning
        public const float MemoryCooldown = 8f;
        public const float ScoreSmoothing = 0.55f;
        public const float ScoreMomentumWeight = 0.18f;
        public const float MemoryPenaltyMultiplier = 0.5f;
        public const float CurrentTargetBonus = 0.2f;
        public const float VolatilitySmoothing = 0.35f;
        public const float VolatilityNormalization = 0.65f;
        public const float TargetConfidenceScoreNormalization = 1.6f;
        public const float TargetConfidenceDecayTime = 2.4f;
        public const float TargetConfidenceSmoothing = 0.25f;

        // Target switching rules
        public const float TargetSwitchBias = 0.22f;
        public const float TargetSwitchRatioLocked = 0.18f;
        public const float TargetSwitchRatioFree = 0.08f;
        public const float TargetEtaAdvantage = 0.7f;
        public const float TargetHoldMin = 1.2f;
        public const float TargetHoldMax = 2.6f;

        // Macro evaluation helpers
        public const float EndgameTimeHorizon = 25f;

        // Strategic planner tuning
        public const int StrategicNeighbourCount = 4;
        public const int StrategicPredictionDepth = 3;
        public const int StrategicBranchingFactor = 4;
        public const float StrategicFutureDiscount = 0.82f;
        public const float StrategicScoreWeight = 1.05f;
        public const float StrategicCentralityWeight = 0.55f;
        public const float StrategicDominationWeight = 0.5f;
        public const float StrategicSwingWeight = 0.45f;
        public const float StrategicAdjacencyWeight = 0.35f;
        public const float StrategicTravelPenalty = 0.25f;
        public const float StrategicClosenessEpsilon = 0.35f;
        public const float StrategicTriangleCycleWeight = 1.1f;
        public const float StrategicSquareCycleWeight = 0.75f;
        public const float StrategicDegreeWeight = 0.35f;
        public const float StrategicTravelNormalization = 8.5f;
        public const float StrategicEdgeNormalization = 18f;

        // Environmental hazard constants
        public const float MineDangerReach = 4f;
        public const float AsteroidBuffer = 1.25f;
        public const float ProjectileAvoidanceRadius = 1.4f;
        public const float DangerPredictionHorizon = 3f;
        public const float EnemyPressureRadius = 8.5f;
        public const float EnemyInterceptRadius = 9.5f;
        public const float EnemyFireLaneReach = 11f;

        // Hazard weights
        public const float MineDangerWeight = 1.1f;
        public const float AsteroidDangerWeight = 0.7f;
        public const float ProjectileDangerWeight = 1.25f;
        public const float EnemyLaneDangerWeight = 1.35f;

        // Scoring weights
        public const float ControlWeight = 1.15f;
        public const float CaptureSwingWeight = 0.9f;
        public const float DistanceWeight = 0.45f;
        public const float SafetyWeight = 0.55f;
        public const float DangerPenaltyWeight = 0.35f;
        public const float OpenAreaWeight = 0.3f;
        public const float CentralityWeight = 0.25f;
        public const float TravelWeight = 0.65f;
        public const float EnemyArrivalWeight = 0.7f;
        public const float ContestWeight = 0.3f;
        public const float OrientationWeight = 0.18f;
        public const float ApproachWeight = 0.12f;
        public const float TurnPenaltyWeight = 0.6f;
        public const float EnemyPressurePenalty = 0.55f;
        public const float EnemyInterceptPenalty = 0.45f;
        public const float EndgameSwingWeight = 0.4f;
        public const float UncontestedBonus = 0.18f;
        public const float QuickCaptureBonus = 0.25f;
        public const float SlowArrivalPenalty = 0.25f;

        // Debug visuals
        public const float DebugSphereSize = 0.3f;
        public const float DebugTextSize = 0.8f;
        public const float DebugPredictionSphereSize = 0.22f;
        public const float DebugPredictionTextScale = 0.65f;
        public const int DebugPredictionPreviewCount = 3;
        public static readonly Color DebugLineColor = new(0.2f, 0.9f, 0.2f, 0.8f);
        public static readonly Color DebugSphereColor = new(0.4f, 1f, 0.4f, 0.8f);
        public static readonly Color DebugPredictionLineColor = new(0.2f, 0.55f, 1f, 0.7f);
        public static readonly Color DebugPredictionSphereColor = new(0.35f, 0.7f, 1f, 0.75f);
        public static readonly Color DebugPredictionTextColor = new(0.85f, 0.95f, 1f, 0.9f);
    }
}
