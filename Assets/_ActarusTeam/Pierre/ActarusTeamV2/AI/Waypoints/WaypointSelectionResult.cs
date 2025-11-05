using System;
using System.Collections.Generic;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{

    public readonly struct WaypointSelectionResult
    {

        public static WaypointSelectionResult Empty { get; } = new (null, float.MinValue, float.PositiveInfinity, Array.Empty<WayPointView>());
        
        public WaypointSelectionResult(WayPointView target, float score, float eta, IReadOnlyList<WayPointView> futureWaypoints)
        {
            TargetWaypoint = target;
            Score = score;
            EstimatedTimeToTarget = eta;
            FutureWaypoints = futureWaypoints ?? Array.Empty<WayPointView>();
        }

        /// <summary>
        /// Gets the primary waypoint to capture next.
        /// </summary>
        public WayPointView TargetWaypoint { get; }

        /// <summary>
        /// Gets the evaluation score associated with the target waypoint.
        /// </summary>
        public float Score { get; }

        /// <summary>
        /// Gets the estimated time required to reach the target waypoint.
        /// </summary>
        public float EstimatedTimeToTarget { get; }

        /// <summary>
        /// Gets the predicted follow-up waypoints that the ship should aim for afterwards.
        /// </summary>
        public IReadOnlyList<WayPointView> FutureWaypoints { get; }

        /// <summary>
        /// Gets a value indicating whether the selection contains a valid target.
        /// </summary>
        public bool HasTarget => TargetWaypoint != null;
    }
}
