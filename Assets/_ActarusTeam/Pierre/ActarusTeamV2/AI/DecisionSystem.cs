using DoNotModify;
using UnityEngine;

namespace Teams.ActarusControllerV2.pierre
{
    public enum ShipState
    {
        Idle,
        Capture,
        Attack,
        Retreat,
        Orbit,
        Evade
    }

    public readonly struct DecisionContext
    {
        public readonly SpaceShipView Self;
        public readonly WayPointView Waypoint;

        public readonly bool HasThreat;
        public readonly bool InPenalty;
        public readonly bool LowEnergy;

        public readonly bool HasWaypoint;
        public readonly bool WaypointFriendlyOwned;
        public readonly bool WaypointNeutral;
        public readonly bool WaypointEnemyOwned;
        public readonly bool EnemyVisible;

        public readonly int OwnedCount;
        public readonly int EnemyCount;
        public readonly int NeutralCount;
        public readonly int TotalCount;

        public readonly bool StrongLead;
        public readonly bool NeedsMoreCapture;
        public readonly bool HoldLead;
        public readonly bool Endgame;

        public readonly WayPointView Pivot;
        public readonly float EnemyAggression;
        public readonly float EnemyCaptureFocus;

        public DecisionContext(Blackboard bb, GameData data,
            float retreatEnergyThreshold, float holdRatio, float endgameTime)
        {
            Self = bb.Self;
            Waypoint = bb.TargetWaypoint;

            HasThreat = bb.HasImminentThreat;
            InPenalty = Self.HitPenaltyCountdown > 0f || Self.StunPenaltyCountdown > 0f;
            LowEnergy = Self.Energy < retreatEnergyThreshold;

            HasWaypoint = Waypoint != null;
            WaypointFriendlyOwned = HasWaypoint && Waypoint.Owner == Self.Owner;
            WaypointNeutral = HasWaypoint && Waypoint.Owner < 0;
            WaypointEnemyOwned = HasWaypoint && Waypoint.Owner >= 0 && Waypoint.Owner != Self.Owner;

            EnemyVisible = bb.EnemyVisible && bb.Enemy != null;

            OwnedCount = EnemyCount = NeutralCount = TotalCount = 0;

            if (data?.WayPoints != null)
            {
                foreach (var wp in data.WayPoints)
                {
                    if (wp == null)
                        continue;
                    
                    if (wp.Owner == Self.Owner) 
                        OwnedCount++;
                    
                    else if (wp.Owner < 0)
                    {
                        NeutralCount++;   
                    }
                    else
                    {
                        EnemyCount++;   
                    }
                }
                TotalCount = OwnedCount + EnemyCount + NeutralCount;
            }

            StrongLead = OwnedCount >= EnemyCount + 1;
            NeedsMoreCapture = OwnedCount <= EnemyCount || NeutralCount > 0;
            Endgame = data != null && data.timeLeft <= endgameTime;
            HoldLead = StrongLead || Endgame;

            Pivot = bb.PivotPoint;
            EnemyAggression = bb.EnemyAggressionIndex;
            EnemyCaptureFocus = bb.EnemyCaptureFocus;
        }
    }

    public sealed class DecisionSystem
    {
        private const float EvadeMinDuration = 0.35f;
        private const float RetreatEnergyThreshold = 0.18f;
        private const float HoldRatio = 0.6f;
        private const float EndgameTime = 18f;

        private readonly Blackboard _bb;

        public DecisionSystem(Blackboard blackboard) => _bb = blackboard;

        public void UpdateDecision(GameData data)
        {
            if (_bb.Self == null) return;

            ResetIntentions();
            TrackEnemyBehavior(data);

            var ctx = new DecisionContext(_bb, data, RetreatEnergyThreshold, HoldRatio, EndgameTime);
            ShipState next = EvaluateState(ctx);

            if (next != _bb.CurrentState)
            {
                _bb.CurrentState = next;
                _bb.LastStateChangeTime = Time.time;
            }

            Apply(ctx, next);
        }

        private ShipState EvaluateState(in DecisionContext ctx)
        {
            if (ctx.HasThreat && EvadeReady()) return ShipState.Evade;
            if (ctx.InPenalty || ctx.LowEnergy) return ShipState.Retreat;

            bool contest = ctx.WaypointEnemyOwned || ctx.WaypointNeutral;

            if (ctx.EnemyAggression < 0.3f && ctx.WaypointEnemyOwned) 
                return ShipState.Attack;

            if (ctx.HoldLead && ctx.Pivot != null)
            {
                if (_bb.TargetWaypoint != ctx.Pivot)
                    _bb.TargetWaypoint = ctx.Pivot;

                return ctx.EnemyVisible ? ShipState.Attack : ShipState.Orbit;
            }

            if (ctx.NeedsMoreCapture && contest)
                return ShipState.Capture;

            if (ctx.EnemyVisible)
            {
                return ctx.EnemyAggression > 0.5f ? ShipState.Retreat : ShipState.Attack;
            }

            if (ctx.HasWaypoint) return contest ? ShipState.Capture : ShipState.Orbit;

            return ShipState.Idle;
        }

        private void Apply(in DecisionContext ctx, ShipState state)
        {
            _bb.ShouldCapture = state == ShipState.Capture;
            _bb.ShouldEngageEnemy = state == ShipState.Attack;
            _bb.ShouldRetreat = state == ShipState.Retreat;
            _bb.ShouldEvade = state == ShipState.Evade;
            _bb.ShouldOrbit = state == ShipState.Orbit;

            _bb.ShouldShoot = state == ShipState.Attack;
            _bb.ShouldDropMine = state == ShipState.Retreat && ctx.EnemyAggression > 0.6f;
        }

        private void ResetIntentions()
        {
            _bb.ShouldCapture = _bb.ShouldEngageEnemy = _bb.ShouldRetreat =
            _bb.ShouldEvade = _bb.ShouldOrbit = _bb.ShouldShoot = _bb.ShouldDropMine = false;
        }

        private bool EvadeReady() => _bb.CurrentState != ShipState.Evade || Time.time - _bb.LastStateChangeTime > EvadeMinDuration;

        private void TrackEnemyBehavior(GameData data)
        {
            if (_bb.EnemyVisible)
            {
                _bb.EnemyAggressionIndex = Mathf.Lerp(_bb.EnemyAggressionIndex, 1f, 0.02f);   
            }
            else
            {
                _bb.EnemyAggressionIndex = Mathf.Lerp(_bb.EnemyAggressionIndex, 0f, 0.01f);   
            }
        }
    }
}
