using System.Collections.Generic;
using System.Linq;
using DoNotModify;
using NaughtyAttributes;
using Teams.ActarusControllerV2.pierre;
using UnityEngine;


namespace Teams.ActarusController.Shahine
{
    /// <summary>
    /// Central data store shared across all AI subsystems.
    /// </summary>
    public sealed class Blackboard : MonoBehaviour
    {
        [ReadOnly] public SpaceShipView myShip;
        [ReadOnly] public SpaceShipView enemyShip;

        [ReadOnly] public List<WayPointView> waypoints;
        [ReadOnly] public List<AsteroidView> asteroids;
        [ReadOnly] public List<MineView> mines;
        [ReadOnly] public List<BulletView> bullets;


        [Header("Targets")] private WaypointPrioritySystem _waypointPrioritySystem;
        
        [ReadOnly] public WayPointView nextWayPoint;
        [ReadOnly] public WayPointView targetWaypoint;
        [ReadOnly] public WayPointView lastWayPoint;

        [ReadOnly] public float distanceToNextTarget;
        [ReadOnly] public float distanceToTarget;
        [ReadOnly] public float distanceToLastTarget;

        [ReadOnly] public float energy;
        [ReadOnly] public float timeLeft;

        [ReadOnly] public bool hasToDropMine;
        [ReadOnly] public bool hasToShoot;
        [ReadOnly] public bool hasToFireShockwave;
 
        public float angleTolerance = 25f;

        public bool UseOldWaypointSystemPriority;
        
        
        public void InitializeFromGameData(SpaceShipView ship, GameData data)
        {
            myShip = ship;
            Debug.Log("SpaceShipt orientation : " + myShip.Orientation + ", SpaceShip Look At angle : " + myShip.LookAt);
            enemyShip = data.SpaceShips.First(s => s.Owner != ship.Owner);
            waypoints = data.WayPoints;
            asteroids = data.Asteroids;
            
            mines = data.Mines;
            bullets = data.Bullets;
            energy = ship.Energy;
            timeLeft = data.timeLeft;

            _waypointPrioritySystem = new WaypointPrioritySystem();
            if (UseOldWaypointSystemPriority)
            {
                targetWaypoint = GetNearestWaypoint(myShip.Position);
                nextWayPoint = GetNearestWaypoint(targetWaypoint.Position);
            }
            else
            {
                WaypointSelectionResult selectionResult = _waypointPrioritySystem.SelectBestWaypoint(ship, data);
                targetWaypoint = selectionResult.TargetWaypoint;
                nextWayPoint = selectionResult.FutureWaypoints[0];
            }
            
            if (targetWaypoint != null)
                distanceToTarget = Vector2.Distance(ship.Position, targetWaypoint.Position);

            if (nextWayPoint != null)
                distanceToNextTarget = Vector2.Distance(ship.Position, nextWayPoint.Position);
            
        }

        public void UpdateFromGameData(GameData data)
        {
            mines = data.Mines;
            bullets = data.Bullets;
            energy = myShip.Energy;
            timeLeft = data.timeLeft;
            
            if (targetWaypoint == null || targetWaypoint.Owner == myShip.Owner)
            {
                lastWayPoint = targetWaypoint;
                
                
                if (UseOldWaypointSystemPriority)
                {
                    targetWaypoint = nextWayPoint ?? GetNearestWaypoint(myShip.Position);
                    nextWayPoint = GetNextWayPoint();
                }
                else
                {
                    WaypointSelectionResult selectionResult = _waypointPrioritySystem.SelectBestWaypoint(myShip, data);
                    targetWaypoint = selectionResult.TargetWaypoint;
                    nextWayPoint = selectionResult.FutureWaypoints[0];
                }
                
                if (targetWaypoint != lastWayPoint && myShip.Energy >=  myShip.MineEnergyCost + myShip.ShockwaveEnergyCost) // Je garde toujours assez pour une shockwave d'urgence
                    hasToDropMine = true;
            }

            if (targetWaypoint != null)
                distanceToTarget = Vector2.Distance(myShip.Position, targetWaypoint.Position);
            
            if (targetWaypoint != null)
                distanceToTarget = Vector2.Distance(myShip.Position, targetWaypoint.Position);
            
            if (nextWayPoint != null)
                distanceToNextTarget = Vector2.Distance(myShip.Position, nextWayPoint.Position);

        }

