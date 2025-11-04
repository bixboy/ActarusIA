using DoNotModify;
using UnityEngine;

namespace Teams.ActarusControllerV2.pierre
{
    /// <summary>
    /// Central data store shared across all AI subsystems.
    /// </summary>
    public sealed class Blackboard
    {
        /// <summary>
        /// Gets or sets the controlled spaceship reference.
        /// </summary>
        public SpaceShipView Self { get; set; }

        /// <summary>
        /// Gets or sets the current enemy reference, if any.
        /// </summary>
        public SpaceShipView Enemy { get; set; }

        /// <summary>
        /// Gets or sets the target waypoint of interest.
        /// </summary>
        public WayPointView TargetWaypoint { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the enemy is visible.
        /// </summary>
        public bool EnemyVisible { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether an imminent threat is detected.
        /// </summary>
        public bool HasImminentThreat { get; set; }

        /// <summary>
        /// Gets or sets the proximity factor (0..1) to the nearest obstacle.
        /// </summary>
        public float ObstacleProximity { get; set; }

        /// <summary>
        /// Gets or sets the raw intent for shooting.
        /// </summary>
        public bool ShouldShoot { get; set; }

        /// <summary>
        /// Gets or sets the raw intent for dropping a mine.
        /// </summary>
        public bool ShouldDropMine { get; set; }

        /// <summary>
        /// Gets or sets the raw intent for triggering the shockwave.
        /// </summary>
        public bool ShouldShockwave { get; set; }

        /// <summary>
        /// Gets or sets the desired direction for the spaceship velocity.
        /// </summary>
        public Vector2 DesiredDirection { get; set; }

        /// <summary>
        /// Gets or sets the desired speed for the spaceship.
        /// </summary>
        public float DesiredSpeed { get; set; }

        /// <summary>
        /// Gets or sets the current steering vector produced by the steering system.
        /// </summary>
        public Vector2 Steering { get; set; }

        /// <summary>
        /// Gets or sets the active state of the AI.
        /// </summary>
        public ShipState CurrentState { get; set; }

        /// <summary>
        /// Gets or sets the last time the state changed.
        /// </summary>
        public float LastStateChangeTime { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Blackboard"/> class.
        /// </summary>
        /// <param name="self">The controlled ship view.</param>
        public Blackboard(SpaceShipView self)
        {
            Self = self;
            CurrentState = ShipState.Idle;
            DesiredDirection = Vector2.zero;
            Steering = Vector2.zero;
            DesiredSpeed = 0f;
            LastStateChangeTime = 0f;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Blackboard"/> class.
        /// </summary>
        public Blackboard()
            : this(null)
        {
        }

        /// <summary>
        /// Converts an angle in degrees to a direction vector.
        /// </summary>
        /// <param name="degrees">Angle in degrees.</param>
        /// <returns>Normalized direction vector pointing along the provided angle.</returns>
        public static Vector2 AngleToDir(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        /// <summary>
        /// Normalizes an angle to the [0, 360) range.
        /// </summary>
        /// <param name="angle">Angle in degrees.</param>
        /// <returns>The normalized angle.</returns>
        public static float NormalizeAngle(float angle)
        {
            angle = Mathf.Repeat(angle, 360f);
            if (angle < 0f)
            {
                angle += 360f;
            }

            return angle;
        }
    }
}
