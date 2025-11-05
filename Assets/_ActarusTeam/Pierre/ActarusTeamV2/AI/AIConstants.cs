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

        // Target switching rules
        public const float TargetSwitchBias = 0.22f;
        public const float TargetSwitchRatioLocked = 0.18f;
        public const float TargetSwitchRatioFree = 0.08f;
        public const float TargetEtaAdvantage = 0.7f;
        public const float TargetHoldMin = 1.2f;
        public const float TargetHoldMax = 2.6f;

        // Macro evaluation helpers
        public const float EndgameTimeHorizon = 25f;

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
        public static readonly Color DebugLineColor = new(0.2f, 0.9f, 0.2f, 0.8f);
        public static readonly Color DebugSphereColor = new(0.4f, 1f, 0.4f, 0.8f);
    }
}