        public WayPointView GetNearestWaypoint(Vector2 from)
        {
            return waypoints
                .Where(w => w.Owner != myShip.Owner)
                .OrderBy(w => Vector2.Distance(from, w.Position))
                .FirstOrDefault();;
        }
        
        
        public WayPointView GetNextWayPoint()
        {
            Vector2 currentVelocity = myShip.Velocity.sqrMagnitude > 0.01f 
                ? myShip.Velocity.normalized 
                : (targetWaypoint.Position - myShip.Position).normalized;

            Vector2 currentTargetPos = targetWaypoint.Position;
            
            return waypoints
                .Where(w =>
                    w != targetWaypoint &&  
                    w.Owner != myShip.Owner)                
                .OrderByDescending(w =>
                {
                    // Distance
                    float distScore = 1f - Mathf.Clamp01(Vector2.Distance(currentTargetPos, w.Position) / 10f);
                        
                    // Alignement 
                    Vector2 dirToNext = (w.Position - currentTargetPos).normalized;
                    float alignment = Vector2.Dot(currentVelocity, dirToNext); // 1 = aligné, -1 = opposé
                    float alignScore = Mathf.Max(0f, alignment); // on ignore les directions opposées

                    // Score global pondéré 
                    // pondération : 60% inertie (alignement), 40% distance
                    return alignScore * 0.6f + distScore * 0.4f;
                }).FirstOrDefault();
        }

        public bool IsInFrontOfMine()
        {
            foreach (MineView mine in mines)
            {
                if (AimingHelpers.CanHit(myShip, mine.Position, angleTolerance))
                    return true;
            }

            return false;
        }

        public bool IsTargetedByEnemy()
        {
            if (bullets == null || bullets.Count == 0)
                return false;

            Vector2 myPos = myShip.Position;

            foreach (var bullet in bullets)
            {
                Vector2 bulletPos = bullet.Position;
                Vector2 bulletDir = bullet.Velocity.normalized; 
                Vector2 toMe = myPos - bulletPos;

                float distance = toMe.magnitude;
                if (distance > 5f) 
                    continue;

                float alignment = Vector2.Dot(bulletDir, toMe.normalized);

                if (alignment > 0.95f)
                {
                    float cross = Mathf.Abs(bulletDir.x * toMe.y - bulletDir.y * toMe.x);
                    if (cross < myShip.Radius * 1.5f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsInFrontOfEnemy()
        {
            Vector2 myPos = myShip.Position;
            Vector2 enemyPos = enemyShip.Position;
            float distance = Vector2.Distance(myPos, enemyPos);


            float mySpeed = myShip.Velocity.magnitude;
            float maxSpeed = myShip.SpeedMax;
            
            float bulletTime = distance / Bullet.Speed;


            float speedFactor = Mathf.Clamp01(1f - (mySpeed / maxSpeed));
            
            float distanceFactor = Mathf.Clamp(distance / 8f, 0.5f, 1.5f);


            float controlFactor = Mathf.Clamp01(1f - myShip.Thrust);
            
            float hitTimeTolerance =
                (bulletTime * 0.5f + 0.1f) * distanceFactor * (0.5f + 0.5f * speedFactor + 0.3f * controlFactor);
            
            hitTimeTolerance = Mathf.Clamp(hitTimeTolerance, 0.2f, 2f);
            
            return AimingHelpers.CanHit(myShip, enemyPos, enemyShip.Velocity, hitTimeTolerance);
        }


    }
}
