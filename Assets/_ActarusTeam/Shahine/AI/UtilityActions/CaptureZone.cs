using DoNotModify;
using NaughtyAttributes;
using UnityEditor;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public class CaptureZone : UtilityAction
    {

        public float borderValue = 0.9f;
        public CaptureZone(Blackboard bb) : base(bb)
        {
        }

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

            // 🔹 Point d'entrée optimal (bord du waypoint)
            Vector2 entryPoint = ComputeEntryPoint(_bb.targetWaypoint, _bb.nextWayPoint);

            // 🔹 Calcul de l'orientation cible
            float targetOrient = RotateShipToTarget(entryPoint);
            input.targetOrientation = targetOrient;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.myShip.Orientation, targetOrient));
            float distance = Vector2.Distance(_bb.myShip.Position, entryPoint);
            float speed = _bb.myShip.Velocity.magnitude;

            // === 1️⃣ Adaptation de la poussée selon angle et distance ===

            // Base thrust = plus fort si bien aligné, plus faible si virage serré
            float alignFactor = Mathf.Clamp01(1f - angleDiff / 45f);
            float distanceFactor = Mathf.Clamp01(distance / 4f); // plus on est loin, plus on accélère
            float brakingFactor = Mathf.Clamp01(speed / _bb.myShip.SpeedMax);

            // Thrust final combiné
            input.thrust = alignFactor * distanceFactor * (1f - 0.6f * brakingFactor);

            // Si l’angle est trop grand → priorise la rotation, coupe les gaz
            if (angleDiff > 60f)
                input.thrust = 0f;

            // === 2️⃣ Freinage intelligent à l’approche ===
            float stopDistance = _bb.targetWaypoint.Radius + 0.4f;
            if (distance < stopDistance)
            {
                // Ralentir progressivement pour ne pas overshoot
                float slowFactor = Mathf.InverseLerp(stopDistance, 0f, distance);
                input.thrust *= Mathf.Lerp(0.4f, 0.0f, 1f - slowFactor);

                // Et oriente-toi vers la prochaine cible si elle existe
                if (_bb.nextWayPoint != null)
                    input.targetOrientation = RotateShipToTarget(_bb.nextWayPoint.Position);
            }

            // === 3️⃣ Sécurité : si on est quasi arrêté, forcer la précision ===
            if (speed < 0.2f && distance < stopDistance * 1.2f)
            {
                input.thrust = 0.15f;
                input.targetOrientation = RotateShipToTarget(_bb.targetWaypoint.Position);
            }

            return input;
        }

        public float RotateShipToTarget(Vector2 targetPosition)
        {
            return AimingHelpers.ComputeSteeringOrient(
                _bb.myShip,
                targetPosition,
                1.05f
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

