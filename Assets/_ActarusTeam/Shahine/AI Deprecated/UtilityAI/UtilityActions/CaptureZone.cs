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
        private bool _isFirstFrame = true;

        [MinMaxSlider(0.1f, 1.8f)] public Vector2 MinMaxOvershoot = new Vector2(0.9f, 1.35f);
        // Optionnel : pénalité de proximité (réduit l’overshoot quand on est très proche)
        [SerializeField, Range(0f, 0.5f)] private float proximityPenalty = 0.2f;

        public CaptureZone(Blackboard bb) : base(bb)
        {
        }
        

        public override InputData Execute()
        {
            InputData input = new InputData();

            if (_bb.TargetWaypoint == null)
                return input;

            float targetOrient;
            if (_bb.MyShip.Velocity.sqrMagnitude < 0.0001f)
            {
                // On calcule manuellement l'angle à partir du regard (LookAt)
                Vector2 lookDir = _bb.MyShip.LookAt.normalized;
                float deltaAngle = Vector2.SignedAngle(lookDir, _bb.TargetWaypoint.Position - _bb.MyShip.Position);
                deltaAngle *= 1.125f; // ton overshoot par défaut
                deltaAngle = Mathf.Clamp(deltaAngle, -170f, 170f);
                float velocityOrientation = Vector2.SignedAngle(Vector2.right, lookDir);
                targetOrient = velocityOrientation + deltaAngle;
            }
            else
            {
                // Cas normal : on utilise la fonction existante
                targetOrient = RotateShipToTarget(ComputeEntryPoint(_bb.TargetWaypoint, _bb.NextWayPoint));
            }
            input.targetOrientation = targetOrient;
            
            
            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(_bb.MyShip.Orientation, targetOrient));

            // Propulsion lissée selon l'alignement
            if (angleDiff < _bb.AngleTolerance)
            {
                input.thrust = Mathf.Lerp(0.3f, 1f, 1 - angleDiff / _bb.AngleTolerance);
                Debug.Log(_bb.DistanceToTarget - _bb.TargetWaypoint.Radius);
                if (_bb.DistanceToTarget - _bb.TargetWaypoint.Radius <= breakDistance)
                {
                    input.thrust = 0;
                    RotateShipToTarget(_bb.NextWayPoint.Position);
                }
            }
            else
                input.thrust = 0f;
            
            return input;
        }
        
        private float ComputeContextOvershoot(SpaceShipView ship, Vector2 targetPos)
        {
            // 1) Données instantanées
            Vector2 toTarget = targetPos - ship.Position;
            float distance = toTarget.magnitude;
            Vector2 velocity = ship.Velocity;
            float speed = Mathf.Max(velocity.magnitude, 0.01f);

            // 2) Angle à rattraper (entre vitesse et cible)
            float alpha = Mathf.Abs(Vector2.SignedAngle(velocity, toTarget));

            // 3) Temps dispo et rotation max réaliste
            float tArrive = distance / speed;                           // s
            float alphaMax = ship.RotationSpeed * tArrive;              // deg (RotationSpeed est en deg/s)

            // 4) Sévérité du virage
            float S = alpha / Mathf.Max(alphaMax, 0.01f);               // >1 => trop serré

            // 5) Remap S -> [0..1] puis vers ton slider min/max
            //   - S >= 1.5  => très dur => t=0  (overshoot proche du min)
            //   - S <= 0.3  => facile  => t=1  (overshoot proche du max)
            float t = Mathf.InverseLerp(1.5f, 0.3f, Mathf.Clamp(S, 0f, 10f));
            t = Mathf.SmoothStep(0f, 1f, t); // lissage

            float overshoot = Mathf.Lerp(MinMaxOvershoot.x, MinMaxOvershoot.y, t);

            // 6) Ajustement de proximité (évite de passer au-dessus quand on est collé)
            if (_bb?.TargetWaypoint != null)
            {
                float r = _bb.TargetWaypoint.Radius;
                float close = Mathf.Clamp01(r / Mathf.Max(distance, 0.01f));
                overshoot -= proximityPenalty * close; // réduit overshoot en approche
            }

            return Mathf.Clamp(overshoot, MinMaxOvershoot.x, MinMaxOvershoot.y);
        }


        public float RotateShipToTarget(Vector2 targetPosition)
        {
            float k = ComputeContextOvershoot(_bb.MyShip, targetPosition);
            return AimingHelpers.ComputeSteeringOrient(_bb.MyShip, targetPosition, k);
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
            if (current == null)
                return Vector2.zero;

            if (current == next || next == null)
                return current.Position;

            Vector2 wc = current.Position;
            Vector2 wn = next.Position;
            float r = current.Radius;

            // Directions normalisées
            Vector2 fromShip = _bb.MyShip.Position - wc; 
            Vector2 toNext   = wn - wc;                  

            // Base : direction moyenne
            Vector2 bisector = Bisector(fromShip, toNext);
            
            return wc + bisector * r * borderValue;
        }
        
        
        private void OnDrawGizmos()
        {
            if (_bb == null || _bb.MyShip == null || _bb.TargetWaypoint == null || _bb.NextWayPoint == null)
                return;

            Vector2 shipPos = _bb.MyShip.Position;
            Vector2 entry = ComputeEntryPoint(_bb.TargetWaypoint, _bb.NextWayPoint);
            Vector2 next = _bb.NextWayPoint.Position;

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

            
            
            Vector2 toTarget = _bb.TargetWaypoint.Position - shipPos;
            float angleToTarget = Vector2.SignedAngle(_bb.MyShip.LookAt, toTarget);

            float halfAngle = _bb.AngleTolerance / 2;
            float length = 2.0f; 

            bool isAligned = Mathf.Abs(angleToTarget) <= _bb.AngleTolerance ;
            Gizmos.color = isAligned ? Color.green : Color.red;
            
            Vector2 dir = new Vector2(_bb.MyShip.LookAt.x, _bb.MyShip.LookAt.y);
            Vector2 leftDir = Quaternion.Euler(0, 0, halfAngle) * dir;
            Vector2 rightDir = Quaternion.Euler(0, 0, -halfAngle) * dir;

            Gizmos.DrawLine(shipPos, shipPos + leftDir * length);
            Gizmos.DrawLine(shipPos, shipPos + rightDir * length);
        }

    }
}

