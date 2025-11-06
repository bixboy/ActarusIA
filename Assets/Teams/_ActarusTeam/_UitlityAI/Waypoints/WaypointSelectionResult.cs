using System;
using System.Collections.Generic;
using DoNotModify;

namespace Teams.Actarus
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
        
        public WayPointView TargetWaypoint { get; }
        
        public float Score { get; }
        
        public float EstimatedTimeToTarget { get; }
        
        public IReadOnlyList<WayPointView> FutureWaypoints { get; }
        
        public bool HasTarget => TargetWaypoint != null;
    }
}
