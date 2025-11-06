using System.Collections.Generic;
using System.Linq;
using DoNotModify;
using Teams.ActarusController.Shahine.UtilityActions;
using Teams.ActarusControllerV2.pierre;
using UnityEngine;
using UtilityAI;

namespace Teams.ActarusController.Shahine
{
    /// <summary>
    /// Modular spaceship controller orchestrating the different AI subsystems (Utility AI version).
    /// </summary>
    [System.Serializable]
    public sealed class CombatModeBrain
    {
        [SerializeField]
        private List<CombatModeUtilityAction> actions = new();

        public IReadOnlyList<CombatModeUtilityAction> Actions => actions;

        public void Initialize(Context context)
        {
            if (context == null)
                return;

            foreach (var action in actions)
            {
                action?.Initialize(context);
            }
        }

        public void AddAction(CombatModeUtilityAction action)
        {
            if (action == null || actions.Contains(action))
                return;

            actions.Add(action);
        }

        public CombatModeUtilityAction Decide(Context context)
        {
            if (context == null || actions == null || actions.Count == 0)
                return null;

            CombatModeUtilityAction best = null;
            float highestUtility = float.MinValue;

            foreach (var action in actions)
            {
                if (action == null)
                    continue;

                float utility = action.CalculateUtility(context);
                if (utility > highestUtility)
                {
                    highestUtility = utility;
                    best = action;
                }
            }

            return best;
        }
    }

    public sealed class ActarusControllerUtilityAI : BaseSpaceShipController
    {
        public List<AIAction> actions;
        [Tooltip("Utility brain deciding when to switch between Hunt and Capture combat modes.")]
        public CombatModeBrain combatBrain = new();
        public Context context;


        // Cache
        private SpaceShipView _enemyShip;
        private WaypointPrioritySystem _waypointSystem;
        
        private WayPointView _targetWaypoint;
        private WayPointView _nextWaypoint;
        private WayPointView _lastWaypoint;

        private float _enemyAggressionIndex;
        private float _initialMatchTime = -1f;
        private float _lastCombatModeSwitchTime;

        public enum CombatMode
        {
            Capture,
            Hunt
        }

        public CombatMode CurrentCombatMode { get; private set; } = CombatMode.Capture;


        // --- INITIALISATION ---
        public override void Initialize(SpaceShipView spaceship, GameData data)
        {
            context = new Context(this);

            // Init toutes les actions
            foreach (var action in actions)
                action.Initialize(context);

            combatBrain?.Initialize(context);

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

            _initialMatchTime = data.timeLeft;
            _lastCombatModeSwitchTime = Time.time;

            context.SetData("scoreLead", spaceship.Score - _enemyShip.Score);
            context.SetData("waypointLead", spaceship.WaypointScore - _enemyShip.WaypointScore);
            context.SetData("myEnergyNormalized", Mathf.Clamp01(spaceship.Energy));
            context.SetData("enemyWeak", Mathf.Clamp01(1f - _enemyShip.Energy));
            context.SetData("enemyAggressionIndex", _enemyAggressionIndex);
            context.SetData("enemyDistance", Vector2.Distance(spaceship.Position, _enemyShip.Position));
            context.SetData("enemyRunningAway", 0f);
            context.SetData("timeLeftNormalized", _initialMatchTime > 0f ? Mathf.Clamp01(data.timeLeft / _initialMatchTime) : 1f);
            context.SetData("timeSinceLastCombatModeSwitch", 0f);
            context.SetData("LastCombatModeSwitchTime", _lastCombatModeSwitchTime);
            context.SetData("CurrentCombatMode", CurrentCombatMode);
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
            context.SetData("enemyDistance", enemyDistance);

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

            int scoreLead = spaceship.Score - _enemyShip.Score;
            int waypointLead = spaceship.WaypointScore - _enemyShip.WaypointScore;

            context.SetData("scoreLead", scoreLead);
            context.SetData("waypointLead", waypointLead);
            context.SetData("myEnergyNormalized", Mathf.Clamp01(spaceship.Energy));
            context.SetData("enemyWeak", Mathf.Clamp01(1f - _enemyShip.Energy));

            UpdateEnemyAggression(spaceship);
            context.SetData("enemyAggressionIndex", _enemyAggressionIndex);

            context.SetData("enemyRunningAway", EvaluateEnemyRunningAway(spaceship));

            if (_initialMatchTime <= 0f)
                _initialMatchTime = data.timeLeft;

            float normalizedTimeLeft = _initialMatchTime > 0f ? Mathf.Clamp01(data.timeLeft / _initialMatchTime) : 1f;
            context.SetData("timeLeftNormalized", normalizedTimeLeft);

            float timeSinceSwitch = Time.time - _lastCombatModeSwitchTime;
            context.SetData("timeSinceLastCombatModeSwitch", timeSinceSwitch);
            context.SetData("LastCombatModeSwitchTime", _lastCombatModeSwitchTime);
            context.SetData("CurrentCombatMode", CurrentCombatMode);
        }


