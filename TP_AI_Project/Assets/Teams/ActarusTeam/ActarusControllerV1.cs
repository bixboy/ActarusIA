using UnityEngine;
using DoNotModify;

namespace Teams.ActarusController
{
    public sealed class ActarusControllerV1 : BaseSpaceShipController
    {
        // ─────────────────────────────────────────────────────────────────────────
        // États
        // ─────────────────────────────────────────────────────────────────────────
        
        private enum ShipState
        {
            Idle,
            Capture,
            Attack,
            Retreat,
            Orbit,
            Evade
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Blackboard
        // ─────────────────────────────────────────────────────────────────────────
        
        private class Blackboard
        {
            public SpaceShipView Self;
            public SpaceShipView Enemy;
            public WayPointView TargetWaypoint;

            public bool EnemyVisible;
            public bool ShouldShoot;
            public bool ShouldDropMine;
            public bool ShouldShockwave;

            public Vector2 DesiredDirection;
            public float DesiredSpeed;
            
            public Vector2 Steering;
            public float LastStateChangeTime;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Réglages
        // ─────────────────────────────────────────────────────────────────────────

        // Perception
        private const float EnemyDetectionRange = 7.0f;
        private const float BulletThreatRadius = 2.2f;
        private const float MineThreatRadiusMultiplier = 1.05f;
        private const float AsteroidLookAheadTime = 0.8f;
        private const float BulletLookAheadTime = 0.6f;
        private const float EvadeMinDuration = 0.35f;

        // Tir/armes
        private const float ShootAngleToleranceDeg = 9f;
        private const float FirePredTime = 0.65f;
        private const float MinimumEnergyReserve = 0.12f;
        private const float MineDropRange = 1.9f;
        private const float ShockwaveTriggerRadius = 2.1f;
        private const float ShockwaveVelocityDot = 0.22f;

        // Énergie
        private const float LowEnergyThreshold = 0.25f;
        private const float MidEnergyThreshold = 0.55f;
        private const float RetreatEnergyThreshold = 0.18f;

        // Navigation/Steering
        private const float ResponseTime = 0.45f;
        private const float DampingWeight = 0.8f;
        private const float AvoidWeight = 2.2f;
        private const float TargetWeight = 1.0f;
        private const float OrbitWeight = 0.65f;
        private const float RecenterWeight = 0.45f;
        private const float SafeClearance = 0.35f;
        private const float MaxSteeringClamp = 3.0f;

        // Distances/angles
        private const float CaptureLookAhead = 3.2f;
        private const float AttackLookAhead = 3.0f;
        private const float RetreatLookAhead = 2.6f;
        private const float OrbitDesiredRadiusAdd = 0.15f;
        private const float CloseToWaypointDist = 1.2f;

        // Divers
        private const float AlignToSteeringThrustBoost = 0.35f;
        private const float MinThrustWhenTurning = 0.15f;
        
        private readonly Blackboard BB = new Blackboard();
        private ShipState _state = ShipState.Idle;

        // ─────────────────────────────────────────────────────────────────────────
        public override void Initialize(SpaceShipView spaceship, GameData data)
        {
            BB.Self = spaceship;
            BB.Enemy = FindEnemy(spaceship, data);
            BB.TargetWaypoint = null;
            BB.LastStateChangeTime = 0f;
            _state = ShipState.Idle;
        }

