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
        [SerializeField, Range(0.05f, 0.95f)] private float pursuitSmoothing = 0.55f; // Stabilise la poursuite en lissant le point visé.

        [Header("Waypoint interception")]
        [SerializeField] private float waypointInterceptRadius = 4f;
        [SerializeField] private float waypointBias = 1.35f;
        [SerializeField] private float enemyWaypointBias = 2.5f;
        [SerializeField] private float neutralWaypointBias = 1.1f;
        [SerializeField, Range(1f, 4f)] private float captureLockRadiusMultiplier = 1.75f;
        [SerializeField, Range(3f, 25f)] private float captureLockReleaseDistance = 12f;
        [SerializeField, Range(0.01f, 0.3f)] private float captureLockDistanceWeight = 0.08f;
        [SerializeField, Range(0.1f, 3f)] private float focusStickinessDuration = 1.1f; // Stabilise la stratégie waypoint.
        [SerializeField, Range(0f, 2f)] private float focusSwitchScoreMargin = 0.35f; // Evite les changements trop fréquents.
        [SerializeField, Range(0f, 2f)] private float focusRetentionBonus = 0.75f; // Encourage le maintien du waypoint courant.

        [Header("Close range control")]
        [SerializeField, Range(0.5f, 4f)] private float orbitEnterDistance = 1.75f; // Distance d'entrée dans le strafe.
        [SerializeField, Range(0.5f, 5f)] private float orbitExitDistance = 2.35f; // Distance de sortie du strafe.
        [SerializeField, Range(0.5f, 4f)] private float orbitLateralDistance = 1.6f; // Rayon latéral du strafe.
        [SerializeField, Range(0.2f, 1.5f)] private float orbitStrength = 0.8f; // Poids du strafe sur le point de poursuite.
        [SerializeField, Range(0.1f, 2f)] private float shockwaveRetreatBuffer = 0.85f; // Anticipe le recul face au shockwave.

        [Header("Weapons")]
        [SerializeField, Range(0.05f, 0.6f)] private float predictiveHitTolerance = 0.2f;
        [SerializeField, Range(1f, 20f)] private float shootAngleTolerance = 12f;
        [SerializeField, Range(0.5f, 3f)] private float shockwaveDistance = 1.75f;
        [SerializeField, Range(0.5f, 3f)] private float mineDropDistance = 1.5f;
        [SerializeField, Range(2f, 15f)] private float thrustDistanceBoost = 9f; // Donne du punch à la chasse distante.
        [SerializeField, Range(0f, 1f)] private float minimumAlignmentThrust = 0.3f; // Evite les coupures nettes d'accélération.

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

            WayPointView previousFocus = context.GetData<WayPointView>("HuntFocusWaypoint");
            float previousFocusScore = context.GetData<float>("HuntFocusScore");
            float lastFocusSwitchTime = context.GetData<float>("HuntFocusTimestamp");

            WayPointView captureLock = UpdateCaptureLock(context, myShip, enemy);

            Vector2 pursuitPoint = ComputePursuitPoint(myShip, enemy);
            WayPointView focusWaypoint = null;
            float focusScore = 0f;

            if (captureLock != null)
            {
                pursuitPoint = ComputeCaptureIntercept(captureLock, enemy, pursuitPoint);
                focusWaypoint = captureLock;
                focusScore = float.MaxValue;
            }
            else
            {
                WayPointView interceptionWaypoint = SelectInterceptionWaypoint(context, myShip, enemy, pursuitPoint, previousFocus, out float interceptionScore);
                if (interceptionWaypoint != null)
                {
                    pursuitPoint = BlendPursuitWithWaypoint(interceptionWaypoint, enemy, pursuitPoint);
                    focusWaypoint = interceptionWaypoint;
                    focusScore = interceptionScore;
                }
            }

            float now = Time.time;
            focusWaypoint = ResolveFocusWaypoint(context, focusWaypoint, focusScore, previousFocus, previousFocusScore, captureLock != null, lastFocusSwitchTime, now, out focusScore);

            float distanceToEnemy = Vector2.Distance(myShip.Position, enemy.Position);
            bool enemyCanShockwave = enemy.Energy >= enemy.ShockwaveEnergyCost && enemy.ShockwaveEnergyCost > Mathf.Epsilon;

            pursuitPoint = AdjustForCloseRange(context, myShip, enemy, pursuitPoint, distanceToEnemy, enemyCanShockwave);
            pursuitPoint = SmoothPursuitPoint(context, pursuitPoint);

            context.SetData("HuntTargetPoint", pursuitPoint);
            context.SetData("HuntFocusWaypoint", focusWaypoint);
            context.SetData("HuntFocusScore", focusScore);
            context.SetData("HuntFocusTimestamp", focusWaypoint == previousFocus ? lastFocusSwitchTime : now);

            bool hasPredictiveShot = TryComputePredictiveShot(myShip, enemy, out Vector2 interceptPoint, out float predictiveOrientation, distanceToEnemy);

            if (hasPredictiveShot)
            {
                pursuitPoint = Vector2.Lerp(pursuitPoint, interceptPoint, 0.65f); // Préserve la stabilité tout en favorisant le tir anticipé.
                context.SetData("HuntTargetPoint", pursuitPoint);
            }

            float targetOrientation = hasPredictiveShot
                ? predictiveOrientation
                : ComputeTargetOrientation(myShip, pursuitPoint);
            input.targetOrientation = targetOrientation;

            float angleDiff = Mathf.Abs(Mathf.DeltaAngle(myShip.Orientation, targetOrientation));
            bool isOrbiting = distanceToEnemy < orbitExitDistance;
            input.thrust = ComputeAdaptiveThrust(distanceToEnemy, angleDiff, isOrbiting);

            if (hasPredictiveShot && angleDiff <= shootAngleTolerance && myShip.Energy >= myShip.ShootEnergyCost &&
                AimingHelpers.CanHit(myShip, enemy.Position, enemy.Velocity, predictiveHitTolerance))
            {
                input.shoot = true;
            }

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

        private bool TryComputePredictiveShot(SpaceShipView myShip, SpaceShipView enemy, out Vector2 interceptPoint, out float interceptOrientation, float distanceToEnemy)
        {
            interceptPoint = enemy.Position;
            interceptOrientation = myShip.Orientation;

            if (myShip == null || enemy == null)
                return false;

            Vector2 shooterPos = myShip.Position;
            Vector2 targetPos = enemy.Position;
            Vector2 targetVel = enemy.Velocity;

            float bulletSpeed = Bullet.Speed;
            Vector2 relativePos = targetPos - shooterPos;
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

            // Raffine la solution en recalculant la distance réelle parcourue par la balle.
            for (int i = 0; i < 3; i++)
            {
                Vector2 futureTargetPos = targetPos + targetVel * time;
                float distance = Vector2.Distance(futureTargetPos, shooterPos);
                float newTime = distance / bulletSpeed;
                time = Mathf.Lerp(time, newTime, 0.65f);
            }

            if (time <= 0f || time > maxPredictionTime)
                return false;

            interceptPoint = targetPos + targetVel * time;
            Vector2 toIntercept = interceptPoint - shooterPos;
            if (toIntercept.sqrMagnitude < epsilon)
                return false;

            float interceptDistance = toIntercept.magnitude;
            if (interceptDistance > distanceToEnemy + bulletSpeed * maxPredictionTime)
                return false;

            interceptOrientation = Mathf.Atan2(toIntercept.y, toIntercept.x) * Mathf.Rad2Deg;

            float orientationDelta = Mathf.Abs(Mathf.DeltaAngle(myShip.Orientation, interceptOrientation));
            if (orientationDelta > shootAngleTolerance * 1.75f)
                return false;

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

        private WayPointView SelectInterceptionWaypoint(Context context, SpaceShipView myShip, SpaceShipView enemy, Vector2 pursuitPoint, WayPointView previousFocus, out float bestScore)
        {
            var waypoints = context.GetData<List<WayPointView>>("Waypoints");
            if (waypoints == null || waypoints.Count == 0)
            {
                bestScore = 0f;
                return null;
            }

            bestScore = float.MinValue;
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

                if (previousFocus != null && waypoint == previousFocus)
                    score += focusRetentionBonus;

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

        private Vector2 AdjustForCloseRange(Context context, SpaceShipView myShip, SpaceShipView enemy, Vector2 pursuitPoint, float distanceToEnemy, bool enemyCanShockwave)
        {
            if (enemy == null)
                return pursuitPoint;

            Vector2 toEnemy = enemy.Position - myShip.Position;

            if (distanceToEnemy < orbitEnterDistance)
            {
                float orbitSign = GetOrbitDirection(context, myShip, enemy, toEnemy);
                Vector2 lateral = Vector2.Perpendicular(toEnemy).normalized * orbitLateralDistance * orbitSign;
                Vector2 orbitTarget = enemy.Position - toEnemy.normalized * Mathf.Min(distanceToEnemy, orbitEnterDistance * 0.9f) + lateral;
                pursuitPoint = Vector2.Lerp(pursuitPoint, orbitTarget, orbitStrength);
            }
            else if (distanceToEnemy > orbitExitDistance)
            {
                context.SetData("HuntOrbitSign", 0f);
            }

            if (enemyCanShockwave && distanceToEnemy < shockwaveDistance + shockwaveRetreatBuffer)
            {
                float retreatFactor = 1f - Mathf.Clamp01((distanceToEnemy - shockwaveDistance) / Mathf.Max(0.1f, shockwaveRetreatBuffer));
                Vector2 retreatDir = (-toEnemy).normalized;
                Vector2 retreatTarget = myShip.Position + retreatDir * (shockwaveDistance + shockwaveRetreatBuffer);
                pursuitPoint = Vector2.Lerp(pursuitPoint, retreatTarget, retreatFactor);
            }

            return pursuitPoint;
        }

        private float GetOrbitDirection(Context context, SpaceShipView myShip, SpaceShipView enemy, Vector2 toEnemy)
        {
            float orbitSign = context.GetData<float>("HuntOrbitSign");
            if (Mathf.Abs(orbitSign) < 0.01f)
            {
                float cross = 0f;
                if (enemy.Velocity.sqrMagnitude > 0.01f)
                    cross = Mathf.Sign(Vector3.Cross(new Vector3(toEnemy.x, toEnemy.y, 0f), new Vector3(enemy.Velocity.x, enemy.Velocity.y, 0f)).z);

                if (Mathf.Abs(cross) < 0.1f)
                    cross = Mathf.Sign(Vector2.Dot(myShip.LookAt.normalized, Vector2.Perpendicular(toEnemy)));

                if (Mathf.Abs(cross) < 0.1f)
                    cross = 1f;

                orbitSign = cross;
            }
            else
            {
                float tendency = Mathf.Sign(Vector2.Dot(enemy.Velocity, Vector2.Perpendicular(toEnemy)));
                orbitSign = Mathf.Lerp(orbitSign, Mathf.Abs(tendency) < 0.1f ? orbitSign : tendency, 0.1f);
            }

            context.SetData("HuntOrbitSign", orbitSign);
            return Mathf.Sign(orbitSign);
        }

        private Vector2 SmoothPursuitPoint(Context context, Vector2 targetPoint)
        {
            bool hasLast = context.GetData<bool>("HuntHasLastPursuit");
            if (!hasLast)
            {
                context.SetData("HuntHasLastPursuit", true);
                context.SetData("HuntLastPursuitPoint", targetPoint);
                return targetPoint;
            }

            Vector2 lastPoint = context.GetData<Vector2>("HuntLastPursuitPoint");
            float smoothing = Mathf.Clamp01(1f - pursuitSmoothing);
            Vector2 smoothed = Vector2.Lerp(lastPoint, targetPoint, smoothing);
            context.SetData("HuntLastPursuitPoint", smoothed);
            return smoothed;
        }

        private float ComputeAdaptiveThrust(float distanceToEnemy, float angleDiff, bool isOrbiting)
        {
            float alignmentFactor = Mathf.Cos(Mathf.Deg2Rad * Mathf.Min(angleDiff, 180f));
            float alignmentBoost = Mathf.InverseLerp(180f, 0f, angleDiff);
            float thrust = Mathf.Lerp(minimumAlignmentThrust, 1f, Mathf.SmoothStep(0f, 1f, alignmentBoost));

            float distanceFactor = Mathf.Clamp01(distanceToEnemy / Mathf.Max(1f, thrustDistanceBoost));
            thrust = Mathf.Lerp(thrust, 1f, distanceFactor * 0.85f);

            if (angleDiff < thrustAlignmentAngle)
                thrust = Mathf.Max(thrust, Mathf.Lerp(0.85f, 1f, 1f - angleDiff / thrustAlignmentAngle));

            if (isOrbiting)
                thrust = Mathf.Lerp(thrust, 0.65f, 0.35f);

            thrust *= Mathf.Clamp01(0.5f + 0.5f * Mathf.Max(0f, alignmentFactor));
            return Mathf.Clamp01(thrust);
        }

        private WayPointView ResolveFocusWaypoint(Context context, WayPointView candidate, float candidateScore, WayPointView previous, float previousScore, bool isCaptureLock, float lastSwitchTime, float now, out float resolvedScore)
        {
            resolvedScore = candidateScore;

            if (isCaptureLock)
            {
                if (candidate != null)
                {
                    context.SetData("HuntFocusTimestamp", now);
                    resolvedScore = candidateScore;
                }
                return candidate;
            }

            if (previous == null)
            {
                if (candidate != null)
                    context.SetData("HuntFocusTimestamp", now);
                return candidate;
            }

            if (candidate == previous)
            {
                resolvedScore = Mathf.Max(candidateScore, previousScore);
                return previous;
            }

            float elapsed = now - lastSwitchTime;
            if (elapsed < focusStickinessDuration)
            {
                resolvedScore = previousScore;
                return previous;
            }

            if (candidate == null)
            {
                resolvedScore = previousScore;
                return previous;
            }

            if (candidateScore < previousScore + focusSwitchScoreMargin)
            {
                resolvedScore = previousScore;
                return previous;
            }

            context.SetData("HuntFocusTimestamp", now);
            resolvedScore = candidateScore;
            return candidate;
        }
    }
}
