using DoNotModify;
using NaughtyAttributes;
using UnityEditor;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class CaptureZone : UtilityAction
    {

        public float borderValue = 0.9f;
        public float breakDistance = 1.25f;

        public CaptureZone(Blackboard bb) : base(bb)
        {
        }
        

        protected override float GetInputValue(Scorer scorer)
        {
            if (_bb == null || _bb.myShip == null || _bb.targetWaypoint == null)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.Distance:
                    return Vector2.Distance(_bb.myShip.Position, _bb.targetWaypoint.Position);

                case ScorerInputType.Ownership:
                    return _bb.targetWaypoint.Owner == -1 ? 1f : 0.5f;

                case ScorerInputType.Speed:
                    return _bb.myShip.Velocity.magnitude;

                default:
                    return 0f;
            }
        }

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (_bb.targetWaypoint == null)
                return input;
            
            // Calcul d'orientation inertielle
            float targetOrient = RotateShipToTarget(ComputeEntryPoint(_bb.targetWaypoint, _bb.nextWayPoint));

            input.targetOrientation = targetOrient;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.myShip.Orientation, targetOrient));

            // Propulsion lissée selon l'alignement
            if (angleDiff < _bb.angleTolerance)
            {
                input.thrust = Mathf.Lerp(0.3f, 1f, 1 - angleDiff / _bb.angleTolerance);
                if (_bb.distanceToTarget - _bb.targetWaypoint.Radius < breakDistance)
                {
                    input.thrust = Mathf.Lerp(input.thrust, 0f, 0.2f);
                    RotateShipToTarget(_bb.nextWayPoint.Position);
                }
            }
            else
                input.thrust = 0f;
            
            return input;
        }


        public float RotateShipToTarget(Vector2 targetPosition)
        {
            return AimingHelpers.ComputeSteeringOrient(
                _bb.myShip,
                targetPosition
            );
        }

        public float CrossProduct(Vector2 v1, Vector2 v2)
        {
            return v1.x * v2.y - v1.y * v2.x;
        }
        
        public static Vector2 Bisector(Vector2 v1, Vector2 v2)
        {
            if (v1 == Vector2.zero || v2 == Vector2.zero)
                return Vector2.zero; // Cas particulier : vecteur nul

            Vector2 uNorm = v1.normalized;
            Vector2 vNorm = v2.normalized;
            Vector2 bisectrice = uNorm + vNorm;

            if (bisectrice == Vector2.zero)
                return Vector2.zero; // Cas où u et v sont opposés → pas de bissectrice intérieure

            return bisectrice.normalized;
        }
        
        private Vector2 ComputeEntryPoint(WayPointView current, WayPointView next)
        {
            if (current == null || next == null)
                return Vector2.zero;

            Vector2 wc = current.Position;
            Vector2 wn = next.Position;
            float r = current.Radius;

            // Directions normalisées
            Vector2 fromShip = _bb.myShip.Position - wc; 
            Vector2 toNext   = wn - wc;                  

            // Base : direction moyenne
            Vector2 bisector = Bisector(fromShip, toNext);
            
            return wc + bisector * r * borderValue;
        }
        
        
        private void OnDrawGizmos()
        {
            if (_bb == null || _bb.myShip == null || _bb.targetWaypoint == null || _bb.nextWayPoint == null)
                return;

            Vector2 shipPos = _bb.myShip.Position;
            Vector2 entry = ComputeEntryPoint(_bb.targetWaypoint, _bb.nextWayPoint);
            Vector2 next = _bb.nextWayPoint.Position;

            // --- Couleur de la trajectoire ---
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f); // orange / doré

            Gizmos.DrawLine(shipPos, entry);
            Gizmos.DrawLine(entry, next);
            
            // --- Points de repère ---
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(entry, 0.05f); // point d’entrée

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(shipPos, 0.05f); // vaisseau

            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(next, 0.05f); // waypoint suivant
        }

    }
}

