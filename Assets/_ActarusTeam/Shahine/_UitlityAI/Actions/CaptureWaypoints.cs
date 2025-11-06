using DoNotModify;
using NaughtyAttributes;
using Teams.ActarusController.Shahine;
using UnityEditor;
using UnityEngine;

namespace UtilityAI
{
    [CreateAssetMenu(menuName = "UtilityAI/AIAction/CaptureWaypoints")]
    public class CaptureWaypoints : AIAction
    {
        public float borderValue = 0.9f;
        public float breakDistance = 1.25f;
        private bool _isFirstFrame = true;

        [MinMaxSlider(0.1f, 1.8f)] public Vector2 MinMaxOvershoot = new Vector2(0.9f, 1.35f);
        // Optionnel : pénalité de proximité (réduit l’overshoot quand on est très proche)
        [SerializeField, Range(0f, 0.5f)] private float proximityPenalty = 0.2f;
        

        public override InputData Execute(Context context)
        {
            InputData input = new InputData();

            var myShip = context.GetData<SpaceShipView>("MyShip");
            var targetWaypoint = context.GetData<WayPointView>("TargetWaypoint");
            var nextWaypoint = context.GetData<WayPointView>("NextWaypoint");
            var angleTolerance = context.GetData<float>("AngleTolerance");
            var distanceToTarget = context.GetData<float>("DistanceToTarget");

            if (targetWaypoint == null || myShip == null)
                return input;

            float targetOrient;
            if (myShip.Velocity.sqrMagnitude < 0.0001f)
            {
                // On calcule manuellement l'angle à partir du regard
                Vector2 lookDir = myShip.LookAt.normalized;
                
                float deltaAngle = Vector2.SignedAngle(lookDir, targetWaypoint.Position - myShip.Position);
                deltaAngle *= 1.125f;
                deltaAngle = Mathf.Clamp(deltaAngle, -170f, 170f);
                
                float velocityOrientation = Vector2.SignedAngle(Vector2.right, lookDir);
                targetOrient = velocityOrientation + deltaAngle;
            }
            else
            {
                targetOrient = RotateShipToTarget(myShip, ComputeEntryPoint(myShip, targetWaypoint, nextWaypoint));
            }

            input.targetOrientation = targetOrient;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(myShip.Orientation, targetOrient));

            // Propulsion lissée selon l'alignement
            if (angleDiff < angleTolerance)
            {
                input.thrust = Mathf.Lerp(0.3f, 1f, 1 - angleDiff / angleTolerance);
                // Debug.Log(distanceToTarget - targetWaypoint.Radius);

                if (distanceToTarget - targetWaypoint.Radius <= breakDistance)
                {
                    input.thrust = 0;
                    RotateShipToTarget(myShip, nextWaypoint.Position);
                }
            }
            else
                input.thrust = 0f;

            DrawActionGizmos(context);
            
            return input;
        }


        private float ComputeContextOvershoot(SpaceShipView ship, Vector2 targetPos)
        {
            Vector2 toTarget = targetPos - ship.Position;
            float distance = toTarget.magnitude;
            
            Vector2 velocity = ship.Velocity;
            float speed = Mathf.Max(velocity.magnitude, 0.01f);

            float alpha = Mathf.Abs(Vector2.SignedAngle(velocity, toTarget));
            float tArrive = distance / speed;
            float alphaMax = ship.RotationSpeed * tArrive;
            float S = alpha / Mathf.Max(alphaMax, 0.01f);

            float t = Mathf.InverseLerp(1.5f, 0.3f, Mathf.Clamp(S, 0f, 10f));
            t = Mathf.SmoothStep(0f, 1f, t);

            float overshoot = Mathf.Lerp(MinMaxOvershoot.x, MinMaxOvershoot.y, t);

            // Ajustement de proximité
            float r = ship.Radius;
            float close = Mathf.Clamp01(r / Mathf.Max(distance, 0.01f));
            overshoot -= proximityPenalty * close;

            return Mathf.Clamp(overshoot, MinMaxOvershoot.x, MinMaxOvershoot.y);
        }


        private float RotateShipToTarget(SpaceShipView ship, Vector2 targetPosition)
        {
            float k = ComputeContextOvershoot(ship, targetPosition);
            return AimingHelpers.ComputeSteeringOrient(ship, targetPosition, k);
        }


        private float CrossProduct(Vector2 v1, Vector2 v2)
        {
            return v1.x * v2.y - v1.y * v2.x;
        }


        public static Vector2 Bisector(Vector2 v1, Vector2 v2)
        {
            if (v1 == Vector2.zero || v2 == Vector2.zero)
                return Vector2.zero;

            Vector2 uNorm = v1.normalized;
            Vector2 vNorm = v2.normalized;
            Vector2 bisectrice = uNorm + vNorm;

            return bisectrice == Vector2.zero ? Vector2.zero : bisectrice.normalized;
        }


        private Vector2 ComputeEntryPoint(SpaceShipView myShip, WayPointView current, WayPointView next)
        {
            if (current == null)
                return Vector2.zero;

            if (current == next || next == null)
                return current.Position;

            Vector2 wc = current.Position;
            Vector2 wn = next.Position;
            float r = current.Radius;

            Vector2 fromShip = myShip.Position - wc;
            Vector2 toNext = wn - wc;

            Vector2 bisector = Bisector(fromShip, toNext);
            return wc + bisector * r * borderValue;
        }

        public override void DrawActionGizmos(Context context)
        {
            var myShip = context.GetData<SpaceShipView>("MyShip");
            var targetWaypoint = context.GetData<WayPointView>("TargetWaypoint");
            var nextWaypoint = context.GetData<WayPointView>("NextWaypoint");
            var angleTolerance = context.GetData<float>("AngleTolerance");
            var distanceToTarget = context.GetData<float>("DistanceToTarget");
            UtilityAIDebugDrawer.DrawThisFrame(() =>
            {
                Vector2 shipPos = myShip.Position;
                Vector2 entry = ComputeEntryPoint(myShip, targetWaypoint, nextWaypoint);
                Vector2 next = nextWaypoint != null ? nextWaypoint.Position : entry;

                // --- Couleur de la trajectoire ---
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                Gizmos.DrawLine(shipPos, entry);
                Gizmos.DrawLine(entry, next);

                // --- Points ---
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(entry, 0.05f);

                Gizmos.color = Color.red;
                Gizmos.DrawSphere(shipPos, 0.05f);

                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(next, 0.05f);

                // --- Cone de vision / alignement ---
                float halfAngle = angleTolerance * 0.5f;
                float length = 2.0f;
                Vector2 dir = myShip.LookAt.normalized;

                Vector2 leftDir = Quaternion.Euler(0, 0, halfAngle) * dir;
                Vector2 rightDir = Quaternion.Euler(0, 0, -halfAngle) * dir;

                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(shipPos, shipPos + leftDir * length);
                Gizmos.DrawLine(shipPos, shipPos + rightDir * length);
            });

        }
    }
}