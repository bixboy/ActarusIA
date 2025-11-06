using System.Collections.Generic;
using DoNotModify;
using UnityEngine;

namespace Teams.Actarus
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
        [SerializeField, Range(1f, 4f)] private float captureLockRadiusMultiplier = 1.75f;
        [SerializeField, Range(3f, 25f)] private float captureLockReleaseDistance = 12f;
        [SerializeField, Range(0.01f, 0.3f)] private float captureLockDistanceWeight = 0.08f;

        [Header("Weapons")]
        [SerializeField, Range(0.05f, 0.6f)] private float predictiveHitTolerance = 0.2f;
        [SerializeField, Range(1f, 20f)] private float shootAngleTolerance = 12f;
        [SerializeField, Range(0.5f, 3f)] private float shockwaveDistance = 1.75f;
        [SerializeField, Range(0.5f, 3f)] private float mineDropDistance = 1.5f;

        public override float CalculateUtility(Context context)
        {
            if (context == null)
                return 0f;
        
            var controller = context.ControllerUtilityAI;
            if (!controller || controller.CurrentCombatMode != ActarusControllerUtilityAI.CombatMode.Hunt)
                return 0f;
        
            var myShip = context.GetData<SpaceShipView>("MyShip");
            var enemy = context.GetData<SpaceShipView>("EnemyShip");
            if (myShip == null || enemy == null)
                return 0f;
        
            float distanceNormalized = Mathf.Clamp01(context.GetData<float>("EnemyDistanceNormalized"));
            float proximityScore = 1f - distanceNormalized;
        
            int enemyWaypoints = Mathf.Max(0, context.GetData<int>("EnemyWaypointCount"));
            int totalWaypoints = Mathf.Max(1, context.GetData<int>("TotalWaypointCount"));
            float enemyControlRatio = Mathf.Clamp01((float)enemyWaypoints / totalWaypoints);
        
            WayPointView lockedWaypoint = context.GetData<WayPointView>("HuntLockedWaypoint");
            float lockBonus = lockedWaypoint != null ? 0.5f : 0f;
        
            return 1.5f + proximityScore + enemyControlRatio + lockBonus;
        }

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

            WayPointView captureLock = UpdateCaptureLock(context, myShip, enemy);

            Vector2 pursuitPoint = ComputePursuitPoint(myShip, enemy);
            WayPointView focusWaypoint = null;

            if (captureLock != null)
            {
                pursuitPoint = ComputeCaptureIntercept(captureLock, enemy, pursuitPoint);
                focusWaypoint = captureLock;
            }
            else
            {
                WayPointView interceptionWaypoint = SelectInterceptionWaypoint(context, myShip, enemy, pursuitPoint);
                if (interceptionWaypoint != null)
                {
                    pursuitPoint = BlendPursuitWithWaypoint(interceptionWaypoint, enemy, pursuitPoint);
                    focusWaypoint = interceptionWaypoint;
                }
            }

            context.SetData("HuntFocusWaypoint", focusWaypoint);

            bool hasPredictiveShot = TryComputePredictiveShot(myShip, enemy, out Vector2 interceptPoint, out float predictiveOrientation);

            if (hasPredictiveShot)
                pursuitPoint = interceptPoint;

            context.SetData("HuntTargetPoint", pursuitPoint);

            float targetOrientation = hasPredictiveShot
                ? predictiveOrientation
                : ComputeTargetOrientation(myShip, pursuitPoint);
            input.targetOrientation = targetOrientation;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(myShip.Orientation, targetOrientation));
            float pursuitThrottle = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(1f - angleDiff / 150f));

            if (angleDiff < thrustAlignmentAngle)
                pursuitThrottle = Mathf.Max(pursuitThrottle, Mathf.Lerp(0.85f, 1f, 1f - angleDiff / thrustAlignmentAngle));

            input.thrust = Mathf.Clamp01(pursuitThrottle);

            if (hasPredictiveShot && angleDiff <= shootAngleTolerance && myShip.Energy >= myShip.ShootEnergyCost &&
                AimingHelpers.CanHit(myShip, enemy.Position, enemy.Velocity, predictiveHitTolerance))
            {
                input.shoot = true;
            }

            float distanceToEnemy = Vector2.Distance(myShip.Position, enemy.Position);
            float distanceBoost = Mathf.Clamp01(distanceToEnemy / 10f);
            input.thrust = Mathf.Max(input.thrust, Mathf.Lerp(0.75f, 1f, distanceBoost));

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
            var lockedWaypoint = context.GetData<WayPointView>("HuntLockedWaypoint");

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

                if (lockedWaypoint != null)
                {
                    Gizmos.color = Color.cyan;
                    float threatRadius = Mathf.Max(lockedWaypoint.Radius * captureLockRadiusMultiplier, 1.5f);
                    Gizmos.DrawWireSphere(lockedWaypoint.Position, threatRadius);
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

        private WayPointView UpdateCaptureLock(Context context, SpaceShipView myShip, SpaceShipView enemy)
        {
            if (context == null || myShip == null)
                return null;

            WayPointView lockedWaypoint = context.GetData<WayPointView>("HuntLockedWaypoint");

            if (lockedWaypoint != null)
            {
                bool nowMine = lockedWaypoint.Owner == myShip.Owner;
                bool enemyClose = enemy != null &&
                                  Vector2.Distance(enemy.Position, lockedWaypoint.Position) <= captureLockReleaseDistance;

                if (nowMine && !enemyClose)
                {
                    context.SetData("HuntLockedWaypoint", null);
                    lockedWaypoint = null;
                }
            }

            if (lockedWaypoint == null)
            {
                lockedWaypoint = FindEnemyCaptureWaypoint(context, myShip, enemy);
                if (lockedWaypoint != null)
                    context.SetData("HuntLockedWaypoint", lockedWaypoint);
            }

            return lockedWaypoint;
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

        private Vector2 ComputeCaptureIntercept(WayPointView waypoint, SpaceShipView enemy, Vector2 pursuitPoint)
        {
            Vector2 center = waypoint.Position;
            float radius = Mathf.Max(waypoint.Radius, 0.5f);
            float threatRadius = Mathf.Max(radius * captureLockRadiusMultiplier, radius + 1f);

            if (enemy == null)
                return center;

            Vector2 toEnemy = enemy.Position - center;
            float enemyDistance = toEnemy.magnitude;

            if (enemyDistance < Mathf.Epsilon)
            {
                Vector2 fallbackDir = pursuitPoint - center;
                if (fallbackDir.sqrMagnitude < Mathf.Epsilon)
                    fallbackDir = Vector2.right;

                return center + fallbackDir.normalized * threatRadius;
            }

            float clampedDistance = Mathf.Clamp(enemyDistance, radius * 0.85f, threatRadius);
            return center + toEnemy.normalized * clampedDistance;
        }

        private Vector2 BlendPursuitWithWaypoint(WayPointView waypoint, SpaceShipView enemy, Vector2 pursuitPoint)
        {
            if (waypoint == null)
                return pursuitPoint;

            float threatRadius = Mathf.Max(waypoint.Radius * captureLockRadiusMultiplier, waypoint.Radius + 1f);
            float blend = 0.35f;

            if (enemy != null)
            {
                float enemyDistance = Vector2.Distance(enemy.Position, waypoint.Position);
                float proximity = 1f - Mathf.Clamp01(enemyDistance / (threatRadius * 1.25f));
                blend = Mathf.Lerp(blend, 0.65f, proximity);
            }

            return Vector2.Lerp(pursuitPoint, waypoint.Position, blend);
        }

        private WayPointView FindEnemyCaptureWaypoint(Context context, SpaceShipView myShip, SpaceShipView enemy)
        {
            var waypoints = context.GetData<List<WayPointView>>("Waypoints");
            if (waypoints == null || waypoints.Count == 0 || enemy == null)
                return null;

            float bestScore = float.MinValue;
            WayPointView bestWaypoint = null;

            foreach (WayPointView waypoint in waypoints)
            {
                if (waypoint == null)
                    continue;

                bool enemyOwns = waypoint.Owner == enemy.Owner;
                float enemyDistance = Vector2.Distance(enemy.Position, waypoint.Position);
                float threatRadius = Mathf.Max(waypoint.Radius * captureLockRadiusMultiplier, 1.5f);
                bool enemyThreat = enemyDistance <= threatRadius;

                if (!enemyOwns && !enemyThreat)
                    continue;

                float myDistance = Vector2.Distance(myShip.Position, waypoint.Position);

                float score = 0f;

                if (enemyOwns)
                    score += enemyWaypointBias * 1.35f;
                else
                    score += neutralWaypointBias;

                if (enemyThreat)
                    score += waypointBias;

                score -= myDistance * captureLockDistanceWeight;

                if (enemyDistance < myDistance)
                    score += waypointBias * 0.5f;

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
