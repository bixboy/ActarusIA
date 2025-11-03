using UnityEngine;
using DoNotModify;

namespace Teams.MonEquipe
{
    /// <summary>
    ///     IA principale pour le TP "IA de vaisseau spatial".
    ///     Implémente une machine à états finie pilotée par un blackboard afin de capturer les balises,
    ///     gérer les combats rapprochés et survivre aux projectiles.
    ///     L'algorithme s'appuie exclusivement sur les données exposées via GameData.
    /// </summary>
    public class MonEquipeController : BaseSpaceShipController
    {
        /// <summary>Représente les différents modes opératoires de l'IA.</summary>
        private enum ShipState
        {
            Idle,
            Capture,
            Attack,
            Retreat,
            Defend
        }

        /// <summary>Stocke les informations partagées entre les états.</summary>
        private class Blackboard
        {
            public SpaceShipView Self;
            public SpaceShipView Enemy;
            public WayPointView TargetWaypoint;
            public bool EnemyVisible;
            public bool ShouldShoot;
            public bool ShouldDropMine;
            public bool ShouldFireShockwave;
        }

        /// <summary>Structure locale facilitant la restitution des commandes d'un état.</summary>
        private struct StateOutput
        {
            public float thrust;
            public float orientation;
            public bool shoot;
            public bool dropMine;
            public bool shockwave;
        }

        // --- Constantes de réglage ---
        private const float EnemyDetectionRange = 6.5f;
        private const float AttackPredictionTime = 0.75f;
        private const float CaptureLookAheadDistance = 3.0f;
        private const float RetreatLookAheadDistance = 2.5f;
        private const float AvoidanceStrength = 2.0f;
        private const float AvoidanceDistance = 2.0f;
        private const float ShockwaveTriggerRadius = 2.0f;
        private const float ShockwaveVelocityDotThreshold = 0.25f;
        private const float ShootAngleTolerance = 8.0f;
        private const float LowEnergyThreshold = 0.25f;
        private const float MediumEnergyThreshold = 0.55f;
        private const float MinimumEnergyReserve = 0.15f;
        private const float RetreatEnergyThreshold = 0.18f;
        private const float MineDropRange = 1.8f;
        private const float OrbitSlowdownDistance = 1.5f;

        private Blackboard _blackboard = new Blackboard();
        private ShipState _state = ShipState.Idle;

        /// <inheritdoc />
        public override void Initialize(SpaceShipView spaceship, GameData data)
        {
            _blackboard = new Blackboard
            {
                Self = spaceship,
                Enemy = FindEnemyShip(spaceship, data)
            };
            _state = ShipState.Idle;
        }

        /// <inheritdoc />
        public override InputData UpdateInput(SpaceShipView spaceship, GameData data)
        {
            RefreshBlackboard(spaceship, data);

            ShipState newState = EvaluateState();
            if (newState != _state)
            {
                _state = newState;
            }

            StateOutput stateOutput = ExecuteStateBehavior(data);

            float thrust = Mathf.Clamp01(stateOutput.thrust);
            float orientation = NormalizeAngle(stateOutput.orientation);
            bool shoot = stateOutput.shoot && spaceship.Energy > spaceship.ShootEnergyCost + MinimumEnergyReserve;
            bool dropMine = stateOutput.dropMine && spaceship.Energy > spaceship.MineEnergyCost + MinimumEnergyReserve;
            bool shockwave = (stateOutput.shockwave || _blackboard.ShouldFireShockwave) && spaceship.Energy > spaceship.ShockwaveEnergyCost;

            return new InputData(thrust, orientation, shoot, dropMine, shockwave);
        }

        /// <summary>Met à jour les informations partagées pour le tick courant.</summary>
        private void RefreshBlackboard(SpaceShipView spaceship, GameData data)
        {
            _blackboard.Self = spaceship;
            _blackboard.Enemy = FindEnemyShip(spaceship, data);
            _blackboard.TargetWaypoint = FindPriorityWaypoint(spaceship, data);
            _blackboard.EnemyVisible = IsEnemyVisible(spaceship, _blackboard.Enemy, data);
            _blackboard.ShouldShoot = EvaluateShouldShoot(spaceship, _blackboard.Enemy);
            _blackboard.ShouldDropMine = EvaluateShouldDropMine(spaceship, _blackboard.Enemy);
            _blackboard.ShouldFireShockwave = EvaluateShouldFireShockwave(spaceship, data);
        }

