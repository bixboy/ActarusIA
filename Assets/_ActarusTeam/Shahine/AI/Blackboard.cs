using System.Collections.Generic;
using System.Linq;
using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    /// <summary>
    /// Central data store shared across all AI subsystems.
    /// </summary>
    public sealed class Blackboard
    {
        public SpaceShipView myShip;
        public SpaceShipView enemyShip;

        public List<WayPointView> waypoints;
        public List<AsteroidView> asteroids;

        public WayPointView targetWaypoint;
        public float distanceToTarget;
        public float energy;
        public float timeLeft;

        
        public static Blackboard InitializeFromGameData(SpaceShipView ship, GameData data)
        {
            var bb = new Blackboard
            {
                myShip = ship,
                enemyShip = data.SpaceShips.First(s => s.Owner != ship.Owner),
                waypoints = data.WayPoints,
                asteroids = data.Asteroids,
                energy = ship.Energy,
                timeLeft = data.timeLeft,
                
                // Sélection de la balise non contrôlée la plus proche
                targetWaypoint = data.WayPoints
                    .Where(w => w.Owner != ship.Owner)
                    .OrderBy(w => Vector2.Distance(ship.Position, w.Position))
                    .FirstOrDefault()
            };

            if (bb.targetWaypoint != null)
                bb.distanceToTarget = Vector2.Distance(ship.Position, bb.targetWaypoint.Position);

            return bb;
        }

        public void UpdateFromGameData(GameData data)
        {
            energy = myShip.Energy;
            timeLeft = data.timeLeft;
            
            if (targetWaypoint == null || targetWaypoint.Owner == myShip.Owner)
            {
                targetWaypoint = data.WayPoints
                    .Where(w => w.Owner != myShip.Owner)
                    .OrderBy(w => Vector2.Distance(myShip.Position, w.Position))
                    .FirstOrDefault();
            }

            if (targetWaypoint != null)
                distanceToTarget = Vector2.Distance(myShip.Position, targetWaypoint.Position);
        }
        
        public static Vector2 AngleToDir(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        /// <summary>
        /// Normalizes an angle to the [0, 360) range.
        /// </summary>
        /// <param name="angle">Angle in degrees.</param>
        /// <returns>The normalized angle.</returns>
        public static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            if (angle < 0f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
