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
        private readonly WaypointStrategicPlanner _planner = new();
        private readonly WaypointDebugDrawer _debugDrawer = new();

        private float _nextEvaluationTime;
        private int _lastEnvironmentSignature = int.MinValue;
        private BehaviorProfileId _lastProfileId = BehaviorProfileId.Balanced;

        public WaypointSelectionResult SelectBestWaypoint(SpaceShipView self, GameData data)
        {
            if (self == null || data?.WayPoints == null || data.WayPoints.Count == 0)
                return WaypointSelectionResult.Empty;

            ScoreboardSnapshot scoreboard = CaptureScoreboard(self, data);
            int environmentSignature = ComputeEnvironmentSignature(data, scoreboard);
            BehaviorProfile profile = BehaviorProfiles.Select(scoreboard.MyScore, scoreboard.BestOpponentScore, scoreboard.WaypointCount);
            bool profileChanged = profile.Id != _lastProfileId;
            bool environmentChanged = environmentSignature != _lastEnvironmentSignature || profileChanged;

            if (!environmentChanged &&
                Time.time < _nextEvaluationTime &&
                _memorySystem.TryGetCachedSelection(out WaypointSelectionResult cached))
            {
                WaypointSelectionResult snapshot = CloneSelection(cached);
                _lastProfileId = profile.Id;
                _debugDrawer.DrawSelection(self, snapshot.TargetWaypoint, snapshot.EstimatedTimeToTarget, snapshot.Score, snapshot.FutureWaypoints);
                return snapshot;
            }

            Dictionary<WayPointView, WaypointMetrics> metrics = _metricSystem.ComputeMetrics(self, data);
            if (metrics.Count == 0)
            {
                WaypointSelectionResult empty = _memorySystem.Decide(null, null, profile, default);
                _lastEnvironmentSignature = environmentSignature;
                _nextEvaluationTime = Time.time + AIConstants.EvaluationIntervalMin;
                _lastProfileId = profile.Id;
                return empty;
            }

            float endgameUrgency = EndgameUrgency(data);

            Dictionary<WayPointView, float> rawScores = _evaluator.Evaluate(metrics, profile, endgameUrgency);
            WaypointStrategicPlanner.StrategicPlanResult plan = _planner.Plan(metrics, rawScores);
            WaypointSelectionResult selection = _memorySystem.Decide(metrics, rawScores, profile, plan);

            _lastEnvironmentSignature = environmentSignature;
            _lastProfileId = profile.Id;
            float evaluationInterval = ComputeEvaluationInterval(environmentChanged, selection.TargetWaypoint != null, profile);
            _nextEvaluationTime = Time.time + evaluationInterval;

            WaypointSelectionResult snapshotSelection = CloneSelection(selection);
            _debugDrawer.DrawSelection(self, snapshotSelection.TargetWaypoint, snapshotSelection.EstimatedTimeToTarget, snapshotSelection.Score, snapshotSelection.FutureWaypoints);
            return snapshotSelection;
        }

        private static WaypointSelectionResult CloneSelection(in WaypointSelectionResult selection)
        {
            if (!selection.HasTarget)
                return WaypointSelectionResult.Empty;

            return new WaypointSelectionResult(
                selection.TargetWaypoint,
                selection.Score,
                selection.EstimatedTimeToTarget,
                CreatePredictionSnapshot(selection.FutureWaypoints));
        }

        private static IReadOnlyList<WayPointView> CreatePredictionSnapshot(IReadOnlyList<WayPointView> predictions)
        {
            if (predictions == null || predictions.Count == 0)
                return Array.Empty<WayPointView>();

            if (predictions is WayPointView[] array)
                return (WayPointView[])array.Clone();

            return new List<WayPointView>(predictions);
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

        private float ComputeEvaluationInterval(bool environmentChanged, bool hasTarget, in BehaviorProfile profile)
        {
            float stability = Mathf.Clamp01(_memorySystem.Stability);
            float confidence = Mathf.Clamp01(_memorySystem.TargetConfidence);

            float interval = Mathf.Lerp(AIConstants.EvaluationIntervalMin, AIConstants.EvaluationIntervalMax, stability);
            float confidenceWeight = AIConstants.EvaluationConfidenceBias * Mathf.Clamp(profile.ConfidenceBias, 0.5f, 1.5f);
            float confidenceScale = Mathf.Lerp(1f - confidenceWeight, 1f + confidenceWeight, confidence);
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
