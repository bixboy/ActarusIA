using DoNotModify;
using UnityEngine;

namespace Teams.ActarusControllerV1.pierre
{
    /// <summary>
    /// Enumeration of the high-level behaviour states used by the AI.
    /// </summary>
    public enum ShipState
    {
        Idle,
        Capture,
        Attack,
        Retreat,
        Orbit,
        Evade
    }

    /// <summary>
    /// Provides a steering advisory service required by the decision system.
    /// </summary>
    public interface IAvoidanceProvider
    {
        /// <summary>
        /// Computes an emergency evasion direction based on the current context.
        /// </summary>
        /// <param name="data">The game data.</param>
        /// <returns>A normalized direction favouring evasion.</returns>
        Vector2 ComputeEmergencyEvadeDirection(GameData data);
    }

    /// <summary>
    /// Evaluates the current state of the AI and updates the movement intentions.
    /// </summary>
    public sealed class DecisionSystem
    {
        private const float EvadeMinDuration = 0.35f;
        private const float CaptureSlowDistance = 1.2f;
        private const float FirePredictionLeadTime = 0.65f;
        private const float RetreatEnergyThreshold = 0.18f;
        private const float MidEnergyThreshold = 0.55f;

        private readonly Blackboard _blackboard;
        private readonly IAvoidanceProvider _avoidanceProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="DecisionSystem"/> class.
        /// </summary>
        /// <param name="blackboard">Shared blackboard instance.</param>
        /// <param name="avoidanceProvider">Steering helper used for evasive decisions.</param>
        public DecisionSystem(Blackboard blackboard, IAvoidanceProvider avoidanceProvider)
        {
            _blackboard = blackboard;
            _avoidanceProvider = avoidanceProvider;
        }

        /// <summary>
        /// Updates the current state and desired movement based on perception data.
        /// </summary>
        /// <param name="data">The full game state.</param>
        public void UpdateDecision(GameData data)
        {
            if (_blackboard.Self == null)
            {
                return;
            }

            ResetDesiredMovement();

            ShipState nextState = EvaluateState(data);
            if (nextState != _blackboard.CurrentState)
            {
                _blackboard.CurrentState = nextState;
                _blackboard.LastStateChangeTime = Time.time;
            }

            ExecuteStateLogic(data);
        }

        private void ResetDesiredMovement()
        {
            if (_blackboard.Self.Velocity.sqrMagnitude > 0.01f)
            {
                _blackboard.DesiredDirection = _blackboard.Self.Velocity.normalized;
            }
            else
            {
                _blackboard.DesiredDirection = Blackboard.AngleToDir(_blackboard.Self.Orientation);
            }

            _blackboard.DesiredSpeed = _blackboard.Self.SpeedMax * 0.5f;
        }

        private ShipState EvaluateState(GameData data)
        {
            if (_blackboard.HasImminentThreat)
            {
                if (_blackboard.CurrentState != ShipState.Evade || Time.time - _blackboard.LastStateChangeTime > EvadeMinDuration)
                {
                    return ShipState.Evade;
                }
            }

            if (IsInPenalty(_blackboard.Self) || _blackboard.Self.Energy < RetreatEnergyThreshold)
            {
                return ShipState.Retreat;
            }

            if (_blackboard.TargetWaypoint != null && _blackboard.TargetWaypoint.Owner != _blackboard.Self.Owner)
            {
                return ShipState.Capture;
            }

            if (_blackboard.EnemyVisible && _blackboard.Enemy != null)
            {
                return ShipState.Attack;
            }

            if (_blackboard.TargetWaypoint != null)
            {
                return ShipState.Orbit;
            }

            return ShipState.Idle;
        }

        private void ExecuteStateLogic(GameData data)
        {
            switch (_blackboard.CurrentState)
            {
                case ShipState.Capture:
                    DoCaptureLogic();
                    break;
                case ShipState.Attack:
                    DoAttackLogic();
                    break;
                case ShipState.Retreat:
                    DoRetreatLogic();
                    break;
                case ShipState.Orbit:
                    DoOrbitLogic();
                    break;
                case ShipState.Evade:
                    DoEvadeLogic(data);
                    break;
                default:
                    DoIdleLogic();
                    break;
            }
        }

