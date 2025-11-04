using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class CaptureZone : UtilityAction
    {
        // --- Etat de détour (persistant sur quelques frames) ---
        private bool _isDetouring;
        private Vector2 _detourPoint;

        private const float SlowRadius          = 1.2f; // commencer à calmer les gaz avant la balise
        private const float BrakeDistance       = 0.6f; // freinage final (tu l'avais déjà)
        private const float DetourReachRadius   = 0.35f; // rayon pour considérer le point d’évitement atteint
        private const float AsteroidMargin      = 0.25f; // marge autour du radius
        private const float SpeedBonusMax       = 0.4f;  // marge ajoutée à haute vitesse

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

            // Calcul d'orientation inertielle
            float targetOrient = AimingHelpers.ComputeSteeringOrient(
                _bb.myShip,
                _bb.targetWaypoint.Position,
                1.1f
            );

            input.targetOrientation = targetOrient;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.myShip.Orientation, targetOrient));

            // Propulsion lissée selon l'alignement
            if (angleDiff < 35f)
                input.thrust = Mathf.Lerp(0.3f, 1f, 1 - angleDiff / 35f);
            else
                input.thrust = 0f;

            if (_bb.distanceToTarget < _bb.targetWaypoint.Radius + 1f)
                input.thrust = Mathf.Lerp(input.thrust, 0f, 0.5f);

            
            if (_bb.distanceToLastTarget - _bb.targetWaypoint.Radius <= _bb.myShip.Radius / 2 && _bb.lastWayPoint != null) // au centre environ
            {
                // On vérifie qu'on n’a pas déjà posé une mine ici
                if (!_mineDroppedForThisWaypoint && _bb.myShip.Energy >= _bb.myShip.MineEnergyCost)
                {
                    input.dropMine = true; // 🚀 Dépose une mine
                    _mineDroppedForThisWaypoint = true; // évite le spam
                }
            }
            else
            {
                // Si on s’éloigne de la balise, reset pour la suivante
                _mineDroppedForThisWaypoint = false;
            }
            
            return input;
        }
    }
}