        public override InputData UpdateInput(SpaceShipView spaceship, GameData data)
        {
            RefreshBlackboard(spaceship, data);

            ShipState next = EvaluateState(data);
            if (next != _state)
            {
                _state = next;
                BB.LastStateChangeTime = Time.time;
            }

            // Comportement de l’état
            ExecuteStateLogic(data);

            // Steering inertiel
            ComputeSteering(data);

            // Conversion Steering
            float orientation = ComputeTargetOrientation(BB.Self, BB.Steering);
            float thrust = ComputeThrust(BB.Self, BB.Steering);

            bool shoot = BB.ShouldShoot && BB.Self.Energy > (BB.Self.ShootEnergyCost + MinimumEnergyReserve);
            bool dropMine = BB.ShouldDropMine && BB.Self.Energy > (BB.Self.MineEnergyCost + MinimumEnergyReserve);
            bool shock = BB.ShouldShockwave && BB.Self.Energy > BB.Self.ShockwaveEnergyCost;

            return new InputData(thrust, orientation, shoot, dropMine, shock);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Blackboard refresh
        // ─────────────────────────────────────────────────────────────────────────
        
        private void RefreshBlackboard(SpaceShipView self, GameData data)
        {
            BB.Self = self;
            BB.Enemy = FindEnemy(self, data);
            BB.TargetWaypoint = FindPriorityWaypoint(self, data);
            BB.EnemyVisible = IsEnemyVisible(self, BB.Enemy, data);

            BB.ShouldShoot = EvaluateShouldShoot(self, BB.Enemy);
            BB.ShouldDropMine = EvaluateShouldDropMine(self, BB.Enemy);
            BB.ShouldShockwave = EvaluateShouldShockwave(self, data);

            BB.DesiredDirection = (BB.Self.Velocity.sqrMagnitude > 0.01f) ? BB.Self.Velocity.normalized : AngleToDir(BB.Self.Orientation);
            BB.DesiredSpeed = BB.Self.SpeedMax * 0.5f;
            BB.Steering = Vector2.zero;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Évaluation d’état
        // ─────────────────────────────────────────────────────────────────────────
        
        private ShipState EvaluateState(GameData data)
        {
            if (HasImminentThreat(BB.Self, data))
            {
                if (_state != ShipState.Evade || Time.time - BB.LastStateChangeTime > EvadeMinDuration)
                    return ShipState.Evade;
            }

            if (IsInPenalty(BB.Self) || BB.Self.Energy < RetreatEnergyThreshold)
                return ShipState.Retreat;

            if (BB.TargetWaypoint != null && BB.TargetWaypoint.Owner != BB.Self.Owner)
                return ShipState.Capture;

            if (BB.EnemyVisible && BB.Enemy != null)
                return ShipState.Attack;

            if (BB.TargetWaypoint != null)
                return ShipState.Orbit;

            return ShipState.Idle;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Comportements d’état
        // ─────────────────────────────────────────────────────────────────────────
        private void ExecuteStateLogic(GameData data)
        {
            switch (_state)
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
            WayPointView wp = BB.TargetWaypoint;
            Vector2 target = (wp != null) ? wp.Position : BB.Self.Position + AngleToDir(BB.Self.Orientation);

            BB.DesiredDirection = (target - BB.Self.Position);
            float dist = BB.DesiredDirection.magnitude;
            
            if (dist > 0.001f)
                BB.DesiredDirection /= dist;

            float speedRatio = (dist > CloseToWaypointDist) ? 1.0f : Mathf.Lerp(0.45f, 0.8f, Mathf.InverseLerp(0.15f, CloseToWaypointDist, dist));
            BB.DesiredSpeed = BB.Self.SpeedMax * speedRatio;

            BB.ShouldShoot &= BB.EnemyVisible;
            BB.ShouldDropMine &= BB.EnemyVisible;
        }

        private void DoAttackLogic()
        {
            Vector2 predicted = (BB.Enemy != null) ? BB.Enemy.Position + BB.Enemy.Velocity * FirePredTime : BB.Self.Position + AngleToDir(BB.Self.Orientation);

            BB.DesiredDirection = (predicted - BB.Self.Position);
            float dist = BB.DesiredDirection.magnitude;
            if (dist > 0.001f) BB.DesiredDirection /= dist;

            BB.DesiredSpeed = BB.Self.SpeedMax;
        }

        private void DoRetreatLogic()
        {
            Vector2 desired = (BB.Enemy != null) ? (BB.Self.Position - BB.Enemy.Position) : (-BB.Self.Velocity);

            if (desired.sqrMagnitude < 0.001f)
                desired = AngleToDir(BB.Self.Orientation + 180f);

            BB.DesiredDirection = desired.normalized;
            BB.DesiredSpeed = Mathf.Lerp(BB.Self.SpeedMax * 0.35f, BB.Self.SpeedMax * 0.65f, Mathf.Clamp01(BB.Self.Energy / MidEnergyThreshold));

            BB.ShouldShoot = false;
            BB.ShouldDropMine = false;
        }

        private void DoOrbitLogic()
        {
            Vector2 center = (BB.TargetWaypoint != null) ? BB.TargetWaypoint.Position : BB.Self.Position;
            Vector2 radial = BB.Self.Position - center;
            
            if (radial.sqrMagnitude < 0.0001f)
                radial = AngleToDir(BB.Self.Orientation);

            Vector2 tangent = new Vector2(-radial.y, radial.x).normalized;
            BB.DesiredDirection = tangent;
            BB.DesiredSpeed = BB.Self.SpeedMax * 0.6f;
        }

        private void DoEvadeLogic(GameData data)
        {

            Vector2 evadeDir = ComputeEmergencyEvadeDirection(BB.Self, data);
            if (evadeDir.sqrMagnitude < 0.0001f) 
                evadeDir = AngleToDir(BB.Self.Orientation + 90f);

            BB.DesiredDirection = evadeDir.normalized;
            BB.DesiredSpeed = Mathf.Max(BB.Self.SpeedMax * 0.75f, BB.Self.Velocity.magnitude);

            BB.ShouldShoot = false;
            BB.ShouldDropMine = false;
        }

        private void DoIdleLogic()
        {
            BB.DesiredDirection = (BB.Self.Velocity.sqrMagnitude > 0.01f) ? BB.Self.Velocity.normalized : AngleToDir(BB.Self.Orientation);
            BB.DesiredSpeed = BB.Self.SpeedMax * 0.45f;
            
            BB.ShouldDropMine = false;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Steering inertiel
        // ─────────────────────────────────────────────────────────────────────────
        private void ComputeSteering(GameData data)
        {
            var self = BB.Self;

            Vector2 targetVel = BB.DesiredDirection * Mathf.Clamp(BB.DesiredSpeed, 0f, self.SpeedMax);
            Vector2 velError = targetVel - self.Velocity;
            Vector2 forceTarget = velError / Mathf.Max(0.1f, ResponseTime);

            Vector2 forceDamping = -self.Velocity * DampingWeight * Mathf.Clamp01(self.Velocity.magnitude / self.SpeedMax);
            Vector2 forceAvoid = ComputePredictiveAvoidance(self, data) * AvoidWeight;
            Vector2 reboundCorrection = HandleReboundMomentum(self);

            Vector2 unstuck = Vector2.zero;
            if (self.Velocity.magnitude < self.SpeedMax * 0.15f && BB.DesiredSpeed > self.SpeedMax * 0.4f)
            {
                Vector2 lateral = new Vector2(-BB.DesiredDirection.y, BB.DesiredDirection.x);
                unstuck = lateral * 0.8f;
            }

            Vector2 steering = forceTarget + forceDamping + forceAvoid + reboundCorrection + unstuck;

            if (steering.magnitude > MaxSteeringClamp)
                steering = steering.normalized * MaxSteeringClamp;

            float proximity = EstimateObstacleProximity(self, data);
            if (proximity < 1f)
            {
                float slowFactor = Mathf.Lerp(0.5f, 1f, proximity);
                steering *= slowFactor;
            }

            BB.Steering = steering;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Exploite les rebonds
        // ─────────────────────────────────────────────────────────────────────────
        private Vector2 HandleReboundMomentum(SpaceShipView self)
        {
            if (Vector2.Dot(self.Velocity, BB.DesiredDirection) < -0.3f)
            {
                Vector2 reflected = Vector2.Reflect(self.Velocity.normalized, BB.DesiredDirection);
                Vector2 reboundForce = reflected.normalized * (self.SpeedMax * 0.7f);
                Vector2 tangent = new Vector2(-reboundForce.y, reboundForce.x) * 0.4f;

                return (reboundForce + tangent).normalized;
            }

            if (self.Velocity.magnitude < self.SpeedMax * 0.2f && self.Energy > 0.2f)
            {
                Vector2 randomNudge = AngleToDir(Random.Range(0f, 360f)) * 0.6f;
                return (BB.DesiredDirection * 0.8f + randomNudge * 0.2f).normalized;
            }

            return Vector2.zero;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Menaces & évitement
        // ─────────────────────────────────────────────────────────────────────────
        private bool HasImminentThreat(SpaceShipView self, GameData data)
        {
            if (data?.Bullets != null)
            {
                foreach (var b in data.Bullets)
                {
                    if (b == null) continue;
                    if (IsBulletThreatening(self, b)) return true;
                }
            }

            if (data?.Mines != null)
            {
                foreach (var m in data.Mines)
                {
                    if (m == null) continue;
                    if (m.IsActive)
                    {
                        float d = Vector2.Distance(self.Position, m.Position);
                        if (d < (m.ExplosionRadius * MineThreatRadiusMultiplier)) return true;
                    }
                }
            }

            if (data?.Asteroids != null)
            {
                Vector2 futurePos = self.Position + self.Velocity * AsteroidLookAheadTime;
                foreach (var a in data.Asteroids)
                {
                    if (a == null) continue;
                    float safe = a.Radius + self.Radius + SafeClearance;
                    if (Vector2.Distance(futurePos, a.Position) < safe) return true;
                }
            }

            return false;
        }

        private Vector2 ComputePredictiveAvoidance(SpaceShipView self, GameData data)
        {
            Vector2 avoid = Vector2.zero;
            int count = 0;

            Vector2 velocityDir = (self.Velocity.sqrMagnitude > 0.01f)
                ? self.Velocity.normalized
                : AngleToDir(self.Orientation);

            Vector2 futurePos = self.Position + self.Velocity * AsteroidLookAheadTime * 1.2f;

            void AddAvoidance(Vector2 toObstacle, float obstacleRadius)
            {
                float dist = toObstacle.magnitude;
                float safe = obstacleRadius + self.Radius + SafeClearance;

                if (dist >= safe || dist < 0.001f)
                    return;

                float strength = Mathf.Clamp01((safe - dist) / safe);
                Vector2 radial = toObstacle.normalized;

                // Tangente directionnelle (favorise glissement)
                Vector2 tangent = new Vector2(-radial.y, radial.x);
                float side = Mathf.Sign(Vector2.Dot(tangent, velocityDir));
                tangent *= side;

                // Fusion radiale / tangentielle
                Vector2 avoidanceForce = (radial * 0.6f + tangent * 1.6f) * strength;

                // Plus fort devant que derrière
                float frontFactor = Mathf.Clamp01(Vector2.Dot(velocityDir, -radial) + 0.4f);
                avoid += avoidanceForce * frontFactor;
                count++;
            }

            // Astéroïdes
            if (data?.Asteroids != null)
            {
                foreach (var a in data.Asteroids)
                {
                    if (a == null) continue;
                    AddAvoidance(futurePos - a.Position, a.Radius);
                }
            }

            // Mines
            if (data?.Mines != null)
            {
                foreach (var m in data.Mines)
                {
                    if (m == null || !m.IsActive) continue;
                    AddAvoidance(self.Position - m.Position, m.ExplosionRadius);
                }
            }

            // Bullets
            if (data?.Bullets != null)
            {
                foreach (var b in data.Bullets)
                {
                    if (b == null) continue;
                    if (IsBulletThreatening(self, b, out Vector2 repulse))
                    {
                        Vector2 tangent = new Vector2(-repulse.y, repulse.x).normalized;
                        avoid += (repulse * 0.8f + tangent * 0.4f);
                        count++;
                    }
                }
            }

            // Moyenne
            if (count > 0)
            {
                avoid /= count;
                if (avoid.sqrMagnitude > 1e-4f)
                    avoid.Normalize();
            }

            return avoid;
        }
        
        private float EstimateObstacleProximity(SpaceShipView self, GameData data)
        {
            float nearest = float.MaxValue;

            if (data?.Asteroids != null)
            {
                foreach (var a in data.Asteroids)
                {
                    if (a == null) continue;
                    float d = Vector2.Distance(self.Position, a.Position) - (a.Radius + self.Radius);
                    if (d < nearest) nearest = d;
                }
            }

            if (data?.Mines != null)
            {
                foreach (var m in data.Mines)
                {
                    if (m == null || !m.IsActive) continue;
                    float d = Vector2.Distance(self.Position, m.Position) - (m.ExplosionRadius + self.Radius);
                    if (d < nearest) nearest = d;
                }
            }

            return Mathf.Clamp01(nearest / 2.5f);
        }

        private bool IsBulletThreatening(SpaceShipView self, BulletView b)
        {
            return IsBulletThreatening(self, b, out _);
        }

        private bool IsBulletThreatening(SpaceShipView self, BulletView b, out Vector2 repulse)
        {
            repulse = Vector2.zero;

            Vector2 relPos = self.Position - b.Position;
            Vector2 relVel = self.Velocity - b.Velocity;

            float t = BulletLookAheadTime;
            Vector2 futureRel = relPos + relVel * t;
            float distNow = relPos.magnitude;
            float distFuture = futureRel.magnitude;

            if (distFuture + 0.2f < distNow && distFuture < BulletThreatRadius)
            {
                repulse = (futureRel.sqrMagnitude > 0.0001f) ? (futureRel.normalized) : (relPos.normalized);
                float strength = Mathf.Clamp01((BulletThreatRadius - distFuture) / BulletThreatRadius);
                repulse *= strength;
                
                return true;
            }
            return false;
        }

        private Vector2 ComputeEmergencyEvadeDirection(SpaceShipView self, GameData data)
        {
            Vector2 baseAvoid = ComputePredictiveAvoidance(self, data);
            Vector2 perp = new Vector2(-self.Velocity.y, self.Velocity.x);
            
            if (perp.sqrMagnitude > 0.0001f)
                perp.Normalize();
            
            Vector2 combined = baseAvoid + perp * 0.5f;

            return (combined.sqrMagnitude > 0.0001f) ? combined.normalized : baseAvoid;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Conversion Steering
        // ─────────────────────────────────────────────────────────────────────────
        private float ComputeTargetOrientation(SpaceShipView self, Vector2 steering)
        {
            if (steering.sqrMagnitude < 0.0001f)
            {
                Vector2 fwd = AngleToDir(self.Orientation);
                float fallbackAngle = Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg;
                
                return NormalizeAngle(fallbackAngle);
            }

            float angle = Mathf.Atan2(steering.y, steering.x) * Mathf.Rad2Deg;
            return NormalizeAngle(angle);
        }

        private float ComputeThrust(SpaceShipView self, Vector2 steering)
        {
            Vector2 forward = AngleToDir(self.Orientation);
            float align = 0f;
            
            if (steering.sqrMagnitude > 0.0001f)
                align = Mathf.Clamp01(Vector2.Dot(forward, steering.normalized)); // 0..1

            float thrustBase;
            
            if (self.Energy < LowEnergyThreshold) 
                thrustBase = 0.25f;
            
            else if (self.Energy < MidEnergyThreshold) 
                thrustBase = 0.55f;
            else 
                thrustBase = 0.8f;

            if (_state == ShipState.Capture || _state == ShipState.Attack)
                thrustBase = Mathf.Min(1.0f, thrustBase + 0.2f);

            float thrust = thrustBase + align * AlignToSteeringThrustBoost;

            if (align < 0.35f) 
                thrust = Mathf.Max(thrust * 0.5f, MinThrustWhenTurning);

            return Mathf.Clamp01(thrust);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Décisions armes
        // ─────────────────────────────────────────────────────────────────────────
        private bool EvaluateShouldShoot(SpaceShipView self, SpaceShipView enemy)
        {
            if (self == null || enemy == null) 
                return false;
            
            if (self.Energy < self.ShootEnergyCost + MinimumEnergyReserve) 
                return false;

            float distance = Vector2.Distance(self.Position, enemy.Position);
            if (distance > EnemyDetectionRange + 2.0f) 
                return false;

            bool predicted = AimingHelpers.CanHit(self, enemy.Position, enemy.Velocity, 0.22f);
            Vector2 fwd = AngleToDir(self.Orientation);
            Vector2 toEnemy = (enemy.Position - self.Position).normalized;
            float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(fwd, toEnemy), -1f, 1f)) * Mathf.Rad2Deg;

            return predicted || angle < ShootAngleToleranceDeg;
        }

        private bool EvaluateShouldDropMine(SpaceShipView self, SpaceShipView enemy)
        {
            if (self == null || enemy == null)
                return false;
            
            if (self.Energy < self.MineEnergyCost + MinimumEnergyReserve) 
                return false;

            float d = Vector2.Distance(self.Position, enemy.Position);
            if (d > MineDropRange) 
                return false;

            Vector2 toEnemy = (enemy.Position - self.Position).normalized;
            Vector2 fwd = AngleToDir(self.Orientation);
            Vector2 relVel = enemy.Velocity - self.Velocity;

            bool behind = Vector2.Dot(fwd, toEnemy) < 0.25f;
            bool closing = Vector2.Dot(toEnemy, relVel) < 0f;

            return behind && closing;
        }

        private bool EvaluateShouldShockwave(SpaceShipView self, GameData data)
        {
            if (self == null) 
                return false;
            
            if (self.Energy < self.ShockwaveEnergyCost) 
                return false;

            if (data?.Bullets != null)
            {
                foreach (var b in data.Bullets)
                {
                    if (b == null) 
                        continue;
                    
                    Vector2 toShip = self.Position - b.Position;
                    float dist = toShip.magnitude;
                    if (dist > ShockwaveTriggerRadius) 
                        continue;
                    
                    if (b.Velocity.sqrMagnitude < 0.0001f)
                        continue;
                    
                    float dot = Vector2.Dot(b.Velocity.normalized, toShip.normalized);
                    if (dot > ShockwaveVelocityDot) 
                        return true;
                }
            }

            if (data?.Mines != null)
            {
                foreach (var m in data.Mines)
                {
                    if (m == null || !m.IsActive) 
                        continue;
                    
                    float d = Vector2.Distance(self.Position, m.Position);
                    if (d < m.ExplosionRadius * 0.9f) 
                        return true;
                }
            }

            return false;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Utilitaires
        // ─────────────────────────────────────────────────────────────────────────
        private SpaceShipView FindEnemy(SpaceShipView self, GameData data)
        {
            if (data?.SpaceShips == null) 
                return null;
            
            foreach (var s in data.SpaceShips)
                if (s != null && s.Owner != self.Owner) return s;
            
            return null;
        }

        private WayPointView FindPriorityWaypoint(SpaceShipView self, GameData data)
        {
            if (data?.WayPoints == null || data.WayPoints.Count == 0)
                return null;

            WayPointView best = null;
            float bestDist = float.MaxValue;

            foreach (var w in data.WayPoints)
            {
                if (w == null) 
                    continue;
                
                if (w.Owner != self.Owner)
                {
                    float d = Vector2.Distance(self.Position, w.Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = w;
                    }
                }
            }
            
            if (best != null) 
                return best;

            foreach (var w in data.WayPoints)
            {
                if (w == null) 
                    continue;
                
                float d = Vector2.Distance(self.Position, w.Position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = w;
                }
            }
            return best;
        }

        private bool IsEnemyVisible(SpaceShipView self, SpaceShipView enemy, GameData data)
        {
            if (self == null || enemy == null) 
                return false;

            Vector2 toEnemy = enemy.Position - self.Position;
            float distance = toEnemy.magnitude;
            
            if (distance > EnemyDetectionRange)
                return false;
            
            if (distance < 0.001f)
                return true;

            Vector2 dir = toEnemy / distance;
            if (data?.Asteroids != null)
            {
                foreach (var a in data.Asteroids)
                {
                    if (a == null) 
                        continue;
                    
                    Vector2 toAst = a.Position - self.Position;
                    float proj = Vector2.Dot(toAst, dir);
                    
                    if (proj <= 0f || proj >= distance) 
                        continue;
                    
                    Vector2 closest = self.Position + dir * proj;
                    
                    float sep = (a.Position - closest).magnitude;
                    float block = a.Radius + self.Radius * 0.5f;
                    
                    if (sep < block) 
                        return false;
                }
            }
            return true;
        }

        private bool IsInPenalty(SpaceShipView self)
        {
            return self.HitPenaltyCountdown > 0f || self.StunPenaltyCountdown > 0f;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Math utils
        // ─────────────────────────────────────────────────────────────────────────
        private static Vector2 AngleToDir(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }

        private static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            
            if (angle < 0f) 
                angle += 360f;
            
            return angle;
        }
    }
}
