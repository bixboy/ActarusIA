using System.Collections.Generic;
using DoNotModify;
using Teams.ActarusController.Shahine;
using UnityEngine;

namespace UtilityAI
{
    [CreateAssetMenu(menuName = "UtilityAI/AIAction/HuntEnemy")]
    public class HuntEnemy : AIAction
    {
        [Header("Pursuit")]
        [SerializeField, Range(0.1f, 5f)] private float minPredictionTime = 0.35f;
        [SerializeField, Range(0.1f, 5f)] private float maxPredictionTime = 2.4f;
        [SerializeField, Range(0.8f, 1.5f)] private float pursuitOvershoot = 1.1f;
        [SerializeField, Range(5f, 45f)] private float thrustAlignmentAngle = 20f;

        [Header("Waypoint interception")]
        [SerializeField] private float waypointInterceptRadius = 4f;
        [SerializeField] private float waypointBias = 1.35f;
        [SerializeField] private float enemyWaypointBias = 2.5f;
        [SerializeField] private float neutralWaypointBias = 1.1f;

        [Header("Weapons")]
        [SerializeField, Range(0.05f, 0.6f)] private float predictiveHitTolerance = 0.2f;
        [SerializeField, Range(1f, 20f)] private float shootAngleTolerance = 12f;
        [SerializeField, Range(0.5f, 3f)] private float shockwaveDistance = 1.75f;
        [SerializeField, Range(0.5f, 3f)] private float mineDropDistance = 1.5f;

        public override InputData Execute(Context context)
        {
            InputData input = new InputData();
            
            var controller = context.ControllerUtilityAI;
            var myShip = context.GetData<SpaceShipView>("MyShip");
            var enemy = context.GetData<SpaceShipView>("EnemyShip");

            if (myShip == null || enemy == null)
                return input;

            if (controller && controller.CurrentCombatMode != ActarusControllerUtilityAI.CombatMode.Hunt)
                controller.SetCombatMode(ActarusControllerUtilityAI.CombatMode.Hunt);

            Vector2 pursuitPoint = ComputePursuitPoint(myShip, enemy);
            WayPointView interceptionWaypoint = SelectInterceptionWaypoint(context, myShip, enemy, pursuitPoint);

            if (interceptionWaypoint != null)
            {
                pursuitPoint = interceptionWaypoint.Position;
                context.SetData("HuntFocusWaypoint", interceptionWaypoint);
            }
            else
            {
                context.SetData("HuntFocusWaypoint", null);
            }

            bool hasPredictiveShot = TryComputePredictiveShot(myShip, enemy, out Vector2 interceptPoint, out float predictiveOrientation);

            if (hasPredictiveShot)
                pursuitPoint = interceptPoint;

            context.SetData("HuntTargetPoint", pursuitPoint);

            float targetOrientation = hasPredictiveShot
                ? predictiveOrientation
                : ComputeTargetOrientation(myShip, pursuitPoint);
            input.targetOrientation = targetOrientation;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(myShip.Orientation, targetOrientation));
            if (angleDiff < thrustAlignmentAngle)
                input.thrust = Mathf.Lerp(0.4f, 1f, 1f - angleDiff / thrustAlignmentAngle);
            else
                input.thrust = 0f;

            if (hasPredictiveShot && angleDiff <= shootAngleTolerance && myShip.Energy >= myShip.ShootEnergyCost &&
                AimingHelpers.CanHit(myShip, enemy.Position, enemy.Velocity, predictiveHitTolerance))
            {
                input.shoot = true;
            }

            float distanceToEnemy = Vector2.Distance(myShip.Position, enemy.Position);

            if (distanceToEnemy <= shockwaveDistance && myShip.Energy >= myShip.ShockwaveEnergyCost)
                input.fireShockwave = true;

            if (distanceToEnemy <= mineDropDistance && myShip.Energy >= myShip.MineEnergyCost)
            {
                Vector2 toEnemy = (enemy.Position - myShip.Position).normalized;
                float backwardDot = Vector2.Dot(-myShip.LookAt.normalized, toEnemy);
                
                if (backwardDot > 0.65f)
                    input.dropMine = true;
            }

            return input;
        }

