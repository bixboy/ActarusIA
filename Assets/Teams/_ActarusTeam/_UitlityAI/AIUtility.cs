using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.Actarus
{
    /// <summary>
    /// Collection of math helpers shared by the waypoint systems.
    /// The helpers avoid duplicating boilerplate vector logic across the modules.
    /// </summary>
    public static class AIUtility
    {
        /// <summary>
        /// Converts a ship orientation (degrees) into a forward direction vector.
        /// </summary>
        public static Vector2 OrientationToVector(float orientationDegrees)
        {
            float radians = orientationDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        /// <summary>
        /// Returns a normalised vector representing the ship's current forward direction.
        /// Prefers the LookAt hint, falling back to the orientation if required.
        /// </summary>
        public static Vector2 GetForwardVector(SpaceShipView ship)
        {
            if (ship == null)
                return Vector2.up;

            if (ship.LookAt.sqrMagnitude > Mathf.Epsilon)
                return ship.LookAt.normalized;

            return OrientationToVector(ship.Orientation);
        }

        /// <summary>
        /// Computes the minimum distance between a point and a line segment.
        /// </summary>
        public static float DistancePointToSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
        {
            Vector2 segment = segmentEnd - segmentStart;
            float segmentLengthSq = segment.sqrMagnitude;
            if (segmentLengthSq <= Mathf.Epsilon)
                return Vector2.Distance(point, segmentStart);

            float projection = Vector2.Dot(point - segmentStart, segment) / segmentLengthSq;
            projection = Mathf.Clamp01(projection);
            Vector2 closest = segmentStart + projection * segment;
            return Vector2.Distance(point, closest);
        }

        /// <summary>
        /// Computes the geometric centre of the provided waypoints.
        /// </summary>
        public static Vector2 ComputeMapCenter(IReadOnlyList<WayPointView> waypoints)
        {
            Vector2 sum = Vector2.zero;
            int count = 0;

            if (waypoints == null)
                return sum;

            for (int i = 0; i < waypoints.Count; i++)
            {
                WayPointView waypoint = waypoints[i];
                if (waypoint == null)
                    continue;

                sum += waypoint.Position;
                count++;
            }

            return count > 0 ? sum / count : Vector2.zero;
        }
    }
}
