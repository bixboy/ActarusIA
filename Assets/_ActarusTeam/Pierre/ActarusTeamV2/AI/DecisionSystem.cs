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
        public DecisionContext(Blackboard bb, GameData data, in WaypointSelectionResult selection, float retreatEnergyThreshold, float endgameTime)
        {
            Blackboard = bb;
            Self = bb.Self;
            Enemy = bb.Enemy;
            Selection = selection;
            Target = selection.TargetWaypoint ?? bb.TargetWaypoint;

            HasThreat = bb.HasImminentThreat;
            InPenalty = Self != null && (Self.HitPenaltyCountdown > 0f || Self.StunPenaltyCountdown > 0f);
            LowEnergy = Self != null && Self.Energy < retreatEnergyThreshold;
            EnemyVisible = bb.EnemyVisible && bb.Enemy != null;

            CurrentState = bb.CurrentState;
            TimeSinceStateChange = Time.time - bb.LastStateChangeTime;

            Pivot = bb.PivotPoint;
            EnemyAggression = bb.EnemyAggressionIndex;
            EnemyCaptureFocus = bb.EnemyCaptureFocus;

            HasTarget = Target != null;
            TargetFriendlyOwned = HasTarget && Self != null && Target.Owner == Self.Owner;
            TargetNeutral = HasTarget && Target.Owner < 0;
            TargetEnemyOwned = HasTarget && Self != null && Target.Owner >= 0 && Target.Owner != Self.Owner;
            TargetContestable = TargetEnemyOwned || TargetNeutral;

            OwnedCount = EnemyCount = NeutralCount = 0;
            if (data?.WayPoints != null && Self != null)
            {
                foreach (WayPointView waypoint in data.WayPoints)
                {
                    if (waypoint == null)
                        continue;

                    if (waypoint.Owner == Self.Owner)
                        OwnedCount++;
                    else if (waypoint.Owner < 0)
                        NeutralCount++;
                    else
                        EnemyCount++;
                }
            }

            TotalCount = OwnedCount + EnemyCount + NeutralCount;
            StrongLead = OwnedCount >= EnemyCount + 1;
            NeedsCapture = OwnedCount <= EnemyCount || NeutralCount > 0;
            Endgame = data != null && data.timeLeft <= endgameTime;
            HoldLead = StrongLead || Endgame;
        }

        public Blackboard Blackboard { get; }
        public SpaceShipView Self { get; }
        public SpaceShipView Enemy { get; }
        public WayPointView Target { get; }
        public WaypointSelectionResult Selection { get; }

        public bool HasThreat { get; }
        public bool InPenalty { get; }
        public bool LowEnergy { get; }
        public bool EnemyVisible { get; }

        public bool HasTarget { get; }
        public bool TargetFriendlyOwned { get; }
        public bool TargetNeutral { get; }
        public bool TargetEnemyOwned { get; }
        public bool TargetContestable { get; }

        public int OwnedCount { get; }
        public int EnemyCount { get; }
        public int NeutralCount { get; }
        public int TotalCount { get; }

        public bool StrongLead { get; }
        public bool NeedsCapture { get; }
        public bool HoldLead { get; }
        public bool Endgame { get; }

        public WayPointView Pivot { get; }
        public float EnemyAggression { get; }
        public float EnemyCaptureFocus { get; }
        public ShipState CurrentState { get; }
        public float TimeSinceStateChange { get; }
    }

    public sealed class DecisionSystem
    {
        private const float EvadeMinDuration = 0.35f;
        private const float RetreatEnergyThreshold = 0.18f;
        private const float EndgameTime = 18f;
        private const float CaptureSlowDistance = 1.1f;
        private const float OrbitSpeedRatio = 0.60f;
        private const float RetreatMinRatio = 0.35f;
        private const float RetreatMaxRatio = 0.65f;
        private const float AttackLeadTime = 0.55f;

        private readonly Blackboard _bb;

        private delegate bool DecisionPredicate(in DecisionContext ctx);

        private readonly (DecisionPredicate Cond, ShipState State)[] _rules;

        public DecisionSystem(Blackboard blackboard)
        {
            _bb = blackboard;
            _rules = new (DecisionPredicate, ShipState)[]
            {
                (ShouldEvade, ShipState.Evade),
                (ShouldRetreatEnergy, ShipState.Retreat),
                (ShouldRetreatAggression, ShipState.Retreat),
                (ShouldPivotAttack, ShipState.Attack),
                (ShouldPivotOrbit, ShipState.Orbit),
                (ShouldCapture, ShipState.Capture),
                (ShouldAttackEnemyOwned, ShipState.Attack),
                (ShouldAttackVisibleEnemy, ShipState.Attack),
                (ShouldOrbitFriendly, ShipState.Orbit)
            };
        }

        public ShipState ChooseState(GameData data, in WaypointSelectionResult selection)
        {
            if (_bb.Self == null)
            {
                _bb.DesiredDirection = Vector2.zero;
                _bb.DesiredSpeed = 0f;
                _bb.CurrentState = ShipState.Idle;
                return ShipState.Idle;
            }

            TrackEnemyBehavior(data);
            var ctx = new DecisionContext(_bb, data, selection, RetreatEnergyThreshold, EndgameTime);
            
            if (true)
            {
                _bb.CurrentState = ShipState.Capture;
                ApplyMotion(ctx, ShipState.Capture);
                return ShipState.Capture;
            }

            ShipState next = ShipState.Idle;
            for (int i = 0; i < _rules.Length; i++)
            {
                if (_rules[i].Cond(ctx))
                {
                    next = _rules[i].State;
                    break;
                }
            }

            if (next != _bb.CurrentState)
            {
                _bb.CurrentState = next;
                _bb.LastStateChangeTime = Time.time;
            }

            ApplyMotion(ctx, next);
            return next;
        }

        private static bool ShouldEvade(in DecisionContext ctx) =>
            ctx.HasThreat && (ctx.CurrentState != ShipState.Evade || ctx.TimeSinceStateChange > EvadeMinDuration);

        private static bool ShouldRetreatEnergy(in DecisionContext ctx) =>
            ctx.InPenalty || ctx.LowEnergy;

        private static bool ShouldRetreatAggression(in DecisionContext ctx) =>
            ctx.EnemyVisible && ctx.EnemyAggression > 0.55f && !ctx.NeedsCapture;

        private static bool ShouldPivotAttack(in DecisionContext ctx) =>
            ctx.HoldLead && ctx.Pivot != null && ctx.EnemyVisible;

        private static bool ShouldPivotOrbit(in DecisionContext ctx) =>
            ctx.HoldLead && ctx.Pivot != null && !ctx.EnemyVisible;

        private static bool ShouldCapture(in DecisionContext ctx) =>
            ctx.NeedsCapture && ctx.TargetContestable;

        private static bool ShouldAttackEnemyOwned(in DecisionContext ctx) =>
            ctx.TargetEnemyOwned && ctx.EnemyAggression < 0.35f;

        private static bool ShouldAttackVisibleEnemy(in DecisionContext ctx) =>
            ctx.EnemyVisible;

        private static bool ShouldOrbitFriendly(in DecisionContext ctx) =>
            ctx.HasTarget && ctx.TargetFriendlyOwned;

        private void ApplyMotion(in DecisionContext ctx, ShipState state)
        {
            SpaceShipView self = ctx.Self;
            if (self == null)
            {
                _bb.DesiredDirection = Vector2.zero;
                _bb.DesiredSpeed = 0f;
                return;
            }

            Vector2 desiredDir = self.Velocity.sqrMagnitude > 0.01f
                ? self.Velocity
                : Blackboard.AngleToDir(self.Orientation);
            float desiredSpeed = self.SpeedMax * 0.5f;

            switch (state)
            {
                case ShipState.Evade:
                    desiredDir = ComputeEvadeDirection(ctx);
                    desiredSpeed = Mathf.Max(self.SpeedMax * 0.85f, self.Velocity.magnitude);
                    break;

                case ShipState.Retreat:
                    desiredDir = ComputeRetreatDirection(ctx);
                    float energyRatio = Mathf.Clamp01(self.Energy);
                    desiredSpeed = Mathf.Lerp(self.SpeedMax * RetreatMinRatio, self.SpeedMax * RetreatMaxRatio, energyRatio);
                    break;

                case ShipState.Capture:
                    if (ctx.Target != null)
                    {
                        desiredDir = DirectionTo(self.Position, ctx.Target.Position);
                        float distance = Vector2.Distance(self.Position, ctx.Target.Position);
                        float speedRatio = distance > CaptureSlowDistance
                            ? 1f
                            : Mathf.Lerp(0.45f, 0.8f, Mathf.InverseLerp(0.15f, CaptureSlowDistance, distance));
                        desiredSpeed = self.SpeedMax * speedRatio;
                    }
                    break;

                case ShipState.Orbit:
                {
                    WayPointView orbitTarget = ctx.Pivot ?? ctx.Target;
                    if (ctx.Pivot != null && ctx.HoldLead)
                        _bb.TargetWaypoint = ctx.Pivot;

                    if (orbitTarget != null)
                        desiredDir = OrbitDirection(self.Position, orbitTarget.Position);

                    desiredSpeed = self.SpeedMax * OrbitSpeedRatio;
                    break;
                }

                case ShipState.Attack:
                    desiredDir = ComputeAttackDirection(ctx);
                    if (ctx.Pivot != null && ctx.HoldLead)
                        _bb.TargetWaypoint = ctx.Pivot;
                    desiredSpeed = self.SpeedMax;
                    break;

                default:
                    desiredSpeed = self.SpeedMax * 0.45f;
                    break;
            }

            if (desiredDir.sqrMagnitude > 1e-4f)
                desiredDir.Normalize();
            else
                desiredDir = Blackboard.AngleToDir(self.Orientation);

            _bb.DesiredDirection = desiredDir;
            _bb.DesiredSpeed = Mathf.Clamp(desiredSpeed, 0f, self.SpeedMax);
        }

        private static Vector2 ComputeEvadeDirection(in DecisionContext ctx)
        {
            SpaceShipView self = ctx.Self;
            if (self == null)
                return Vector2.zero;

            if (ctx.EnemyVisible)
            {
                Vector2 away = self.Position - ctx.Enemy.Position;
                if (away.sqrMagnitude > 1e-4f)
                    return away;
            }

            if (self.Velocity.sqrMagnitude > 0.01f)
                return -self.Velocity;

            return Blackboard.AngleToDir(self.Orientation + 180f);
        }

        private static Vector2 ComputeRetreatDirection(in DecisionContext ctx)
        {
            SpaceShipView self = ctx.Self;
            if (self == null)
                return Vector2.zero;

            if (ctx.EnemyVisible)
            {
                Vector2 away = self.Position - ctx.Enemy.Position;
                if (away.sqrMagnitude > 1e-4f)
                    return away;
            }

            if (ctx.Target != null)
            {
                Vector2 awayTarget = self.Position - ctx.Target.Position;
                if (awayTarget.sqrMagnitude > 1e-4f)
                    return awayTarget;
            }

            if (self.Velocity.sqrMagnitude > 0.01f)
                return -self.Velocity;

            return Blackboard.AngleToDir(self.Orientation + 180f);
        }

        private static Vector2 ComputeAttackDirection(in DecisionContext ctx)
        {
            SpaceShipView self = ctx.Self;
            if (self == null)
                return Vector2.zero;

            if (ctx.EnemyVisible)
            {
                Vector2 predicted = ctx.Enemy.Position + ctx.Enemy.Velocity * AttackLeadTime;
                Vector2 toPredicted = predicted - self.Position;
                if (toPredicted.sqrMagnitude > 1e-4f)
                    return toPredicted;
            }

            if (ctx.Target != null)
            {
                Vector2 toTarget = ctx.Target.Position - self.Position;
                if (toTarget.sqrMagnitude > 1e-4f)
                    return toTarget;
            }

            if (ctx.Selection.FutureWaypoints.Count > 0)
            {
                WayPointView next = ctx.Selection.FutureWaypoints[0];
                if (next != null)
                {
                    Vector2 toNext = next.Position - self.Position;
                    if (toNext.sqrMagnitude > 1e-4f)
                        return toNext;
                }
            }

            return self.Velocity.sqrMagnitude > 0.01f ? self.Velocity : Blackboard.AngleToDir(self.Orientation);
        }

        private static Vector2 OrbitDirection(Vector2 selfPosition, Vector2 center)
        {
            Vector2 radial = selfPosition - center;
            if (radial.sqrMagnitude < 1e-6f)
                return new Vector2(0f, 1f);

            Vector2 tangent = new Vector2(-radial.y, radial.x);
            return tangent;
        }

        private static Vector2 DirectionTo(Vector2 origin, Vector2 target)
        {
            Vector2 delta = target - origin;
            return delta.sqrMagnitude > 1e-6f ? delta : Vector2.zero;
        }

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
