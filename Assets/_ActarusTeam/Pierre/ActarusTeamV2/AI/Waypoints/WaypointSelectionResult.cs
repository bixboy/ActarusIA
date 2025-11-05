using System;
using System.Collections.Generic;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Encapsulates the outcome of a waypoint selection evaluation.
    /// </summary>
    public readonly struct WaypointSelectionResult
    {
        /// <summary>
        /// Gets an empty selection result.
        /// </summary>
        public static WaypointSelectionResult Empty { get; } =
            new WaypointSelectionResult(null, float.MinValue, float.PositiveInfinity, Array.Empty<WayPointView>());

        /// <summary>
        /// Initializes a new instance of the <see cref="WaypointSelectionResult"/> struct.
        /// </summary>
        /// <param name="target">The waypoint that should be targeted next.</param>
        /// <param name="score">The associated evaluation score.</param>
        /// <param name="eta">The estimated travel time to reach the waypoint.</param>
        /// <param name="futureWaypoints">The predicted future waypoints to capture after the target.</param>
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
