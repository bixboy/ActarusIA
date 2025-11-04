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

            InputData input = new InputData();

            if (_blackboard.targetWaypoint != null)
            {
                float targetOrient = AimingHelpers.ComputeSteeringOrient(
                    ship,
                    _blackboard.targetWaypoint.Position,
                    1.1f
                );

                input.targetOrientation = targetOrient;

                float angleDiff = Mathf.Abs(Mathf.DeltaAngle(ship.Orientation, targetOrient));

                if (angleDiff < 30f)
                {
                    input.thrust = Mathf.Lerp(0.5f, 1f, 1 - angleDiff / 30f);
                }
                else
                {
                    input.thrust = 0f;
                }

                // Freinage progressif à l’approche
                if (_blackboard.distanceToTarget < 0.6f)
                    input.thrust = Mathf.Lerp(input.thrust, 0f, 0.2f);
            }

            return input;
        }
    }
}
