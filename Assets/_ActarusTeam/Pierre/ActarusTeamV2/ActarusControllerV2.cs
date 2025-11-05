using DoNotModify;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Modular spaceship controller orchestrating the different AI subsystems.
    /// </summary>
    public sealed class ActarusControllerV2 : BaseSpaceShipController
    {
        private Blackboard _blackboard;
        private PerceptionSystem _perception;
        private WaypointPrioritySystem _waypointSystem;
        private DecisionSystem _decision;
        private SteeringSystem _steering;
        private CombatSystem _combat;

        /// <inheritdoc />
        public override void Initialize(SpaceShipView spaceship, GameData data)
        {
            _blackboard = new Blackboard(spaceship);
            _steering = new SteeringSystem(_blackboard);
            _perception = new PerceptionSystem(_blackboard);
            _decision = new DecisionSystem(_blackboard);
            _combat = new CombatSystem(_blackboard);

            _waypointSystem = new WaypointPrioritySystem();
        }


        /// <inheritdoc />
        public override InputData UpdateInput(SpaceShipView spaceship, GameData data)
        {
            _perception.UpdatePerception(spaceship, data);
            WaypointSelectionResult selection = _waypointSystem.SelectBestWaypoint(spaceship, data);
            _blackboard.TargetWaypoint = selection.TargetWaypoint;
            _blackboard.TargetWaypointPredictions = selection.FutureWaypoints;

            _combat.UpdateWeapons(data);
            _decision.UpdateDecision(data);
            _steering.UpdateSteering(data);
            _combat.CommitCommands();

            return new InputData(
                _steering.ThrustCommand,
                _steering.OrientationCommand,
                _combat.ShouldShoot,
                _combat.ShouldDropMine,
                _combat.ShouldShockwave);
        }
    }
}
