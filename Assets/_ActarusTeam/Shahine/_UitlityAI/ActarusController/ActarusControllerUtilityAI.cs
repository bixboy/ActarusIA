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
        public enum CombatMode
        {
            None,
            Capture,
            Hunt
        }

        public List<AIAction> actions;
        public Context context;


        [Header("Combat mode")] [SerializeField]
        private CombatMode initialCombatMode = CombatMode.Capture;
        
        [SerializeField, Tooltip("Use only score-based switching if true, otherwise use only waypoint-based switching.")]
        private bool useScoreForCombatModeOnly = true;

        [SerializeField, Tooltip("Minimum score lead required before switching to the Hunt mode.")]
        private int scoreLeadForHunt = 4;

        [SerializeField, Tooltip("Score lead threshold below which we fall back to Capture mode.")]
        private int scoreLeadForCapture = 2;

        [SerializeField, Tooltip("Minimum percentage of waypoints owned before switching to Hunt mode.")]
        [Range(0f, 1f)]
        private float ownedRatioForHunt = 0.6f;

        [SerializeField, Tooltip("Percentage of waypoints owned below which we return to Capture mode.")]
        [Range(0f, 1f)]
        private float ownedRatioForCapture = 0.45f;

        [SerializeField, Tooltip("Minimum time in seconds to wait before allowing another combat mode swap.")]
        private float combatModeSwitchCooldown = 5f;

        // Cache
        private SpaceShipView _enemyShip;
        private WaypointPrioritySystem _waypointSystem;

        private WayPointView _targetWaypoint;
        private WayPointView _nextWaypoint;
        private WayPointView _lastWaypoint;

        private CombatMode _currentCombatMode = CombatMode.None;
        private CombatMode _previousCombatMode = CombatMode.None;
        private float _lastCombatModeChangeTime;
        

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
            context.SetData("ScoreLead", spaceship.Score - _enemyShip.Score);
            context.SetData("MyEnergyLeft", spaceship.Energy);
            context.SetData("EnemyEnergyLeft", _enemyShip.Energy);
            context.SetData("TimeLeft", data.timeLeft);
            SetCombatMode(initialCombatMode, true);
            if (combatModeSwitchCooldown > 0f)
                _lastCombatModeChangeTime = Mathf.Max(0f, Time.time - combatModeSwitchCooldown);
            context.SetData("OwnedWaypointCount", 0);
            context.SetData("EnemyWaypointCount", 0);
            context.SetData("NeutralWaypointCount", 0);
            context.SetData("OwnedWaypointRatio", 0f);
            context.SetData("TotalWaypointCount", data.WayPoints?.Count ?? 0);

            float initialEnemyDistance = Vector2.Distance(spaceship.Position, _enemyShip.Position);
            context.SetData("EnemyDistanceToMyShip", initialEnemyDistance);
            float normalizationDistance = Mathf.Max(spaceship.SpeedMax * 8f, 1f);
            context.SetData("EnemyDistanceNormalized", Mathf.Clamp01(initialEnemyDistance / normalizationDistance));

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
            context.SetData("EnemyAngleToForward", ComputeEnemyAngle(spaceship, _enemyShip));
            context.SetData("CurrentCombatMode", _currentCombatMode);
            context.SetData("PreviousCombatMode", _previousCombatMode);
            context.SetData("TimeInCurrentCombatMode", 0f);
            context.SetData("HuntFocusWaypoint", null);
            context.SetData("HuntLockedWaypoint", null);
            context.SetData("HuntTargetPoint", spaceship.Position);
        }


        // --- MISE À JOUR À CHAQUE FRAME ---
        public void UpdateContext(SpaceShipView spaceship, GameData data)
        {
            if (context == null)
                return;
            
            // Données basiques
            int myScore = spaceship.Score;
            int enemyScore = _enemyShip.Score;
            int scoreLead = myScore - enemyScore;

            context.SetData("MyScore", myScore);
            context.SetData("EnemyScore", enemyScore);
            context.SetData("ScoreLead", scoreLead);
            context.SetData("MyEnergyLeft", spaceship.Energy);
            context.SetData("EnemyEnergyLeft", _enemyShip.Energy);
            context.SetData("TimeLeft", data.timeLeft);

            // Position et distances
            float enemyDistance = Vector2.Distance(spaceship.Position, _enemyShip.Position);
            context.SetData("EnemyDistanceToMyShip", enemyDistance);
            float normalizationDistance = Mathf.Max(spaceship.SpeedMax * 8f, 1f);
            context.SetData("EnemyDistanceNormalized", Mathf.Clamp01(enemyDistance / normalizationDistance));
            context.SetData("EnemyAngleToForward", ComputeEnemyAngle(spaceship, _enemyShip));
            context.SetData("TimeInCurrentCombatMode", Time.time - _lastCombatModeChangeTime);

            UpdateWaypointStatistics(spaceship, data, out int totalWaypoints, out int ownedWaypoints, out int enemyWaypoints, out int neutralWaypoints);
            EvaluateCombatMode(totalWaypoints, ownedWaypoints, scoreLead);

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

            if (bestAction != null)
            {
                context.SetData("CurrentActionName", bestAction.name);
                return bestAction.Execute(context);
            }

            context.SetData("CurrentActionName", string.Empty);
            return new InputData();
        }

        
        private static bool EnemyIsInFront(SpaceShipView myShip, SpaceShipView enemyShip, float angleTolerance)
        {
            return IsPointInCone(myShip.Position, myShip.LookAt, enemyShip.Position, angleTolerance);
        }

        private static bool EnemyIsBehind(SpaceShipView myShip, SpaceShipView enemyShip, float angleTolerance)
        {
            return IsPointInCone(myShip.Position, -myShip.LookAt, enemyShip.Position, angleTolerance);
        }

        private void UpdateWaypointStatistics(SpaceShipView spaceship, GameData data, out int totalWaypoints, out int ownedWaypoints, out int enemyWaypoints, out int neutralWaypoints)
        {
            totalWaypoints = data?.WayPoints?.Count ?? 0;
            ownedWaypoints = 0;
            enemyWaypoints = 0;
            neutralWaypoints = 0;

            if (data?.WayPoints != null)
            {
                foreach (WayPointView waypoint in data.WayPoints)
                {
                    if (waypoint == null)
                        continue;

                    if (waypoint.Owner == spaceship.Owner)
                        ownedWaypoints++;
                    else if (_enemyShip != null && waypoint.Owner == _enemyShip.Owner)
                        enemyWaypoints++;
                    else
                        neutralWaypoints++;
                }
            }

            float ownedRatio = totalWaypoints > 0 ? (float)ownedWaypoints / totalWaypoints : 0f;

            context.SetData("TotalWaypointCount", totalWaypoints);
            context.SetData("OwnedWaypointCount", ownedWaypoints);
            context.SetData("EnemyWaypointCount", enemyWaypoints);
            context.SetData("NeutralWaypointCount", neutralWaypoints);
            context.SetData("OwnedWaypointRatio", ownedRatio);
        }

        private void EvaluateCombatMode(int totalWaypoints, int ownedWaypoints, int scoreLead)
        {
            if (context == null)
                return;

            if (_currentCombatMode == CombatMode.None)
            {
                SetCombatMode(CombatMode.Capture, true);
                return;
            }

            float ownedRatio = totalWaypoints > 0 ? (float)ownedWaypoints / totalWaypoints : 0f;

            bool huntConditionsMet, captureConditionsMet;

            if (useScoreForCombatModeOnly)
            {
                huntConditionsMet = scoreLead >= scoreLeadForHunt;
                captureConditionsMet = scoreLead <= scoreLeadForCapture;
            }
            else
            {
                huntConditionsMet = ownedRatio >= ownedRatioForHunt;
                captureConditionsMet = ownedRatio <= ownedRatioForCapture;
            }


            if (combatModeSwitchCooldown > 0f)
            {
                float timeSinceChange = Time.time - _lastCombatModeChangeTime;
                if (timeSinceChange < combatModeSwitchCooldown)
                    return;
            }

            if (_currentCombatMode != CombatMode.Hunt && huntConditionsMet)
            {
                SetCombatMode(CombatMode.Hunt);
            }
            else if (_currentCombatMode == CombatMode.Hunt && captureConditionsMet && !huntConditionsMet)
            {
                SetCombatMode(CombatMode.Capture);
            }
        }

        private static float ComputeEnemyAngle(SpaceShipView myShip, SpaceShipView enemyShip)
        {
            Vector2 forward = myShip.LookAt.normalized;
            Vector2 toEnemy = (enemyShip.Position - myShip.Position).normalized;
            return Vector2.Angle(forward, toEnemy);
        }

        private static bool IsPointInCone(Vector2 origin, Vector2 direction, Vector2 point, float angleDeg)
        {
            Vector2 toPoint = (point - origin).normalized;
            float dot = Vector2.Dot(direction.normalized, toPoint);
            float halfAngle = angleDeg * 0.5f;
            return dot > Mathf.Cos(halfAngle * Mathf.Deg2Rad);
        }

        public CombatMode CurrentCombatMode => _currentCombatMode;

        public CombatMode PreviousCombatMode => _previousCombatMode;

        public bool SetCombatMode(CombatMode newMode, bool force = false)
        {
            if (!force && newMode == _currentCombatMode)
                return false;

            _previousCombatMode = _currentCombatMode;
            _currentCombatMode = newMode;
            _lastCombatModeChangeTime = Time.time;

            if (context != null)
            {
                
                Debug.Log(newMode);
                
                context.SetData("PreviousCombatMode", _previousCombatMode);
                context.SetData("CurrentCombatMode", _currentCombatMode);
                context.SetData("TimeInCurrentCombatMode", 0f);
            }

            return true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (scoreLeadForHunt < scoreLeadForCapture)
                scoreLeadForHunt = scoreLeadForCapture;

            if (ownedRatioForHunt < ownedRatioForCapture)
                ownedRatioForHunt = ownedRatioForCapture;

            scoreLeadForCapture = Mathf.Max(0, scoreLeadForCapture);
            scoreLeadForHunt = Mathf.Max(scoreLeadForHunt, scoreLeadForCapture);
            ownedRatioForCapture = Mathf.Clamp01(ownedRatioForCapture);
            ownedRatioForHunt = Mathf.Clamp01(Mathf.Max(ownedRatioForHunt, ownedRatioForCapture));
            combatModeSwitchCooldown = Mathf.Max(0f, combatModeSwitchCooldown);
        }
#endif
    }
}
