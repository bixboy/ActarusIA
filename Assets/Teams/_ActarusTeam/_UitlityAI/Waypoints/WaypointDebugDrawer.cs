using System.Collections.Generic;
using UnityEngine;
using DoNotModify;

namespace Teams.Actarus
{
    public class WaypointDebugDrawer
    {
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DrawSelection(SpaceShipView self, WayPointView waypoint, float eta, float score, IReadOnlyList<WayPointView> predictedWaypoints)
        {
            if (self == null || waypoint == null)
                return;

            const float textOffset = 0.75f;
            const float lineDuration = 0.25f;

            Debug.DrawLine(self.Position, waypoint.Position, AIConstants.DebugLineColor, lineDuration);
            DebugExtension.DrawSphere(waypoint.Position, AIConstants.DebugSphereColor, AIConstants.DebugSphereSize);
            DebugExtension.DrawText(waypoint.Position + Vector2.up * textOffset, $"ETA={eta:F1}s | SCORE={score:F2}", Color.white, AIConstants.DebugTextSize, lineDuration);

            DrawPredictions(waypoint, predictedWaypoints, textOffset, lineDuration);
        }

        private static void DrawPredictions(WayPointView origin, IReadOnlyList<WayPointView> predictedWaypoints, float textOffset, float lineDuration)
        {
            if (origin == null || predictedWaypoints == null || predictedWaypoints.Count == 0)
                return;

            int previewCount = Mathf.Min(predictedWaypoints.Count, AIConstants.DebugPredictionPreviewCount);
            if (previewCount <= 0)
                return;

            Vector2 previousPosition = origin.Position;
            const float labelStride = 0.32f;

            for (int i = 0; i < previewCount; i++)
            {
                WayPointView nextWaypoint = predictedWaypoints[i];
                if (nextWaypoint == null)
                    continue;

                float interpolation = (i + 1f) / (previewCount + 1f);
                Color lineColor = Color.Lerp(AIConstants.DebugLineColor, AIConstants.DebugPredictionLineColor, interpolation);

                Debug.DrawLine(previousPosition, nextWaypoint.Position, lineColor, lineDuration);
                DebugExtension.DrawSphere(nextWaypoint.Position, AIConstants.DebugPredictionSphereColor, AIConstants.DebugPredictionSphereSize);

                Vector2 labelPosition = nextWaypoint.Position + Vector2.up * (textOffset + labelStride * (i + 1));
                DebugExtension.DrawText(labelPosition, $"P{i + 1}", AIConstants.DebugPredictionTextColor, AIConstants.DebugTextSize * AIConstants.DebugPredictionTextScale, lineDuration);

                previousPosition = nextWaypoint.Position;
            }
        }
    }
}
