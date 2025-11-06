using DoNotModify;
using UnityEngine;

namespace Teams.ActarusController.Shahine.UtilityActions
{
    public abstract class CombatModeUtilityAction : UtilityAction
    {
        [Header("Score Thresholds")]
        [SerializeField] protected int overallLeadForHunt = 2;
        [SerializeField] protected int waypointLeadForHunt = 1;
        [SerializeField] protected int scoreDeficitTolerance = 0;

        [Header("Resource Requirements")]
        [SerializeField, Range(0f, 1f)] protected float minimumEnergyForHunt = 0.5f;
        [SerializeField] protected float captureFocusTimeThreshold = 12f;

        [Header("Adaptation")]
        [SerializeField, Range(0f, 25f)] protected float maxHuntDistance = 12f;
        [SerializeField, Range(0f, 1f)] protected float enemyAggressionTolerance = 0.65f;
        [SerializeField, Range(0f, 1f)] protected float clutchAggressionBoost = 1.35f;

        [Header("Advanced Tactical Logic")]
        [SerializeField] protected int hysteresisMargin = 1;
        [SerializeField, Range(0f, 1f)] protected float enemyLowEnergyThreshold = 0.25f;

        [Header("Stability")]
        [SerializeField] protected float minimumModeDuration = 3f;

        protected CombatModeUtilityAction(Blackboard bb) : base(bb) {}

        protected virtual void Awake()
        {
            ConfigureAvailability(true, true);
        }

        protected override float GetInputValue(Scorer scorer)
        {
            if (!_bb)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.TargetWaypointOwnership:
                    return Mathf.Max(0, _bb.waypointLead);

                case ScorerInputType.MyShipEnergyLeft:
                    return _bb.myShip != null ? _bb.myShip.Energy : 0f;

                case ScorerInputType.ShipSpeed:
                    return _bb.enemyShip != null ? _bb.enemyShip.Velocity.magnitude : 0f;

                default:
                    return 0f;
            }
        }

        protected bool HasWaitedMinimumDuration()
        {
            if (!_bb)
                return false;

            return Time.time - _bb.lastCombatModeSwitchTime >= minimumModeDuration;
        }

        protected CombatEvaluation EvaluateCombatSituation()
        {
            CombatEvaluation ev = new CombatEvaluation();

            if (!_bb || _bb.myShip == null || _bb.enemyShip == null)
            {
                ev.HasValidData = false;
                return ev;
            }

            ev.HasValidData = true;
            ev.ComfortableLead = _bb.scoreLead >= overallLeadForHunt || _bb.waypointLead >= waypointLeadForHunt;
            ev.AcceptableDeficit = _bb.scoreLead >= -Mathf.Abs(scoreDeficitTolerance);
            ev.EnoughEnergy = _bb.myShip.Energy >= minimumEnergyForHunt;
            ev.EnoughTime = _bb.timeLeft > captureFocusTimeThreshold;
            ev.EnemyDistance = Vector2.Distance(_bb.myShip.Position, _bb.enemyShip.Position);
            ev.EnemyCloseEnough = ev.EnemyDistance <= maxHuntDistance;
            ev.EnemyAggressive = _bb.enemyAggressionIndex > enemyAggressionTolerance;
            ev.LosingLateGame = _bb.timeLeft < captureFocusTimeThreshold && _bb.scoreLead < 0;
            ev.EnoughEnergyForClutch = _bb.myShip.Energy >= minimumEnergyForHunt * clutchAggressionBoost;
            ev.EnemyWeak = _bb.enemyShip.Energy <= enemyLowEnergyThreshold;

            Vector2 toEnemy = (_bb.enemyShip.Position - _bb.myShip.Position).normalized;
            float closing = Vector2.Dot(_bb.enemyShip.Velocity.normalized, toEnemy);
            ev.EnemyRunningAway = closing > 0.5f;

            return ev;
        }

        protected struct CombatEvaluation
        {
            public bool HasValidData;
            public bool ComfortableLead;
            public bool AcceptableDeficit;
            public bool EnoughEnergy;
            public bool EnoughTime;
            public bool EnemyCloseEnough;
            public bool EnemyAggressive;
            public bool LosingLateGame;
            public bool EnoughEnergyForClutch;
            public bool EnemyWeak;
            public bool EnemyRunningAway;
            public float EnemyDistance;
        }
    }
}
