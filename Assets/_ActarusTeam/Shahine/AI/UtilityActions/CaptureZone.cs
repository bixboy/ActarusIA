using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class CaptureZone : UtilityAction
    {
        public CaptureZone(Blackboard bb) : base(bb) { }

        public override float ComputeUtility()
        {
            // Si aucune cible, utilité = 0
            if (_bb.targetWaypoint == null)
                return 0f;

            // Utilité basée sur la proximité et la neutralité de la balise
            float dist = Vector2.Distance(_bb.myShip.Position, _bb.targetWaypoint.Position);
            float distFactor = Mathf.Clamp01(1f - (dist / 10f)); // plus proche = score plus haut
            float ownerFactor = _bb.targetWaypoint.Owner == -1 ? 1f : 0.8f; // neutre > ennemie

            return distFactor * ownerFactor;
        }

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (_bb.targetWaypoint == null)
                return input;

            // Calcul d'orientation inertielle
            float targetOrient = AimingHelpers.ComputeSteeringOrient(
                _bb.myShip,
                _bb.targetWaypoint.Position,
                1.1f
            );

            input.targetOrientation= targetOrient;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.myShip.Orientation, targetOrient));

            // Propulsion lissée selon l'alignement
            if (angleDiff < 35f)
                input.thrust = Mathf.Lerp(0.3f, 1f, 1 - angleDiff / 35f);
            else
                input.thrust = 0f;

            // Freinage automatique à l’approche
            float distance = Vector2.Distance(_bb.myShip.Position, _bb.targetWaypoint.Position);
            if (distance < 0.6f)
                input.thrust = Mathf.Lerp(input.thrust, 0f, 0.15f);

            return input;
        }
    }
}