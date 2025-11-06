using System.Collections.Generic;
using System.Linq;
using DoNotModify;
using Teams.ActarusControllerV2.pierre;
using UnityEngine;
using UtilityAI;

namespace Teams.ActarusController.Shahine
{
    /// <summary>
    /// Modular spaceship controller orchestrating the different AI subsystems (Utility AI version).
    /// </summary>
    public sealed class ActarusControllerUtilityAI : BaseSpaceShipController
    {
        public List<AIAction> actions;
        public Context context;
        
        
        // Cache
        private SpaceShipView _enemyShip;
        private WaypointPrioritySystem _waypointSystem;
        
        private WayPointView _targetWaypoint;
        private WayPointView _nextWaypoint;
        private WayPointView _lastWaypoint;
        

        // --- INITIALISATION ---
        public override void Initialize(SpaceShipView spaceship, GameData data)
        {
            context = new Context(this);

            // Init toutes les actions
            foreach (var action in actions)
                action.Initialize(context);

            // Cache rapide
            _enemyShip = data.SpaceShips.First(s => s.Owner != spaceship.Owner);
            _waypointSystem = new WaypointPrioritySystem();

            // --- Initialisation des données contextuelles ---
            context.SetData("MyShip", spaceship);
            context.SetData("EnemyShip", _enemyShip);
            context.SetData("Waypoints", data.WayPoints);
            context.SetData("Asteroids", data.Asteroids);
            context.SetData("Mines", data.Mines);
            context.SetData("Bullets", data.Bullets);

            context.SetData("MyScore", spaceship.Score);
            context.SetData("EnemyScore", _enemyShip.Score);
            context.SetData("MyEnergyLeft", spaceship.Energy);
            context.SetData("EnemyEnergyLeft", _enemyShip.Energy);
            context.SetData("TimeLeft", data.timeLeft);

            // Sélection initiale de cibles
            WaypointSelectionResult selection = _waypointSystem.SelectBestWaypoint(spaceship, data);
            _targetWaypoint = selection.TargetWaypoint;
            _nextWaypoint = selection.FutureWaypoints.FirstOrDefault();

            if (_targetWaypoint != null)
                context.SetData("TargetWaypoint", _targetWaypoint);
            if (_nextWaypoint != null)
                context.SetData("NextWaypoint", _nextWaypoint);

            // Calcul des distances
            if (_targetWaypoint != null)
                context.SetData("DistanceToTarget", Vector2.Distance(spaceship.Position, _targetWaypoint.Position));

            if (_nextWaypoint != null)
                context.SetData("DistanceToNextTarget", Vector2.Distance(spaceship.Position, _nextWaypoint.Position));

            // Facteurs angulaires initiaux
            context.SetData("AngleTolerance", 25f);
            context.SetData("IsEnemyInFront", EnemyIsInFront(spaceship, _enemyShip, 25f));
            context.SetData("IsEnemyInBack", EnemyIsBehind(spaceship, _enemyShip, 25f));
        }


        // --- MISE À JOUR À CHAQUE FRAME ---
        public void UpdateContext(SpaceShipView spaceship, GameData data)
        {
            if (context == null)
                return;
            
            // Données basiques
            context.SetData("MyScore", spaceship.Score);
            context.SetData("EnemyScore", _enemyShip.Score);
            context.SetData("MyEnergyLeft", spaceship.Energy);
            context.SetData("EnemyEnergyLeft", _enemyShip.Energy);
            context.SetData("TimeLeft", data.timeLeft);

            // Position et distances
            float enemyDistance = Vector2.Distance(spaceship.Position, _enemyShip.Position);
            context.SetData("EnemyDistanceToMyShip", enemyDistance);

            // Réévaluation des waypoints si capturés
            if (_targetWaypoint == null || _targetWaypoint.Owner == spaceship.Owner)
            {
                _lastWaypoint = _targetWaypoint;

                WaypointSelectionResult selection = _waypointSystem.SelectBestWaypoint(spaceship, data);
                _targetWaypoint = selection.TargetWaypoint;
                _nextWaypoint = selection.FutureWaypoints.FirstOrDefault();

                context.SetData("TargetWaypoint", _targetWaypoint);
                context.SetData("NextWaypoint", _nextWaypoint);
                context.SetData("LastWaypoint", _lastWaypoint);

                if (_targetWaypoint != null)
                    context.SetData("DistanceToTarget", Vector2.Distance(spaceship.Position, _targetWaypoint.Position));

                if (_nextWaypoint != null)
                    context.SetData("DistanceToNextTarget", Vector2.Distance(spaceship.Position, _nextWaypoint.Position));
            }

            // Angles / Visibilité
            context.SetData("IsEnemyInFront", EnemyIsInFront(spaceship, _enemyShip, context.GetData<float>("AngleTolerance")));
            context.SetData("IsEnemyInBack", EnemyIsBehind(spaceship, _enemyShip, context.GetData<float>("AngleTolerance")));

            // Mines / tirs
            context.SetData("Mines", data.Mines);
            context.SetData("Bullets", data.Bullets);
        }


        // --- DECISION ---
        public override InputData UpdateInput(SpaceShipView spaceship, GameData data)
        {
            UpdateContext(spaceship, data);
            
            
            return Decide();
        }

        private InputData Decide()
        {
            if (actions == null || actions.Count == 0)
                return new InputData();

            AIAction bestAction = null;
            float highestUtility = float.MinValue;

            foreach (var action in actions)
            {
                float utility = action.CalculateUtility(context);
                if (utility > highestUtility)
                {
                    highestUtility = utility;
                    bestAction = action;
                }
            }

            return bestAction != null ? bestAction.Execute(context) : new InputData();
        }

        
        private static bool EnemyIsInFront(SpaceShipView myShip, SpaceShipView enemyShip, float angleTolerance)
        {
            return IsPointInCone(myShip.Position, myShip.LookAt, enemyShip.Position, angleTolerance);
        }

        private static bool EnemyIsBehind(SpaceShipView myShip, SpaceShipView enemyShip, float angleTolerance)
        {
            return IsPointInCone(myShip.Position, -myShip.LookAt, enemyShip.Position, angleTolerance);
        }

        private static bool IsPointInCone(Vector2 origin, Vector2 direction, Vector2 point, float angleDeg)
        {
            Vector2 toPoint = (point - origin).normalized;
            float dot = Vector2.Dot(direction.normalized, toPoint);
            float halfAngle = angleDeg * 0.5f;
            return dot > Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        }
    }
}
