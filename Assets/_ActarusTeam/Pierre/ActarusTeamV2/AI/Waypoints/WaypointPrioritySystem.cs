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
        
        public WayPointView SelectBestWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data?.WayPoints == null || data.WayPoints.Count == 0)
                return null;

            if (Time.time < _nextEvaluationTime && _memorySystem.TryGetCachedTarget(out WayPointView cachedWaypoint, out float cachedEta, out float cachedScore))
            {
                _debugDrawer.DrawSelection(self, cachedWaypoint, cachedEta, cachedScore);
                return cachedWaypoint;
            }

            Dictionary<WayPointView, WaypointMetrics> metrics = _metricSystem.ComputeMetrics(self, data);
            if (metrics.Count == 0)
                return null;

            float deficitFactor = ScoreDeficitFactor(self, data);
            float aggressionBias = Mathf.Lerp(0.75f, 1.4f, deficitFactor);
            float cautionBias = Mathf.Lerp(1.35f, 0.8f, deficitFactor);
            float endgameUrgency = EndgameUrgency(data);

            var context = new WaypointEvaluationContext(deficitFactor, aggressionBias, cautionBias, endgameUrgency);
            Dictionary<WayPointView, float> rawScores = _evaluator.Evaluate(metrics, context);

            _memorySystem.ProcessEvaluation(metrics, rawScores, out WayPointView bestWaypoint, out float bestScore, out float bestEta);

            _nextEvaluationTime = Time.time + AIConstants.EvaluationInterval;

            _debugDrawer.DrawSelection(self, bestWaypoint, bestEta, bestScore);
            return bestWaypoint;
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
