using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class CaptureZone : UtilityAction
    {
        // --- Etat de détour (persistant sur quelques frames) ---
        private bool _isDetouring;
        private Vector2 _detourPoint;
        
        private bool _mineDroppedForThisWaypoint = false;
        public CaptureZone(Blackboard bb) : base(bb) { }

        public override float ComputeUtility()
        {
            // Si aucune cible, utilité = 0
            if (_bb.targetWaypoint == null)
                return 0f;

            // Utilité basée sur la proximité et la neutralité de la balise
            float dist = Vector2.Distance(_bb.myShip.Position, _bb.targetWaypoint.Position);
            float distFactor = Mathf.Clamp01(1f - dist / 10f); // plus proche = score plus haut
            float ownerFactor = _bb.targetWaypoint.Owner == -1 ? 1f : 0.8f; // neutre > ennemie

            return distFactor * ownerFactor;
        }

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (_bb.targetWaypoint == null)
                return input;

            float targetOrient = RotateShipToTarget(_bb.targetWaypoint.Position);
            input.targetOrientation = targetOrient;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.myShip.Orientation, targetOrient));

            // Propulsion lissée selon l'alignement
            if (angleDiff < 35f)
                input.thrust = Mathf.Lerp(0.3f, 1f, 1 - angleDiff / 35f);
            else
                input.thrust = 0f;

            // Freinage
            if (_bb.distanceToTarget < _bb.targetWaypoint.Radius + 0.6f)
            {
                input.thrust = 0f;
                if (_bb.nextWayPoint != null)
                    input.targetOrientation = RotateShipToTarget(_bb.nextWayPoint.Position);
            }
            
            return input;
        }

        public float RotateShipToTarget(Vector2 targetPosition)
        {
            return AimingHelpers.ComputeSteeringOrient(
                _bb.myShip,
                targetPosition,
                1.1f
            );
        }
    }
}