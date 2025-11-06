using System.Collections.Generic;
using DoNotModify;
using NaughtyAttributes;
using UnityEngine;


namespace Teams.Actarus
{
    [CreateAssetMenu(menuName = "UtilityAI/AIAction/CaptureWaypoints")]
        public class CaptureWaypoints : AIAction
        {
            public float borderValue = 0.9f;
            public float breakDistance = 1.25f;
            private bool _isFirstFrame = true;

            [MinMaxSlider(0.1f, 1.8f)] public Vector2 MinMaxOvershoot = new Vector2(0.9f, 1.35f);

            [SerializeField, Range(0f, 0.5f)] private float proximityPenalty = 0.2f;

            [Header("Combat")]
            [SerializeField, Range(1f, 30f)] private float captureShootAngle = 8f;
            [SerializeField, Range(0.05f, 0.6f)] private float capturePredictiveTolerance = 0.25f;
            [SerializeField, Range(1f, 12f)] private float mineShootDistance = 6f;
            [SerializeField, Range(1f, 45f)] private float mineShootAngle = 12f;
            [SerializeField, Range(1f, 90f)] private float mineApproachAlignmentAngle = 35f;
            
            [SerializeField, Range(1f, 10f)] private float securityShockWaveRadius = 2.2f;
            
            
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

                if (context.ControllerUtilityAI != null &&
                    context.ControllerUtilityAI.CurrentCombatMode != ActarusControllerUtilityAI.CombatMode.Capture)
                {
                    context.ControllerUtilityAI.SetCombatMode(ActarusControllerUtilityAI.CombatMode.Capture);
                }

