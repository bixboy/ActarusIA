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

    public interface IAvoidanceProvider
    {
        Vector2 ComputeEmergencyEvadeDirection(GameData data);
    }

    /// <summary>
    /// Contexte évalué une seule fois par tick pour réduire la logique conditionnelle en double.
    /// </summary>
    public readonly struct DecisionContext
    {
        public readonly bool HasThreat;
        public readonly bool InPenalty;
        public readonly bool LowEnergy;
        public readonly bool HasWaypoint;
        public readonly bool WaypointEnemyOwned;
        public readonly bool EnemyVisible;

        public DecisionContext(Blackboard bb, float retreatEnergyThreshold)
        {
            var self = bb.Self;
            HasThreat = bb.HasImminentThreat;
            InPenalty = self.HitPenaltyCountdown > 0f || self.StunPenaltyCountdown > 0f;
            LowEnergy = self.Energy < retreatEnergyThreshold;
            HasWaypoint = bb.TargetWaypoint != null;
            WaypointEnemyOwned = HasWaypoint && bb.TargetWaypoint.Owner != self.Owner;
            EnemyVisible = bb.EnemyVisible && bb.Enemy != null;
        }
    }

    public sealed class DecisionSystem
    {
        // ───── Constants / tuning ─────
        private const float EvadeMinDuration = 0.35f;
        private const float CaptureSlowDistance = 1.2f;
        private const float RetreatEnergyThreshold = 0.18f;
        private const float MidEnergyThreshold = 0.55f;

        // Prediction
        private const float MinBulletSpeedFallback = 6f; // au cas où BulletView.Speed <= 0

        // Evade
        private const float EvadeBaseSpeedRatio = 0.75f;
        private const float EvadeBurstSpeedRatio = 1.25f;
        private const float EvadeBurstEnergyThreshold = 0.35f;

        private readonly Blackboard _blackboard;
        private readonly IAvoidanceProvider _avoidanceProvider;

        public DecisionSystem(Blackboard blackboard, IAvoidanceProvider avoidanceProvider)
        {
            _blackboard = blackboard;
            _avoidanceProvider = avoidanceProvider;
        }

        // ─────────────────────────────────────────
        // Public entry
        // ─────────────────────────────────────────
        public void UpdateDecision(GameData data)
        {
            if (_blackboard.Self == null)
                return;

            // Mouvement par défaut (réinitialisation légère)
            ResetDesiredMovement();

            // Évalue le contexte une seule fois
            var ctx = new DecisionContext(_blackboard, RetreatEnergyThreshold);

            // Choix d’état
            ShipState nextState = EvaluateState(ctx);
            if (nextState != _blackboard.CurrentState)
            {
                _blackboard.CurrentState = nextState;
                _blackboard.LastStateChangeTime = Time.time;
            }

            // Exécution
            ExecuteStateLogic(data, ctx);
        }

        // ─────────────────────────────────────────
        // State machine
        // ─────────────────────────────────────────
        private ShipState EvaluateState(DecisionContext ctx)
        {
            if (ctx.HasThreat && EvadeReady())
                return ShipState.Evade;

            if (ctx.InPenalty || ctx.LowEnergy)
                return ShipState.Retreat;

            if (ctx.WaypointEnemyOwned)
                return ShipState.Capture;

            if (ctx.EnemyVisible)
                return ShipState.Attack;

            if (ctx.HasWaypoint)
                return ShipState.Orbit;

            return ShipState.Idle;
        }

        private void ExecuteStateLogic(GameData data, DecisionContext ctx)
        {
            switch (_blackboard.CurrentState)
            {
                case ShipState.Capture:
                    DoCaptureLogic(ctx);
                    break;

                case ShipState.Attack:
                    DoAttackLogic();
                    break;

                case ShipState.Retreat:
                    DoRetreatLogic(ctx);
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

        // ─────────────────────────────────────────
        // States
        // ─────────────────────────────────────────
        private void DoCaptureLogic(DecisionContext ctx)
        {
            // Vise le point
            Vector2 selfPos = _blackboard.Self.Position;
            Vector2 target = ctx.HasWaypoint
                ? _blackboard.TargetWaypoint.Position
                : selfPos + SafeDirFromOrientation(_blackboard.Self.Orientation);

            Vector2 toCenter = target - selfPos;
            Vector2 dir = NormalizeOrFallback(toCenter, SafeDirFromOrientation(_blackboard.Self.Orientation));

            // Petit strafe si l’ennemi est visible (évite d’être une cible statique)
            if (ctx.EnemyVisible)
            {
                // 25% de tangente
                Vector2 tangent = new(-dir.y, dir.x);
                dir = (dir + 0.25f * tangent).normalized;
            }

            // Vitesse adaptative : plus proche → ralentit pour stabiliser la capture
            float dist = toCenter.magnitude;
            float speedRatio = dist > CaptureSlowDistance
                ? 1.0f
                : Mathf.Lerp(0.45f, 0.8f, Mathf.InverseLerp(0.15f, CaptureSlowDistance, dist));

            SetMovement(dir, _blackboard.Self.SpeedMax * speedRatio);

            // Feu / mine uniquement si ennemi visible
            _blackboard.ShouldShoot = ctx.EnemyVisible;
            _blackboard.ShouldDropMine = ctx.EnemyVisible;
        }

        private void DoAttackLogic()
        {
            Vector2 dir;
            if (_blackboard.Enemy != null)
            {
                Vector2 predicted = PredictTarget(_blackboard.Self, _blackboard.Enemy);
                dir = NormalizeOrFallback(predicted - _blackboard.Self.Position, SafeDirFromOrientation(_blackboard.Self.Orientation));
            }
            else
            {
                dir = SafeDirFromOrientation(_blackboard.Self.Orientation);
            }

            SetMovement(dir, _blackboard.Self.SpeedMax);
            _blackboard.ShouldShoot = true;         // on privilégie l’agression ici
            // Laisse la logique de mine globale décider selon ton design,
            // sinon : _blackboard.ShouldDropMine = false;
        }

        private void DoRetreatLogic(DecisionContext ctx)
        {
            Vector2 dir;

            if (_blackboard.Enemy != null)
            {
                // Fuite “duelle”
                dir = NormalizeOrFallback(_blackboard.Self.Position - _blackboard.Enemy.Position,
                                          SafeDirFromOrientation(_blackboard.Self.Orientation + 180f));
            }
            else if (ctx.HasWaypoint && _blackboard.TargetWaypoint.Owner != _blackboard.Self.Owner)
            {
                // Repli “tactique” : on se dirige vers un objectif utile (neutre/ennemi)
                dir = NormalizeOrFallback(_blackboard.TargetWaypoint.Position - _blackboard.Self.Position,
                                          SafeDirFromOrientation(_blackboard.Self.Orientation));
            }
            else
            {
                // Fallback : inverse de la vitesse ou orientation opposée
                dir = (_blackboard.Self.Velocity.sqrMagnitude > 0.01f)
                    ? _blackboard.Self.Velocity.normalized * -1f
                    : SafeDirFromOrientation(_blackboard.Self.Orientation + 180f);
            }

            float speed = Mathf.Lerp(
                _blackboard.Self.SpeedMax * 0.35f,
                _blackboard.Self.SpeedMax * 0.65f,
                Mathf.Clamp01(_blackboard.Self.Energy / MidEnergyThreshold));

            SetMovement(dir, speed);

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
                radial = SafeDirFromOrientation(_blackboard.Self.Orientation);

            Vector2 tangent = new Vector2(-radial.y, radial.x).normalized;

            SetMovement(tangent, _blackboard.Self.SpeedMax * 0.6f);
            // Optionnel : tirer uniquement si ennemi visible, sinon pas de feu
            // _blackboard.ShouldShoot = _blackboard.EnemyVisible;
        }

        private void DoEvadeLogic(GameData data)
        {
            Vector2 evadeDir = _avoidanceProvider != null
                ? _avoidanceProvider.ComputeEmergencyEvadeDirection(data)
                : Vector2.zero;

            if (evadeDir.sqrMagnitude < 0.0001f)
                evadeDir = SafeDirFromOrientation(_blackboard.Self.Orientation + 90f);

            float speedRatio = (_blackboard.Self.Energy > EvadeBurstEnergyThreshold)
                ? EvadeBurstSpeedRatio
                : EvadeBaseSpeedRatio;

            float speed = Mathf.Max(_blackboard.Self.SpeedMax * speedRatio, _blackboard.Self.Velocity.magnitude);

            SetMovement(evadeDir.normalized, speed);

            _blackboard.ShouldShoot = false;
            _blackboard.ShouldDropMine = false;
        }

        private void DoIdleLogic()
        {
            Vector2 dir = (_blackboard.Self.Velocity.sqrMagnitude > 0.01f)
                ? _blackboard.Self.Velocity.normalized
                : SafeDirFromOrientation(_blackboard.Self.Orientation);

            SetMovement(dir, _blackboard.Self.SpeedMax * 0.45f);

            _blackboard.ShouldShoot = false;
            _blackboard.ShouldDropMine = false;
        }

        // ─────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────
        private void ResetDesiredMovement()
        {
            Vector2 dir = (_blackboard.Self.Velocity.sqrMagnitude > 0.01f)
                ? _blackboard.Self.Velocity.normalized
                : SafeDirFromOrientation(_blackboard.Self.Orientation);

            SetMovement(dir, _blackboard.Self.SpeedMax * 0.5f);
            _blackboard.ShouldShoot = false;
            _blackboard.ShouldDropMine = false;
        }

        private bool EvadeReady()
        {
            return _blackboard.CurrentState != ShipState.Evade
                   || Time.time - _blackboard.LastStateChangeTime > EvadeMinDuration;
        }

        private void SetMovementInstance(Vector2 direction, float speed)
        {
            _blackboard.DesiredDirection = (direction.sqrMagnitude > 1.0001f) ? direction.normalized : direction;
            _blackboard.DesiredSpeed = Mathf.Max(0f, speed);
        }

        // surcharge interne pour garder l’API courte
        private void SetMovement(Vector2 direction, float speed)
        {
            SetMovementInstance(direction, speed);
        }

        private static Vector2 SafeDirFromOrientation(float orientationDeg)
        {
            float r = orientationDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static Vector2 NormalizeOrFallback(Vector2 v, Vector2 fallback)
        {
            if (v.sqrMagnitude > 0.0001f) return v.normalized;
            return (fallback.sqrMagnitude > 0.0001f) ? fallback.normalized : Vector2.right;
        }

        /// <summary>
        /// Prédiction de tir basée sur la distance et la vitesse du projectile.
        /// </summary>
        private static Vector2 PredictTarget(SpaceShipView self, SpaceShipView enemy)
        {
            Vector2 toEnemy = enemy.Position - self.Position;
            float distance = toEnemy.magnitude;
            float bulletSpeed = BulletView.Speed;
            if (bulletSpeed <= 0.01f) bulletSpeed = MinBulletSpeedFallback;

            float leadTime = distance / bulletSpeed;
            return enemy.Position + enemy.Velocity * leadTime;
        }

        private static bool IsInPenalty(SpaceShipView self)
        {
            return self.HitPenaltyCountdown > 0f || self.StunPenaltyCountdown > 0f;
        }
    }
}