        public override void DrawActionGizmos(Context context)
        {
            var myShip = context.GetData<SpaceShipView>("MyShip");
            var enemy = context.GetData<SpaceShipView>("EnemyShip");
            var pursuitPoint = context.GetData<Vector2>("HuntTargetPoint");
            var focusWaypoint = context.GetData<WayPointView>("HuntFocusWaypoint");

            if (myShip == null || enemy == null)
                return;

            UtilityAIDebugDrawer.DrawThisFrame(() =>
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawLine(myShip.Position, pursuitPoint);
                Gizmos.DrawSphere(pursuitPoint, 0.1f);

                Gizmos.color = Color.red;
                Gizmos.DrawLine(myShip.Position, enemy.Position);

                if (focusWaypoint != null)
                {
                    Gizmos.color = Color.yellow;
                    Gizmos.DrawWireSphere(focusWaypoint.Position, waypointInterceptRadius);
                }
            });
        }

        private Vector2 ComputePursuitPoint(SpaceShipView myShip, SpaceShipView enemy)
        {
            float distance = Vector2.Distance(myShip.Position, enemy.Position);
            float speed = Mathf.Max(myShip.SpeedMax, 0.1f);
            float estimatedTime = distance / speed;
            float clampedTime = Mathf.Clamp(estimatedTime * pursuitOvershoot, minPredictionTime, maxPredictionTime);
            return enemy.Position + enemy.Velocity * clampedTime;
        }

        private float ComputeTargetOrientation(SpaceShipView myShip, Vector2 targetPosition)
        {
            if (myShip.Velocity.sqrMagnitude < 0.0001f)
            {
                Vector2 lookDir = myShip.LookAt.normalized;
                float deltaAngle = Vector2.SignedAngle(lookDir, targetPosition - myShip.Position);
                deltaAngle *= pursuitOvershoot;
                deltaAngle = Mathf.Clamp(deltaAngle, -170f, 170f);
                float velocityOrientation = Vector2.SignedAngle(Vector2.right, lookDir);
                return velocityOrientation + deltaAngle;
            }

            return AimingHelpers.ComputeSteeringOrient(myShip, targetPosition, pursuitOvershoot);
        }

        private bool TryComputePredictiveShot(SpaceShipView myShip, SpaceShipView enemy, out Vector2 interceptPoint, out float interceptOrientation)
        {
            interceptPoint = enemy.Position;
            interceptOrientation = myShip.Orientation;

            if (myShip == null || enemy == null)
                return false;

            Vector2 shooterPos = myShip.Position;
            Vector2 targetPos = enemy.Position;
            Vector2 targetVel = enemy.Velocity;
            Vector2 relativePos = targetPos - shooterPos;

            float bulletSpeed = Bullet.Speed;
            float a = targetVel.sqrMagnitude - bulletSpeed * bulletSpeed;
            float b = 2f * Vector2.Dot(relativePos, targetVel);
            float c = relativePos.sqrMagnitude;

            const float epsilon = 1e-5f;
            float time;

            if (Mathf.Abs(a) < epsilon)
            {
                if (Mathf.Abs(b) < epsilon)
                {
                    if (c < epsilon)
                        return false;

                    time = Mathf.Sqrt(c) / bulletSpeed;
                }
                else
                {
                    time = -c / b;
                    if (time < 0f)
                        return false;
                }
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant < 0f)
                    return false;

                float sqrtDiscriminant = Mathf.Sqrt(discriminant);
                float t1 = (-b + sqrtDiscriminant) / (2f * a);
                float t2 = (-b - sqrtDiscriminant) / (2f * a);

                time = float.PositiveInfinity;

                if (t1 > 0f)
                    time = Mathf.Min(time, t1);
                if (t2 > 0f)
                    time = Mathf.Min(time, t2);

                if (!float.IsFinite(time) || time == float.PositiveInfinity)
                    return false;
            }

            if (time > maxPredictionTime)
                return false;

            interceptPoint = targetPos + targetVel * time;
            Vector2 toIntercept = interceptPoint - shooterPos;
            if (toIntercept.sqrMagnitude < epsilon)
                return false;

            interceptOrientation = Mathf.Atan2(toIntercept.y, toIntercept.x) * Mathf.Rad2Deg;
            return true;
        }

        private WayPointView SelectInterceptionWaypoint(Context context, SpaceShipView myShip, SpaceShipView enemy, Vector2 pursuitPoint)
        {
            var waypoints = context.GetData<List<WayPointView>>("Waypoints");
            if (waypoints == null || waypoints.Count == 0)
                return null;

            float bestScore = float.MinValue;
            WayPointView bestWaypoint = null;

            foreach (WayPointView waypoint in waypoints)
            {
                if (waypoint == null)
                    continue;

                bool ownedByMe = waypoint.Owner == myShip.Owner;
                bool ownedByEnemy = enemy != null && waypoint.Owner == enemy.Owner;

                float distToMe = Vector2.Distance(myShip.Position, waypoint.Position);
                float distToEnemy = enemy != null ? Vector2.Distance(enemy.Position, waypoint.Position) : float.MaxValue;

                if (distToEnemy > waypointInterceptRadius && ownedByMe)
                    continue;

                float score = 0f;

                if (ownedByEnemy)
                    score += enemyWaypointBias;
                else if (!ownedByMe)
                    score += neutralWaypointBias;

                if (distToEnemy <= waypointInterceptRadius)
                    score += waypointBias * (1f - Mathf.Clamp01(distToMe / Mathf.Max(1f, waypointInterceptRadius)));

                float pursuitAlignment = 1f - Mathf.Clamp01(Vector2.Distance(pursuitPoint, waypoint.Position) / (waypointInterceptRadius * 2f));
                score += pursuitAlignment;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestWaypoint = waypoint;
                }
            }

            return bestWaypoint;
        }
    }
}
