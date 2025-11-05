using DoNotModify;
using UnityEngine;

namespace Teams.ActarusControllerV1.pierre
{
    /// <summary>
    /// Handles weapon decision making such as shooting, mine dropping, and shockwaves.
    /// </summary>
    public sealed class CombatSystem
    {
        private const float ShootAngleToleranceDeg = 9f;
        private const float MinimumEnergyReserve = 0.12f;
        private const float MineDropRange = 1.9f;
        private const float ShockwaveTriggerRadius = 2.1f;
        private const float ShockwaveVelocityDot = 0.22f;
        private const float EnemyDetectionRange = 7.0f;

        private readonly Blackboard _blackboard;

        private bool _shoot;
        private bool _dropMine;
        private bool _shockwave;

        /// <summary>
        /// Initializes a new instance of the <see cref="CombatSystem"/> class.
        /// </summary>
        /// <param name="blackboard">Shared blackboard instance.</param>
        public CombatSystem(Blackboard blackboard)
        {
            _blackboard = blackboard;
        }

        /// <summary>
        /// Evaluates weapon usage intentions and stores them on the blackboard.
        /// </summary>
        /// <param name="data">The current game state.</param>
        public void UpdateWeapons(GameData data)
        {
            if (_blackboard.Self == null)
            {
                _blackboard.ShouldShoot = false;
                _blackboard.ShouldDropMine = false;
                _blackboard.ShouldShockwave = false;
                return;
            }

            _blackboard.ShouldShoot = EvaluateShouldShoot(_blackboard.Self, _blackboard.Enemy);
            _blackboard.ShouldDropMine = EvaluateShouldDropMine(_blackboard.Self, _blackboard.Enemy);
            _blackboard.ShouldShockwave = EvaluateShouldShockwave(_blackboard.Self, data);
        }

        /// <summary>
        /// Computes the final weapon commands after other systems have modified intentions.
        /// </summary>
        public void CommitCommands()
        {
            if (_blackboard.Self == null)
            {
                _shoot = false;
                _dropMine = false;
                _shockwave = false;
                return;
            }

            float energy = _blackboard.Self.Energy;
            _shoot = _blackboard.ShouldShoot && energy > (_blackboard.Self.ShootEnergyCost + MinimumEnergyReserve);
            _dropMine = _blackboard.ShouldDropMine && energy > (_blackboard.Self.MineEnergyCost + MinimumEnergyReserve);
            _shockwave = _blackboard.ShouldShockwave && energy > _blackboard.Self.ShockwaveEnergyCost;
        }

        /// <summary>
        /// Gets a value indicating whether the ship should fire its primary weapon.
        /// </summary>
        public bool ShouldShoot => _shoot;

        /// <summary>
        /// Gets a value indicating whether the ship should drop a mine.
        /// </summary>
        public bool ShouldDropMine => _dropMine;

        /// <summary>
        /// Gets a value indicating whether the ship should trigger its shockwave.
        /// </summary>
        public bool ShouldShockwave => _shockwave;

        private bool EvaluateShouldShoot(SpaceShipView self, SpaceShipView enemy)
        {
            if (self == null || enemy == null)
            {
                return false;
            }

            if (self.Energy < self.ShootEnergyCost + MinimumEnergyReserve)
            {
                return false;
            }

            float distance = Vector2.Distance(self.Position, enemy.Position);
            if (distance > EnemyDetectionRange + 2.0f)
            {
                return false;
            }

            bool predicted = AimingHelpers.CanHit(self, enemy.Position, enemy.Velocity, 0.22f);
            Vector2 forward = Blackboard.AngleToDir(self.Orientation);
            Vector2 toEnemy = (enemy.Position - self.Position).normalized;
            float angle = Mathf.Acos(Mathf.Clamp(Vector2.Dot(forward, toEnemy), -1f, 1f)) * Mathf.Rad2Deg;

            return predicted || angle < ShootAngleToleranceDeg;
        }

        private bool EvaluateShouldDropMine(SpaceShipView self, SpaceShipView enemy)
        {
            if (self == null || enemy == null)
            {
                return false;
            }

            if (self.Energy < self.MineEnergyCost + MinimumEnergyReserve)
            {
                return false;
            }

            float distance = Vector2.Distance(self.Position, enemy.Position);
            if (distance > MineDropRange)
            {
                return false;
            }

            Vector2 toEnemy = (enemy.Position - self.Position).normalized;
            Vector2 forward = Blackboard.AngleToDir(self.Orientation);
            Vector2 relativeVelocity = enemy.Velocity - self.Velocity;

            bool enemyBehind = Vector2.Dot(forward, toEnemy) < 0.25f;
            bool closing = Vector2.Dot(toEnemy, relativeVelocity) < 0f;

            return enemyBehind && closing;
        }

        private bool EvaluateShouldShockwave(SpaceShipView self, GameData data)
        {
            if (self == null)
            {
                return false;
            }

            if (self.Energy < self.ShockwaveEnergyCost)
            {
                return false;
            }

            if (data?.Bullets != null)
            {
                foreach (var bullet in data.Bullets)
                {
                    if (bullet == null)
                    {
                        continue;
                    }

                    Vector2 toShip = self.Position - bullet.Position;
                    float distance = toShip.magnitude;
                    if (distance > ShockwaveTriggerRadius)
                    {
                        continue;
                    }

                    if (bullet.Velocity.sqrMagnitude < 0.0001f)
                    {
                        continue;
                    }

                    float dot = Vector2.Dot(bullet.Velocity.normalized, toShip.normalized);
                    if (dot > ShockwaveVelocityDot)
                    {
                        return true;
                    }
                }
            }

            if (data?.Mines != null)
            {
                foreach (var mine in data.Mines)
                {
                    if (mine == null || !mine.IsActive)
                    {
                        continue;
                    }

                    float distance = Vector2.Distance(self.Position, mine.Position);
                    if (distance < mine.ExplosionRadius * 0.9f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
