using System.Collections.Generic;
using System.Linq;
using DoNotModify;
using NaughtyAttributes;
using Teams.ActarusControllerV2.pierre;
using UnityEngine;
using UnityEngine.Serialization;


namespace Teams.ActarusController.Shahine
{
    public sealed class Blackboard : MonoBehaviour
    {
        [Header("Me")]
        [ReadOnly] public SpaceShipView MyShip;
        [ReadOnly] public int MyScore;
        [ReadOnly] public float MyEnergyLeft;
        
        [Header("Enemy")]
        [ReadOnly] public SpaceShipView EnemyShip;
        [ReadOnly] public int EnemyScore;
        [ReadOnly] public float EnemyEnergyLeft;
        [ReadOnly] public float EnemyDistanceToMyShip;
        
        
        public List<WayPointView> Waypoints;
        public List<AsteroidView> Asteroids;
        public List<MineView> Mines;
        public List<BulletView> Bullets;


        private WaypointPrioritySystem _waypointPrioritySystem;
        
        [Header("Last Target")]
        [ReadOnly] public WayPointView LastWayPoint;
        [ReadOnly] public float DistanceToLastTarget;
        
        [Header("Current Target")]
        [ReadOnly] public WayPointView TargetWaypoint;
        [ReadOnly] public float DistanceToTarget;
        
        
        [Header("Next Target")]
        [ReadOnly] public WayPointView NextWayPoint;
        [ReadOnly] public float DistanceToNextTarget;
        
        
        [ReadOnly] public float TimeLeft;

        [Header("Actions")]
        [ReadOnly] public bool HasToDropMine;
        [ReadOnly] public bool HasToShoot;
        [ReadOnly] public bool HasToFireShockwave;
 
        
        [Header("Settings")]
        public float AngleTolerance = 25f;
        public bool UseOldWaypointSystemPriority;
        
        
        public void InitializeFromGameData(SpaceShipView ship, GameData data)
        {
            MyShip = ship;
            EnemyShip = data.SpaceShips.First(s => s.Owner != ship.Owner);
            Waypoints = data.WayPoints;
            Asteroids = data.Asteroids;

            MyScore = ship.Score;
            EnemyScore = EnemyShip.Score;
            EnemyDistanceToMyShip = Vector2.Distance(MyShip.Position, EnemyShip.Position);
            Mines = data.Mines;
            Bullets = data.Bullets;
            MyEnergyLeft = ship.Energy;
            TimeLeft = data.timeLeft;

            _waypointPrioritySystem = new WaypointPrioritySystem();
            if (UseOldWaypointSystemPriority)
            {
                TargetWaypoint = GetNearestWaypoint(MyShip.Position);
                NextWayPoint = GetNearestWaypoint(TargetWaypoint.Position);
            }
            else
            {
                WaypointSelectionResult selectionResult = _waypointPrioritySystem.SelectBestWaypoint(ship, data);
                TargetWaypoint = selectionResult.TargetWaypoint;
                NextWayPoint = selectionResult.FutureWaypoints[0];
            }
            
            if (TargetWaypoint != null)
                DistanceToTarget = Vector2.Distance(ship.Position, TargetWaypoint.Position);

            if (NextWayPoint != null)
                DistanceToNextTarget = Vector2.Distance(ship.Position, NextWayPoint.Position);
            
        }

        public void UpdateFromGameData(GameData data)
        {
            Mines = data.Mines;
            Bullets = data.Bullets;
            MyEnergyLeft = MyShip.Energy;
            TimeLeft = data.timeLeft;
            
            if (TargetWaypoint == null || TargetWaypoint.Owner == MyShip.Owner)
            {
                LastWayPoint = TargetWaypoint;
                
                
                if (UseOldWaypointSystemPriority)
                {
                    TargetWaypoint = NextWayPoint ?? GetNearestWaypoint(MyShip.Position);
                    NextWayPoint = GetNextWayPoint();
                }
                else
                {
                    WaypointSelectionResult selectionResult = _waypointPrioritySystem.SelectBestWaypoint(MyShip, data);
                    TargetWaypoint = selectionResult.TargetWaypoint;
                    NextWayPoint = selectionResult.FutureWaypoints[0];
                }
                
                if (TargetWaypoint != LastWayPoint && MyShip.Energy >=  MyShip.MineEnergyCost + MyShip.ShockwaveEnergyCost) // Je garde toujours assez pour une shockwave d'urgence
                    HasToDropMine = true;
            }

            if (TargetWaypoint != null)
                DistanceToTarget = Vector2.Distance(MyShip.Position, TargetWaypoint.Position);
            
            if (TargetWaypoint != null)
                DistanceToTarget = Vector2.Distance(MyShip.Position, TargetWaypoint.Position);
            
            if (NextWayPoint != null)
                DistanceToNextTarget = Vector2.Distance(MyShip.Position, NextWayPoint.Position);

        }

        public WayPointView GetNearestWaypoint(Vector2 from)
        {
            return Waypoints
                .Where(w => w.Owner != MyShip.Owner)
                .OrderBy(w => Vector2.Distance(from, w.Position))
                .FirstOrDefault();;
        }
        
        
        public WayPointView GetNextWayPoint()
        {
            Vector2 currentVelocity = MyShip.Velocity.sqrMagnitude > 0.01f 
                ? MyShip.Velocity.normalized 
                : (TargetWaypoint.Position - MyShip.Position).normalized;

            Vector2 currentTargetPos = TargetWaypoint.Position;
            
            return Waypoints
                .Where(w =>
                    w != TargetWaypoint &&  
                    w.Owner != MyShip.Owner)                
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
            foreach (MineView mine in Mines)
            {
                if (AimingHelpers.CanHit(MyShip, mine.Position, AngleTolerance))
                    return true;
            }

            return false;
        }

        public bool IsTargetedByEnemy()
        {
            if (Bullets == null || Bullets.Count == 0)
                return false;

            Vector2 myPos = MyShip.Position;

            foreach (var bullet in Bullets)
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
                    if (cross < MyShip.Radius * 1.5f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsInFrontOfEnemy()
        {
            Vector2 myPos = MyShip.Position;
            Vector2 enemyPos = EnemyShip.Position;
            float distance = Vector2.Distance(myPos, enemyPos);


            float mySpeed = MyShip.Velocity.magnitude;
            float maxSpeed = MyShip.SpeedMax;
            
            float bulletTime = distance / Bullet.Speed;


            float speedFactor = Mathf.Clamp01(1f - (mySpeed / maxSpeed));
            
            float distanceFactor = Mathf.Clamp(distance / 8f, 0.5f, 1.5f);


            float controlFactor = Mathf.Clamp01(1f - MyShip.Thrust);
            
            float hitTimeTolerance =
                (bulletTime * 0.5f + 0.1f) * distanceFactor * (0.5f + 0.5f * speedFactor + 0.3f * controlFactor);
            
            hitTimeTolerance = Mathf.Clamp(hitTimeTolerance, 0.2f, 2f);
            
            return AimingHelpers.CanHit(MyShip, enemyPos, EnemyShip.Velocity, hitTimeTolerance);
        }
        
        public static bool IsPointInCone(Vector2 origin, Vector2 direction, Vector2 point, float angleDeg)
        {
            Vector2 toPoint = (point - origin).normalized;

            // Produit scalaire
            float dot = Vector2.Dot(direction.normalized, toPoint);

            // Demi-angle du cône
            float halfAngle = angleDeg * 0.5f;

            // Si le cos(angle) est plus grand que la limite, le point est dans le cône
            return dot > Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        }


    }
}
