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
        private Blackboard _blackboard;
        private UtilityAgent _agent;


        /// <inheritdoc />
        public override void Initialize(SpaceShipView spaceship, GameData data)
        {
            _blackboard = Blackboard.InitializeFromGameData(spaceship, data);
            _agent = new UtilityAgent(_blackboard);
            _agent.RegisterAction(new CaptureZone(_blackboard));
        }

        /// <inheritdoc />
        public override InputData UpdateInput(SpaceShipView ship, GameData data)
        {
            _blackboard.UpdateFromGameData(data);

            InputData input = _agent.Decide();

            return input;
        }
    }
}
