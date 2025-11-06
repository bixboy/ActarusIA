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
        [SerializeField, Range(1f, 20f)] private float shootAngleTolerance = 12f;
        [SerializeField, Range(0.05f, 0.6f)] private float predictiveHitTolerance = 0.2f;
        [SerializeField, Range(0.5f, 3f)] private float shockwaveDistance = 1.75f;
        [SerializeField, Range(0.5f, 3f)] private float mineDropDistance = 1.5f;

        public override InputData Execute(Context context)
        {
            InputData input = new InputData();
            
            Debug.Log("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");

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

            context.SetData("HuntTargetPoint", pursuitPoint);

            float targetOrientation = ComputeTargetOrientation(myShip, pursuitPoint);
            input.targetOrientation = targetOrientation;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(myShip.Orientation, targetOrientation));
            if (angleDiff < thrustAlignmentAngle)
                input.thrust = Mathf.Lerp(0.4f, 1f, 1f - angleDiff / thrustAlignmentAngle);
            else
                input.thrust = 0f;

            bool hasPredictiveShot = AimingHelpers.CanHit(myShip, enemy.Position, enemy.Velocity, predictiveHitTolerance);
            bool hasDirectShot = AimingHelpers.CanHit(myShip, enemy.Position, shootAngleTolerance);
            input.shoot = hasPredictiveShot || hasDirectShot;

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