        // --- DECISION ---
        public override InputData UpdateInput(SpaceShipView spaceship, GameData data)
        {
            UpdateContext(spaceship, data);

            UpdateCombatModeDecision();


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

        private void UpdateCombatModeDecision()
        {
            if (combatBrain == null || context == null)
                return;

            CombatModeUtilityAction best = combatBrain.Decide(context);
            best?.ApplyMode(context);
        }

        public void SetCombatMode(CombatMode newMode)
        {
            if (CurrentCombatMode == newMode)
                return;

            CurrentCombatMode = newMode;
            _lastCombatModeSwitchTime = Time.time;

            context?.SetData("CurrentCombatMode", CurrentCombatMode);
            context?.SetData("LastCombatModeSwitchTime", _lastCombatModeSwitchTime);
            context?.SetData("timeSinceLastCombatModeSwitch", 0f);
        }

        private void UpdateEnemyAggression(SpaceShipView myShip)
        {
            if (myShip == null || _enemyShip == null)
                return;

            bool aimingAtUs = AimingHelpers.CanHit(_enemyShip, myShip.Position, 20f);

            float distance = Vector2.Distance(myShip.Position, _enemyShip.Position);
            Vector2 toMyShip = (myShip.Position - _enemyShip.Position).normalized;
            float relativeClosingSpeed = _enemyShip.Velocity.sqrMagnitude > 0.0001f ? Vector2.Dot(_enemyShip.Velocity.normalized, toMyShip) : 0f;
            bool closingIn = relativeClosingSpeed > 0.5f;
            bool closeRange = distance < 4.5f;

            float targetAggression = (aimingAtUs || closingIn || closeRange) ? 1f : 0f;
            float lerpSpeed = targetAggression > _enemyAggressionIndex ? 0.06f : 0.02f;
            _enemyAggressionIndex = Mathf.Lerp(_enemyAggressionIndex, targetAggression, lerpSpeed);
        }

        private float EvaluateEnemyRunningAway(SpaceShipView myShip)
        {
            if (myShip == null || _enemyShip == null)
                return 0f;

            Vector2 enemyVelocity = _enemyShip.Velocity;
            if (enemyVelocity.sqrMagnitude < 0.0001f)
                return 0f;

            Vector2 toEnemy = (_enemyShip.Position - myShip.Position).normalized;
            float alignment = Vector2.Dot(enemyVelocity.normalized, toEnemy);

            // alignment >= 1 => sprinting directly away, alignment <= 0 => moving towards us
            return Mathf.Clamp01(Mathf.InverseLerp(0.1f, 1f, alignment));
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only helper illustrating how to populate the combat mode brain from code, mirroring
        /// the classic UtilityAgent initialisation with explicit <c>Brain.AddAction()</c> calls.
        /// Trigger the context menu to automatically create in-memory Hunt/Capture actions and register them.
        /// </summary>
        [ContextMenu("Utility AI/Configure Default Combat Mode Actions")]
        private void ConfigureDefaultCombatModeBrain()
        {
            if (combatBrain == null)
                combatBrain = new CombatModeBrain();

            if (combatBrain.Actions.Count > 0)
                return;

            var hunt = ScriptableObject.CreateInstance<SwitchToHuntModeAction>();
            var capture = ScriptableObject.CreateInstance<SwitchToCaptureModeAction>();

            combatBrain.AddAction(hunt);
            combatBrain.AddAction(capture);
        }
#endif


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