        /// <summary>Choisit l'état optimal en fonction du contexte courant.</summary>
        private ShipState EvaluateState()
        {
            if (_blackboard.Self == null)
                return ShipState.Idle;

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
                return ShipState.Defend;
            }

            return ShipState.Idle;
        }

        /// <summary>Exécute le comportement associé à l'état courant.</summary>
        private StateOutput ExecuteStateBehavior(GameData data)
        {
            return _state switch
            {
                ShipState.Capture => ExecuteCapture(data),
                ShipState.Attack => ExecuteAttack(data),
                ShipState.Retreat => ExecuteRetreat(data),
                ShipState.Defend => ExecuteDefend(data),
                _ => ExecuteIdle(data)
            };
        }

        /// <summary>Déplacement prioritaire vers une balise à capturer.</summary>
        private StateOutput ExecuteCapture(GameData data)
        {
            WayPointView waypoint = _blackboard.TargetWaypoint;
            Vector2 waypointPosition = waypoint != null ? waypoint.Position : _blackboard.Self.Position + OrientationToDirection(_blackboard.Self.Orientation);
            Vector2 toWaypoint = waypointPosition - _blackboard.Self.Position;
            float distance = toWaypoint.magnitude;

            // Lorsque nous sommes déjà dans le rayon de capture, adopter un mouvement tangentiel pour rester dans la zone.
            Vector2 desiredDirection = toWaypoint;
            if (waypoint != null)
            {
                float captureRadius = waypoint.Radius + _blackboard.Self.Radius * 0.8f;
                if (distance < Mathf.Max(captureRadius, OrbitSlowdownDistance))
                {
                    desiredDirection = Vector2.Perpendicular(toWaypoint);
                    if (desiredDirection.sqrMagnitude < Mathf.Epsilon)
                    {
                        desiredDirection = OrientationToDirection(_blackboard.Self.Orientation + 90f);
                    }
                }
            }

            Vector2 navigationDirection = BuildNavigationDirection(desiredDirection, _blackboard.Self, data);
            Vector2 lookTarget = _blackboard.Self.Position + navigationDirection * CaptureLookAheadDistance;
            float orientation = ComputeOrientationTowards(_blackboard.Self, lookTarget);
            float thrust = ComputeThrustForEnergy(_blackboard.Self, true, distance);

            bool shoot = _blackboard.EnemyVisible && _blackboard.ShouldShoot;
            bool dropMine = _blackboard.EnemyVisible && _blackboard.ShouldDropMine;

            return new StateOutput
            {
                thrust = thrust,
                orientation = orientation,
                shoot = shoot,
                dropMine = dropMine,
                shockwave = false
            };
        }

        /// <summary>Approche offensive de l'adversaire.</summary>
        private StateOutput ExecuteAttack(GameData data)
        {
            Vector2 desiredDirection = OrientationToDirection(_blackboard.Self.Orientation);
            float distance = 0f;
            if (_blackboard.Enemy != null)
            {
                Vector2 predictedEnemy = PredictEnemyPosition(_blackboard.Enemy, AttackPredictionTime);
                desiredDirection = predictedEnemy - _blackboard.Self.Position;
                distance = desiredDirection.magnitude;
            }

            Vector2 navigationDirection = BuildNavigationDirection(desiredDirection, _blackboard.Self, data);
            Vector2 lookTarget = _blackboard.Self.Position + navigationDirection * CaptureLookAheadDistance;
            float orientation = ComputeOrientationTowards(_blackboard.Self, lookTarget);
            float thrust = ComputeThrustForEnergy(_blackboard.Self, true, distance);

            return new StateOutput
            {
                thrust = thrust,
                orientation = orientation,
                shoot = _blackboard.ShouldShoot,
                dropMine = _blackboard.ShouldDropMine,
                shockwave = false
            };
        }

