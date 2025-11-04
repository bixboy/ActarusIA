using UnityEngine;
using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    public class WaypointDebugDrawer
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DrawSelection(SpaceShipView self, WayPointView waypoint, float eta, float score)
        {
            if (self == null || waypoint == null)
                return;

            const float textOffset = 0.75f;
            const float lineDuration = 0.25f;

            Debug.DrawLine(self.Position, waypoint.Position, AIConstants.DebugLineColor, lineDuration);
            DebugExtension.DrawSphere(waypoint.Position, AIConstants.DebugSphereColor, AIConstants.DebugSphereSize);
            DebugExtension.DrawText(waypoint.Position + Vector2.up * textOffset, $"ETA={eta:F1}s | SCORE={score:F2}", Color.white, AIConstants.DebugTextSize, lineDuration);
        }
    }
}
