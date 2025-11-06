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
            if (_blackboard.HasToDropMine && CanDropMine)
            {
                Debug.Log(_blackboard.DistanceToLastTarget + _blackboard.LastWayPoint.Radius + _blackboard.MyShip.Radius);
                if (_blackboard.DistanceToLastTarget + _blackboard.LastWayPoint.Radius + _blackboard.MyShip.Radius <= 0.7f)
                {
                    input.dropMine = true;
                    _blackboard.HasToDropMine = false;
                }
            }

            return input;
        }
    }
}