        /// <summary>Fuite temporaire afin de recharger l'énergie ou d'attendre la fin d'une pénalité.</summary>
        private StateOutput ExecuteRetreat(GameData data)
        {
            Vector2 retreatDirection = -_blackboard.Self.Velocity;
            if (_blackboard.Enemy != null)
            {
                Vector2 awayFromEnemy = _blackboard.Self.Position - _blackboard.Enemy.Position;
                if (awayFromEnemy.sqrMagnitude > Mathf.Epsilon)
                {
                    retreatDirection = awayFromEnemy;
                }
            }
            if (retreatDirection.sqrMagnitude < Mathf.Epsilon)
            {
                retreatDirection = OrientationToDirection(_blackboard.Self.Orientation + 180f);
            }

            Vector2 navigationDirection = BuildNavigationDirection(retreatDirection, _blackboard.Self, data);
            Vector2 lookTarget = _blackboard.Self.Position + navigationDirection * RetreatLookAheadDistance;
            float orientation = ComputeOrientationTowards(_blackboard.Self, lookTarget);
            float thrust = ComputeThrustForEnergy(_blackboard.Self, false, retreatDirection.magnitude);

            return new StateOutput
            {
                thrust = thrust,
                orientation = orientation,
                shoot = false,
                dropMine = false,
                shockwave = false
            };
        }

        /// <summary>Maintien d'une position capturée en effectuant une orbite défensive.</summary>
        private StateOutput ExecuteDefend(GameData data)
        {
            Vector2 center = _blackboard.TargetWaypoint != null ? _blackboard.TargetWaypoint.Position : _blackboard.Self.Position;
            Vector2 radial = _blackboard.Self.Position - center;
            if (radial.sqrMagnitude < Mathf.Epsilon)
            {
                radial = OrientationToDirection(_blackboard.Self.Orientation);
            }
            Vector2 tangent = new Vector2(-radial.y, radial.x);

            Vector2 navigationDirection = BuildNavigationDirection(tangent, _blackboard.Self, data);
            Vector2 lookTarget = _blackboard.Self.Position + navigationDirection * CaptureLookAheadDistance;
            float orientation = ComputeOrientationTowards(_blackboard.Self, lookTarget);
            float thrust = ComputeThrustForEnergy(_blackboard.Self, false, radial.magnitude);

            return new StateOutput
            {
                thrust = thrust,
                orientation = orientation,
                shoot = _blackboard.EnemyVisible && _blackboard.ShouldShoot,
                dropMine = _blackboard.ShouldDropMine,
                shockwave = false
            };
        }

        /// <summary>Comportement neutre : alignement sur la vitesse actuelle avec un faible boost.</summary>
        private StateOutput ExecuteIdle(GameData data)
        {
            Vector2 desiredDirection = _blackboard.Self.Velocity.sqrMagnitude > Mathf.Epsilon
                ? _blackboard.Self.Velocity
                : OrientationToDirection(_blackboard.Self.Orientation);

            Vector2 navigationDirection = BuildNavigationDirection(desiredDirection, _blackboard.Self, data);
            Vector2 lookTarget = _blackboard.Self.Position + navigationDirection * CaptureLookAheadDistance;
            float orientation = ComputeOrientationTowards(_blackboard.Self, lookTarget);
            float thrust = ComputeThrustForEnergy(_blackboard.Self, false, desiredDirection.magnitude);

            return new StateOutput
            {
                thrust = thrust,
                orientation = orientation,
                shoot = _blackboard.EnemyVisible && _blackboard.ShouldShoot,
                dropMine = false,
                shockwave = false
            };
        }

