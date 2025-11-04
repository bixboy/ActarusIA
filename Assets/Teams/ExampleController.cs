using DoNotModify;

/// <summary>
/// Modular spaceship controller orchestrating the different AI subsystems.
/// </summary>
public sealed class ExampleController : BaseSpaceShipController
{
    /// <inheritdoc />
    public override void Initialize(SpaceShipView spaceship, GameData data)
    {

    }

    /// <inheritdoc />
    public override InputData UpdateInput(SpaceShipView spaceship, GameData data)
    {
        SpaceShipView otherSpaceship = data.GetSpaceShipForOwner(1 - spaceship.Owner);
        float thrust = 1.0f;
        float targetOrient = spaceship.Orientation + 90.0f;
        bool needShoot = AimingHelpers.CanHit(spaceship, otherSpaceship.Position, otherSpaceship.Velocity, 0.15f);
        return new InputData(thrust, targetOrient, needShoot, false, false);
    }

};
