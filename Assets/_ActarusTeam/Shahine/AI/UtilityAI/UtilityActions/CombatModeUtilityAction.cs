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

        [Header("Stability")]
        [SerializeField] protected float minimumModeDuration = 3f;

        protected CombatModeUtilityAction(Blackboard bb) : base(bb) {}

        protected virtual void Awake()
        {
            ConfigureAvailability(true, true);
        }

        protected override float GetInputValue(Scorer scorer)
        {
            if (_bb == null)
                return 0f;

            switch (scorer.inputType)
            {
                case ScorerInputType.Ownership:
                    return Mathf.Max(0, _bb.waypointLead);

                case ScorerInputType.Energy:
                    return _bb.myShip != null ? _bb.myShip.Energy : 0f;

                case ScorerInputType.Speed:
                    return _bb.enemyShip != null ? _bb.enemyShip.Velocity.magnitude : 0f;

                default:
                    return 0f;
            }
        }

        protected bool HasWaitedMinimumDuration()
        {
            if (_bb == null)
                return false;

            return Time.time - _bb.lastCombatModeSwitchTime >= minimumModeDuration;
        }

        protected CombatEvaluation EvaluateCombatSituation()
        {
            CombatEvaluation evaluation = new CombatEvaluation();

            if (_bb == null || _bb.myShip == null || _bb.enemyShip == null)
            {
                evaluation.HasValidData = false;
                return evaluation;
            }

            evaluation.HasValidData = true;
            evaluation.ComfortableLead = _bb.scoreLead >= overallLeadForHunt || _bb.waypointLead >= waypointLeadForHunt;
            evaluation.AcceptableDeficit = _bb.scoreLead >= -Mathf.Abs(scoreDeficitTolerance);
            evaluation.EnoughEnergy = _bb.myShip.Energy >= minimumEnergyForHunt;
            evaluation.EnoughTime = _bb.timeLeft > captureFocusTimeThreshold;
            evaluation.EnemyDistance = Vector2.Distance(_bb.myShip.Position, _bb.enemyShip.Position);
            evaluation.EnemyCloseEnough = evaluation.EnemyDistance <= maxHuntDistance;
            evaluation.EnemyAggressive = _bb.enemyAggressionIndex > enemyAggressionTolerance;
            evaluation.LosingLateGame = _bb.timeLeft < captureFocusTimeThreshold && _bb.scoreLead < 0;
            evaluation.EnoughEnergyForClutch = _bb.myShip.Energy >= minimumEnergyForHunt * clutchAggressionBoost;

            return evaluation;
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
            public float EnemyDistance;
        }
    }
}