        /// <summary>Recherche l'adversaire principal.</summary>
        private SpaceShipView FindEnemyShip(SpaceShipView self, GameData data)
        {
            if (data?.SpaceShips == null)
                return null;
            foreach (SpaceShipView candidate in data.SpaceShips)
            {
                if (candidate != null && candidate.Owner != self.Owner)
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>Retourne la balise prioritaire à capturer ou défendre.</summary>
        private WayPointView FindPriorityWaypoint(SpaceShipView self, GameData data)
        {
            if (data?.WayPoints == null || data.WayPoints.Count == 0)
                return null;

            WayPointView closestNotOwned = null;
            float bestDistance = float.MaxValue;
            foreach (WayPointView waypoint in data.WayPoints)
            {
                if (waypoint == null)
                    continue;
                float distance = Vector2.Distance(self.Position, waypoint.Position);
                if (waypoint.Owner != self.Owner && distance < bestDistance)
                {
                    bestDistance = distance;
                    closestNotOwned = waypoint;
                }
            }
            if (closestNotOwned != null)
            {
                return closestNotOwned;
            }

            // Toutes les balises nous appartiennent : retourner la plus proche pour rester en défense.
            WayPointView fallback = null;
            float fallbackDistance = float.MaxValue;
            foreach (WayPointView waypoint in data.WayPoints)
            {
                if (waypoint == null)
                    continue;
                float distance = Vector2.Distance(self.Position, waypoint.Position);
                if (distance < fallbackDistance)
                {
                    fallbackDistance = distance;
                    fallback = waypoint;
                }
            }
            return fallback;
        }

        /// <summary>Détermine si l'adversaire est détectable sans obstacle majeur.</summary>
        private bool IsEnemyVisible(SpaceShipView self, SpaceShipView enemy, GameData data)
        {
            if (self == null || enemy == null)
                return false;

            Vector2 toEnemy = enemy.Position - self.Position;
            float distance = toEnemy.magnitude;
            if (distance > EnemyDetectionRange)
                return false;
            if (distance < Mathf.Epsilon)
                return true;

            Vector2 direction = toEnemy / distance;
            foreach (AsteroidView asteroid in data.Asteroids)
            {
                if (asteroid == null)
                    continue;
                Vector2 toAsteroid = asteroid.Position - self.Position;
                float projection = Vector2.Dot(toAsteroid, direction);
                if (projection <= 0f || projection >= distance)
                    continue;
                Vector2 closestPoint = self.Position + direction * projection;
                float separation = (asteroid.Position - closestPoint).magnitude;
                float blockingRadius = asteroid.Radius + self.Radius * 0.5f;
                if (separation < blockingRadius)
                    return false;
            }

            return true;
        }

        /// <summary>Indique si une pénalité de déplacement est en cours.</summary>
        private bool IsInPenalty(SpaceShipView self)
        {
            return self.HitPenaltyCountdown > 0f || self.StunPenaltyCountdown > 0f;
        }

        /// <summary>Prédit la position de l'ennemi dans un court futur pour améliorer le tracking.</summary>
        private Vector2 PredictEnemyPosition(SpaceShipView enemy, float timeAhead)
        {
            if (enemy == null)
                return Vector2.zero;
            timeAhead = Mathf.Clamp(timeAhead, 0f, 2f);
            return enemy.Position + enemy.Velocity * timeAhead;
        }

        /// <summary>Calcule l'orientation cible vers un point en tenant compte de l'inertie actuelle.</summary>
        private float ComputeOrientationTowards(SpaceShipView self, Vector2 worldTarget)
        {
            Vector2 direction = worldTarget - self.Position;
            if (self.Velocity.sqrMagnitude < 0.01f || direction.sqrMagnitude < 0.01f)
            {
                if (direction.sqrMagnitude < 0.01f)
                {
                    direction = OrientationToDirection(self.Orientation);
                }
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                return NormalizeAngle(angle);
            }

            float steering = AimingHelpers.ComputeSteeringOrient(self, worldTarget);
            return NormalizeAngle(steering);
        }

        /// <summary>Combine la direction désirée et l'évitement des obstacles.</summary>
        private Vector2 BuildNavigationDirection(Vector2 desiredDirection, SpaceShipView self, GameData data)
        {
            Vector2 desired = desiredDirection.sqrMagnitude > Mathf.Epsilon
                ? desiredDirection.normalized
                : OrientationToDirection(self.Orientation);

            Vector2 avoidance = ComputeAvoidanceVector(self, data);
            Vector2 combined = desired + avoidance * AvoidanceStrength;
            if (combined.sqrMagnitude < Mathf.Epsilon)
            {
                combined = desired;
            }
            return combined.normalized;
        }

        /// <summary>Renvoie un vecteur de répulsion basé sur les astéroïdes et mines visibles.</summary>
        private Vector2 ComputeAvoidanceVector(SpaceShipView self, GameData data)
        {
            Vector2 avoidance = Vector2.zero;

            if (data?.Asteroids != null)
            {
                foreach (AsteroidView asteroid in data.Asteroids)
                {
                    if (asteroid == null)
                        continue;
                    Vector2 offset = self.Position - asteroid.Position;
                    float distance = offset.magnitude;
                    float safeRadius = asteroid.Radius + self.Radius + AvoidanceDistance;
                    if (distance < Mathf.Epsilon)
                        distance = 0.001f;
                    if (distance < safeRadius)
                    {
                        float strength = (safeRadius - distance) / safeRadius;
                        avoidance += offset.normalized * strength;
                    }
                }
            }

            if (data?.Mines != null)
            {
                foreach (MineView mine in data.Mines)
                {
                    if (mine == null)
                        continue;
                    Vector2 offset = self.Position - mine.Position;
                    float distance = offset.magnitude;
                    float safeRadius = mine.ExplosionRadius + self.Radius;
                    if (distance < Mathf.Epsilon)
                        distance = 0.001f;
                    if (distance < safeRadius)
                    {
                        float strength = (safeRadius - distance) / safeRadius;
                        avoidance += offset.normalized * strength;
                    }
                }
            }

            return avoidance;
        }

        /// <summary>Retourne le vecteur unitaire correspondant à une orientation en degrés.</summary>
        private Vector2 OrientationToDirection(float orientation)
        {
            float rad = orientation * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }

        /// <summary>Convertit une orientation dans l'intervalle [0, 360).</summary>
        private float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            if (angle < 0f)
            {
                angle += 360f;
            }
            return angle;
        }

        /// <summary>Ajuste le niveau de propulsion en fonction de l'énergie disponible.</summary>
        private float ComputeThrustForEnergy(SpaceShipView self, bool needSpeed, float distanceToTarget)
        {
            float energy = self.Energy;
            if (energy < LowEnergyThreshold)
            {
                return needSpeed ? 0.35f : 0.1f;
            }
            if (energy < MediumEnergyThreshold)
            {
                return needSpeed ? 0.7f : 0.35f;
            }

            if (!needSpeed && distanceToTarget < OrbitSlowdownDistance)
            {
                return 0.45f;
            }

            return needSpeed ? 1.0f : 0.65f;
        }

        /// <summary>Décide si l'on doit tirer sur l'adversaire.</summary>
        private bool EvaluateShouldShoot(SpaceShipView self, SpaceShipView enemy)
        {
            if (self == null || enemy == null)
                return false;
            if (self.Energy < self.ShootEnergyCost + MinimumEnergyReserve)
                return false;

            float distance = Vector2.Distance(self.Position, enemy.Position);
            if (distance > EnemyDetectionRange + 1.5f)
                return false;

            bool predictedHit = AimingHelpers.CanHit(self, enemy.Position, enemy.Velocity, 0.2f);
            Vector2 forward = OrientationToDirection(self.Orientation);
            Vector2 toEnemy = (enemy.Position - self.Position).normalized;
            float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(forward, toEnemy), -1f, 1f)) * Mathf.Rad2Deg;
            bool enemyInFront = angle < ShootAngleTolerance;