                float targetOrient;
                if (myShip.Velocity.sqrMagnitude < 0.0001f)
                {

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
                
                if (angleDiff < angleTolerance)
                {
                    input.thrust = Mathf.Lerp(0.3f, 1f, 1 - angleDiff / angleTolerance);
                    if (distanceToTarget - targetWaypoint.Radius <= breakDistance)
                    {
                        input.thrust = 0;
                        RotateShipToTarget(myShip, nextWaypoint.Position);
                    }
                }
                else
                    input.thrust = 0f;

                bool shootEnemy = ShouldShootEnemy(myShip, context.GetData<SpaceShipView>("EnemyShip"));
                bool shootMine = ShouldShootMineAlongPath(context, myShip);
                bool shootWaypointMine = ShouldShootMineOnWaypoint(context, myShip, targetWaypoint);

                input.shoot = shootEnemy || shootMine || shootWaypointMine;

                if (ShouldShockwave(context, myShip))
                {
                    input.fireShockwave = true;
                    input.thrust = 0;
                    input.targetOrientation = myShip.Orientation;
                }

                if (ShouldDropMineOnCapturedWaypoint(context, myShip))
                    input.dropMine = true;

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


            private bool ShouldShootEnemy(SpaceShipView myShip, SpaceShipView enemy)
            {
                if (myShip == null || enemy == null)
                    return false;

                if (myShip.Energy < myShip.ShootEnergyCost)
                    return false;

                Vector2 toEnemy = enemy.Position - myShip.Position;
                if (toEnemy.sqrMagnitude <= Mathf.Epsilon)
                    return false;

                Vector2 lookDir = myShip.LookAt.normalized;
                float angleToEnemy = Vector2.Angle(lookDir, toEnemy);
                if (angleToEnemy > captureShootAngle)
                    return false;

                return AimingHelpers.CanHit(myShip, enemy.Position, enemy.Velocity, capturePredictiveTolerance);
            }


            private bool ShouldShootMineAlongPath(Context context, SpaceShipView myShip)
            {
                if (context == null || myShip == null)
                    return false;

                if (myShip.Energy < myShip.ShootEnergyCost)
                    return false;

                var mines = context.GetData<List<MineView>>("Mines");
                if (mines == null || mines.Count == 0)
                    return false;

                Vector2 lookDir = myShip.LookAt.normalized;
                bool hasVelocity = myShip.Velocity.sqrMagnitude > 0.05f;
                Vector2 moveDir = hasVelocity ? myShip.Velocity.normalized : lookDir;

                foreach (MineView mine in mines)
                {
                    if (mine == null)
                        continue;

                    Vector2 toMine = mine.Position - myShip.Position;
                    float sqrDistance = toMine.sqrMagnitude;
                    if (sqrDistance <= Mathf.Epsilon)
                        continue;

                    if (sqrDistance > mineShootDistance * mineShootDistance)
                        continue;

                    float angleToMine = Vector2.Angle(lookDir, toMine);
                    if (angleToMine > mineShootAngle)
                        continue;

                    float approachAngle = Vector2.Angle(moveDir, toMine);
                    if (approachAngle > mineApproachAlignmentAngle)
                        continue;

                    if (!AimingHelpers.CanHit(myShip, mine.Position, mineShootAngle))
                        continue;

                    return true;
                }

                return false;
            }
            
            private bool ShouldShootMineOnWaypoint(Context context, SpaceShipView myShip, WayPointView waypoint)
            {
                if (context == null || myShip == null || waypoint == null)
                    return false;

                if (myShip.Energy < myShip.ShootEnergyCost)
                    return false;

                var mines = context.GetData<List<MineView>>("Mines");
                if (mines == null || mines.Count == 0)
                    return false;

                foreach (var mine in mines)
                {
                    if (mine == null || !mine.IsActive)
                        continue;

                    float dist = Vector2.Distance(mine.Position, waypoint.Position);
                    if (dist > waypoint.Radius + mine.BulletHitRadius)
                        continue;

                    if (!AimingHelpers.CanHit(myShip, mine.Position, mineShootAngle))
                        continue;

                    return true;
                }

                return false;
            }
            

            private bool ShouldDropMineOnCapturedWaypoint(Context context, SpaceShipView myShip)
            {
                WayPointView lastTarget = context.GetData<WayPointView>("LastWaypoint");
                float distanceToLastTarget = context.GetData<float>("DistanceToLastTarget");

                if (lastTarget == null) return false;

                return distanceToLastTarget <= 0.7f && myShip.Energy >= myShip.MineEnergyCost + myShip.ShockwaveEnergyCost;
            }
            
            private bool ShouldShockwave(Context context, SpaceShipView myShip)
            {
                return ShouldShockwaveEnemy(context, myShip)
                       || ShouldShockwaveForMineCollision(context, myShip)
                       || ShouldShockwaveForProjectile(context, myShip);
            }
            
            
            private bool ShouldShockwaveEnemy(Context context, SpaceShipView myShip)
            {
                if (myShip == null || myShip.Energy < myShip.ShockwaveEnergyCost)
                    return false;

                var enemyShip = context.GetData<SpaceShipView>("EnemyShip");

                if (enemyShip == null) return false;
                
                return Vector2.Distance(myShip.Position, enemyShip.Position) <= securityShockWaveRadius;
            }
            
            private bool ShouldShockwaveForMineCollision(Context context, SpaceShipView myShip)
            {
                if (context == null || myShip == null || myShip.Energy < myShip.ShockwaveEnergyCost)
                    return false;

                var mines = context.GetData<List<MineView>>("Mines");
                if (mines == null || mines.Count == 0) return false;

                foreach (var mine in mines)
                {
                    if (mine == null || !mine.IsActive) continue;

                    Vector2 toMine = mine.Position - myShip.Position;
                    Vector2 velocity = myShip.Velocity;
                    if (velocity.sqrMagnitude < 0.01f) continue;

     
                    float approach = Vector2.Dot(velocity.normalized, toMine.normalized);
                    if (approach <= 0.95f) continue;


                    float t = Vector2.Dot(toMine, velocity) / velocity.sqrMagnitude;
                    if (t < 0f) continue;

                    Vector2 closestPoint = myShip.Position + velocity * t;
                    float closestDistance = Vector2.Distance(closestPoint, mine.Position);
                    

           
                    if (closestDistance <= securityShockWaveRadius)
                    {
                        float angleToMine = Vector2.Angle(myShip.LookAt, toMine);

   
                        if (angleToMine > mineShootAngle)
                            return true;
                    }
                }

                return false;
            }

            
            
            private bool ShouldShockwaveForProjectile(Context context, SpaceShipView myShip)
            {
                if (context == null || myShip == null || myShip.Energy < myShip.ShockwaveEnergyCost * 2f)
                    return false;

                var bullets = context.GetData<List<BulletView>>("Bullets");
                if (bullets == null || bullets.Count == 0) return false;

                foreach (var bulletView in bullets)
                {
                    if (bulletView == null) continue;

                    Vector2 relPos = bulletView.Position - myShip.Position;
                    Vector2 relVel = bulletView.Velocity - myShip.Velocity;

                    if (relVel.sqrMagnitude < 0.01f) continue;

                    float dot = Vector2.Dot(relPos, relVel);
                    if (dot >= 0f) continue;

                    float t = -dot / relVel.sqrMagnitude;
                    if (t < 0f) continue;

                    Vector2 closestPoint = relPos + relVel * t;
                    if (closestPoint.magnitude <= myShip.Radius * 0.9f)
                        return true;
                }

                return false;
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

                UtilityAIDebugDrawer.DrawPersistent(() =>
                {
                    Vector2 shipPos = myShip.Position;
                    Vector2 entry = ComputeEntryPoint(myShip, targetWaypoint, nextWaypoint);
                    Vector2 next = nextWaypoint != null ? nextWaypoint.Position : entry;

    
                    Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
                    Gizmos.DrawLine(shipPos, entry);
                    Gizmos.DrawLine(entry, next);

  
                    Gizmos.color = Color.green;
                    Gizmos.DrawSphere(entry, 0.05f);

                    Gizmos.color = Color.red;
                    Gizmos.DrawSphere(shipPos, 0.05f);

                    Gizmos.color = Color.yellow;
                    Gizmos.DrawSphere(next, 0.05f);

           
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