        private void DoCaptureLogic()
        {
            WayPointView waypoint = _blackboard.TargetWaypoint;
            Vector2 targetPosition = waypoint != null
                ? waypoint.Position
                : _blackboard.Self.Position + Blackboard.AngleToDir(_blackboard.Self.Orientation);

            Vector2 desired = targetPosition - _blackboard.Self.Position;
            float distance = desired.magnitude;
            if (distance > 0.001f)
            {
                desired /= distance;
            }

            float speedRatio = distance > CaptureSlowDistance
                ? 1.0f
                : Mathf.Lerp(0.45f, 0.8f, Mathf.InverseLerp(0.15f, CaptureSlowDistance, distance));

            _blackboard.DesiredDirection = desired;
            _blackboard.DesiredSpeed = _blackboard.Self.SpeedMax * speedRatio;

            _blackboard.ShouldShoot &= _blackboard.EnemyVisible;
            _blackboard.ShouldDropMine &= _blackboard.EnemyVisible;
        }

        private void DoAttackLogic()
        {
            Vector2 predicted = _blackboard.Enemy != null
                ? _blackboard.Enemy.Position + _blackboard.Enemy.Velocity * FirePredictionLeadTime
                : _blackboard.Self.Position + Blackboard.AngleToDir(_blackboard.Self.Orientation);

            Vector2 desired = predicted - _blackboard.Self.Position;
            float distance = desired.magnitude;
            if (distance > 0.001f)
            {
                desired /= distance;
            }

            _blackboard.DesiredDirection = desired;
            _blackboard.DesiredSpeed = _blackboard.Self.SpeedMax;
        }

        private void DoRetreatLogic()
        {
            Vector2 desired = _blackboard.Enemy != null
                ? (_blackboard.Self.Position - _blackboard.Enemy.Position)
                : (-_blackboard.Self.Velocity);

            if (desired.sqrMagnitude < 0.001f)
            {
                desired = Blackboard.AngleToDir(_blackboard.Self.Orientation + 180f);
            }

            _blackboard.DesiredDirection = desired.normalized;
            _blackboard.DesiredSpeed = Mathf.Lerp(
                _blackboard.Self.SpeedMax * 0.35f,
                _blackboard.Self.SpeedMax * 0.65f,
                Mathf.Clamp01(_blackboard.Self.Energy / MidEnergyThreshold));

            _blackboard.ShouldShoot = false;
            _blackboard.ShouldDropMine = false;
        }

        private void DoOrbitLogic()
        {
            Vector2 center = _blackboard.TargetWaypoint != null
                ? _blackboard.TargetWaypoint.Position
                : _blackboard.Self.Position;

            Vector2 radial = _blackboard.Self.Position - center;
            if (radial.sqrMagnitude < 0.0001f)
            {
                radial = Blackboard.AngleToDir(_blackboard.Self.Orientation);
            }

            Vector2 tangent = new Vector2(-radial.y, radial.x).normalized;
            _blackboard.DesiredDirection = tangent;
            _blackboard.DesiredSpeed = _blackboard.Self.SpeedMax * 0.6f;
        }

        private void DoEvadeLogic(GameData data)
        {
            Vector2 evadeDir = _avoidanceProvider != null
                ? _avoidanceProvider.ComputeEmergencyEvadeDirection(data)
                : Vector2.zero;

            if (evadeDir.sqrMagnitude < 0.0001f)
            {
                evadeDir = Blackboard.AngleToDir(_blackboard.Self.Orientation + 90f);
            }

            _blackboard.DesiredDirection = evadeDir.normalized;
            _blackboard.DesiredSpeed = Mathf.Max(
                _blackboard.Self.SpeedMax * 0.75f,
                _blackboard.Self.Velocity.magnitude);

            _blackboard.ShouldShoot = false;
            _blackboard.ShouldDropMine = false;
        }

        private void DoIdleLogic()
        {
            if (_blackboard.Self.Velocity.sqrMagnitude > 0.01f)
            {
                _blackboard.DesiredDirection = _blackboard.Self.Velocity.normalized;
            }
            else
            {
                _blackboard.DesiredDirection = Blackboard.AngleToDir(_blackboard.Self.Orientation);
            }

            _blackboard.DesiredSpeed = _blackboard.Self.SpeedMax * 0.45f;
            _blackboard.ShouldDropMine = false;
        }

        private static bool IsInPenalty(SpaceShipView self)
        {
            return self.HitPenaltyCountdown > 0f || self.StunPenaltyCountdown > 0f;
        }
    }
}