            return predictedHit || enemyInFront;
        }

        /// <summary>Décide si l'on doit déposer une mine défensive.</summary>
        private bool EvaluateShouldDropMine(SpaceShipView self, SpaceShipView enemy)
        {
            if (self == null || enemy == null)
                return false;
            if (self.Energy < self.MineEnergyCost + MinimumEnergyReserve)
                return false;

            float distance = Vector2.Distance(self.Position, enemy.Position);
            if (distance > MineDropRange)
                return false;

            Vector2 toEnemy = (enemy.Position - self.Position).normalized;
            Vector2 forward = OrientationToDirection(self.Orientation);
            Vector2 relativeVelocity = enemy.Velocity - self.Velocity;
            bool enemyBehind = Vector2.Dot(forward, toEnemy) < 0.25f;
            bool enemyClosing = Vector2.Dot(toEnemy, relativeVelocity) < 0f;

            return enemyBehind && enemyClosing;
        }

        /// <summary>Détection d'une menace immédiate justifiant l'utilisation d'une onde de choc.</summary>
        private bool EvaluateShouldFireShockwave(SpaceShipView self, GameData data)
        {
            if (self == null)
                return false;
            if (self.Energy < self.ShockwaveEnergyCost)
                return false;

            if (data?.Bullets != null)
            {
                foreach (BulletView bullet in data.Bullets)
                {
                    if (bullet == null)
                        continue;
                    Vector2 toShip = self.Position - bullet.Position;
                    float distance = toShip.magnitude;
                    if (distance > ShockwaveTriggerRadius)
                        continue;
                    if (bullet.Velocity.sqrMagnitude < Mathf.Epsilon)
                        continue;
                    float dot = Vector2.Dot(bullet.Velocity.normalized, toShip.normalized);
                    if (dot > ShockwaveVelocityDotThreshold)
                    {
                        return true;
                    }
                }
            }

            if (data?.Mines != null)
            {
                foreach (MineView mine in data.Mines)
                {
                    if (mine == null || !mine.IsActive)
                        continue;
                    float distance = Vector2.Distance(self.Position, mine.Position);
                    if (distance < mine.ExplosionRadius * 0.9f)
                        return true;
                }
            }

            return false;
        }
    }
}
