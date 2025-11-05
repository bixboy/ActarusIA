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
        
        public WaypointSelectionResult SelectBestWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data?.WayPoints == null || data.WayPoints.Count == 0)
                return WaypointSelectionResult.Empty;

            if (Time.time < _nextEvaluationTime && _memorySystem.TryGetCachedTarget(out WayPointView cachedWaypoint, out float cachedEta, out float cachedScore, out IReadOnlyList<WayPointView> cachedPredictions))
            {
                _debugDrawer.DrawSelection(self, cachedWaypoint, cachedEta, cachedScore);
                return new WaypointSelectionResult(cachedWaypoint, cachedScore, cachedEta, CreatePredictionSnapshot(cachedPredictions));
            }

            Dictionary<WayPointView, WaypointMetrics> metrics = _metricSystem.ComputeMetrics(self, data);
            if (metrics.Count == 0)
                return WaypointSelectionResult.Empty;

            float deficitFactor = ScoreDeficitFactor(self, data);
            float aggressionBias = Mathf.Lerp(0.75f, 1.4f, deficitFactor);
            float cautionBias = Mathf.Lerp(1.35f, 0.8f, deficitFactor);
            float endgameUrgency = EndgameUrgency(data);

            var context = new WaypointEvaluationContext(deficitFactor, aggressionBias, cautionBias, endgameUrgency);
            Dictionary<WayPointView, float> rawScores = _evaluator.Evaluate(metrics, context);

            _memorySystem.ProcessEvaluation(metrics, rawScores, out WayPointView bestWaypoint, out float bestScore, out float bestEta, out IReadOnlyList<WayPointView> futureWaypoints);

            _nextEvaluationTime = Time.time + AIConstants.EvaluationInterval;

            _debugDrawer.DrawSelection(self, bestWaypoint, bestEta, bestScore);
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

        private float ScoreDeficitFactor(SpaceShipView self, GameData data)
        {
            if (self == null || !GameManager.Instance)
                return 0.5f;

            int myScore = GameManager.Instance.GetScoreForPlayer(self.Owner);
            int bestOpponentScore = myScore;

            if (data?.SpaceShips != null)
            {
                foreach (SpaceShipView ship in data.SpaceShips)
                {
                    if (ship == null || ship.Owner == self.Owner)
                        continue;

                    int score = GameManager.Instance.GetScoreForPlayer(ship.Owner);
                    if (score > bestOpponentScore)
                        bestOpponentScore = score;
                }
            }

            int totalWaypoints = data?.WayPoints?.Count ?? 1;
            float scoreDiff = bestOpponentScore - myScore;
            float normalized = Mathf.Clamp01((scoreDiff / Mathf.Max(1f, totalWaypoints)) * 0.5f + 0.5f);
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
    }
}
