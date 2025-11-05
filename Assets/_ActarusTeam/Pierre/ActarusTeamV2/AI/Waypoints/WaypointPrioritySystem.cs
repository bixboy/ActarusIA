using System;
using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{

    public class WaypointPrioritySystem
    {
        private readonly WaypointMetricSystem _metricSystem = new();
        private readonly WaypointEvaluator _evaluator = new();
        private readonly WaypointMemorySystem _memorySystem = new();
        private readonly WaypointDebugDrawer _debugDrawer = new();

        private float _nextEvaluationTime;
        private int _lastEnvironmentSignature = int.MinValue;
        
        public WaypointSelectionResult SelectBestWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data?.WayPoints == null || data.WayPoints.Count == 0)
                return WaypointSelectionResult.Empty;

            ScoreboardSnapshot scoreboard = CaptureScoreboard(self, data);
            int environmentSignature = ComputeEnvironmentSignature(data, scoreboard);
            bool environmentChanged = environmentSignature != _lastEnvironmentSignature;

            if (!environmentChanged &&
                Time.time < _nextEvaluationTime &&
                _memorySystem.TryGetCachedTarget(out WayPointView cachedWaypoint, out float cachedEta, out float cachedScore, out IReadOnlyList<WayPointView> cachedPredictions))
            {
                _debugDrawer.DrawSelection(self, cachedWaypoint, cachedEta, cachedScore, cachedPredictions);
                return new WaypointSelectionResult(cachedWaypoint, cachedScore, cachedEta, CreatePredictionSnapshot(cachedPredictions));
            }

            Dictionary<WayPointView, WaypointMetrics> metrics = _metricSystem.ComputeMetrics(self, data);
            if (metrics.Count == 0)
            {
                _memorySystem.ProcessEvaluation(null, null, out _, out _, out _, out _);
                _lastEnvironmentSignature = environmentSignature;
                _nextEvaluationTime = Time.time + AIConstants.EvaluationIntervalMin;
                return WaypointSelectionResult.Empty;
            }

            float deficitFactor = ScoreDeficitFactor(scoreboard);
            float aggressionBias = Mathf.Lerp(0.75f, 1.4f, deficitFactor);
            float cautionBias = Mathf.Lerp(1.35f, 0.8f, deficitFactor);
            float endgameUrgency = EndgameUrgency(data);

            var context = new WaypointEvaluationContext(deficitFactor, aggressionBias, cautionBias, endgameUrgency);
            Dictionary<WayPointView, float> rawScores = _evaluator.Evaluate(metrics, context);

            _memorySystem.ProcessEvaluation(metrics, rawScores, out WayPointView bestWaypoint, out float bestScore, out float bestEta, out IReadOnlyList<WayPointView> futureWaypoints);

            _lastEnvironmentSignature = environmentSignature;
            float evaluationInterval = ComputeEvaluationInterval(environmentChanged, bestWaypoint != null);
            _nextEvaluationTime = Time.time + evaluationInterval;

            _debugDrawer.DrawSelection(self, bestWaypoint, bestEta, bestScore, futureWaypoints);
            return new WaypointSelectionResult(bestWaypoint, bestScore, bestEta, CreatePredictionSnapshot(futureWaypoints));
        }

        private static IReadOnlyList<WayPointView> CreatePredictionSnapshot(IReadOnlyList<WayPointView> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return Array.Empty<WayPointView>();

            if (predictions is WayPointView[] array)
                return (WayPointView[])array.Clone();

            return new List<WayPointView>(predictions);
        }

        private static float ScoreDeficitFactor(ScoreboardSnapshot scoreboard)
        {
            if (scoreboard.WaypointCount <= 0)
                return 0.5f;

            float scoreDiff = scoreboard.BestOpponentScore - scoreboard.MyScore;
            float normalized = Mathf.Clamp01((scoreDiff / Mathf.Max(1f, scoreboard.WaypointCount)) * 0.5f + 0.5f);
            return normalized;
        }

        private float EndgameUrgency(GameData data)
        {
            if (data == null)
                return 0f;

            float timeLeft = Mathf.Max(0f, data.timeLeft);
            if (AIConstants.EndgameTimeHorizon <= Mathf.Epsilon)
                return 0f;

            return Mathf.Clamp01(1f - timeLeft / AIConstants.EndgameTimeHorizon);
        }

        private float ComputeEvaluationInterval(bool environmentChanged, bool hasTarget)
        {
            float stability = Mathf.Clamp01(_memorySystem.Stability);
            float confidence = Mathf.Clamp01(_memorySystem.TargetConfidence);

            float interval = Mathf.Lerp(AIConstants.EvaluationIntervalMin, AIConstants.EvaluationIntervalMax, stability);
            float confidenceScale = Mathf.Lerp(1f - AIConstants.EvaluationConfidenceBias, 1f + AIConstants.EvaluationConfidenceBias, confidence);
            interval *= confidenceScale;

            if (!hasTarget)
                interval = Mathf.Max(AIConstants.EvaluationIntervalMin, interval * 0.75f);

            if (environmentChanged)
                interval = Mathf.Max(AIConstants.EvaluationIntervalMin, interval * AIConstants.EnvironmentChangeIntervalMultiplier);

            return Mathf.Clamp(interval, AIConstants.EvaluationIntervalMin, AIConstants.EvaluationIntervalMax);
        }

        private static ScoreboardSnapshot CaptureScoreboard(SpaceShipView self, GameData data)
        {
            var snapshot = new ScoreboardSnapshot
            {
                WaypointCount = data?.WayPoints?.Count ?? 0,
                MyScore = 0,
                BestOpponentScore = 0
            };

            if (self == null)
                return snapshot;

            GameManager manager = GameManager.Instance;
            if (!manager)
                return snapshot;

            snapshot.MyScore = manager.GetScoreForPlayer(self.Owner);
            int bestOpponent = snapshot.MyScore;

            if (data?.SpaceShips != null)
            {
                foreach (SpaceShipView ship in data.SpaceShips)
                {
                    if (ship == null || ship.Owner == self.Owner)
                        continue;

                    int score = manager.GetScoreForPlayer(ship.Owner);
                    if (score > bestOpponent)
                        bestOpponent = score;
                }
            }

            snapshot.BestOpponentScore = bestOpponent;
            return snapshot;
        }

        private static int ComputeEnvironmentSignature(GameData data, ScoreboardSnapshot scoreboard)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + scoreboard.MyScore;
                hash = hash * 31 + scoreboard.BestOpponentScore;
                hash = hash * 31 + scoreboard.WaypointCount;

                if (data?.WayPoints != null)
                {
                    for (int i = 0; i < data.WayPoints.Count; i++)
                    {
                        WayPointView waypoint = data.WayPoints[i];
                        int owner = waypoint != null ? waypoint.Owner : -2;
                        hash = hash * 31 + owner;
                    }
                }

                float timeLeft = data != null ? Mathf.Max(0f, data.timeLeft) : 0f;
                int timeBucket = Mathf.RoundToInt(timeLeft * AIConstants.EnvironmentSignatureTimeFactor);
                hash = hash * 31 + timeBucket;

                return hash;
            }
        }

        private struct ScoreboardSnapshot
        {
            public int MyScore;
            public int BestOpponentScore;
            public int WaypointCount;
        }
    }
}
