using DoNotModify;
using Teams.ActarusController.Shahine.UtilityActions;
using UnityEngine;

namespace Teams.ActarusController.Shahine
{
    /// <summary>
    /// Modular spaceship controller orchestrating the different AI subsystems.
    /// </summary>
    public sealed class ActarusControllerV1 : BaseSpaceShipController
    {
        [SerializeField] private Blackboard _blackboard;

        [SerializeField] private UtilityAgent _agent;


        [Header("Debug")] public bool CanDropMine;


        /// <inheritdoc />
        public override void Initialize(SpaceShipView spaceship, GameData data)
        {
            _blackboard = GetComponent<Blackboard>();
            _blackboard.InitializeFromGameData(spaceship, data);
            _agent = GetComponent<UtilityAgent>();
            _agent.Init(_blackboard);
        }

        /// <inheritdoc />
        public override InputData UpdateInput(SpaceShipView ship, GameData data)
        {
            _blackboard.UpdateFromGameData(data);

            InputData input = _agent.Decide();
            if (_blackboard.hasToDropMine && CanDropMine)
            {
                Debug.Log(_blackboard.distanceToLastTarget + _blackboard.lastWayPoint.Radius + _blackboard.myShip.Radius);
                if (_blackboard.distanceToLastTarget + _blackboard.lastWayPoint.Radius + _blackboard.myShip.Radius <= 0.7f)
                {
                    input.dropMine = true;
                    _blackboard.hasToDropMine = false;
                }
            }

            return input;
        }
    }
}
